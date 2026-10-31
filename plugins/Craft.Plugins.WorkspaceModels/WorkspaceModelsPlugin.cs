// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.WorkspaceModels;
using Idrak.Abstraction.Formats;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(WorkspaceModelsPackage))]

namespace Craft.Plugins.WorkspaceModels;

/// <summary>The assembly's plug-in package: what Craft (or the idrak tool's -P) registers when it loads this plug-in.</summary>
public sealed class WorkspaceModelsPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.WorkspaceModels";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new WorkspaceModelsRegistration()];
}

/// <summary>
/// Registers "craft" in Idrak's <see cref="ModelSources"/>, "workspace" in Craft's <see cref="ModelRoleSources"/>, the editor
/// plug-in and its command, each with how to take it back.
/// </summary>
public sealed class WorkspaceModelsRegistration() : IdrakPlugin(WorkspaceModelSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => ModelSources.Register(WorkspaceModelSource.Instance), () => ModelSources.Unregister(WorkspaceModelSource.SourceName));
        Part(() => ModelRoleSources.Register(WorkspaceRoleSource.Instance), () => ModelRoleSources.Unregister(WorkspaceRoleSource.SourceName));
        Part(() => EditorPlugins.Register(WorkspaceModelsPlugin.Instance), () => EditorPlugins.Unregister(WorkspaceModelSettings.PluginName));
        Part(() => EditorCommands.Register(WorkspaceModelsPlugin.EditCommand), () => EditorCommands.Unregister(WorkspaceModelsPlugin.EditCommand.Name));
    }
}

/// <summary>The plug-in's settings (plug-in "workspaceModels", variables <c>CRAFT_PLUGIN_WORKSPACEMODELS_&lt;KEY&gt;</c>).</summary>
public static class WorkspaceModelSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "workspaceModels";

    /// <summary>The settings it declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("file", WorkspaceModelFile.DefaultPath, "The workspace's model file, relative to the workspace: its names (craft:NAME) and the model of each role."),
        new("workspace", "", "The workspace the names are read from outside the editor (the idrak tool); empty: the current folder. In the editor: the open folder."),
    ];

    /// <summary>The variable of <paramref name="key"/>.</summary>
    public static string Variable(string key) => PluginSetting.VariableName(PluginName, key);
}

/// <summary>
/// Craft's model role source "workspace": the model the workspace's file gives each role (a name of the file as
/// <c>craft:NAME</c>, any other model name as it is, a relative folder made full), and the names it defines for the Models
/// page. Read from the file kept in memory while it is unchanged (the plug-in reads it off the UI thread when the folder opens).
/// </summary>
public sealed class WorkspaceRoleSource : IModelRoleSource
{
    /// <summary>The source's name.</summary>
    public const string SourceName = "workspace";

    private WorkspaceRoleSource()
    {
    }

    /// <summary>The source.</summary>
    public static WorkspaceRoleSource Instance { get; } = new();

    /// <inheritdoc />
    public string Name => SourceName;

    /// <inheritdoc />
    public string Summary => "The workspace's .craft/model.json: its names (craft:NAME) and the model of each role";

    /// <inheritdoc />
    public string? ModelFor(string role, string? workspaceFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (workspaceFolder is null || WorkspaceModelFile.For(workspaceFolder, WorkspaceModelSource.File) is not { } file || !file.Roles.TryGetValue(role, out var model))
        {
            return null;
        }

        if (file.Models.ContainsKey(model))
        {
            return WorkspaceModelSource.Prefix + model;
        }

        return model.StartsWith(WorkspaceModelSource.Prefix, StringComparison.Ordinal) || Path.IsPathFullyQualified(model) || model.Contains(':', StringComparison.Ordinal)
            ? model
            : Path.GetFullPath(Path.Combine(workspaceFolder, model)) is var full && (Directory.Exists(full) || File.Exists(full)) ? full : model;
    }

    /// <inheritdoc />
    public IReadOnlyList<NamedModel> Names(string? workspaceFolder) =>
        workspaceFolder is not null && WorkspaceModelFile.For(workspaceFolder, WorkspaceModelSource.File) is { } file ? file.Named() : [];

    /// <inheritdoc />
    public string? FileFor(string? workspaceFolder) => workspaceFolder is null ? null : Path.GetFullPath(Path.Combine(workspaceFolder, WorkspaceModelSource.File));
}

/// <summary>
/// The editor plug-in "workspaceModels": while active, the names come from the editor's open folder (read off the UI
/// thread when it opens or changes, so the AI plug-in's settings never wait on the file), and the command
/// "Models: Edit the workspace's models" opens <c>.craft/model.json</c> (a new one when there is none).
/// </summary>
public sealed class WorkspaceModelsPlugin : IEditorPlugin
{
    private WorkspaceModelsPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static WorkspaceModelsPlugin Instance { get; } = new();

    /// <summary>Opens the workspace's model file.</summary>
    public static IEditorCommand EditCommand { get; } = new EditorCommand("workspaceModels.edit", "Models: Edit the workspace's models (.craft/model.json)",
        async (host, token) =>
        {
            if (host.WorkspaceFolder is not { } folder)
            {
                await host.ShowMessageAsync("Models", "Open a folder first: a workspace names its models in its .craft/model.json.").ConfigureAwait(true);
                return;
            }

            string path = Path.GetFullPath(Path.Combine(folder, WorkspaceModelSource.File));
            if (await host.OpenDocumentAsync(path, token).ConfigureAwait(true) is null)
            {
                await host.ShowMessageAsync("Models", $"Could not open {path}.").ConfigureAwait(true);
            }
        }, host => host.WorkspaceFolder is not null);

    /// <inheritdoc />
    public string Name => WorkspaceModelSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "Workspace model names: craft:NAME from the project's .craft/model.json, and its model for chat, completion and the agent";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => WorkspaceModelSettings.Declared;

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new Session(host);
    }

    private sealed class Session : IDisposable
    {
        private readonly IEditorHost _host;
        private string? _folder;

        public Session(IEditorHost host)
        {
            _host = host;
            WorkspaceModelSource.File = host.Settings.Get(WorkspaceModelSettings.PluginName, "file") is { Length: > 0 } file ? file : WorkspaceModelFile.DefaultPath;
            host.ActiveDocumentChanged += OnChanged;
            host.DocumentSaved += OnSaved;
            Follow();
        }

        public void Dispose()
        {
            _host.ActiveDocumentChanged -= OnChanged;
            _host.DocumentSaved -= OnSaved;
            WorkspaceModelSource.Reset();
        }

        private void OnChanged(object? sender, EventArgs e) => Follow();

        // The model file saved in the editor: read again off the UI thread.
        private void OnSaved(object? sender, EditorDocumentEventArgs e)
        {
            if (_folder is not null && e.Document.FilePath is { } path
                && string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(_folder, WorkspaceModelSource.File)), StringComparison.OrdinalIgnoreCase))
            {
                Preload(_folder);
            }
        }

        // The editor's folder, when it changed: the source reads its names from it, the file read off the UI thread.
        private void Follow()
        {
            string? folder = _host.WorkspaceFolder;
            if (string.Equals(folder, _folder, StringComparison.Ordinal))
            {
                return;
            }

            _folder = folder;
            WorkspaceModelSource.Workspace = folder;
            if (folder is not null)
            {
                Preload(folder);
            }
        }

        private void Preload(string folder) => _ = Task.Run(() =>
        {
            try
            {
                _ = WorkspaceModelFile.For(folder, WorkspaceModelSource.File);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _host.Dispatch(() => _host.Log(EditorLogLevel.Warning, error.Message));
            }
        });
    }
}
