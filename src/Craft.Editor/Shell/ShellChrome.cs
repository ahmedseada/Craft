// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Shell;

/// <summary>A row of an open menu: its title, its chord, the command it runs, and whether a line comes before it.</summary>
/// <param name="Title">What it shows.</param>
/// <param name="Chord">The chord that runs it, or null.</param>
/// <param name="CommandId">The command.</param>
/// <param name="StartsGroup">Whether a line separates it from the row before (a new group).</param>
/// <param name="Enabled">Whether the command can run now.</param>
public sealed record MenuRow(string Title, string? Chord, string CommandId, bool StartsGroup, bool Enabled);

/// <summary>The title bar's menus as rows (<see cref="EditorMenus"/> over <see cref="EditorCommands"/>).</summary>
public static class ShellMenus
{
    /// <summary>The chord a key binding gives <paramref name="commandId"/> (the first one), or null.</summary>
    public static string? ChordOf(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        foreach (var binding in EditorKeyBindings.All)
        {
            if (binding.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase))
            {
                return binding.Name;
            }
        }

        return null;
    }

    /// <summary>The title a menu shows for <paramref name="command"/>: its title without the palette's "Area: " prefix.</summary>
    public static string MenuTitle(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var title = command.Title;
        int colon = title.IndexOf(": ", StringComparison.Ordinal);
        return colon > 0 && colon < 24 ? title[(colon + 2)..] : title;
    }

    /// <summary>
    /// The rows of <paramref name="menu"/>: its items whose command is registered, by group and order, each with its
    /// chord (a binding's, else the item's <see cref="MenuItem.Shortcut"/>) and whether it can run in <paramref name="host"/>.
    /// </summary>
    public static IReadOnlyList<MenuRow> Rows(string menu, IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var rows = new List<MenuRow>();
        int? group = null;
        foreach (var item in EditorMenus.ItemsOf(menu))
        {
            if (EditorCommands.Find(item.CommandId) is not { } command)
            {
                continue;
            }

            bool enabled;
            try
            {
                enabled = command.CanExecute(host);
            }
#pragma warning disable CA1031 // A command whose check throws shows disabled, never a crash.
            catch (Exception)
#pragma warning restore CA1031
            {
                enabled = false;
            }

            rows.Add(new MenuRow(item.Title ?? MenuTitle(command), ChordOf(command.Name) ?? item.Shortcut, command.Name, group is not null && group != item.Group, enabled));
            group = item.Group;
        }

        return rows;
    }
}

/// <summary>The state of a language's semantic layer as the shell shows it (the explorer's card, the status bar).</summary>
/// <param name="Engine">What computes it ("Roslyn"), or the language's name.</param>
/// <param name="State">"Ready", "Loading", "Waiting" (a large file waits for the command) or "Off" (no semantic layer).</param>
/// <param name="Projects">"7 projects", or "".</param>
/// <param name="Memory">"412 MB", or "".</param>
/// <param name="IsReady">Whether it is ready.</param>
public sealed record SemanticCard(string Engine, string State, string Projects, string Memory, bool IsReady)
{
    /// <summary>The status bar's text: "Roslyn ready".</summary>
    public string StatusText => $"{Engine} {State.ToLowerInvariant()}";
}

/// <summary>The texts of the shell's chrome, made from what the editor holds (testable without a UI).</summary>
public static class ShellText
{
    /// <summary>"Spaces: 4" or "Tab size: 4".</summary>
    public static string Indentation(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.Create(CultureInfo.InvariantCulture, $"{(options.InsertSpaces ? "Spaces" : "Tab size")}: {options.TabSize}");
    }

    /// <summary>"CRLF", "LF", "CR" for a line break.</summary>
    public static string LineEnding(string lineEnding) => lineEnding switch { "\r\n" => "CRLF", "\n" => "LF", "\r" => "CR", var other => other };

    /// <summary>"UTF-8", "UTF-8 with BOM", "UTF-16LE".</summary>
    public static string EncodingName(Encoding encoding, bool hasBom)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        string name = encoding.WebName.ToUpperInvariant();
        return hasBom ? name + " with BOM" : name;
    }

    /// <summary>"Ln 22, Col 33" (one-based).</summary>
    public static string Position(int line, int column) => string.Create(CultureInfo.InvariantCulture, $"Ln {line + 1}, Col {column + 1}");

    /// <summary>A size as the design writes it: "412 MB", "3.1 GB", "840 KB".</summary>
    public static string Bytes(long bytes)
    {
        const double Kilo = 1024, Mega = Kilo * 1024, Giga = Mega * 1024;
        return bytes >= Giga ? string.Create(CultureInfo.InvariantCulture, $"{bytes / Giga:0.0} GB")
            : bytes >= Mega ? string.Create(CultureInfo.InvariantCulture, $"{bytes / Mega:0} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, bytes) / Kilo:0} KB");
    }

    /// <summary>The semantic layer's card for <paramref name="model"/> (its session's telemetry), or null when the language has none.</summary>
    public static SemanticCard? Semantic(EditorModel? model)
    {
        if (model?.SemanticSession is not { } session)
        {
            return null;
        }

        var telemetry = session.Telemetry;
        string engine = telemetry.Engine ?? model.LanguageName;
        if (session.IsSemanticDeferred)
        {
            return new SemanticCard(engine, "Waiting", "a large file: run 'Enable semantics'", "", false);
        }

        if (!telemetry.IsBuilt)
        {
            return new SemanticCard(engine, "Loading", "", "", false);
        }

        string projects = string.Create(CultureInfo.InvariantCulture, $"{telemetry.Projects} {(telemetry.Projects == 1 ? "project" : "projects")}");
        return new SemanticCard(engine, "Ready", projects, Bytes(telemetry.MemoryBytes), true);
    }

    /// <summary>"⚠ 1" / "✕ 2 ⚠ 1": the errors and warnings, or "" with none.</summary>
    public static string Problems(int errors, int warnings)
    {
        if (errors == 0 && warnings == 0)
        {
            return "";
        }

        var text = new StringBuilder();
        if (errors > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"✕ {errors}");
        }

        if (warnings > 0)
        {
            text.Append(errors > 0 ? "  " : "").Append(CultureInfo.InvariantCulture, $"⚠ {warnings}");
        }

        return text.ToString();
    }

    /// <summary>
    /// The breadcrumb's parts: the file's folder within the workspace (its last folder; the path when outside one), the
    /// file's name, then the language's scopes (the types and the member around the caret).
    /// </summary>
    public static IReadOnlyList<ScopeName> Breadcrumb(string? filePath, string? workspaceRoot, IReadOnlyList<ScopeName> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var parts = new List<ScopeName>(scopes.Count + 2);
        if (filePath is not null)
        {
            string? folder = Path.GetDirectoryName(filePath);
            if (folder is not null)
            {
                string shown = workspaceRoot is not null && folder.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))
                    : folder;
                if (shown.Length > 0)
                {
                    parts.Add(new ScopeName(shown, ScopeKind.Namespace));
                }
            }

            parts.Add(new ScopeName(Path.GetFileName(filePath), ScopeKind.Namespace));
        }

        parts.AddRange(scopes);
        return parts;
    }

    /// <summary>The model chip's text: "store:coder-q4 · vulkan:0", or "no model" when no provider has a chat model.</summary>
    public static string ModelChip(ModelProviderStatus? status)
    {
        if (status?.ChatModel is not { } model)
        {
            return "no model";
        }

        return status.Device is { } device ? $"{model} · {device}" : model;
    }

    /// <summary>The status bar's model text: "local · vulkan:0 · 3.1 GB in use · 41 tok/s", the parts it measured; "" with no model.</summary>
    public static string ModelUsage(ModelProviderStatus? status)
    {
        if (status?.ChatModel is null)
        {
            return "";
        }

        var parts = new List<string>(4) { status.Where.Length > 0 ? status.Where : "model" };
        if (status.Device is { } device)
        {
            parts.Add(device);
        }

        if (status.DeviceMemoryBytes is { } memory)
        {
            parts.Add(Bytes(memory) + " in use");
        }

        if (status.TokensPerSecond is { } speed)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{speed:0} tok/s"));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// <paramref name="status"/> with what Idrak's telemetry measured (<see cref="ModelTelemetry"/>: the telemetry plug-in's)
    /// where the provider measured nothing itself: the speed, the first token, the device memory.
    /// </summary>
    public static ModelProviderStatus WithMeasures(ModelProviderStatus status, ModelMeasures measures)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(measures);
        if (ReferenceEquals(measures, ModelMeasures.None) || status.TokensPerSecond is not null && status.FirstToken is not null && status.DeviceMemoryBytes is not null)
        {
            return status;
        }

        return status with
        {
            TokensPerSecond = status.TokensPerSecond ?? measures.TokensPerSecond,
            FirstToken = status.FirstToken ?? measures.FirstToken,
            DeviceMemoryBytes = status.DeviceMemoryBytes ?? measures.DeviceMemoryBytes,
        };
    }

    /// <summary>The first registered model provider's status (the one the shell shows), or null when none is registered.</summary>
    public static (IModelProvider Provider, ModelProviderStatus Status)? ModelStatus()
    {
        foreach (var provider in ModelProviders.All)
        {
            return (provider, WithMeasures(provider.Status, ModelTelemetry.Current));
        }

        return null;
    }
}

/// <summary>
/// The branch of a folder's git repository, read from its <c>.git</c> folder as bytes (no git program): the folder or a
/// parent holding <c>.git</c> (a folder, or a file "gitdir: PATH" as a worktree or submodule has), its <c>HEAD</c>
/// "ref: refs/heads/NAME" gives NAME; a detached HEAD gives its commit's first seven characters.
/// </summary>
public static class GitBranch
{
    private const int MaxHeadBytes = 4096;
    private static readonly System.Buffers.SearchValues<byte> HexDigits = System.Buffers.SearchValues.Create("0123456789abcdef"u8);

    /// <summary>The branch of the repository holding <paramref name="folder"/>, or null when it is in none.</summary>
    public static string? Read(string? folder)
    {
        for (var current = folder is null ? null : new DirectoryInfo(Path.GetFullPath(folder)); current is not null; current = current.Parent)
        {
            string git = Path.Combine(current.FullName, ".git");
            try
            {
                if (Directory.Exists(git))
                {
                    return Head(Path.Combine(git, "HEAD"));
                }

                if (File.Exists(git))
                {
                    return GitDir(File.ReadAllBytes(git), current.FullName) is { } dir ? Head(Path.Combine(dir, "HEAD")) : null;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>The branch a HEAD file's bytes name, or the detached commit's short hash, or null.</summary>
    public static string? FromHead(ReadOnlySpan<byte> head)
    {
        head = head.Trim("\r\n \t"u8);
        ReadOnlySpan<byte> prefix = "ref: refs/heads/"u8;
        if (head.StartsWith(prefix))
        {
            return Encoding.UTF8.GetString(head[prefix.Length..]);
        }

        if (head.StartsWith("ref: "u8))
        {
            return Encoding.UTF8.GetString(head[5..]);
        }

        return head.Length >= 7 && head.IndexOfAnyExcept(HexDigits) < 0 ? Encoding.UTF8.GetString(head[..7]) : null;
    }

    private static string? Head(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> buffer = stackalloc byte[256];
        int read = stream.Read(buffer);
        return read <= 0 || stream.Length > MaxHeadBytes ? null : FromHead(buffer[..read]);
    }

    private static string? GitDir(ReadOnlySpan<byte> file, string folder)
    {
        file = file.Trim("\r\n \t"u8);
        ReadOnlySpan<byte> prefix = "gitdir:"u8;
        if (!file.StartsWith(prefix))
        {
            return null;
        }

        string dir = Encoding.UTF8.GetString(file[prefix.Length..].Trim(" \t"u8));
        return Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(folder, dir));
    }
}

/// <summary>A measure the Idrak telemetry tab lists: the metric, where it came from, its last value and how many there were.</summary>
/// <param name="Source">Who recorded it (a plug-in's name, or "host").</param>
/// <param name="Metric">The metric ("ai.model.load").</param>
/// <param name="Last">Its last value.</param>
/// <param name="Unit">Its unit.</param>
/// <param name="Count">How many were recorded.</param>
/// <param name="Mean">Their mean.</param>
public sealed record TelemetryRow(string Source, string Metric, double Last, string Unit, int Count, double Mean)
{
    /// <summary>"ai.model.load  4,210 ms (last), 1 recorded".</summary>
    public string Text => string.Create(CultureInfo.InvariantCulture, $"{Source} · {Metric}  {Last:0.###} {Unit}  (mean {Mean:0.###} over {Count})");
}

/// <summary>
/// The measures recorded through the host (<see cref="IEditorHost.Record"/>, the plug-ins' and the library's telemetry),
/// one row per source and metric: memory grows with the metrics' names, never with the measures.
/// </summary>
public sealed class TelemetryLog : IDisposable
{
    private readonly EditorHost _host;
    private readonly Dictionary<(string Source, string Metric), int> _index = [];
    private readonly List<TelemetryRow> _rows = [];

    /// <summary>The log of <paramref name="host"/>'s measures, from now on.</summary>
    public TelemetryLog(EditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        host.Recorded += OnRecorded;
    }

    /// <summary>The rows, in the order their metrics first came.</summary>
    public IReadOnlyList<TelemetryRow> Rows => _rows;

    /// <summary>Raised after a measure changed the rows (on the thread the host recorded it on).</summary>
    public event EventHandler? Changed;

    /// <summary>Adds a measure.</summary>
    public void Add(string source, string metric, double value, string unit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(metric);
        if (_index.TryGetValue((source, metric), out int at))
        {
            var row = _rows[at];
            int count = row.Count + 1;
            _rows[at] = row with { Last = value, Unit = unit, Count = count, Mean = row.Mean + ((value - row.Mean) / count) };
        }
        else
        {
            _index[(source, metric)] = _rows.Count;
            _rows.Add(new TelemetryRow(source, metric, value, unit, 1, value));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose() => _host.Recorded -= OnRecorded;

    private void OnRecorded(object? sender, EditorMeasureEventArgs e) => Add(e.Source, e.Metric, e.Value, e.Unit);
}

/// <summary>
/// The Edit menu's commands (undo, redo, cut, copy, paste, select all, find, replace, format, go to definition), each the
/// editor's own key sent to the active editor, so the menu and the keys do one thing. Registered on the public contracts;
/// disposing takes them back.
/// </summary>
public sealed class ShellEditCommands : IDisposable
{
    /// <summary>The commands: id, title, key, modifiers, and the shortcut the menu shows.</summary>
    public static IReadOnlyList<(string Id, string Title, EditorKey Key, KeyModifiers Modifiers, string Shortcut)> Keys { get; } =
    [
        ("craft.undo", "Edit: Undo", EditorKey.Z, KeyModifiers.Control, "Ctrl+Z"),
        ("craft.redo", "Edit: Redo", EditorKey.Y, KeyModifiers.Control, "Ctrl+Y"),
        ("craft.cut", "Edit: Cut", EditorKey.X, KeyModifiers.Control, "Ctrl+X"),
        ("craft.copy", "Edit: Copy", EditorKey.C, KeyModifiers.Control, "Ctrl+C"),
        ("craft.paste", "Edit: Paste", EditorKey.V, KeyModifiers.Control, "Ctrl+V"),
        ("craft.selectAll", "Edit: Select all", EditorKey.A, KeyModifiers.Control, "Ctrl+A"),
        ("craft.find", "Edit: Find", EditorKey.F, KeyModifiers.Control, "Ctrl+F"),
        ("craft.replace", "Edit: Replace", EditorKey.H, KeyModifiers.Control, "Ctrl+H"),
        ("craft.formatDocument", "Edit: Format document", EditorKey.F, KeyModifiers.Shift | KeyModifiers.Alt, "Shift+Alt+F"),
        ("craft.goToDefinition", "Edit: Go to definition", EditorKey.F12, KeyModifiers.None, "F12"),
    ];

    private readonly List<string> _registered = [];

    /// <summary>Registers the commands over <paramref name="active"/> (the active editor, or null) and their Edit menu items.</summary>
    public ShellEditCommands(Func<EditorModel?> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        int order = 0;
        foreach (var (id, title, key, modifiers, shortcut) in Keys)
        {
            EditorCommands.Register(new EditorCommand(id, title, (_, _) =>
            {
                active()?.OnKey(key, modifiers);
                return Task.CompletedTask;
            }, _ => active() is not null));
            EditorMenus.Register(new MenuItem(EditorMenus.Edit, id, Order: order++, Group: order <= 2 ? 0 : order <= 6 ? 1 : order <= 8 ? 2 : 3, Shortcut: shortcut));
            _registered.Add(id);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var id in _registered)
        {
            EditorMenus.Unregister(EditorMenus.Edit, id);
            EditorCommands.Unregister(id);
        }

        _registered.Clear();
    }
}
