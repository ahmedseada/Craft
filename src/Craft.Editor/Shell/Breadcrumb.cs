// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>What a breadcrumb's part is, so a click knows what to list.</summary>
public enum BreadcrumbPartKind
{
    /// <summary>A folder of the file's path (a click lists its entries in quick open).</summary>
    Folder,

    /// <summary>The file (a click lists its folder's entries).</summary>
    File,

    /// <summary>A symbol around the caret (a click lists its siblings with '@').</summary>
    Symbol,

    /// <summary>"…": the middle parts folded for want of width (a click acts as the last part it holds).</summary>
    Ellipsis,
}

/// <summary>A part of the breadcrumb.</summary>
/// <param name="Text">What it shows.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Path">A folder's or the file's full path; null for a symbol.</param>
/// <param name="Scope">A symbol's kind (its colour); <see cref="ScopeKind.Namespace"/> for a folder or the file.</param>
public sealed record BreadcrumbPart(string Text, BreadcrumbPartKind Kind, string? Path, ScopeKind Scope)
{
    /// <summary>For <see cref="BreadcrumbPartKind.Ellipsis"/>: the parts it holds, in order; else none.</summary>
    public IReadOnlyList<BreadcrumbPart> Folded { get; init; } = [];

    /// <summary>The part a click acts as: itself, or for "…" the last part it holds.</summary>
    public BreadcrumbPart Target => Kind == BreadcrumbPartKind.Ellipsis && Folded.Count > 0 ? Folded[^1] : this;
}

/// <summary>
/// The breadcrumb over the focused group's editor (the X restart's piece 3b): the file's path within the workspace (each
/// folder from the workspace's root; the full folder when the file is outside it), the file's name, then the language's
/// scopes around the caret. When the parts are wider than the bar, the middle ones fold into one "…" (the measured widths
/// decide), keeping the first folder, the file and the symbols as long as they fit.
/// </summary>
public static class BreadcrumbLayout
{
    /// <summary>The text of the folded part.</summary>
    public const string Ellipsis = "…";

    /// <summary>The parts of <paramref name="filePath"/> in <paramref name="workspaceRoot"/> with <paramref name="scopes"/>.</summary>
    public static IReadOnlyList<BreadcrumbPart> Parts(string? filePath, string? workspaceRoot, IReadOnlyList<ScopeName> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var parts = new List<BreadcrumbPart>(scopes.Count + 6);
        if (filePath is not null)
        {
            string full = Path.GetFullPath(filePath);
            string? folder = Path.GetDirectoryName(full);
            string? root = workspaceRoot is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
            if (folder is not null && root is not null && IsUnder(folder, root))
            {
                // Each folder from the workspace's root down to the file's (the root itself by its name).
                var chain = new List<string>();
                for (string? at = folder; at is not null && IsUnder(at, root); at = Path.GetDirectoryName(at))
                {
                    chain.Add(at);
                }

                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    parts.Add(new BreadcrumbPart(Path.GetFileName(Path.TrimEndingDirectorySeparator(chain[i])) is { Length: > 0 } name ? name : chain[i],
                        BreadcrumbPartKind.Folder, chain[i], ScopeKind.Namespace));
                }
            }
            else if (folder is not null)
            {
                parts.Add(new BreadcrumbPart(folder, BreadcrumbPartKind.Folder, folder, ScopeKind.Namespace));
            }

            parts.Add(new BreadcrumbPart(Path.GetFileName(full), BreadcrumbPartKind.File, full, ScopeKind.Namespace));
        }

        foreach (var scope in scopes)
        {
            parts.Add(new BreadcrumbPart(scope.Name, BreadcrumbPartKind.Symbol, null, scope.Kind));
        }

        return parts;
    }

    /// <summary>
    /// The parts shown in <paramref name="available"/> width: all when they fit (each part's <paramref name="widths"/> plus
    /// a <paramref name="separator"/> between two), else the middle ones from the second onwards folded into one "…" of
    /// <paramref name="ellipsisWidth"/>, the fewest that make it fit, never the first part, the file or the last symbol;
    /// when even that does not fit, the most it can fold.
    /// </summary>
    public static IReadOnlyList<BreadcrumbPart> Fold(IReadOnlyList<BreadcrumbPart> parts, IReadOnlyList<double> widths, double separator, double ellipsisWidth, double available)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(widths);
        if (widths.Count != parts.Count)
        {
            throw new ArgumentException($"{widths.Count} widths for {parts.Count} parts.", nameof(widths));
        }

        double total = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            total += widths[i] + (i > 0 ? separator : 0);
        }

        if (total <= available || parts.Count < 3)
        {
            return parts;
        }

        // Foldable: from the second part up to (not including) the file, and past it every symbol but the last.
        int file = -1;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Kind == BreadcrumbPartKind.File)
            {
                file = i;
                break;
            }
        }

        int last = parts.Count - 1;
        int foldEnd = 1;                                          // folds parts[1..foldEnd)
        double shown = total + ellipsisWidth + separator;         // "…" added, nothing folded yet
        while (shown > available)
        {
            int next = foldEnd;
            if (next == file)
            {
                next++;                                           // the file stays: fold the symbols after it instead
            }

            if (next >= last)
            {
                break;
            }

            shown -= widths[next] + separator;
            foldEnd = next + 1;
        }

        if (foldEnd <= 1)
        {
            return parts;
        }

        var folded = new List<BreadcrumbPart>();
        var result = new List<BreadcrumbPart> { parts[0] };
        for (int i = 1; i < foldEnd; i++)
        {
            if (i == file)
            {
                continue;
            }

            folded.Add(parts[i]);
        }

        result.Add(new BreadcrumbPart(Ellipsis, BreadcrumbPartKind.Ellipsis, null, ScopeKind.Namespace) { Folded = folded });
        for (int i = 1; i < parts.Count; i++)
        {
            if (i >= foldEnd || i == file)
            {
                result.Add(parts[i]);
            }
        }

        return result;
    }

    private static bool IsUnder(string folder, string root) =>
        folder.Length >= root.Length && folder.StartsWith(root, StringComparison.OrdinalIgnoreCase)
        && (folder.Length == root.Length || folder[root.Length] is '/' or '\\');
}

/// <summary>
/// The '@' list's preview (Up and Down show each symbol in the editor, Escape goes back): it keeps the editor's selection
/// and scroll when the list opens, selects each previewed symbol's name and scrolls to it, and puts the kept ones back on
/// <see cref="Restore"/>; a pick keeps where it went (<see cref="End"/>). Used on the UI thread.
/// </summary>
public sealed class SymbolPreview
{
    private EditorModel? _editor;
    private SelectionState _selection;
    private float _scrollX;
    private float _scrollY;
    private int _version;
    private bool _moved;

    /// <summary>The editor being previewed in, or null.</summary>
    public EditorModel? Editor => _editor;

    /// <summary>Starts previewing in <paramref name="editor"/>: its selection and scroll are kept.</summary>
    public void Begin(EditorModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        _selection = editor.Selection;
        _scrollX = editor.ScrollX;
        _scrollY = editor.ScrollY;
        _version = editor.Snapshot.Version;
        _moved = false;
    }

    /// <summary>Shows <paramref name="item"/> (an '@' row) in the editor: its name selected and scrolled into view; false for another row.</summary>
    public bool Show(QuickOpenItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_editor is not { } editor || item.SymbolStart < 0 || editor.Snapshot.Version != _version)
        {
            return false;
        }

        int length = editor.Snapshot.Length;
        int start = Math.Clamp(item.SymbolStart, 0, length);
        int end = Math.Clamp(item.SymbolStart + item.SymbolLength, start, length);
        editor.Select(end, start);
        editor.EnsureCaretVisible();
        _moved = true;
        return true;
    }

    /// <summary>Escape: the kept selection and scroll back; false when nothing was previewed.</summary>
    public bool Restore()
    {
        if (_editor is not { } editor || !_moved)
        {
            _editor = null;
            return false;
        }

        _editor = null;
        if (editor.Snapshot.Version != _version)
        {
            return false;                                         // the text changed meanwhile: left where it is
        }

        editor.Select(_selection.Anchor, _selection.Caret);
        editor.ScrollToOffsets(_scrollX, _scrollY, instant: true);
        return true;
    }

    /// <summary>A pick: the editor stays where the last preview put it.</summary>
    public void End() => _editor = null;
}
