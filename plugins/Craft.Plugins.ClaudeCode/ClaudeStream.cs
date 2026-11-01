// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Craft.Abstractions;

namespace Craft.Plugins.ClaudeCode;

/// <summary>
/// Claude Code's headless stream (<c>--output-format stream-json</c>), read a line at a time: its session's start (the
/// model, the MCP servers' state), the assistant's text and tool calls, the tools' results, and the run's result (its
/// session, turns, time, cost, or its error), as the agent panel's steps. A line it does not know is passed over.
/// </summary>
public sealed class ClaudeStream
{
    private const int DetailCharacters = 90;
    private const int DetailsCharacters = 6000;
    private readonly Dictionary<string, string> _tools = new(StringComparer.Ordinal);

    /// <summary>The session (for <c>--resume</c>), once the stream named it.</summary>
    public string? Session { get; private set; }

    /// <summary>Whether the result line came.</summary>
    public bool Ended { get; private set; }

    /// <summary>Whether the result was an error.</summary>
    public bool Failed { get; private set; }

    /// <summary>What the result said: its text for a success, its error for a failure.</summary>
    public string Result { get; private set; } = "";

    /// <summary>The run's end in a line ("Done in 6 turns · 41 s · $0.12").</summary>
    public string End { get; private set; } = "";

    /// <summary>Whether the session's start said Craft's MCP server was connected (null: not said).</summary>
    public bool? CraftConnected { get; private set; }

    /// <summary>The steps of <paramref name="line"/> (none for a line it does not know).</summary>
    public IReadOnlyList<AgentEngineEvent> Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length == 0 || line[0] != '{')
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            if (Text(root, "session_id") is { Length: > 0 } session)
            {
                Session = session;
            }

            return Text(root, "type") switch
            {
                "system" => SystemLine(root),
                "assistant" => AssistantLine(root),
                "user" => UserLine(root),
                "result" => ResultLine(root),
                _ => [],
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private List<AgentEngineEvent> SystemLine(JsonElement root)
    {
        if (Text(root, "subtype") != "init")
        {
            return [];
        }

        var servers = new List<string>();
        if (root.TryGetProperty("mcp_servers", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var server in list.EnumerateArray())
            {
                string name = Text(server, "name") ?? "?";
                string status = Text(server, "status") ?? "?";
                servers.Add($"{name}: {status}");
                if (name == McpConfig.ServerName)
                {
                    CraftConnected = status == "connected";
                }
            }
        }

        string model = Text(root, "model") ?? "its default model";
        string mcp = servers.Count == 0 ? "no MCP server" : "MCP " + string.Join(", ", servers);
        return [new AgentEngineEvent(AgentEngineStep.Note, $"Claude Code started: {model} · {mcp}")];
    }

    private List<AgentEngineEvent> AssistantLine(JsonElement root)
    {
        var steps = new List<AgentEngineEvent>();
        foreach (var block in Content(root))
        {
            switch (Text(block, "type"))
            {
                case "text" when Text(block, "text") is { Length: > 0 } text:
                    steps.Add(new AgentEngineEvent(AgentEngineStep.Text, text));
                    break;
                case "tool_use":
                    string name = Text(block, "name") ?? "tool";
                    string id = Text(block, "id") ?? "";
                    _tools[id] = name;
                    block.TryGetProperty("input", out var input);
                    steps.Add(new AgentEngineEvent(AgentEngineStep.ToolCall, name)
                    {
                        Id = id,
                        Detail = Cut(Summary(input), DetailCharacters),
                        Details = input.ValueKind == JsonValueKind.Undefined ? null : Cut(input.GetRawText(), DetailsCharacters),
                    });
                    break;
            }
        }

        return steps;
    }

    private List<AgentEngineEvent> UserLine(JsonElement root)
    {
        var steps = new List<AgentEngineEvent>();
        foreach (var block in Content(root))
        {
            if (Text(block, "type") != "tool_result")
            {
                continue;
            }

            string id = Text(block, "tool_use_id") ?? "";
            bool failed = block.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True;
            string output = ResultText(block);
            steps.Add(new AgentEngineEvent(AgentEngineStep.ToolResult, _tools.GetValueOrDefault(id) ?? "tool")
            {
                Id = id,
                Ok = !failed,
                Detail = Cut(FirstLine(output), DetailCharacters),
                Details = Cut(output, DetailsCharacters),
            });
        }

        return steps;
    }

    private List<AgentEngineEvent> ResultLine(JsonElement root)
    {
        Ended = true;
        string subtype = Text(root, "subtype") ?? "";
        Failed = (root.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True) || subtype.StartsWith("error", StringComparison.Ordinal);
        Result = Text(root, "result") ?? "";
        int turns = root.TryGetProperty("num_turns", out var n) && n.TryGetInt32(out int count) ? count : 0;
        double seconds = root.TryGetProperty("duration_ms", out var d) && d.TryGetDouble(out double ms) ? ms / 1000 : 0;
        string cost = root.TryGetProperty("total_cost_usd", out var c) && c.TryGetDouble(out double usd) && usd > 0 ? string.Create(CultureInfo.InvariantCulture, $" · ${usd:0.00}") : "";
        string said = string.Create(CultureInfo.InvariantCulture, $"{turns} turns · {seconds:0.#} s") + cost;
        if (Failed)
        {
            string why = subtype switch
            {
                "error_max_turns" => "it reached its turn limit (--max-turns)",
                "error_during_execution" => "it failed while it worked",
                _ => Result.Length > 0 ? Result : "it ended with an error",
            };
            End = $"Claude Code stopped: {why} ({said}).";
            return [new AgentEngineEvent(AgentEngineStep.Error, End)];
        }

        End = $"Done in {said}.";
        return [];
    }

    private static IEnumerable<JsonElement> Content(JsonElement root)
    {
        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object)
                {
                    yield return block;
                }
            }
        }
    }

    // A tool's input in a line: its command, its file, its pattern, its address, else its first value.
    private static string Summary(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        foreach (var key in (ReadOnlySpan<string>)["command", "file_path", "path", "pattern", "url", "query", "description"])
        {
            if (Text(input, key) is { Length: > 0 } value)
            {
                return value;
            }
        }

        foreach (var property in input.EnumerateObject())
        {
            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.GetRawText();
        }

        return "";
    }

    private static string ResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content))
        {
            return "";
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? "";
        }

        var text = new StringBuilder();
        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (Text(part, "text") is { } piece)
                {
                    text.Append(piece);
                }
            }
        }

        return text.ToString();
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string FirstLine(string text)
    {
        var trimmed = text.TrimStart();
        int end = trimmed.IndexOf('\n', StringComparison.Ordinal);
        return (end < 0 ? trimmed : trimmed[..end]).TrimEnd('\r');
    }

    /// <summary><paramref name="text"/> cut to <paramref name="max"/> characters, said so.</summary>
    public static string Cut(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= max ? text : string.Concat(text.AsSpan(0, max), string.Create(CultureInfo.InvariantCulture, $"… ({text.Length - max:N0} more characters)"));
    }
}
