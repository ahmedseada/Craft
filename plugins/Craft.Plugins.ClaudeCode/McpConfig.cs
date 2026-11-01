// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using System.Text.Json;

namespace Craft.Plugins.ClaudeCode;

/// <summary>
/// The MCP configuration Claude Code is started with (<c>--mcp-config</c>): Craft's running server as "craft" over HTTP,
/// with its bearer token. Written to the user's data folder (<c>&lt;data&gt;/claude-code/mcp-craft.json</c>), never the
/// workspace (so never git), and readable by the user only where the platform has it: Unix mode 600, a Windows ACL of
/// the user alone. Each run writes it again (the token changes with the server).
/// </summary>
public static class McpConfig
{
    /// <summary>The server's name in the config (Claude Code names its tools <c>mcp__craft__…</c>).</summary>
    public const string ServerName = "craft";

    /// <summary>The config's text for the server at <paramref name="address"/> with <paramref name="token"/> (none: no header).</summary>
    public static string Text(string address, string? token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("mcpServers");
            json.WriteStartObject(ServerName);
            json.WriteString("type", "http");
            json.WriteString("url", address);
            if (token is { Length: > 0 })
            {
                json.WriteStartObject("headers");
                json.WriteString("Authorization", "Bearer " + token);
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The config's file under <paramref name="dataFolder"/> (the user's data folder).</summary>
    public static string PathIn(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        return Path.Combine(Path.GetFullPath(dataFolder), "claude-code", "mcp-craft.json");
    }

    /// <summary>
    /// Writes the config for <paramref name="address"/> and <paramref name="token"/> under <paramref name="dataFolder"/>,
    /// refusing a data folder inside <paramref name="workspace"/>; its path. Off the UI thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">The data folder is inside the workspace (the token would be in the project).</exception>
    public static string Write(string dataFolder, string? workspace, string address, string? token)
    {
        string path = PathIn(dataFolder);
        if (workspace is { Length: > 0 } && IsInside(path, workspace))
        {
            throw new InvalidOperationException($"Craft's data folder ({dataFolder}) is inside the open folder: the MCP config holds a token, so it is not written there. Set CRAFT_DATA_FOLDER elsewhere.");
        }

        string folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        PrivateFiles.RestrictFolder(folder);

        // Written to a new file made private first, then moved over the old one: the token is never readable by others.
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, "");
        PrivateFiles.RestrictFile(temporary);
        File.WriteAllText(temporary, Text(address, token));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>Whether <paramref name="path"/> is inside <paramref name="folder"/> (ignoring case on Windows).</summary>
    public static bool IsInside(string path, string folder)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, comparison);
    }
}
