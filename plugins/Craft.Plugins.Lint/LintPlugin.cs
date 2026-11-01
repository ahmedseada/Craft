// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Lint;
using Craft.Plugins.Npm;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(LintPluginPackage))]

namespace Craft.Plugins.Lint;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Lint\.</summary>
public sealed class LintPluginPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.Lint";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new LintRegistration(), new PrettierRegistration()];
}

/// <summary>Registers the editor plug-in "lint", with how to take it back (its linters, commands and code actions are added per host).</summary>
public sealed class LintRegistration() : IdrakPlugin(LintSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts() =>
        Part(() => EditorPlugins.Register(LintPlugin.Instance), () => EditorPlugins.Unregister(LintSettings.PluginName));
}

/// <summary>Registers the editor plug-in "prettier", with how to take it back (its formatter is registered per host).</summary>
public sealed class PrettierRegistration() : IdrakPlugin(PrettierSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts() =>
        Part(() => EditorPlugins.Register(PrettierPlugin.Instance), () => EditorPlugins.Unregister(PrettierSettings.PluginName));
}

/// <summary>
/// The lint plug-in ("lint", phase W): while active, one <see cref="LintSession"/> per host registers the linters "eslint"
/// and "tslint" and the code actions "lint", lints on save and on "Lint workspace", and publishes what it finds.
/// </summary>
public sealed class LintPlugin : IEditorPlugin
{
    /// <summary>"Lint: Lint this file".</summary>
    public const string LintFileCommand = "lint.file";

    /// <summary>"Lint: Lint workspace".</summary>
    public const string LintWorkspaceCommand = "lint.workspace";

    /// <summary>"Lint: Fix ESLint problems in file".</summary>
    public const string FixFileCommand = "lint.fixFile";

    /// <summary>"Lint: Set up ESLint".</summary>
    public const string SetupCommand = "lint.setup";

    /// <summary>"Lint: Forget this workspace's approvals".</summary>
    public const string ForgetCommand = "lint.forget";

    /// <summary>The code action provider's name.</summary>
    public const string ProviderName = "lint";

    private LintPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static LintPlugin Instance { get; } = new();

    /// <summary>The active session (the host the plug-in was activated in last), or null.</summary>
    internal static LintSession? Session { get; private set; }

    /// <inheritdoc />
    public string Name => LintSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "Lint: the project's ESLint (TSLint where only tslint.json is) on save and on Lint workspace, its problems in the editor and Problems, its fixes as one diff, Set up ESLint";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => LintSettings.Declared;

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var session = new LintSession(host);
        Session = session;
        return new Deactivation(() =>
        {
            if (ReferenceEquals(Session, session))
            {
                Session = null;
            }

            session.Dispose();
        });
    }

    internal sealed class Deactivation(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

/// <summary>The Prettier plug-in ("prettier", phase W): while active, registers the document formatter "prettier" (<see cref="PrettierFormatter"/>).</summary>
public sealed class PrettierPlugin : IEditorPlugin
{
    private PrettierPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static PrettierPlugin Instance { get; } = new();

    /// <inheritdoc />
    public string Name => PrettierSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "Prettier: the project's own Prettier formats JS, TS, CSS, HTML, JSON, Markdown and YAML where the project has a Prettier configuration";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => PrettierSettings.Declared;

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var session = new PrettierSession(host);
        return new LintPlugin.Deactivation(session.Dispose);
    }
}

/// <summary>Prettier in one host: the formatter registered, its settings, the sandbox gate and the scratch folder; what it found forgotten as the workspace changes.</summary>
internal sealed class PrettierSession : IDisposable
{
    private readonly IEditorHost _host;
    private bool _disposed;

    public PrettierSession(IEditorHost host)
    {
        _host = host;
        Settings = PrettierSettings.Read(host.Settings);
        Gate = new LintGate(host);
        ScratchFolder = Path.Combine(host.DataFolder ?? Path.GetTempPath(), "lint", "tmp");
        PrettierFormatter.Session = this;
        PrettierFormatter.Instance.Forget();
        DocumentFormatters.Register(PrettierFormatter.Instance);
        host.WorkspaceChanged += OnWorkspaceChanged;
        host.DocumentSaved += OnDocumentSaved;
        host.ActiveDocumentChanged += OnActiveDocumentChanged;
        _ = LintNode.FindAsync(host);
    }

    /// <summary>The settings, as read on the UI thread last.</summary>
    public PrettierSettings Settings { get; private set; }

    /// <summary>The sandbox gate (the lint plug-in's settings and approvals).</summary>
    public LintGate Gate { get; }

    /// <summary>Where a format's text and result are written (the user's data folder, never the workspace).</summary>
    public string ScratchFolder { get; }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e) => PrettierFormatter.Instance.Forget();

    private void OnDocumentSaved(object? sender, EditorDocumentEventArgs e)
    {
        Settings = PrettierSettings.Read(_host.Settings);
        string name = Path.GetFileName(e.Document.FilePath ?? "");
        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase) || LintProjects.PrettierConfigs.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            PrettierFormatter.Instance.Forget();
            LintProjects.Forget();
        }
    }

    // A file shown: what formats it is found off the UI thread, so the status bar's tooltip can say it.
    private void OnActiveDocumentChanged(object? sender, EventArgs e)
    {
        Settings = PrettierSettings.Read(_host.Settings);
        if (_host.ActiveDocument?.FilePath is { } path && PrettierFormatter.Instance.Formats(path, _host.ActiveDocument.LanguageName))
        {
            _ = PrettierFormatter.Instance.FitAsync(path, _host.WorkspaceFolder);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.WorkspaceChanged -= OnWorkspaceChanged;
        _host.DocumentSaved -= OnDocumentSaved;
        _host.ActiveDocumentChanged -= OnActiveDocumentChanged;
        DocumentFormatters.Unregister(PrettierFormatter.FormatterName);
        if (ReferenceEquals(PrettierFormatter.Session, this))
        {
            PrettierFormatter.Session = null;
        }
    }
}
