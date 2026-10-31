// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Tools;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(ToolsPackage))]
[assembly: ToolProvider(typeof(FolderToolProvider))]

namespace Craft.Plugins.Tools;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Tools\.</summary>
public sealed class ToolsPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.Tools";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new ToolsRegistration()];
}

/// <summary>Registers the editor's tool set "editor" (<see cref="EditorToolSets"/>) and the editor plug-in "tools" that declares the tools' settings, each with how to take it back.</summary>
public sealed class ToolsRegistration() : IdrakPlugin(ToolSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorToolSets.Register(EditorToolSet.Instance), () => EditorToolSets.Unregister(EditorToolSets.Editor));
        Part(() => EditorPlugins.Register(ToolsPlugin.Instance), () => EditorPlugins.Unregister(ToolSettings.PluginName));
    }
}

/// <summary>
/// The editor's own tool set, "editor" (plan phase P1): <see cref="EditorTools.Live"/> over the running editor and
/// <see cref="EditorTools.Folder"/> over a folder, with the tools plug-in's settings as the host (or, outside the app, the
/// environment) gives them at the time the tools are made.
/// </summary>
public sealed class EditorToolSet : IEditorToolSet
{
    private EditorToolSet()
    {
    }

    /// <summary>The set.</summary>
    public static EditorToolSet Instance { get; } = new();

    /// <inheritdoc />
    public string Name => EditorToolSets.Editor;

    /// <inheritdoc />
    public string Summary => "The editor's own tools: open documents, read, selection, diagnostics, search, find_symbol, propose_edit (a diff the user accepts)";

    /// <inheritdoc />
    public IReadOnlyList<ITool> Live(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return EditorTools.Live(host, Settings(host));
    }

    /// <inheritdoc />
    public IReadOnlyList<ITool> Folder(string folder) => EditorTools.Folder(folder, ToolSettings.FromEnvironment());

    // The host's settings when the tools plug-in is registered there; else the environment (a host that loaded the set alone).
    private static ToolSettings Settings(IEditorHost host) =>
        EditorPlugins.Find(ToolSettings.PluginName) is not null ? ToolSettings.From(host.Settings) : ToolSettings.FromEnvironment();
}

/// <summary>
/// The tools plug-in as the editor lists it ("tools"): it declares the tools' settings (the settings page shows them as
/// <c>CRAFT_PLUGIN_TOOLS_*</c>); the tools themselves reach the AI plug-ins through <see cref="EditorToolSets"/>.
/// </summary>
public sealed class ToolsPlugin : IEditorPlugin
{
    private ToolsPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static ToolsPlugin Instance { get; } = new();

    /// <inheritdoc />
    public string Name => ToolSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "The editor's tools for models (chat, the agent, MCP clients): read, search, find symbols, diagnostics, edits proposed as a diff";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => ToolSettings.Declared;

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _ = ToolSettings.From(host.Settings);              // a value that does not parse is reported at activation, naming its variable
        return Inactive.Instance;
    }

    private sealed class Inactive : IDisposable
    {
        public static Inactive Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// The editor's tools for the idrak tool (<c>idrak tools test Craft.Plugins.Tools.dll</c>, <c>idrak mcp serve
/// Craft.Plugins.Tools.dll</c>, <c>idrak chat MODEL --tools Craft.Plugins.Tools.dll</c>): the folder backing over
/// <c>CRAFT_PLUGIN_TOOLS_TOOLSFOLDER</c>, else the current folder, with the plug-in's limits from the environment.
/// </summary>
public sealed class FolderToolProvider : IToolProvider
{
    /// <summary>The tools, made once.</summary>
    public IReadOnlyList<ITool> Tools { get; } = Make();

    private static IReadOnlyList<ITool> Make()
    {
        var settings = ToolSettings.FromEnvironment();
        return EditorTools.Folder(settings.ToolsFolder.Length > 0 ? settings.ToolsFolder : Environment.CurrentDirectory, settings);
    }
}
