// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Idrak.Abstraction.Generation;
using Idrak.Mcp;

namespace Craft.Ai;

/// <summary>
/// The editor's live tools served to MCP clients from the running app (plan phase A1, on Idrak's S0 host):
/// <see cref="McpHost"/> over the "http" transport on a loopback address, with a bearer token, so an outside agent or
/// <c>idrak chat MODEL --mcp ADDRESS --mcp-token TOKEN</c> works on the open documents. Its edits are proposals the user
/// accepts in the diff view, as every edit is.
/// </summary>
public sealed class EditorMcpServer : IAsyncDisposable
{
    /// <summary>The transport it serves over (Idrak.Mcp's <c>McpServerTransports</c>).</summary>
    public const string Transport = "http";

    private readonly McpHost _host;

    private EditorMcpServer(McpHost host) => _host = host;

    /// <summary>Where clients connect (<c>http://127.0.0.1:PORT/mcp</c>).</summary>
    public string Address => _host.Address;

    /// <summary>The bearer token clients send, or null when none is required.</summary>
    public string? Token => _host.Token;

    /// <summary>The tools served.</summary>
    public int ToolCount => _host.ToolCount;

    /// <summary>
    /// Serves <paramref name="host"/>'s live tools at <see cref="AiSettings.McpAddress"/> with <see cref="AiSettings.McpToken"/>
    /// (empty: a random one; "none": no token).
    /// </summary>
    public static Task<EditorMcpServer> StartAsync(IEditorHost host, AiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        return StartAsync(ToolRegistry.Create().Add(EditorTools.Live(host, settings)).Build(), settings, cancellationToken);
    }

    /// <summary>Serves <paramref name="registry"/> as the editor's MCP server.</summary>
    public static async Task<EditorMcpServer> StartAsync(IToolRegistry registry, AiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        var options = new McpHostOptions { ServerName = "craft", Address = settings.McpAddress }
            .WithToken(settings.McpToken.Length == 0 ? null : settings.McpToken);
        return new EditorMcpServer(await McpHost.StartAsync(Transport, registry, options, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>What a client's configuration takes, for the user to copy.</summary>
    public string Describe() =>
        $"MCP server ({Transport}, {ToolCount} tools): {Address}\n"
        + (Token is { } token ? $"Authorization: Bearer {token}\n" : "No token is required.\n")
        + $"From the idrak tool: idrak chat MODEL --mcp {Address}{(Token is { } t ? " --mcp-token " + t : "")}";

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
