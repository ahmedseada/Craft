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
    /// chord (the keys in effect, <see cref="EditorHost.KeysOf"/>, so a remap shows; else the item's <see cref="MenuItem.Shortcut"/>)
    /// and whether it can run in <paramref name="host"/>.
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

            string? chord = host is EditorHost editorHost ? editorHost.KeysOf(command.Name) : ChordOf(command.Name);
            rows.Add(new MenuRow(item.Title ?? MenuTitle(command), chord ?? item.Shortcut, command.Name, group is not null && group != item.Group, enabled));
            group = item.Group;
        }

        return rows;
    }
}

/// <summary>The state of a language's semantic layer as the shell shows it (the status bar while it loads or failed, the language item's tooltip, B10's notification).</summary>
/// <param name="Engine">What computes it ("Roslyn"), or the language's name.</param>
/// <param name="State">"Ready", "Loading", "Waiting" (a large file waits for the command), or "Failed" (its workspace could not be built).</param>
/// <param name="Projects">"7 projects", or "".</param>
/// <param name="Memory">"412 MB", or "".</param>
/// <param name="IsReady">Whether it is ready.</param>
public sealed record SemanticCard(string Engine, string State, string Projects, string Memory, bool IsReady)
{
    /// <summary>The state as one phrase: "Roslyn ready".</summary>
    public string StatusText => $"{Engine} {State.ToLowerInvariant()}";

    /// <summary>What the build of its workspace took ("2.3 s"), or "".</summary>
    public string BuildTime { get; init; } = "";

    /// <summary>Why its workspace failed (the error's words), or null.</summary>
    public string? Failure { get; init; }

    /// <summary>Whether its workspace failed to build.</summary>
    public bool IsFailed => State == "Failed";

    /// <summary>
    /// The status bar's text (B10): only while the workspace loads ("Roslyn loading…") or when it failed ("Roslyn
    /// failed"); nothing once it is ready (the notification centre said so) or while a large file waits.
    /// </summary>
    public string StatusBarText => State switch
    {
        "Loading" => $"{Engine} loading…",
        "Failed" => $"{Engine} failed",
        _ => "",
    };

    /// <summary>The language item's tooltip: the facts the explorer's card held ("Roslyn: 1 project, 6 MB, built in 2.3 s").</summary>
    public string Tooltip => State switch
    {
        "Ready" => string.Join(", ", new[] { Projects, Memory, BuildTime.Length > 0 ? "built in " + BuildTime : "" }.Where(p => p.Length > 0)) is { Length: > 0 } facts
            ? $"{Engine}: {facts}" : $"{Engine}: ready",
        "Failed" => $"{Engine}: the workspace could not be built: {Failure}",
        "Waiting" => $"{Engine}: {Projects}",
        _ => $"{Engine}: loading the workspace",
    };
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
            return telemetry.Failure is { } failure
                ? new SemanticCard(engine, "Failed", "", "", false) { Failure = failure }
                : new SemanticCard(engine, "Loading", "", "", false);
        }

        string projects = string.Create(CultureInfo.InvariantCulture, $"{telemetry.Projects} {(telemetry.Projects == 1 ? "project" : "projects")}");
        return new SemanticCard(engine, "Ready", projects, Bytes(telemetry.MemoryBytes), true) { BuildTime = Seconds(telemetry.BuildTime) };
    }

    /// <summary>A duration as the shell writes it: "2.3 s", "41 s", "1 min 4 s", "120 ms".</summary>
    public static string Seconds(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
        {
            return "";
        }

        return time.TotalSeconds < 1 ? string.Create(CultureInfo.InvariantCulture, $"{time.TotalMilliseconds:0} ms")
            : time.TotalSeconds < 10 ? string.Create(CultureInfo.InvariantCulture, $"{time.TotalSeconds:0.0} s")
            : time.TotalMinutes < 1 ? string.Create(CultureInfo.InvariantCulture, $"{time.TotalSeconds:0} s")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes} min {time.Seconds} s");
    }

    /// <summary>
    /// The diagnostic among <paramref name="current"/> (the editor's now, in the current text) that a Problems row listed
    /// earlier as <paramref name="listed"/> stands for: the same id and message, the nearest to where it was; null when it
    /// is gone. A row's code action then runs on the problem as it is, never on a span of an older text.
    /// </summary>
    public static EditorDiagnostic? CurrentProblem(ReadOnlySpan<EditorDiagnostic> current, EditorDiagnostic listed)
    {
        EditorDiagnostic? best = null;
        long bestDistance = long.MaxValue;
        foreach (var diagnostic in current)
        {
            if (diagnostic.Id == listed.Id && diagnostic.Message == listed.Message && diagnostic.Severity == listed.Severity)
            {
                long distance = Math.Abs((long)diagnostic.Start - listed.Start);
                if (distance < bestDistance)
                {
                    best = diagnostic;
                    bestDistance = distance;
                }
            }
        }

        return best;
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

    /// <summary>
    /// The model chip's text for the role <paramref name="role"/> (<see cref="ModelRoleKeys"/>; null: the chat's):
    /// "store:coder-q4 · vulkan:0", "same as chat: store:coder-q4 · vulkan:0" when the agent takes the chat's model and
    /// device, or "no model" when the role has none.
    /// </summary>
    public static string ModelChip(ModelProviderStatus? status, string? role = null)
    {
        var found = status?.RoleOf(role);
        string? model = found is null ? status?.ChatModel : found.Model;
        if (model is null)
        {
            return "no model";
        }

        string text = PlannedDevice(status, role) is { } device ? $"{model} · {device}" : model;
        if (found is { Inherits: { } other, InheritsModel: true })
        {
            return found.InheritsDevice ? $"same as {other.ToLowerInvariant()}: {text}" : $"{model} (same as {other.ToLowerInvariant()}) · {PlannedDevice(status, role)}".TrimEnd(' ', '·');
        }

        return found is { Enabled: false } ? text + " · off" : text;
    }

    /// <summary>
    /// The device to show for the role <paramref name="role"/> of <paramref name="status"/> (null: the chat's): the one
    /// its model runs on once loaded, else the one its setting names ("cpu"), else what "auto" picks on this machine marked
    /// so ("cuda:0 (auto)"), else null (the owner's note 6: the GPU shows before the first load). A provider that gives no
    /// roles' devices answers with its own (the device in use, its device setting).
    /// </summary>
    public static string? PlannedDevice(ModelProviderStatus? status, string? role = null)
    {
        if (status is null)
        {
            return null;
        }

        var found = status.RoleOf(role);
        bool own = found?.Key is not null;
        if ((own ? found!.Device : status.Device) is { } device)
        {
            return device;
        }

        string? choice = own ? found!.DeviceChoice : status.DeviceChoice;
        if (choice is { Length: > 0 } && !choice.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return choice;
        }

        return status.AutoDevice is { } auto ? auto + " (auto)" : null;
    }

    /// <summary>
    /// The composer's model picker for the role <paramref name="role"/> of <paramref name="status"/> (the owner's notes 11
    /// and 25; null: the chat's): "Same as chat" first for a role that inherits (the agent), the models (the role's marked),
    /// then the devices to run on (the role's choice marked, "auto" saying what it picks), then the Models and devices page.
    /// </summary>
    public static IReadOnlyList<ModelPick> ModelPicks(ModelProviderStatus? status, string? role = null)
    {
        var picks = new List<ModelPick>();
        if (status is null)
        {
            picks.Add(new ModelPick(ModelPickKind.Page, "", "No model provider is installed: open Models and devices…"));
            return picks;
        }

        var found = status.RoleOf(role);
        bool own = found?.Key is not null;
        string? model = found is null ? status.ChatModel : found.Model;
        string? inherits = found?.Inherits;
        if (inherits is not null)
        {
            string label = $"Same as {inherits.ToLowerInvariant()}";
            picks.Add(new ModelPick(ModelPickKind.Model, "", found!.InheritsModel ? "✓ " + label : label));
        }

        foreach (var name in status.Models)
        {
            bool current = model is not null && name.Equals(model, StringComparison.OrdinalIgnoreCase) && found is not { InheritsModel: true };
            picks.Add(new ModelPick(ModelPickKind.Model, name, current ? $"✓ {name}" : name));
        }

        if ((own ? found!.DeviceSetting : status.DeviceSetting) is not null)
        {
            if (inherits is not null)
            {
                string label = $"Run on the {inherits.ToLowerInvariant()}'s device";
                picks.Add(new ModelPick(ModelPickKind.Device, "", found!.InheritsDevice ? "✓ " + label : label));
            }

            string choice = (own ? found!.DeviceChoice : status.DeviceChoice) is { Length: > 0 } set ? set : "auto";
            foreach (var device in status.Devices)
            {
                bool chosen = device.Equals(choice, StringComparison.OrdinalIgnoreCase) && found is not { InheritsDevice: true };
                string label = device.Equals("auto", StringComparison.OrdinalIgnoreCase) && status.AutoDevice is { } auto
                    ? $"Run on auto (the fastest found: {auto})"
                    : $"Run on {device}";
                picks.Add(new ModelPick(ModelPickKind.Device, device, chosen ? "✓ " + label : label));
            }
        }

        picks.Add(new ModelPick(ModelPickKind.Page, "", status.Models.Count == 0 ? "Add a model… (Models and devices)" : "Another model… (Models and devices)"));
        return picks;
    }

    /// <summary>
    /// What picking <paramref name="pick"/> at the chip of the role <paramref name="role"/> writes (note 25: each chip its
    /// own role's settings): the variable, its value (null clears it: the default, or for a role that inherits the other
    /// role's), and the command that loads the role's model after a model pick; null for a pick that writes nothing (the page).
    /// </summary>
    public static ModelPickWrite? WriteOf(ModelProviderStatus? status, string? role, ModelPick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (status is null)
        {
            return null;
        }

        var found = status.RoleOf(role);
        bool own = found?.Key is not null;
        bool inherits = found?.Inherits is not null;
        switch (pick.Kind)
        {
            case ModelPickKind.Model when (own ? found!.ModelSetting : status.ModelSetting) is { } setting:
                return new ModelPickWrite(setting.Variable, pick.Value.Length == 0 ? null : pick.Value, own ? found!.LoadCommand ?? status.LoadCommand : status.LoadCommand);
            case ModelPickKind.Device when (own ? found!.DeviceSetting : status.DeviceSetting) is { } setting:
                // "auto" is the default of a role's own device; for a role that inherits, an empty one is the other's, so
                // auto is written as such.
                string? value = pick.Value.Length == 0 || (!inherits && pick.Value.Equals("auto", StringComparison.OrdinalIgnoreCase)) ? null : pick.Value;
                return new ModelPickWrite(setting.Variable, value, null);
            default:
                return null;
        }
    }

    /// <summary>
    /// The status bar's model item's tooltip (note 25): each role's model, the device it runs on once loaded (else what
    /// it would take), and its state, one line each; what to do when no provider is installed.
    /// </summary>
    public static string ModelRolesTooltip(ModelProviderStatus? status)
    {
        if (status is null)
        {
            return "No model provider is installed (the AI plug-in brings \"local\").";
        }

        var lines = new List<string>(status.Roles.Count + 1);
        foreach (var role in status.Roles)
        {
            string device = role.Device is { } running ? " on " + running
                : role.Key is not null && role.Model is not null && PlannedDevice(status, role.Key) is { } planned ? " · " + planned
                : "";
            lines.Add(!role.Enabled ? $"{role.Role}: off"
                : $"{role.Role}: {role.Model ?? "none set"}{device} · {role.State}{(role.InheritsModel && role.Inherits is { } other ? $" (same as {other.ToLowerInvariant()})" : "")}");
        }

        lines.Add("Pick a model with the chip under the chat's or the agent's input; each role's settings are on the settings page.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The status bar's model text: "local · vulkan:0 · 3.1 GB in use · 41 tok/s", the parts it measured; "" with no model.</summary>
    public static string ModelUsage(ModelProviderStatus? status)
    {
        if (status?.ChatModel is null)
        {
            return "";
        }

        var parts = new List<string>(4) { status.Where.Length > 0 ? status.Where : "model" };
        if (PlannedDevice(status) is { } device)
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

    /// <summary>The first registered model provider's status (the one the shell shows), or null when none is registered.</summary>
    public static (IModelProvider Provider, ModelProviderStatus Status)? ModelStatus()
    {
        foreach (var provider in ModelProviders.All)
        {
            return (provider, provider.Status);
        }

        return null;
    }
}

/// <summary>What a row of the composer's model picker does.</summary>
public enum ModelPickKind
{
    /// <summary>Makes <see cref="ModelPick.Value"/> the role's model (the role's model setting; empty: the other role's) and loads it.</summary>
    Model,

    /// <summary>Makes <see cref="ModelPick.Value"/> the role's device to run on (its device setting; empty: the other role's).</summary>
    Device,

    /// <summary>Opens the Models and devices page.</summary>
    Page,
}

/// <summary>What a pick at the composer's model chip writes (<see cref="ShellText.WriteOf"/>).</summary>
/// <param name="Variable">The setting's variable ("CRAFT_PLUGIN_AI_AGENTMODEL").</param>
/// <param name="Value">Its new value, or null to clear it (its default; for a role that inherits, the other role's).</param>
/// <param name="LoadCommand">The command that loads the role's model after the write, or null.</param>
public sealed record ModelPickWrite(string Variable, string? Value, string? LoadCommand);

/// <summary>A row of the composer's model picker (<see cref="ShellText.ModelPicks"/>).</summary>
/// <param name="Kind">What it does.</param>
/// <param name="Value">The model's or the device's name ("" for the page).</param>
/// <param name="Label">Its text.</param>
public sealed record ModelPick(ModelPickKind Kind, string Value, string Label);

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

/// <summary>
/// The Edit menu's commands (undo, redo, cut, copy, paste, select all, find, replace, format, go to definition), each the
/// editor's own key sent to the active editor, so the menu and the keys do one thing; the editor's line commands and the
/// standard shortcuts (<see cref="StandardShortcuts"/>: toggle comment, move and copy lines, select line, expand the
/// selection, quick fix) with their bindings, and the Visual Studio keymap. Registered on the public contracts;
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

    /// <summary>More of the editor's own keys as commands, for the palette and the keyboard page (not in the menu).</summary>
    public static IReadOnlyList<(string Id, string Title, EditorKey Key, KeyModifiers Modifiers)> MoreKeys { get; } =
    [
        ("craft.findNext", "Edit: Find next", EditorKey.F3, KeyModifiers.None),
        ("craft.findPrevious", "Edit: Find previous", EditorKey.F3, KeyModifiers.Shift),
        ("craft.triggerCompletion", "Edit: Trigger completion", EditorKey.Space, KeyModifiers.Control),
        ("craft.parameterHints", "Edit: Parameter hints", EditorKey.Space, KeyModifiers.Control | KeyModifiers.Shift),
    ];

    /// <summary>The editor's context menu items these commands give: the command, its group and its order.</summary>
    public static IReadOnlyList<(string Id, int Group, int Order)> EditorContextItems { get; } =
    [
        ("craft.cut", 0, 0), ("craft.copy", 0, 1), ("craft.paste", 0, 2),
        ("craft.goToDefinition", 1, 0),
        ("craft.formatDocument", 2, 0), (StandardShortcuts.QuickFix, 2, 1),
    ];

    /// <summary>The read-only text's context menu items (<see cref="EditorContextMenus.ReadOnlyText"/>: Output's): copy, select all, find.</summary>
    public static IReadOnlyList<(string Id, int Group, int Order)> ReadOnlyTextItems { get; } =
    [
        ("craft.copy", 0, 0), ("craft.selectAll", 0, 1), ("craft.find", 1, 0),
    ];

    private readonly List<string> _registered = [];
    private readonly List<(string Menu, string Id)> _menus = [];
    private readonly List<KeyBinding> _bindings = [];
    private readonly Func<EditorModel?> _active;
    private readonly ShellModel? _shell;
    private bool _keyMapRegistered;

    /// <summary>
    /// Registers the commands over <paramref name="active"/> (the active editor, or null) and their Edit menu items, the
    /// standard bindings and the Visual Studio keymap; with <paramref name="shell"/>, quick fix too (it asks through the shell's UI).
    /// </summary>
    public ShellEditCommands(Func<EditorModel?> active, ShellModel? shell = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        _active = active;
        _shell = shell;
        int order = 0;
        foreach (var (id, title, key, modifiers, shortcut) in Keys)
        {
            Register(id, title, model => model.OnKey(key, modifiers));
            EditorMenus.Register(new MenuItem(EditorMenus.Edit, id, Order: order++, Group: order <= 2 ? 0 : order <= 6 ? 1 : order <= 8 ? 2 : 3, Shortcut: shortcut));
            _menus.Add((EditorMenus.Edit, id));
        }

        foreach (var (id, title, key, modifiers) in MoreKeys)
        {
            Register(id, title, model => model.OnKey(key, modifiers));
        }

        int lineOrder = 0;
        foreach (var (id, title, run, inMenu) in StandardShortcuts.LineCommands)
        {
            Register(id, title, run);
            if (inMenu)
            {
                EditorMenus.Register(new MenuItem(EditorMenus.Edit, id, Order: lineOrder++, Group: 4));
                _menus.Add((EditorMenus.Edit, id));
            }
        }

        foreach (var command in ComposerInput.Commands)
        {
            EditorCommands.Register(command);
            _registered.Add(command.Name);
        }

        if (shell is not null)
        {
            EditorCommands.Register(new EditorCommand(StandardShortcuts.QuickFix, "Edit: Quick fix", (_, token) => QuickFixAsync(token), _ => _active() is not null && shell.Tabs.Active is not null,
                "The code actions the plug-ins offer for the problems at the caret"));
            _registered.Add(StandardShortcuts.QuickFix);
        }

        foreach (var binding in StandardShortcuts.Bindings)
        {
            EditorKeyBindings.Register(binding);
            _bindings.Add(binding);
        }

        // The editor's context menu (phase X): the edits, the navigation and the code's own commands; Find references is
        // the shell's, the AI actions their plug-in's (each runs only when picked).
        foreach (var (id, group, place) in EditorContextItems)
        {
            if (id == StandardShortcuts.QuickFix && shell is null)
            {
                continue;
            }

            EditorMenus.Register(new MenuItem(EditorContextMenus.Editor, id, Order: place, Group: group));
            _menus.Add((EditorContextMenus.Editor, id));
        }

        // A read-only text's menu (the owner's note 26): the commands that do not edit, on the text it was opened on.
        foreach (var (id, group, place) in ReadOnlyTextItems)
        {
            EditorMenus.Register(new MenuItem(EditorContextMenus.ReadOnlyText, id, Order: place, Group: group));
            _menus.Add((EditorContextMenus.ReadOnlyText, id));
        }

                if (EditorKeyMaps.Find(StandardShortcuts.VisualStudio) is null)
        {
            EditorKeyMaps.Register(StandardShortcuts.VisualStudioKeyMap);
            _keyMapRegistered = true;
        }

        // The host read CRAFT_KEYMAP before this keymap was registered: read it again now.
        if (shell is not null)
        {
            shell.Host.SetKeyBindings(shell.Options.KeyBindings, shell.Options.KeyMap);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var (menu, id) in _menus)
        {
            EditorMenus.Unregister(menu, id);
        }

        foreach (var id in _registered)
        {
            EditorCommands.Unregister(id);
        }

        foreach (var binding in _bindings)
        {
            var current = binding.Then is { } then ? EditorKeyBindings.Find(binding.Chord, then) : EditorKeyBindings.Find(binding.Chord);
            if (ReferenceEquals(current, binding))
            {
                EditorKeyBindings.Unregister(binding);
            }
        }

        if (_keyMapRegistered)
        {
            EditorKeyMaps.Unregister(StandardShortcuts.VisualStudio);
            _keyMapRegistered = false;
        }

        _menus.Clear();
        _registered.Clear();
        _bindings.Clear();
    }

    private void Register(string id, string title, Func<EditorModel, bool> run)
    {
        bool edits = id is not ("craft.copy" or "craft.selectAll" or "craft.find" or "craft.findNext" or "craft.findPrevious");
        EditorCommands.Register(new EditorCommand(id, title, (_, _) =>
        {
            if (Target() is { } model)
            {
                run(model);
            }

            return Task.CompletedTask;
        }, _ => Target() is { } model && (!edits || !model.IsReadOnly)));
        _registered.Add(id);
    }

    // The editor a context menu was opened on (a read-only text's: Output's), else the active one.
    private EditorModel? Target() => EditorContextMenus.Target is { Item: EditorModel model } ? model : _active();

    // The code actions the providers offer for the problems at the caret (or for the caret's line when there are none):
    // one runs at once, several are picked from a list; none is said so.
    private async Task QuickFixAsync(CancellationToken cancellationToken)
    {
        if (_shell is not { Tabs.Active: { } tab } shell || _active() is not { } model)
        {
            return;
        }

        int caret = model.Selection.Caret;
        var diagnostics = model.DiagnosticsAt(caret);
        var context = new CodeActionContext(tab.HostDocument, diagnostics.Count > 0 ? diagnostics.Min(d => d.Start) : model.Selection.Start,
            diagnostics.Count > 0 ? diagnostics.Max(d => d.End) : model.Selection.End, diagnostics);
        var actions = CodeActionProviders.ActionsFor(context, (name, error) => shell.Host.Log(EditorLogLevel.Warning, $"The code actions of {name} failed.", error));
        if (actions.Count == 0)
        {
            shell.Host.ShowStatus("Quick fix", diagnostics.Count > 0 ? "No plug-in offers a fix for the problem here." : "Nothing to fix here.");
            return;
        }

        int picked = actions.Count == 1 ? 0 : await shell.Ui.PickAsync("Quick fix", [.. actions.Select(a => a.Title)], cancellationToken);
        if (picked < 0 || picked >= actions.Count)
        {
            return;
        }

        try
        {
            await actions[picked].RunAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            shell.Host.Log(EditorLogLevel.Error, $"The code action '{actions[picked].Title}' failed.", error);
        }
    }
}
