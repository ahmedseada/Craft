// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>A document's problems for the Problems list: its key (its path, or its title), its title, its text and its diagnostics, and what it belongs to.</summary>
/// <param name="Key">What tells it from the others (its full path, or its title for a new document).</param>
/// <param name="Title">Its name as the list shows it and the copy writes it ("Program.cs").</param>
/// <param name="Snapshot">The text the diagnostics are in, or null for a closed file's (<see cref="Lines"/>).</param>
/// <param name="Diagnostics">Its diagnostics (the hidden ones are left out).</param>
/// <param name="Owner">What the page finds it by (its tab), or null.</param>
public sealed record ProblemSource(string Key, string Title, ITextSnapshot? Snapshot, IReadOnlyList<EditorDiagnostic> Diagnostics, object? Owner = null)
{
    /// <summary>A closed file's problems by line and column (W, contract 6: no text read), used when <see cref="Snapshot"/> is null.</summary>
    public IReadOnlyList<FileDiagnostic> Lines { get; init; } = [];

    /// <summary>The problems of the closed file <paramref name="path"/>, by line and column, its title its name.</summary>
    public static ProblemSource ForClosedFile(string path, IReadOnlyList<FileDiagnostic> lines, object? owner = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(lines);
        return new ProblemSource(path, Path.GetFileName(path), null, [], owner) { Lines = lines };
    }
}

/// <summary>A file's header in the Problems list: its title, its count, whether it is folded.</summary>
public sealed class ProblemGroup
{
    internal ProblemGroup(string key, string title, object? owner)
    {
        Key = key;
        Title = title;
        Owner = owner;
        Icon = FileIcons.For(key);
    }

    /// <summary>Its source's key.</summary>
    public string Key { get; }

    /// <summary>Its title.</summary>
    public string Title { get; }

    /// <summary>Its file's icon (W: the tree's, by the file's name), drawn before the header.</summary>
    public FileIcon Icon { get; }

    /// <summary>Its source's owner.</summary>
    public object? Owner { get; internal set; }

    /// <summary>Its problems listed (at most the list's limit in all).</summary>
    public List<ProblemItem> Items { get; } = [];

    /// <summary>Whether its rows are folded under the header.</summary>
    public bool Collapsed { get; internal set; }

    /// <summary>"Program.cs  3".</summary>
    public string Header { get; internal set; } = "";
}

/// <summary>A problem's row: its place (from 1), severity, code and message, the texts it is drawn and copied with.</summary>
public sealed class ProblemItem
{
    internal ProblemItem(ProblemGroup group, EditorDiagnostic diagnostic, int line, int column, int position)
    {
        Group = group;
        Diagnostic = diagnostic;
        Line = line;
        Column = column;
        Position = position;
        Text = $"{diagnostic.Id}  {diagnostic.Message.ReplaceLineEndings(" ")}";
        Place = string.Create(CultureInfo.InvariantCulture, $"Ln {line}, Col {column}");
        CopyText = PanelCopy.Problem(group.Title, line, column, diagnostic.Severity, diagnostic.Id, diagnostic.Message);
    }

    /// <summary>Its file.</summary>
    public ProblemGroup Group { get; }

    /// <summary>The diagnostic (as it was listed).</summary>
    public EditorDiagnostic Diagnostic { get; }

    /// <summary>Its line, from 1.</summary>
    public int Line { get; }

    /// <summary>Its column, from 1.</summary>
    public int Column { get; }

    /// <summary>Its position in the text it was listed in.</summary>
    public int Position { get; }

    /// <summary>"CS1002  ; expected".</summary>
    public string Text { get; }

    /// <summary>"Ln 12, Col 5".</summary>
    public string Place { get; }

    /// <summary>As a build tool writes it: "Program.cs(12,5): error CS1002: ; expected".</summary>
    public string CopyText { get; }

    internal bool SameAs(ProblemItem other) =>
        string.Equals(Group.Key, other.Group.Key, StringComparison.Ordinal) && Line == other.Line && Column == other.Column && Position == other.Position
        && Diagnostic.Severity == other.Diagnostic.Severity && string.Equals(Diagnostic.Id, other.Diagnostic.Id, StringComparison.Ordinal)
        && string.Equals(Diagnostic.Message, other.Diagnostic.Message, StringComparison.Ordinal);
}

/// <summary>
/// The Problems list (the owner's note 26): plain rows grouped by file, each file's header foldable with its count, a
/// severity mark per row; a click jumps to the problem (<see cref="Chosen"/>), Ctrl+click and Shift+click pick several,
/// Up and Down move, Enter jumps; Ctrl+C copies the picked rows as a build tool writes them, Copy all every row; Fix and
/// Explain on the row under the pointer or the keyboard (<see cref="ActionRequested"/>). An update that shows the same
/// rows changes nothing (no redraw, the picks kept); the picks follow their problems across updates. At most a limit's rows
/// (<c>CRAFT_PROBLEMS_LIMIT</c>), errors first, then by place.
/// </summary>
public sealed class ProblemsList : PanelList
{
    private const string FixTitle = "Fix";
    private const string BuildFixTitle = "Fix with AI";
    private const string ExplainTitle = "Explain";
    private readonly List<ProblemGroup> _groups = [];
    private readonly List<object> _rows = [];
    private readonly List<ProblemItem> _items = [];
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    /// <summary>The problems in all (with those past the limit).</summary>
    public int Count { get; private set; }

    /// <summary>The errors in all.</summary>
    public int Errors { get; private set; }

    /// <summary>Whether the rows offer Fix and Explain (a code action provider is registered).</summary>
    public bool HasActions { get; private set; }

    /// <summary>The files with problems, in their sources' order.</summary>
    public IReadOnlyList<ProblemGroup> Groups => _groups;

    /// <summary>The rows drawn: each file's header (<see cref="ProblemGroup"/>), then its problems (<see cref="ProblemItem"/>) unless folded.</summary>
    public IReadOnlyList<object> Rows => _rows;

    /// <summary>The problems' rows drawn (the folded ones left out), in order.</summary>
    public IReadOnlyList<ProblemItem> Items => _items;

    /// <summary>The picked problems.</summary>
    public RowSelection<ProblemItem> Selection { get; } = new();

    /// <inheritdoc />
    public override int RowCount => _rows.Count;

    /// <summary>Raised when a problem is opened (a plain click, Enter): the page shows its file and moves the caret there.</summary>
    public event EventHandler<ProblemItem>? Chosen;

    /// <summary>Raised when a row's Fix or Explain is pressed, with the code action kind (<see cref="CodeActionKinds"/>).</summary>
    public event EventHandler<(ProblemItem Item, string Kind)>? ActionRequested;

    /// <summary>
    /// Lists <paramref name="sources"/>' problems (at most <paramref name="limit"/> rows); true when what is shown changed
    /// (else nothing is replaced: the d260816 rule, an update on every keystroke must not rebuild the rows).
    /// </summary>
    public bool Update(IReadOnlyList<ProblemSource> sources, int limit, bool hasActions)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var groups = new List<ProblemGroup>();
        int count = 0;
        int errors = 0;
        int left = Math.Max(0, limit);
        foreach (var source in sources)
        {
            if (source.Snapshot is null)
            {
                ListClosed(source, groups, ref count, ref errors, ref left);
                continue;
            }

            var shown = new List<EditorDiagnostic>();
            foreach (var diagnostic in source.Diagnostics)
            {
                if (diagnostic.Severity != DiagnosticSeverity.Hidden)
                {
                    shown.Add(diagnostic);
                }
            }

            if (shown.Count == 0)
            {
                continue;
            }

            shown.Sort(static (a, b) => a.Severity != b.Severity ? b.Severity.CompareTo(a.Severity) : a.Start.CompareTo(b.Start));
            count += shown.Count;
            foreach (var diagnostic in shown)
            {
                errors += diagnostic.Severity == DiagnosticSeverity.Error ? 1 : 0;
            }

            var group = new ProblemGroup(source.Key, source.Title, source.Owner)
            {
                Collapsed = _collapsed.Contains(source.Key),
                Header = string.Create(CultureInfo.InvariantCulture, $"{source.Title}  {shown.Count}"),
            };
            var snapshot = source.Snapshot;
            foreach (var diagnostic in shown)
            {
                if (left == 0)
                {
                    break;
                }

                int start = Math.Clamp(diagnostic.Start, 0, snapshot.Length);
                int line = snapshot.GetLineIndex(start);
                int column = start - snapshot.GetLine(line).Start;
                group.Items.Add(new ProblemItem(group, diagnostic, line + 1, column + 1, start));
                left--;
            }

            groups.Add(group);
        }

        bool same = hasActions == HasActions && count == Count && SameGroups(groups);
        Count = count;
        Errors = errors;
        HasActions = hasActions;
        if (same)
        {
            // The same rows: keep them (and their picks); only the owners may be new objects.
            for (int i = 0; i < groups.Count; i++)
            {
                _groups[i].Owner = groups[i].Owner;
            }

            return false;
        }

        // The picks follow their problems to the new rows.
        var picked = Selection.InOrder(_items);
        var anchor = Selection.Anchor;
        var focus = Selection.Focus;
        _groups.Clear();
        _groups.AddRange(groups);
        Flatten();
        Selection.Clear();
        foreach (var item in _items)
        {
            foreach (var old in picked)
            {
                if (item.SameAs(old))
                {
                    Selection.Add(item, anchor is not null && item.SameAs(anchor), focus is not null && item.SameAs(focus));
                    break;
                }
            }
        }

        RaiseChanged();
        return true;
    }

    // A closed file's problems (W, contract 6): by line and column as published, no position in a text.
    private void ListClosed(ProblemSource source, List<ProblemGroup> groups, ref int count, ref int errors, ref int left)
    {
        var shown = new List<FileDiagnostic>();
        foreach (var item in source.Lines)
        {
            if (item.Severity != DiagnosticSeverity.Hidden)
            {
                shown.Add(item);
            }
        }

        if (shown.Count == 0)
        {
            return;
        }

        shown.Sort(static (a, b) => a.Severity != b.Severity ? b.Severity.CompareTo(a.Severity) : a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column));
        count += shown.Count;
        var group = new ProblemGroup(source.Key, source.Title, source.Owner)
        {
            Collapsed = _collapsed.Contains(source.Key),
            Header = string.Create(CultureInfo.InvariantCulture, $"{source.Title}  {shown.Count}"),
        };
        foreach (var item in shown)
        {
            errors += item.Severity == DiagnosticSeverity.Error ? 1 : 0;
            if (left == 0)
            {
                continue;
            }

            group.Items.Add(new ProblemItem(group, new EditorDiagnostic(0, 0, item.Severity, item.Id, item.Message), Math.Max(1, item.Line), Math.Max(1, item.Column), -1));
            left--;
        }

        groups.Add(group);
    }

    /// <summary>Folds or unfolds <paramref name="group"/>'s rows (kept across updates by its file).</summary>
    public void Toggle(ProblemGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Collapsed = !group.Collapsed;
        if (group.Collapsed)
        {
            _collapsed.Add(group.Key);
        }
        else
        {
            _collapsed.Remove(group.Key);
        }

        Flatten();
        Selection.Keep(_items);
        RaiseChanged();
    }

    /// <summary>The picked problems as a build tool writes them, a line each (null when none is picked).</summary>
    public override string? CopySelected()
    {
        var picked = Selection.InOrder(_items);
        return picked.Count == 0 ? null : PanelCopy.Lines(picked.Select(p => p.CopyText));
    }

    /// <summary>Every problem listed (the folded ones too), a line each.</summary>
    public override string CopyAll() => PanelCopy.Lines(_groups.SelectMany(g => g.Items).Select(p => p.CopyText));

    /// <inheritdoc />
    public override void Press(FontMetrics metrics, float viewX, float viewY, float scrollY, float width, KeyModifiers modifiers)
    {
        int index = RowAt(metrics, viewY, scrollY);
        if (index < 0)
        {
            return;
        }

        switch (_rows[index])
        {
            case ProblemGroup group:
                Toggle(group);
                break;
            case ProblemItem item:
                if (HasActions && (index == Hover || ReferenceEquals(item, Selection.Focus)) && ButtonAt(metrics, viewX, width, FixTitleOf(item)) is { } kind)
                {
                    ActionRequested?.Invoke(this, (item, kind));
                    return;
                }

                Selection.Click(_items, item, modifiers);
                RaiseChanged();
                if ((modifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == 0)
                {
                    Chosen?.Invoke(this, item);
                }

                break;
        }
    }

    /// <inheritdoc />
    public override bool OnKey(EditorKey key, KeyModifiers modifiers)
    {
        bool control = (modifiers & KeyModifiers.Control) != 0;
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        switch (key)
        {
            case EditorKey.Up or EditorKey.Down when !control:
                if (Selection.Move(_items, key == EditorKey.Up ? -1 : 1, shift))
                {
                    RevealFocus();
                    RaiseChanged();
                }

                return true;
            case EditorKey.Enter when Selection.Focus is { } focus:
                Chosen?.Invoke(this, focus);
                return true;
            case EditorKey.A when control:
                Selection.SelectAll(_items);
                RaiseChanged();
                return true;
            case EditorKey.C when control:
                return Copy(CopySelected() ?? CopyAll());
            case EditorKey.Escape when Selection.Count > 0:
                Selection.Clear();
                RaiseChanged();
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public override void Draw(IEditorCanvas canvas, FontMetrics metrics, EditorColors colors, float scrollY, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(colors);
        float rowHeight = RowHeight(metrics);
        float cell = metrics.CellWidth;
        float inset = (rowHeight - metrics.LineHeight) / 2;
        var text = new TextStyle(colors.Text);
        var quiet = new TextStyle(colors.SecondaryText);
        canvas.FillRect(0, 0, width, height, colors.Surface);
        bool dark = ColorContrast.Luminance(colors.Surface) < 0.18;
        float icon = MathF.Min(metrics.LineHeight, 16);
        var (first, last) = Visible(metrics, scrollY, height);
        for (int index = first; index <= last; index++)
        {
            float top = (index * rowHeight) - scrollY;
            float y = top + inset;
            switch (_rows[index])
            {
                case ProblemGroup group:
                    canvas.FillRect(0, top, width, rowHeight, RowFill(colors, index == Hover, false));
                    canvas.DrawText(group.Collapsed ? "▸" : "▾", cell, y, quiet);
                    FileIconDrawing.Draw(canvas, group.Icon, 3 * cell, top + ((rowHeight - icon) / 2), icon, false, dark);
                    canvas.DrawText(group.Header, (4 * cell) + icon, y, text);
                    break;
                case ProblemItem item:
                    bool picked = Selection.Contains(item);
                    uint fill = RowFill(colors, index == Hover, picked);
                    canvas.FillRect(0, top, width, rowHeight, fill);
                    var (mark, color) = item.Diagnostic.Severity switch
                    {
                        DiagnosticSeverity.Error => ("✕", colors.Error),
                        DiagnosticSeverity.Warning => ("⚠", colors.Warning),
                        _ => ("ℹ", colors.Info),
                    };
                    canvas.DrawText(mark, 3 * cell, y, new TextStyle(color));
                    canvas.DrawText(item.Text, 5 * cell, y, text);
                    // The place at the right, over the message's end; the buttons on the hovered or keyboard's row.
                    bool buttons = HasActions && (index == Hover || ReferenceEquals(item, Selection.Focus));
                    string fix = FixTitleOf(item);
                    float right = buttons ? ButtonsLeft(cell, width, fix) : width - cell;
                    float place = right - ((item.Place.Length + 2) * cell);
                    canvas.FillRect(place - cell, top, width - place + cell, rowHeight, fill);
                    canvas.DrawText(item.Place, place, y, quiet);
                    if (buttons)
                    {
                        DrawButton(canvas, colors, fix, ButtonsLeft(cell, width, fix), top, rowHeight, cell, y);
                        DrawButton(canvas, colors, ExplainTitle, ButtonsLeft(cell, width, fix) + ((fix.Length + 3) * cell), top, rowHeight, cell, y);
                    }

                    if (picked && ReferenceEquals(item, Selection.Focus))
                    {
                        canvas.DrawLine(0, top + rowHeight - 0.5f, width, top + rowHeight - 0.5f, colors.Accent, 1);
                    }

                    break;
            }
        }
    }

    /// <summary>The code action kind of the button at <paramref name="viewX"/> on a row with buttons, or null.</summary>
    public static string? ButtonAt(FontMetrics metrics, float viewX, float width) => ButtonAt(metrics, viewX, width, FixTitle);

    /// <summary>
    /// The code action kind of the button at <paramref name="viewX"/> on a row whose Fix button reads <paramref name="fixTitle"/>
    /// ("Fix with AI" on a build's row, X part 11), or null.
    /// </summary>
    public static string? ButtonAt(FontMetrics metrics, float viewX, float width, string fixTitle)
    {
        ArgumentNullException.ThrowIfNull(fixTitle);
        float cell = metrics.CellWidth;
        float left = ButtonsLeft(cell, width, fixTitle);
        float fixEnd = left + ((fixTitle.Length + 2) * cell);
        float explainStart = left + ((fixTitle.Length + 3) * cell);
        float explainEnd = explainStart + ((ExplainTitle.Length + 2) * cell);
        return viewX >= left && viewX < fixEnd ? CodeActionKinds.Fix
            : viewX >= explainStart && viewX < explainEnd ? CodeActionKinds.Explain
            : null;
    }

    private static float ButtonsLeft(float cell, float width, string fixTitle) => width - ((fixTitle.Length + ExplainTitle.Length + 6) * cell);

    /// <summary>What a row's Fix button says: "Fix with AI" on a build's or test's row (X part 11 item 5), else "Fix".</summary>
    public static string FixTitleOf(ProblemItem item) => (item ?? throw new ArgumentNullException(nameof(item))).Group.Owner is Run.RunProblemFile ? BuildFixTitle : FixTitle;

    private static void DrawButton(IEditorCanvas canvas, EditorColors colors, string title, float left, float top, float rowHeight, float cell, float y)
    {
        float width = (title.Length + 2) * cell;
        canvas.FillRect(left, top + 2, width, rowHeight - 4, colors.RaisedHigh);
        canvas.DrawText(title, left + cell, y, new TextStyle(colors.Text));
    }

    private void RevealFocus()
    {
        if (Selection.Focus is { } focus)
        {
            int at = _rows.IndexOf(focus);
            if (at >= 0)
            {
                Reveal(at);
            }
        }
    }

    private void Flatten()
    {
        _rows.Clear();
        _items.Clear();
        foreach (var group in _groups)
        {
            _rows.Add(group);
            if (!group.Collapsed)
            {
                foreach (var item in group.Items)
                {
                    _rows.Add(item);
                    _items.Add(item);
                }
            }
        }
    }

    private bool SameGroups(List<ProblemGroup> groups)
    {
        if (groups.Count != _groups.Count)
        {
            return false;
        }

        for (int g = 0; g < groups.Count; g++)
        {
            var (now, was) = (groups[g].Items, _groups[g].Items);
            if (now.Count != was.Count || !string.Equals(groups[g].Header, _groups[g].Header, StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = 0; i < now.Count; i++)
            {
                if (!now[i].SameAs(was[i]))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
