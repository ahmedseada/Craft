// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using System.Text.Json;
using Craft.Abstractions;

namespace Craft.Editor.Run;

/// <summary>
/// A workspace's run configurations and its default, as <c>.craft/run.json</c> keeps them (X part 6):
/// <code>
/// {
///   "default": "Craft.App",
///   "configurations": [
///     { "name": "Craft.App", "provider": "dotnet", "target": "src/Craft.App/Craft.App.csproj", "action": "run",
///       "arguments": "--verbose", "environment": { "A": "1" }, "folder": "src", "buildFirst": true,
///       "interactive": "auto" }
///   ]
/// }
/// </code>
/// The default names a configuration, or a target as "provider:id". Read and written with the runtime's JSON reader and
/// writer (no reflection), the file written whole to a temporary file and moved over it.
/// </summary>
/// <param name="Configurations">The configurations, in order.</param>
/// <param name="Default">The configuration (or "provider:id" target) F5 and Ctrl+Shift+B use, or null.</param>
public sealed record RunFile(IReadOnlyList<RunConfiguration> Configurations, string? Default)
{
    /// <summary>The file's name in the workspace's <c>.craft</c> folder.</summary>
    public const string FileName = "run.json";

    /// <summary>No configurations and no default.</summary>
    public static RunFile Empty { get; } = new([], null);

    /// <summary>What reading the file found wrong (each says what was passed over), or empty.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>The file of the workspace <paramref name="root"/>.</summary>
    public static string PathOf(string root) => Path.Combine(root, ".craft", FileName);

    /// <summary>Reads the workspace's file (none: <see cref="Empty"/>; one that does not parse: empty, with the reason in <see cref="Problems"/>).</summary>
    public static RunFile Load(string root)
    {
        var path = PathOf(root);
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            return Parse(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Empty with { Problems = [$"{FileName} could not be read: {e.Message}"] };
        }
    }

    /// <summary>The configurations in <paramref name="json"/> (UTF-8).</summary>
    public static RunFile Parse(ReadOnlySpan<byte> json)
    {
        var problems = new List<string>();
        var configurations = new List<RunConfiguration>();
        string? fallback = null;
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Empty with { Problems = [$"{FileName} is not an object."] };
            }

            if (root.TryGetProperty("default", out var chosen) && chosen.ValueKind == JsonValueKind.String)
            {
                fallback = chosen.GetString();
            }

            if (root.TryGetProperty("configurations", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (Read(item, out var why) is { } configuration)
                    {
                        if (configurations.Exists(c => c.Name == configuration.Name))
                        {
                            problems.Add($"A second configuration named '{configuration.Name}' was passed over.");
                            continue;
                        }

                        configurations.Add(configuration);
                    }
                    else
                    {
                        problems.Add(why!);
                    }
                }
            }
        }
        catch (JsonException e)
        {
            return Empty with { Problems = [$"{FileName} does not parse: {e.Message}"] };
        }

        return new RunFile(configurations, fallback) { Problems = problems };
    }

    /// <summary>The file's text (UTF-8, indented).</summary>
    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            if (Default is { } chosen)
            {
                writer.WriteString("default", chosen);
            }

            writer.WriteStartArray("configurations");
            foreach (var configuration in Configurations)
            {
                writer.WriteStartObject();
                writer.WriteString("name", configuration.Name);
                writer.WriteString("provider", configuration.Provider);
                writer.WriteString("target", configuration.Target);
                writer.WriteString("action", ActionName(configuration.Action));
                if (configuration.Arguments.Length > 0)
                {
                    writer.WriteString("arguments", configuration.Arguments);
                }

                if (configuration.Environment.Count > 0)
                {
                    writer.WriteStartObject("environment");
                    foreach (var (name, value) in configuration.Environment.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        writer.WriteString(name, value);
                    }

                    writer.WriteEndObject();
                }

                if (configuration.Folder is { Length: > 0 } folder)
                {
                    writer.WriteString("folder", folder);
                }

                if (configuration.BuildFirst)
                {
                    writer.WriteBoolean("buildFirst", true);
                }

                if (configuration.Interactive != RunInteractive.Auto)
                {
                    writer.WriteString("interactive", InteractiveName(configuration.Interactive));
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>Writes the workspace's file (its <c>.craft</c> folder made when needed): a temporary file moved over it.</summary>
    public async Task SaveAsync(string root, CancellationToken cancellationToken = default)
    {
        var path = PathOf(root);
        var bytes = ToJson();
        await Task.Run(async () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>"run", "build", "test" or "publish".</summary>
    public static string ActionName(RunActions action) => action switch
    {
        RunActions.Build => "build",
        RunActions.Test => "test",
        RunActions.Publish => "publish",
        _ => "run",
    };

    /// <summary>The action of <paramref name="name"/> ("run", "build", "test", "publish", ignoring case), or <see cref="RunActions.None"/>.</summary>
    public static RunActions ActionOf(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "run" => RunActions.Run,
        "build" => RunActions.Build,
        "test" => RunActions.Test,
        "publish" => RunActions.Publish,
        _ => RunActions.None,
    };

    /// <summary>"auto", "on" or "off".</summary>
    public static string InteractiveName(RunInteractive interactive) => interactive switch
    {
        RunInteractive.On => "on",
        RunInteractive.Off => "off",
        _ => "auto",
    };

    /// <summary>The value of <paramref name="name"/> ("auto", "on", "off", ignoring case), or null for another.</summary>
    public static RunInteractive? InteractiveOf(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" => RunInteractive.Auto,
        "on" => RunInteractive.On,
        "off" => RunInteractive.Off,
        _ => null,
    };

    private static RunConfiguration? Read(JsonElement item, out string? why)
    {
        why = null;
        string? Text(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (item.ValueKind != JsonValueKind.Object)
        {
            why = "A configuration that is not an object was passed over.";
            return null;
        }

        var name = Text("name");
        var provider = Text("provider");
        var target = Text("target");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(target))
        {
            why = $"A configuration without a name, a provider or a target ({name ?? "no name"}) was passed over.";
            return null;
        }

        var action = ActionOf(Text("action") ?? "run");
        if (action == RunActions.None)
        {
            why = $"'{name}': its action '{Text("action")}' is not run, build, test or publish; it was passed over.";
            return null;
        }

        if (InteractiveOf(Text("interactive")) is not { } interactive)
        {
            why = $"'{name}': its interactive '{Text("interactive")}' is not auto, on or off; it was passed over.";
            return null;
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (item.TryGetProperty("environment", out var variables) && variables.ValueKind == JsonValueKind.Object)
        {
            foreach (var variable in variables.EnumerateObject())
            {
                environment[variable.Name] = variable.Value.ValueKind == JsonValueKind.String ? variable.Value.GetString() ?? "" : variable.Value.GetRawText();
            }
        }

        return new RunConfiguration(name.Trim(), provider.Trim(), target.Trim())
        {
            Action = action,
            Arguments = Text("arguments") ?? "",
            Environment = environment.Count == 0 ? RunConfiguration.NoEnvironment : environment,
            Folder = Text("folder") is { Length: > 0 } folder ? folder : null,
            BuildFirst = item.TryGetProperty("buildFirst", out var first) && first.ValueKind == JsonValueKind.True,
            Interactive = interactive,
        };
    }

    /// <inheritdoc />
    public bool Equals(RunFile? other) =>
        other is not null && Default == other.Default && Configurations.SequenceEqual(other.Configurations);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Default, Configurations.Count);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Default = ").Append(Default).Append(", Configurations = ").Append(string.Join(", ", Configurations.Select(c => c.Name)));
        return true;
    }
}
