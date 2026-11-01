// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Code lens (X part 9, <c>CRAFT_CODE_LENS</c>): the registered providers of the document's language
/// (<see cref="CodeLensProviders"/>) are asked once per text version, after <c>CRAFT_CODE_LENS_DELAY_MS</c> without a
/// change, off the UI thread, for the lenses of the document's symbols; each member with lenses gets a row of its own
/// above it (the rows move nothing else: line numbers stay the lines'). A lens whose title costs more (a reference count)
/// is resolved only when its row is on screen, again after the pause, and kept for the version; across an edit the lenses
/// move with the text and keep their last titles until the next answer. A click on a lens's title runs it.
/// </summary>
public sealed partial class EditorModel
{
    private CodeLens[] _lenses = [];
    private int _lensVersion = -1;
    private bool _lensStale;
    private int _lensAskedVersion = -1;
    private DebounceTimer? _lensTimer;
    private DebounceTimer? _lensResolveTimer;
    private CancellationTokenSource? _lensRequest;
    private CancellationTokenSource? _lensResolving;
    private Action? _lensDue;
    private Action? _lensResolveDue;
    private int[] _lensLines = [];
    private int[] _lensBaseRows = [];
    private bool _lensRowsDirty = true;
    private int _lensRowsFolds = -1;
    private int _lensRowsLineCount = -1;
    private bool _lensRowsWrapping;
    private readonly Dictionary<int, LensRow> _lensRowText = [];
    private int _lensResolveFrom = -1;
    private int _lensResolveTo = -1;

    /// <summary>The host the editor's lenses run through (a test's Run through the Run view), or null; the shell sets it.</summary>
    public IEditorHost? Host { get; set; }

    /// <summary>Whether the document shows code lenses (the setting on and a provider for its language).</summary>
    public bool CodeLensShown => Options.CodeLens && _language is not null && CodeLensProviders.For(_language.Name).Count > 0;

    /// <summary>The lenses kept now (in the current text's positions), in order of position.</summary>
    public IReadOnlyList<CodeLens> CodeLenses => _lenses;

    /// <summary>The requests made for code lenses (the providers asked) since the editor opened.</summary>
    public int CodeLensRequests { get; private set; }

    /// <summary>The lenses resolved (asked for their final title) since the editor opened.</summary>
    public int CodeLensResolves { get; private set; }

    /// <summary>The lines that have a lens row above them, in order (lines hidden by a fold left out).</summary>
    public ReadOnlySpan<int> LensLines
    {
        get
        {
            EnsureLensRows();
            return _lensLines;
        }
    }

    /// <summary>Whether <paramref name="line"/> has a lens row above it.</summary>
    public bool HasLensRow(int line)
    {
        EnsureLensRows();
        return _lensLines.Length > 0 && Array.BinarySearch(_lensLines, line) >= 0;
    }

    /// <summary>The rows drawn for the lines (folds counted), without the lens rows.</summary>
    private int TextRowCount => Folding.Map.HasHidden ? Folding.Map.RowCount : Snapshot.LineCount;

    private int TextRowOf(int line) => Folding.Map.HasHidden ? Folding.Map.RowOf(line) : line;

    private int TextLineAt(int row) => Folding.Map.HasHidden ? Folding.Map.LineAt(row) : Math.Clamp(row, 0, Snapshot.LineCount - 1);

    // The lens rows before a text row: the lens lines whose own text row is at or before it.
    private int LensRowsThrough(int textRow)
    {
        var rows = _lensBaseRows;
        int low = 0;
        int high = rows.Length;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (rows[middle] <= textRow)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // The line drawn at a row of the view (a lens row gives the line below it).
    private int LineAtViewRow(int row)
    {
        EnsureLensRows();
        var rows = _lensBaseRows;
        if (rows.Length == 0 || WordWrap.IsActive)
        {
            return TextLineAt(row);
        }

        // Lens i's own row is rows[i] + i (every lens row before it moves it down one); find how many lens rows are at or above `row`.
        int low = 0;
        int high = rows.Length;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (rows[middle] + middle <= row)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low > 0 && rows[low - 1] + low - 1 == row)
        {
            return _lensLines[low - 1];
        }

        return TextLineAt(row - low);
    }

    // A vertical move's target row stepped past lens rows in its direction (they hold no text).
    private int SkipLensRows(int row, int direction)
    {
        if (_lensLines.Length == 0 || direction == 0)
        {
            return row;
        }

        while (row >= 0 && row < RowCount && IsLensRow(row))
        {
            row += direction;
        }

        return row;
    }

    /// <summary>Whether <paramref name="row"/> of the view is a lens row (the line below it then is <see cref="LineAtRow"/>).</summary>
    public bool IsLensRow(int row)
    {
        EnsureLensRows();
        int line = LineAtViewRow(row);
        return HasLensRow(line) && RowOfLine(line) == row + 1;
    }

    private void EnsureLensRows()
    {
        var snapshot = Snapshot;
        bool wrapping = WordWrap.IsActive;
        if (!_lensRowsDirty && _lensRowsFolds == FoldsStamp && _lensRowsLineCount == snapshot.LineCount && _lensRowsWrapping == wrapping)
        {
            return;
        }

        _lensRowsDirty = false;
        _lensRowsWrapping = wrapping;
        _lensRowsFolds = FoldsStamp;
        _lensRowsLineCount = snapshot.LineCount;
        _lensRowText.Clear();
        if (_lenses.Length == 0 || !Options.CodeLens || wrapping)
        {
            // While word wrap is on, the rows are the wrap's (main's, unchanged): lenses wait for it to be off (left for later).
            _lensLines = [];
            _lensBaseRows = [];
            return;
        }

        var lines = new List<int>(_lenses.Length);
        foreach (var lens in _lenses)
        {
            if (lens.Start < 0 || lens.Start > snapshot.Length)
            {
                continue;
            }

            int line = snapshot.GetLineIndex(lens.Start);
            if ((lines.Count == 0 || lines[^1] != line) && !(Folding.Map.HasHidden && Folding.Map.IsHidden(line)))
            {
                lines.Add(line);
            }
        }

        lines.Sort();
        _lensLines = [.. lines.Distinct()];
        _lensBaseRows = new int[_lensLines.Length];
        for (int i = 0; i < _lensLines.Length; i++)
        {
            _lensBaseRows[i] = TextRowOf(_lensLines[i]);
        }
    }

    /// <summary>
    /// The lens row above <paramref name="line"/>: its text (the titles joined by " | ") and each title's columns, made
    /// once while the lenses stay; null when the line has none.
    /// </summary>
    internal LensRow? LensRowOf(int line)
    {
        EnsureLensRows();
        if (!HasLensRow(line))
        {
            return null;
        }

        if (_lensRowText.TryGetValue(line, out var row))
        {
            return row;
        }

        var snapshot = Snapshot;
        var text = snapshot.GetLine(line);
        int indent = 0;
        for (int i = text.Start; i < text.End && snapshot[i] is ' ' or '\t'; i++)
        {
            indent = TextReading.Advance(indent, snapshot[i], Options.TabSize);
        }

        var parts = new List<(int Start, int Length, CodeLens Lens)>();
        var builder = new StringBuilder();
        foreach (var lens in _lenses)
        {
            if (lens.Start < text.Start || lens.Start > text.End)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(" | ");
            }

            parts.Add((indent + builder.Length, lens.Title.Length, lens));
            builder.Append(lens.Title);
        }

        row = new LensRow(builder.ToString(), indent, parts);
        _lensRowText[line] = row;
        return row;
    }

    /// <summary>
    /// The renderer's frame shows <paramref name="firstLine"/> to <paramref name="lastLine"/>: the lenses are asked for once
    /// per version (after the pause), and the unresolved ones of the lines shown are resolved (after the pause).
    /// </summary>
    internal void NoteLensesShown(int firstLine, int lastLine)
    {
        if (!Options.CodeLens || _language is null || _disposed)
        {
            return;
        }

        // The registry is asked only when a request is due (once a version): a warm frame allocates nothing here.
        var snapshot = Snapshot;
        if ((_lensVersion != snapshot.Version || _lensStale) && _lensAskedVersion != snapshot.Version)
        {
            _lensAskedVersion = snapshot.Version;              // once a version, whether a provider serves the language or not
            if (CodeLensProviders.For(_language.Name).Count > 0)
            {
                _lensTimer ??= new DebounceTimer(this);
                _lensTimer.Schedule(Options.CodeLensDelayMilliseconds, _lensDue ??= RequestLenses);
            }
        }

        if (_lenses.Length == 0 || (_lensResolveFrom == firstLine && _lensResolveTo == lastLine && _lensResolving is not null))
        {
            return;
        }

        foreach (var lens in _lenses)
        {
            if (lens.ResolveAsync is null)
            {
                continue;
            }

            int line = lens.Start <= snapshot.Length ? snapshot.GetLineIndex(lens.Start) : -1;
            if (line >= firstLine && line <= lastLine)
            {
                _lensResolveFrom = firstLine;
                _lensResolveTo = lastLine;
                _lensResolveTimer ??= new DebounceTimer(this);
                _lensResolveTimer.Schedule(Options.CodeLensDelayMilliseconds, _lensResolveDue ??= ResolveShownLenses);
                return;
            }
        }
    }

    private void RequestLenses()
    {
        if (_disposed || _language is not { } language)
        {
            return;
        }

        var providers = CodeLensProviders.For(language.Name);
        if (providers.Count == 0)
        {
            return;
        }

        _lensRequest?.Cancel();
        var cancel = new CancellationTokenSource();
        _lensRequest = cancel;
        var snapshot = Snapshot;
        var session = _session;
        string? file = _document.FilePath;
        string? root = WorkspaceRoot;
        var host = Host;
        CodeLensRequests++;
        var task = Task.Run(async () =>
        {
            var symbols = session is null ? [] : await session.GetDocumentSymbolsAsync(snapshot, cancel.Token).ConfigureAwait(false);
            var request = new CodeLensRequest(snapshot, language.Name, session, file, symbols) { WorkspaceRoot = root, Host = host };
            var all = new List<(CodeLens Lens, int Provider)>();
            for (int p = 0; p < providers.Count; p++)
            {
                try
                {
                    foreach (var lens in await providers[p].GetLensesAsync(request, cancel.Token).ConfigureAwait(false))
                    {
                        all.Add((lens, p));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // One provider's failure leaves the others' lenses; it is kept for the status.
                catch (Exception error)
#pragma warning restore CA1031
                {
                    LastSemanticError = error;
                }
            }

            all.Sort(static (a, b) => a.Lens.Start != b.Lens.Start ? a.Lens.Start.CompareTo(b.Lens.Start)
                : a.Lens.Order != b.Lens.Order ? a.Lens.Order.CompareTo(b.Lens.Order) : a.Provider.CompareTo(b.Provider));
            var lenses = new CodeLens[all.Count];
            for (int i = 0; i < lenses.Length; i++)
            {
                lenses[i] = all[i].Lens;
            }

            return lenses;
        }, cancel.Token);
        Track(task, lenses =>
        {
            if (!ReferenceEquals(_lensRequest, cancel))
            {
                return;
            }

            _lensRequest = null;
            if (Snapshot.Version != snapshot.Version)
            {
                return;                                        // the edit since moved the old ones; the next frame asks again
            }

            SetLenses(CarryTitles(lenses), snapshot.Version);
        }, () =>
        {
            if (ReferenceEquals(_lensRequest, cancel))
            {
                _lensRequest = null;                           // failed: not asked again for this text (an edit asks again)
            }
        });
    }

    // A new answer's unresolved lens keeps the title its predecessor at the same place had (a count stays while it is
    // counted again, rather than flickering to its placeholder on every keystroke).
    private CodeLens[] CarryTitles(CodeLens[] fresh)
    {
        if (_lenses.Length == 0)
        {
            return fresh;
        }

        int j = 0;
        for (int i = 0; i < fresh.Length; i++)
        {
            while (j < _lenses.Length && _lenses[j].Start < fresh[i].Start)
            {
                j++;
            }

            for (int k = j; k < _lenses.Length && _lenses[k].Start == fresh[i].Start; k++)
            {
                if (fresh[i].ResolveAsync is not null && _lenses[k].Order == fresh[i].Order && _lenses[k].ResolveAsync is null)
                {
                    fresh[i] = fresh[i] with { Title = _lenses[k].Title };
                    break;
                }
            }
        }

        return fresh;
    }

    private void SetLenses(CodeLens[] lenses, int version)
    {
        int topLine = LineAtRow(VisibleRows(ScrollY, ViewportHeight).First);
        int rowsBefore = RowOfLine(topLine);
        int count = _lensLines.Length;
        _lenses = lenses;
        _lensVersion = version;
        _lensStale = false;
        _lensRowsDirty = true;
        _lensResolveFrom = _lensResolveTo = -1;
        EnsureLensRows();
        if (_lensLines.Length != count)
        {
            // Rows came or went above the screen: the view stays on the same line.
            int moved = RowOfLine(topLine) - rowsBefore;
            if (moved != 0 && ScrollY > 0)
            {
                ScrollY = Math.Max(0, ScrollY + (moved * Metrics.LineHeight));
                ScrollRequested?.Invoke(this, new EditorScrollEventArgs(ScrollX, ScrollY) { Instant = true });
            }

            ContentSizeChanged?.Invoke(this, EventArgs.Empty);
        }

        Invalidate();
    }

    private void ResolveShownLenses()
    {
        if (_disposed || _lenses.Length == 0)
        {
            return;
        }

        _lensResolving?.Cancel();
        var cancel = new CancellationTokenSource();
        _lensResolving = cancel;
        var snapshot = Snapshot;
        int version = snapshot.Version;
        var lenses = _lenses;
        for (int i = 0; i < lenses.Length; i++)
        {
            var lens = lenses[i];
            if (lens.ResolveAsync is not { } resolve || lens.Start > snapshot.Length)
            {
                continue;
            }

            int line = snapshot.GetLineIndex(lens.Start);
            if (line < _lensResolveFrom || line > _lensResolveTo)
            {
                continue;
            }

            CodeLensResolves++;
            var original = lens;
            Track(Task.Run(() => resolve(cancel.Token), cancel.Token), resolved =>
            {
                if (_lensVersion != version || Snapshot.Version != version)
                {
                    return;
                }

                int at = Array.IndexOf(_lenses, original);
                if (at < 0)
                {
                    return;
                }

                var copy = (CodeLens[])_lenses.Clone();
                copy[at] = resolved with { Start = original.Start, ResolveAsync = null };
                _lenses = copy;
                _lensRowText.Clear();
                Invalidate();
            });
        }
    }

    /// <summary>
    /// A press at the view point: when it is on a lens's title (a lens row), that lens runs and true is returned.
    /// </summary>
    private bool PressOnLens(float viewX, float viewY)
    {
        if (_lensLines.Length == 0)
        {
            return false;
        }

        int row = (int)MathF.Floor((viewY + ScrollY) / Metrics.LineHeight);
        if (row < 0 || row >= RowCount || !IsLensRow(row))
        {
            return false;
        }

        _pointerDown = false;
        if (LensRowOf(LineAtRow(row)) is not { } lensRow)
        {
            return true;
        }

        float column = (viewX + ScrollX - TextLeft) / Metrics.CellWidth;
        foreach (var (start, length, lens) in lensRow.Parts)
        {
            if (column >= start && column < start + length)
            {
                if (lens.RunAsync is { } run)
                {
                    LensesRun++;
                    _ = RunLensAsync(run);
                }

                break;
            }
        }

        return true;
    }

    /// <summary>The lenses a click ran since the editor opened.</summary>
    public int LensesRun { get; private set; }

    /// <summary>Runs the lens at index <paramref name="index"/> of <see cref="CodeLenses"/> (as a click on its title does); false when it does nothing.</summary>
    public bool RunLens(int index)
    {
        if (index < 0 || index >= _lenses.Length || _lenses[index].RunAsync is not { } run)
        {
            return false;
        }

        LensesRun++;
        _ = RunLensAsync(run);
        return true;
    }

    private async Task RunLensAsync(Func<CancellationToken, Task> run)
    {
        try
        {
            await run(CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
#pragma warning disable CA1031 // A lens that fails is kept for the status; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            LastSemanticError = error;
            StatusRequested?.Invoke(this, "Code lens: " + error.Message);
        }
    }

    // Each change moves the lenses (one whose member start the change touched is dropped) and asks for new ones.
    private void MapLenses(TextChangedEventArgs change)
    {
        if (_lenses.Length == 0 && _lensVersion < 0)
        {
            return;
        }

        var edits = change.Edits;
        var kept = new List<CodeLens>(_lenses.Length);
        foreach (var lens in _lenses)
        {
            int position = lens.Start;
            bool touched = false;
            int delta = 0;
            foreach (var edit in edits)
            {
                if (position >= edit.End)
                {
                    delta += edit.Text.Length - edit.Length;    // text inserted at a member's start goes above its lens
                }
                else if (position > edit.Start)
                {
                    touched = true;
                    break;
                }
                else
                {
                    break;
                }
            }

            if (!touched)
            {
                kept.Add(delta == 0 ? lens : lens with { Start = position + delta });
            }
        }

        _lenses = [.. kept];
        _lensVersion = change.After.Version;
        _lensStale = true;
        _lensRowsDirty = true;
        _lensResolving?.Cancel();
        _lensResolving = null;
    }

    private void DisposeLenses()
    {
        _lensTimer?.Dispose();
        _lensResolveTimer?.Dispose();
        _lensRequest?.Cancel();
        _lensResolving?.Cancel();
    }

    /// <summary>A lens row: its text, where it starts (the member's indentation) and each lens's title in it.</summary>
    /// <param name="Text">The titles joined by " | ".</param>
    /// <param name="Column">The column it starts at.</param>
    /// <param name="Parts">Each lens's title: its first column (from the line's start), its length, the lens.</param>
    internal sealed record LensRow(string Text, int Column, IReadOnlyList<(int Start, int Length, CodeLens Lens)> Parts);
}
