// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Npm;

/// <summary>
/// The npm plug-in in one editor host. It finds Node off the UI thread (and its version, once, through the plain "process"
/// sandbox), then, only when Node is there: its status item, the Run view's provider, the tree's and the tab's menu on
/// package.json, the palette's commands for each script of each package.json (found off the UI thread, followed through the
/// folder watcher, the editor's saves and <see cref="IEditorHost.WorkspaceChanged"/>), and the runs: each through the sandbox
/// its setting names, asked as "the npm plug-in" with the script's text, its output in a terminal tab of its own, an
/// activity whose Cancel stops the process tree. With no Node it says why, once, and adds nothing.
/// </summary>
internal sealed class NpmSession : IDisposable
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly IEditorHost _host;
    private readonly List<IDisposable> _parts = [];
    private readonly HashSet<string> _commands = new(StringComparer.Ordinal);
    private readonly HashSet<string> _said = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _problems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IActivity> _running = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _scanned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IEditorNotification? _missing;
    private IDisposable? _watch;
    private string? _watched;
    private CancellationTokenSource? _scan;
    private int _scanPending;
    private bool _active;
    private bool _disposed;

    public NpmSession(IEditorHost host)
    {
        _host = host;
        Settings = NpmSettings.Read(host.Settings);
        Install = new NodeInstall(null, null, "Node is being looked for.");
        _ = DetectAsync();
    }

    /// <summary>The settings as last read on the UI thread.</summary>
    public NpmSettings Settings { get; private set; }

    /// <summary>The Node found (or why there is none).</summary>
    public NodeInstall Install { get; private set; }

    /// <summary>The workspace's package.json files as last read, in path order.</summary>
    public IReadOnlyList<NpmPackage> Packages { get; private set; } = [];

    /// <summary>Completes once Node was looked for (and, when found, everything was added).</summary>
    public Task Ready => _ready.Task;

    /// <summary>Completes when the scan running now (or the last) has been applied.</summary>
    public Task Scanned => _scanned.Task;

    /// <summary>The status item ("Node 22.23.4"), or null without Node.</summary>
    public StatusItem? Status { get; private set; }

    /// <summary>Raised on the UI thread after the scripts were read again.</summary>
    public event EventHandler? Changed;

    /// <summary>The package of <paramref name="path"/> (a package.json's full path), or null.</summary>
    public NpmPackage? PackageOf(string? path)
    {
        if (path is null || _host.WorkspaceFolder is not { } root)
        {
            return null;
        }

        string relative = WorkspaceFiles.Relative(root, path);
        return Packages.FirstOrDefault(p => string.Equals(p.File, relative, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Looks for Node again (the settings may have changed); what was added goes, and comes back when it is found.</summary>
    public Task DetectAgainAsync()
    {
        Deactivate();
        return DetectAsync();
    }

    // ---- Node, and what is added once it is found ----

    private async Task DetectAsync()
    {
        Settings = NpmSettings.Read(_host.Settings);
        var settings = Settings;
        var install = await Task.Run(() => NodeInstall.Find(settings, System.Environment.GetEnvironmentVariable, OperatingSystem.IsWindows())).ConfigureAwait(false);
        if (install.Ready)
        {
            install = install with { Version = await ReadVersionAsync(install.Node!).ConfigureAwait(false) };
        }

        _host.Dispatch("npm: Node was looked for", () => Found(install));
    }

    // Node's version: `node --version` once, through Idrak's plain "process" sandbox (a fixed argument to a program this
    // machine's settings name, never a workspace's: no question), ten seconds at most. Null when it cannot be read.
    private static async Task<string?> ReadVersionAsync(string node)
    {
        if (CommandSandboxes.Find(CommandSandboxes.Process) is not { } process)
        {
            return null;
        }

        try
        {
            var limits = new CommandLimits { Time = TimeSpan.FromSeconds(10), MaxOutputCharacters = 200, Requester = NpmRuns.Requester, Purpose = "Node's version" };
            var result = await Task.Run(() => process.RunAsync([node, "--version"], Path.GetTempPath(), limits).AsTask()).ConfigureAwait(false);
            return result.Succeeded ? NodeInstall.ParseVersion(result.Output) : null;
        }
#pragma warning disable CA1031 // A Node that cannot say its version still runs npm; the status item says "Node".
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private void Found(NodeInstall install)
    {
        if (_disposed)
        {
            return;
        }

        Install = install;
        if (!install.Ready)
        {
            // Said once, as a status entry (no toast): why, and a way to look again once the settings are right.
            _missing?.Dispose();
            _missing = _host.ShowStatus("npm", install.Problem!, NotificationSeverity.Warning,
                [new NotificationAction("Look for Node again", _ => DetectAgainAsync(), Primary: true, Dismisses: false)]);
            _host.Log(EditorLogLevel.Information, install.Problem!);
            _ready.TrySetResult();
            return;
        }

        _missing?.Dispose();
        _missing = null;
        Activate();
        _ready.TrySetResult();
    }

    private void Activate()
    {
        _active = true;
        Status = new StatusItem("npm.node", Install.StatusText, StatusAlignment.Right, 20, NpmPlugin.RunViewCommand)
        {
            Tooltip = $"Node.js {Install.Version ?? "(version not read)"}: {Install.Node}\nnpm: {Install.NpmCli}\nClick: the Run view's npm scripts",
        };
        _parts.Add(_host.AddStatusItem(Status));
        Part(() => RunProviders.Register(NpmPlugin.Provider), () => RunProviders.Unregister(NpmRunProvider.ProviderName));
        foreach (var command in StaticCommands())
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
        }

        // The tree's and the tab's menu on a package.json (X part 7): its scripts, its scripts in a terminal, install.
        var when = new MenuWhen { Targets = MenuTargetKinds.File, Endings = [NpmPackages.FileName], Test = t => NpmPackages.IsPackageFile(Path.GetFileName(t.Path.AsSpan())) };
        foreach (var menu in (string[])[EditorContextMenus.Tree, EditorContextMenus.Tab])
        {
            foreach (var item in (MenuItem[])
            [
                new(menu, NpmPlugin.ScriptsMenu, "Run npm script", 0, 55) { Icon = RailIcons.Run, Submenu = NpmPlugin.ScriptsMenu, When = when },
                new(menu, NpmPlugin.TerminalMenu, "Run npm script in a terminal", 1, 55) { Icon = MenuIcons.Terminal, Submenu = NpmPlugin.TerminalMenu, When = when },
                new(menu, NpmPlugin.InstallHereCommand, "npm install", 2, 55) { Icon = NpmPlugin.InstallIcon, When = when },
            ])
            {
                Part(() => EditorMenus.Register(item), () => EditorMenus.Unregister(item.Menu, item.CommandId));
            }
        }

        Part(() => EditorMenuSources.Register(new MenuItemSource(NpmPlugin.ScriptsMenu, NpmPlugin.ScriptsMenu, t => ScriptRows(t, NpmPlugin.ScriptsMenu, NpmPlugin.RunScriptCommand, RailIcons.Run))),
            () => EditorMenuSources.Unregister(NpmPlugin.ScriptsMenu));
        Part(() => EditorMenuSources.Register(new MenuItemSource(NpmPlugin.TerminalMenu, NpmPlugin.TerminalMenu, t => ScriptRows(t, NpmPlugin.TerminalMenu, NpmPlugin.RunScriptInTerminalCommand, MenuIcons.Terminal))),
            () => EditorMenuSources.Unregister(NpmPlugin.TerminalMenu));

        _host.WorkspaceChanged += OnWorkspaceChanged;
        _host.DocumentSaved += OnDocumentSaved;
        _parts.Add(new Undo(() =>
        {
            _host.WorkspaceChanged -= OnWorkspaceChanged;
            _host.DocumentSaved -= OnDocumentSaved;
        }));
        Watch();
        _ = ScanAsync();
    }

    // Takes back everything Activate added (the plug-in's deactivation, or a new look for Node).
    private void Deactivate()
    {
        _active = false;
        _scan?.Cancel();
        _watch?.Dispose();
        _watch = null;
        _watched = null;
        foreach (var id in _commands)
        {
            EditorCommands.Unregister(id);
        }

        _commands.Clear();
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            _parts[i].Dispose();
        }

        _parts.Clear();
        Status = null;
        Packages = [];
    }

    private IEnumerable<IEditorCommand> StaticCommands() =>
    [
        new EditorCommand(NpmPlugin.RunScriptCommand, "npm: Run the script", (_, _) => FromMenuAsync(terminal: false), _ => MenuScript() is not null)
            { Reason = _ => "Pick a script from a package.json's menu, or run \"npm: run …\" from the palette." },
        new EditorCommand(NpmPlugin.RunScriptInTerminalCommand, "npm: Run the script in a terminal", (_, _) => FromMenuAsync(terminal: true), _ => MenuScript() is not null)
            { Reason = _ => "Pick a script from a package.json's menu, or run \"npm: run … in a terminal\" from the palette." },
        new EditorCommand(NpmPlugin.InstallHereCommand, "npm: install", (_, _) => HerePackage() is { } package ? InstallAsync(package) : Task.CompletedTask, _ => HerePackage() is not null)
            { Reason = _ => "No package.json here: open one, or right-click one in the explorer." },
        new EditorCommand(NpmPlugin.RefreshCommand, "npm: Read the scripts again", (_, _) => ScanAsync()),
        new EditorCommand(NpmPlugin.StopCommand, "npm: Stop every npm run", (_, _) => { StopAll(); return Task.CompletedTask; }, _ => _running.Count > 0)
            { Reason = _ => "No npm run is running." },
    ];

    private void Part(Action add, Action remove)
    {
        add();
        _parts.Add(new Undo(remove));
    }

    // ---- the workspace's package.json files ----

    /// <summary>Reads every package.json of the workspace off the UI thread, then shows its scripts; completes when they are in.</summary>
    public Task ScanAsync()
    {
        if (_disposed || !_active)
        {
            return Task.CompletedTask;
        }

        Settings = NpmSettings.Read(_host.Settings);
        if (_scanned.Task.IsCompleted)
        {
            _scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var done = _scanned;
        var previous = _scan;
        var cancel = _scan = new CancellationTokenSource();
        if (previous is not null)
        {
            _ = previous.CancelAsync();
        }

        string? root = _host.WorkspaceFolder;
        var ignore = Settings.Ignore;
        _ = Task.Run(() =>
        {
            IReadOnlyList<NpmPackage> packages = [];
            try
            {
                if (root is not null && Directory.Exists(root))
                {
                    packages = [.. NpmPackages.Find(root, ignore, cancel.Token).Select(file => NpmPackages.Read(root, file))];
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _host.Log(EditorLogLevel.Warning, $"npm: the workspace's package.json files could not be listed ({e.Message}).");
            }

            _host.Dispatch("npm: the scripts were read", () =>
            {
                if (!cancel.IsCancellationRequested && !_disposed && _active)
                {
                    Apply(root, packages);
                }

                done.TrySetResult();
            });
        }, CancellationToken.None);
        return done.Task;
    }

    // The UI thread: each package's commands (one per script and one in a terminal, and its install), added and taken back
    // by id; another manager's lock said once per workspace; a package that cannot be read logged when its problem changes.
    private void Apply(string? root, IReadOnlyList<NpmPackage> packages)
    {
        Packages = packages;
        var wanted = new Dictionary<string, (string Title, Func<Task> Run)>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            string where = package.Folder.Length == 0 ? "" : $" ({package.Folder})";
            string file = package.File;
            foreach (var script in package.Scripts)
            {
                string name = script.Name;
                wanted[NpmPlugin.ScriptCommandId(file, name)] = ($"npm: run {name}{where}", () => RunNamedAsync(file, name, terminal: false));
                wanted[NpmPlugin.TerminalCommandId(file, name)] = ($"npm: run {name} in a terminal{where}", () => RunNamedAsync(file, name, terminal: true));
            }

            wanted[NpmPlugin.InstallCommandId(file)] = ($"npm: install{where}", () => PackageNamed(file) is { } now ? InstallAsync(now) : Task.CompletedTask);

            if (package.Problem is { } problem)
            {
                if (!_problems.TryGetValue(file, out var said) || said != problem)
                {
                    _problems[file] = problem;
                    _host.Log(EditorLogLevel.Warning, $"npm: {file}: {problem}; its scripts are not offered until it reads.");
                }
            }
            else
            {
                _problems.Remove(file);
            }

            if (package.Manager is { } manager && root is not null && _said.Add(root + "|" + manager))
            {
                _host.Notify($"npm: {manager} is not supported yet",
                    $"{package.File} is {manager}'s ({package.ManagerEvidence}). Its scripts still run with npm; installing with {manager} is not supported yet, so \"npm: install\" leaves it to you.");
            }
        }

        foreach (var id in _commands.Where(id => !wanted.ContainsKey(id)).ToList())
        {
            EditorCommands.Unregister(id);
            _commands.Remove(id);
        }

        foreach (var (id, (title, run)) in wanted)
        {
            if (_commands.Add(id))
            {
                EditorCommands.Register(new EditorCommand(id, title, (_, _) => run()));
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private NpmPackage? PackageNamed(string file) => Packages.FirstOrDefault(p => p.File == file);

    private Task RunNamedAsync(string file, string name, bool terminal) =>
        PackageNamed(file) is { } package && package.Script(name) is { } script ? RunAsync(package, script, terminal) : Task.CompletedTask;

    // A menu row's script: the menu's package.json and the row's argument.
    private (NpmPackage Package, NpmScript Script)? MenuScript() =>
        EditorContextMenus.Target is { Path: { } path, Argument: { } name } && PackageOf(path) is { } package && package.Script(name) is { } script ? (package, script) : null;

    private Task FromMenuAsync(bool terminal) => MenuScript() is var (package, script) ? RunAsync(package, script, terminal) : Task.CompletedTask;

    // The package "here": the menu's package.json, else the active document when it is one, else the root's, else the only one.
    private NpmPackage? HerePackage()
    {
        if (EditorContextMenus.Target is { Path: { } path } && PackageOf(path) is { } menu)
        {
            return menu;
        }

        if (PackageOf(_host.ActiveDocument?.FilePath) is { } active)
        {
            return active;
        }

        return Packages.FirstOrDefault(p => p.Folder.Length == 0) ?? (Packages.Count == 1 ? Packages[0] : null);
    }

    private List<MenuItem> ScriptRows(ContextMenuTarget? target, string menu, string command, string icon)
    {
        if (target?.Path is not { } path || PackageOf(path) is not { } package)
        {
            return [];
        }


        var rows = new List<MenuItem>(package.Scripts.Count);
        for (int i = 0; i < package.Scripts.Count; i++)
        {
            var script = package.Scripts[i];
            rows.Add(new MenuItem(menu, command, script.Name, i) { Icon = icon, Argument = script.Name });
        }

        return rows;
    }

    // ---- following the disk ----

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e)
    {
        if (e.Kind != WorkspaceChangeKind.Refreshed)
        {
            _said.Clear();
            _problems.Clear();
            Watch();
        }

        _ = ScanAsync();
    }

    private void OnDocumentSaved(object? sender, EditorDocumentEventArgs e)
    {
        if (e.Document.FilePath is { } path && NpmPackages.Matters(Path.GetFileName(path.AsSpan())))
        {
            ScheduleScan(now: true);
        }
    }

    private void Watch()
    {
        string? root = _host.WorkspaceFolder;
        if (root == _watched)
        {
            return;
        }

        _watch?.Dispose();
        _watch = null;
        _watched = root;
        if (root is null)
        {
            return;
        }

        try
        {
            _watch = FolderWatchers.Get(FolderWatchers.System).Watch(root, OnFolderChange);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            _host.Log(EditorLogLevel.Warning, $"npm: the scripts do not follow the disk ({e.Message}); \"npm: Read the scripts again\" reads them.");
        }
    }

    // The watcher's threads: a package.json or a lock file made, removed or renamed, a folder that held a package removed
    // or renamed, or a lost burst: read again, once per burst.
    private void OnFolderChange(FolderChange change)
    {
        if (change.Kind == FolderChangeKind.Rescan || Relevant(change.Path) || (change.OldPath is { } old && Relevant(old)))
        {
            ScheduleScan(now: false);
        }
    }

    private bool Relevant(string path)
    {
        if (NpmPackages.Matters(Path.GetFileName(path.AsSpan())))
        {
            return true;
        }

        if (_watched is not { } root)
        {
            return false;
        }

        string relative = WorkspaceFiles.Relative(root, path) + "/";
        foreach (var package in Packages)
        {
            if (package.File.StartsWith(relative, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ScheduleScan(bool now)
    {
        if (Interlocked.Exchange(ref _scanPending, 1) == 1)
        {
            return;
        }

        int wait = now ? 0 : Settings.ScanDelayMilliseconds;
        _ = Task.Run(async () =>
        {
            await Task.Delay(wait).ConfigureAwait(false);
            _host.Dispatch("npm: a package.json changed", () =>
            {
                Volatile.Write(ref _scanPending, 0);
                _ = ScanAsync();
            });
        });
    }

    // ---- runs ----

    /// <summary>
    /// Runs <paramref name="script"/> of <paramref name="package"/> through the sandbox of <c>sandbox</c>: in its output tab
    /// ("npm: build"), or with <paramref name="terminal"/> in a terminal tab that takes the keyboard (the sandbox's
    /// interactive start). On the UI thread.
    /// </summary>
    public Task<NpmRunOutcome> RunAsync(NpmPackage package, NpmScript script, bool terminal = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(script);
        var settings = Settings = NpmSettings.Read(_host.Settings);
        string purpose = $"the script {script.Name} of {package.File}: {script.Text}";
        string name = NpmRuns.TabName(package, script.Name);
        return terminal
            ? StartAsync(name, package, NpmRuns.Script(Install, script.Name), purpose, settings.TimeLimitOf(script.Name), $"npm run {script.Name}  ·  {script.Text}")
            : RunInTabAsync(name, package, NpmRuns.Script(Install, script.Name), purpose, settings.TimeLimitOf(script.Name), $"npm run {script.Name}  ·  {script.Text}", null);
    }

    /// <summary>
    /// "npm: install" for <paramref name="package"/> (<c>install</c>, or <c>ci</c> by the setting; ci without npm's lock
    /// file runs install, said): an activity with its elapsed time and last line. A package another manager's lock or
    /// field names is not installed with npm (said). The scripts are read again after it.
    /// </summary>
    public async Task<NpmRunOutcome> InstallAsync(NpmPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var settings = Settings = NpmSettings.Read(_host.Settings);
        if (package.Manager is { } manager)
        {
            string why = $"{package.File} is {manager}'s ({package.ManagerEvidence}): installing with {manager} is not supported yet; run {manager} install in a terminal.";
            _host.Notify($"npm: {manager} is not supported yet", why);
            return new NpmRunOutcome(NpmRunEnd.NotRun, null, why);
        }

        string verb = settings.Install;
        string? note = null;
        if (verb == "ci" && !package.HasNpmLock)
        {
            verb = "install";
            note = "npm ci needs package-lock.json, which this package has not: npm install runs instead.";
        }

        var outcome = await RunInTabAsync(NpmRuns.TabName(package, "install"), package, NpmRuns.Install(Install, verb), $"npm {verb} for {package.File}",
            settings.TimeLimitOf(null), $"npm {verb}", note).ConfigureAwait(true);
        _ = ScanAsync();
        return outcome;
    }

    /// <summary>Cancels every npm run (each ends with its process tree).</summary>
    public void StopAll()
    {
        foreach (var activity in _running.Values.ToList())
        {
            _host.Activities.Cancel(activity.Id);
        }
    }

    // What the runs share before they start: Node, the workspace, the sandbox registered and willing, the name not running.
    private (ICommandSandbox? Sandbox, string Root, string Folder, string? Why) Prepare(string name, NpmPackage package, CommandLimits limits, bool interactive)
    {
        string root = _host.WorkspaceFolder ?? "";
        string folder = root.Length == 0 ? "" : Path.GetFullPath(Path.Combine(root, package.Folder));
        if (!Install.Ready)
        {
            return (null, root, folder, Install.Problem ?? "Node was not found.");
        }

        if (root.Length == 0)
        {
            return (null, root, folder, "No folder is open.");
        }

        if (_running.ContainsKey(name))
        {
            return (null, root, folder, $"{name} is already running: stop it first (its Cancel, or \"npm: Stop every npm run\").");
        }

        string variable = PluginSetting.VariableName(NpmSettings.PluginName, "sandbox");
        if (CommandSandboxes.Find(Settings.Sandbox) is not { } sandbox)
        {
            return (null, root, folder, $"The command sandbox '{Settings.Sandbox}' ({variable}) is not registered (registered: {string.Join(", ", CommandSandboxes.Names)}); nothing ran.");
        }

        if ((interactive ? sandbox.CheckStart(limits) : sandbox.Check(limits)) is { } refusal)
        {
            return (null, root, folder, $"The {sandbox.Name} sandbox ({variable}) cannot run it: {refusal}");
        }

        return (sandbox, root, folder, null);
    }

    private async Task<NpmRunOutcome> RunInTabAsync(string name, NpmPackage package, IReadOnlyList<string> command, string purpose, TimeSpan time, string header, string? note)
    {
        var limits = NpmRuns.Limits(Settings, _host.WorkspaceFolder ?? "", purpose, time);
        var (sandbox, root, folder, why) = Prepare(name, package, limits, interactive: false);
        var output = _host.Terminals?.OpenOutput(name);
        if (sandbox is null)
        {
            return NotRun(why!, output);
        }

        limits = limits with { Root = root };
        output?.Append($"\n\e[2m{(package.Folder.Length == 0 ? "the workspace" : package.Folder)}\e[0m\n\e[1m$ {header}\e[0m\n");
        if (note is not null)
        {
            output?.Append($"\e[33m{note}\e[0m\n");
        }

        output?.Show();
        var clock = Stopwatch.StartNew();
        using var activity = _host.Activities.Start(name, ActivityKinds.Command, cancellable: true);
        _running[name] = activity;
        var lines = new LineBatch(_host, output, activity, clock);
        try
        {
            var watched = limits with { Output = lines.Add };
            var result = await Task.Run(() => sandbox.RunAsync(command, folder, watched, activity.CancellationToken).AsTask(), CancellationToken.None).ConfigureAwait(true);
            await lines.FlushedAsync().ConfigureAwait(true);
            string elapsed = Elapsed(clock.Elapsed);
            if (result.Refused)
            {
                output?.Append($"\e[33mnot run: {result.Output}\e[0m\n");
                activity.Complete("not run");
                return new NpmRunOutcome(NpmRunEnd.NotRun, null, result.Output);
            }

            if (result.TimedOut)
            {
                string limit = $"stopped at its time limit ({Elapsed(limits.Time)}, {PluginSetting.VariableName(NpmSettings.PluginName, "timeLimitMinutes")})";
                output?.Append($"\e[33m{limit}\e[0m\n");
                activity.Fail(limit);
                return new NpmRunOutcome(NpmRunEnd.TimedOut, null, limit);
            }

            if (result.Succeeded)
            {
                output?.Append($"\e[32mexit code 0 · {elapsed}\e[0m\n");
                activity.Complete($"done in {elapsed}");
                return new NpmRunOutcome(NpmRunEnd.Succeeded, 0);
            }

            string failed = string.Create(Culture, $"exit code {result.ExitCode} · {elapsed}");
            output?.Append($"\e[31m{failed}\e[0m\n");
            activity.Fail(failed);
            return new NpmRunOutcome(result.OutOfMemory ? NpmRunEnd.Stopped : NpmRunEnd.Failed, result.ExitCode, result.OutOfMemory ? "its memory limit" : failed);
        }
        catch (OperationCanceledException)
        {
            await lines.FlushedAsync().ConfigureAwait(true);
            output?.Append("\e[33mstopped, with its process tree\e[0m\n");
            return new NpmRunOutcome(NpmRunEnd.Stopped, null, "cancelled");
        }
        catch (NotSupportedException e)
        {
            output?.Append($"\e[33mnot run: {e.Message}\e[0m\n");
            activity.Fail(e.Message);
            return new NpmRunOutcome(NpmRunEnd.NotRun, null, e.Message);
        }
        finally
        {
            _running.Remove(name);
        }
    }

    // A script in a terminal tab that takes the keyboard (X part 6b's interactive start): asked once by the sandbox, then its
    // input, output and error are the tab's; Cancel, Stop and Ctrl+C in the tab end its process tree.
    private async Task<NpmRunOutcome> StartAsync(string name, NpmPackage package, IReadOnlyList<string> command, string purpose, TimeSpan time, string header)
    {
        var limits = NpmRuns.Limits(Settings, _host.WorkspaceFolder ?? "", purpose, time);
        var (sandbox, root, folder, why) = Prepare(name, package, limits, interactive: true);
        if (sandbox is null || _host.Terminals is null)
        {
            why ??= "This window has no terminal to take the script's input.";
            _host.Notify("npm: not run in a terminal", why, NotificationSeverity.Warning);
            return new NpmRunOutcome(NpmRunEnd.NotRun, null, why);
        }

        limits = limits with { Root = root };
        var clock = Stopwatch.StartNew();
        using var activity = _host.Activities.Start(name, ActivityKinds.Command, cancellable: true);
        _running[name] = activity;
        try
        {
            CommandStart start;
            try
            {
                start = await Task.Run(() => sandbox.StartAsync(command, folder, limits, activity.CancellationToken).AsTask(), CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return new NpmRunOutcome(NpmRunEnd.Stopped, null, "cancelled before it started");
            }

            if (start.Refusal is { } refused)
            {
                activity.Complete("not run");
                _host.Notify("npm: not run", refused);
                return new NpmRunOutcome(NpmRunEnd.NotRun, null, refused);
            }

            await using var session = start.Session!;
            using var registration = activity.CancellationToken.Register(session.Stop);
            var tab = _host.Terminals.OpenProgram(new EditorProgramRequest(name, session.Input, session.Output)
            {
                Error = session.Error,
                Stop = session.Stop,
                Folder = folder,
                Header = $"\e[2m{(package.Folder.Length == 0 ? "the workspace" : package.Folder)}\e[0m\n\e[1m$ {header}\e[0m\n",
            });
            if (tab is null)
            {
                session.Stop();
                activity.Complete("not run");
                return new NpmRunOutcome(NpmRunEnd.NotRun, null, "This window has no terminal to take the script's input.");
            }

            tab.Show();
            var result = await session.Completion.ConfigureAwait(true);
            try
            {
                await tab.OutputEnded.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
            }

            string elapsed = Elapsed(clock.Elapsed);
            var (end, text, code) = result switch
            {
                { Stopped: true } => (NpmRunEnd.Stopped, "stopped, with its process tree", -1),
                { TimedOut: true } => (NpmRunEnd.TimedOut, $"stopped at its time limit ({Elapsed(limits.Time)})", -1),
                { OutOfMemory: true } => (NpmRunEnd.Stopped, "stopped at its memory limit", -1),
                { Succeeded: true } => (NpmRunEnd.Succeeded, $"exit code 0 · {elapsed}", 0),
                _ => (NpmRunEnd.Failed, string.Create(Culture, $"exit code {result.ExitCode} · {elapsed}"), result.ExitCode ?? -1),
            };
            tab.End($"\n{(end == NpmRunEnd.Succeeded ? "\e[32m" : "\e[33m")}{text}\e[0m\n", code);
            if (end is NpmRunEnd.Succeeded or NpmRunEnd.Stopped)
            {
                activity.Complete(text);
            }
            else
            {
                activity.Fail(text);
            }

            return new NpmRunOutcome(end, end == NpmRunEnd.Succeeded || end == NpmRunEnd.Failed ? result.ExitCode : null, end == NpmRunEnd.Succeeded ? null : text);
        }
        finally
        {
            _running.Remove(name);
        }
    }

    private NpmRunOutcome NotRun(string why, IEditorTerminalOutput? output)
    {
        if (output is not null)
        {
            output.Append($"\e[33mnot run: {why}\e[0m\n");
            output.Show();
        }

        _host.Notify("npm: not run", why, NotificationSeverity.Warning);
        return new NpmRunOutcome(NpmRunEnd.NotRun, null, why);
    }

    private static string Elapsed(TimeSpan time) =>
        time == Timeout.InfiniteTimeSpan ? "none"
        : time.TotalMinutes >= 1 ? string.Create(Culture, $"{(int)time.TotalMinutes}:{time.Seconds:00} min")
        : string.Create(Culture, $"{time.TotalSeconds:0.0} s");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAll();
        _disposed = true;
        Deactivate();
        _missing?.Dispose();
        _ready.TrySetResult();
        _scanned.TrySetResult();
    }

    /// <summary>
    /// A run's output lines from the sandbox's reader thread: kept, and shown in one append per UI turn (never a dispatch per
    /// line); the activity shows the elapsed time and the last line.
    /// </summary>
    private sealed class LineBatch(IEditorHost host, IEditorTerminalOutput? output, IActivity activity, Stopwatch clock)
    {
        private readonly Lock _gate = new();
        private readonly List<string> _lines = [];
        private TaskCompletionSource? _flushed;
        private bool _pending;

        public void Add(string line)
        {
            bool schedule;
            lock (_gate)
            {
                _lines.Add(line);
                schedule = !_pending;
                _pending = true;
            }

            if (schedule)
            {
                host.Dispatch("npm: a run's output", Flush);
            }
        }

        // Completes once every line given so far is shown.
        public Task FlushedAsync()
        {
            lock (_gate)
            {
                if (!_pending)
                {
                    return Task.CompletedTask;
                }

                _flushed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return _flushed.Task;
            }
        }

        private void Flush()
        {
            string text;
            string last;
            TaskCompletionSource? flushed;
            lock (_gate)
            {
                _pending = false;
                flushed = _flushed;
                _flushed = null;
                if (_lines.Count == 0)
                {
                    flushed?.TrySetResult();
                    return;
                }

                text = string.Join('\n', _lines) + "\n";
                last = _lines[^1];
                _lines.Clear();
            }

            output?.Append(text);
            string plain = Plain(last);
            var elapsed = clock.Elapsed;
            activity.Report(null, string.Create(Culture, $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00} · {(plain.Length <= 80 ? plain : string.Concat(plain.AsSpan(0, 79), "…"))}"));
            flushed?.TrySetResult();
        }

        // The line without its colour sequences (ESC [ … letter), for the status bar.
        private static string Plain(string line)
        {
            if (!line.Contains('\e', StringComparison.Ordinal))
            {
                return line.Trim();
            }

            var text = new System.Text.StringBuilder(line.Length);
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\e' && i + 1 < line.Length && line[i + 1] == '[')
                {
                    i += 2;
                    while (i < line.Length && !char.IsAsciiLetter(line[i]))
                    {
                        i++;
                    }

                    continue;
                }

                text.Append(line[i]);
            }

            return text.ToString().Trim();
        }
    }

    private sealed class Undo(Action undo) : IDisposable
    {
        private Action? _undo = undo;

        public void Dispose() => Interlocked.Exchange(ref _undo, null)?.Invoke();
    }
}
