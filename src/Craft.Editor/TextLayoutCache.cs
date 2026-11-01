// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The platform's half of drawing laid-out text (B1): it lays a <see cref="StyledLine"/> out once (each run's colour,
/// weight and slant on its range) and draws that layout as often as asked. Windows implements it on Win2D's text layout
/// (<c>Platforms/Windows</c>); <see cref="TextLayoutCache"/> decides when to lay out and what to keep. Used on the UI
/// thread, inside a frame.
/// </summary>
public interface ITextLayoutSurface
{
    /// <summary>
    /// Lays <paramref name="line"/> out. For a whole line (<paramref name="wholeLine"/>), null when the layout would not
    /// keep each run at its column (a character of another width, a fallback font): the line is then drawn run by run.
    /// For a single run, never null.
    /// </summary>
    IDisposable? Lay(StyledLine line, bool wholeLine);

    /// <summary>Draws <paramref name="layout"/> (made by <see cref="Lay"/>) with its column 0 at <paramref name="x"/> and its top at <paramref name="top"/>.</summary>
    void Draw(IDisposable layout, float x, float top);
}

/// <summary>
/// Which laid-out text a canvas keeps, platform-neutral (B1, rules 75 and 77): a shown line's layout is kept on the line
/// itself (<see cref="StyledLine.Layout"/>, so its owner bounds it to the lines on screen and a margin) and made again only
/// when the line's runs or this cache's key (the font and the measures, <see cref="Invalidate"/>) change; a lone run
/// (a line number, a panel's or the terminal's text, a popup's) is kept by its text and style while it is drawn in every
/// frame, and dropped at the end of the first frame that does not draw it. A warm frame lays nothing out and allocates
/// nothing. The UI thread's.
/// </summary>
public sealed class TextLayoutCache : IDisposable
{
    private static int s_keys;
    // Lone runs by their characters (a string key: the dictionary's shared, precompiled code, never a struct key's unoptimized
    // code that boxes it, rule 89), each text a short chain of the styles it is drawn in.
    private readonly Dictionary<string, Entry> _runs = new(StringComparer.Ordinal);
    private int _held;
    private int _key = NewKey();
    private long _frame;
    private int _usedThisFrame;
    private int _builtThisFrame;
    private bool _disposed;

    /// <summary>The key of the current font and measures: a line's layout made for another key is made again.</summary>
    public int Key => _key;

    /// <summary>Layouts made since the cache was created (lines and lone runs).</summary>
    public long LayoutsBuilt { get; private set; }

    /// <summary>Lines whose layout did not keep their columns, drawn run by run instead.</summary>
    public long LinesByRuns { get; private set; }

    /// <summary>Lone runs' layouts dropped (not drawn in a frame, or released).</summary>
    public long RunsDropped { get; private set; }

    /// <summary>The lone runs whose layouts are kept now.</summary>
    public int RunsHeld => _held;

    /// <summary>The layouts made in the last frame (0: a warm frame).</summary>
    public int LastFrameBuilt { get; private set; }

    /// <summary>The font or the measures changed: every layout is made again when next drawn; the lone runs' are dropped now.</summary>
    public void Invalidate()
    {
        _key = NewKey();
        ReleaseRuns();
    }

    /// <summary>Starts a frame.</summary>
    public void BeginFrame()
    {
        _frame++;
        _usedThisFrame = 0;
        _builtThisFrame = 0;
    }

    /// <summary>Ends a frame: a frame that drew text drops the lone runs it did not draw.</summary>
    public void EndFrame()
    {
        LastFrameBuilt = _builtThisFrame;
        if (_usedThisFrame == 0 || _held <= _usedThisFrame)
        {
            return;
        }

        foreach (var (key, head) in _runs)
        {
            Entry? kept = null;
            Entry? last = null;
            for (var entry = head; entry is not null; entry = entry.Next)
            {
                if (entry.Frame == _frame)
                {
                    if (last is null)
                    {
                        kept = entry;
                    }
                    else
                    {
                        last.Next = entry;
                    }

                    last = entry;
                }
                else
                {
                    entry.Line.ReleaseLayout();
                    _held--;
                    RunsDropped++;
                }
            }

            if (last is not null)
            {
                last.Next = null;
            }

            if (kept is null)
            {
                _runs.Remove(key);
            }
            else if (!ReferenceEquals(kept, head))
            {
                // The value replaced in place (no change to the dictionary's shape while it is walked).
                System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_runs, key) = kept;
            }
        }
    }

    /// <summary>
    /// Draws <paramref name="line"/> through <paramref name="surface"/> from its kept layout, laying it out first when it
    /// has none for <see cref="Key"/>; a line the surface cannot lay out at its columns is drawn run by run, each run from
    /// its own kept layout, skipping runs outside <paramref name="clipLeft"/> to <paramref name="clipRight"/>.
    /// </summary>
    public void DrawLine(ITextLayoutSurface surface, StyledLine line, float x, float top, float cell, float clipLeft, float clipRight)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(line);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (line.Count == 0)
        {
            return;
        }

        var layout = line.LayoutKey == _key ? line.Layout : null;
        if (layout is null)
        {
            layout = surface.Lay(line, wholeLine: true);
            if (layout is null)
            {
                layout = ByRuns.Instance;
                LinesByRuns++;
            }
            else
            {
                LayoutsBuilt++;
                _builtThisFrame++;
            }

            line.KeepLayout(layout, _key);
        }

        if (!ReferenceEquals(layout, ByRuns.Instance))
        {
            surface.Draw(layout, x, top);
            return;
        }

        foreach (ref readonly var run in line.Runs)
        {
            float at = x + (run.Column * cell);
            if (at > clipRight || at + (run.Text.Length * cell) < clipLeft)
            {
                continue;
            }

            DrawText(surface, run.Text, run.Style, at, top);
        }
    }

    /// <summary>Draws <paramref name="text"/> in <paramref name="style"/> through <paramref name="surface"/> from the layout kept for that text and style, made first when none is.</summary>
    public void DrawText(ITextLayoutSurface surface, string text, in TextStyle style, float x, float top)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (text.Length == 0)
        {
            return;
        }

        _runs.TryGetValue(text, out var head);
        var entry = head;
        while (entry is not null && !Same(entry.Style, style))
        {
            entry = entry.Next;
        }

        if (entry is null)
        {
            var line = new StyledLine();
            line.Add(new StyledRun(text, 0, style));
            entry = new Entry(line, style) { Next = head };
            _runs[text] = entry;
            _held++;
        }

        if (entry.Frame != _frame)
        {
            entry.Frame = _frame;
            _usedThisFrame++;
        }

        var layout = entry.Line.LayoutKey == _key ? entry.Line.Layout : null;
        if (layout is null)
        {
            layout = surface.Lay(entry.Line, wholeLine: false)
                ?? throw new InvalidOperationException("ITextLayoutSurface.Lay returned null for a single run: a run is always laid out.");
            entry.Line.KeepLayout(layout, _key);
            LayoutsBuilt++;
            _builtThisFrame++;
        }

        surface.Draw(layout, x, top);
    }

    /// <summary>Drops every lone run's layout (memory pressure; the lines' layouts belong to their owners).</summary>
    public void ReleaseRuns()
    {
        foreach (var head in _runs.Values)
        {
            for (var entry = head; entry is not null; entry = entry.Next)
            {
                entry.Line.ReleaseLayout();
                RunsDropped++;
            }
        }

        _runs.Clear();
        _held = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseRuns();
        _disposed = true;
    }

    private static int NewKey()
    {
        int key = Interlocked.Increment(ref s_keys);
        return key == 0 ? Interlocked.Increment(ref s_keys) : key;
    }

    // The style's fields compared directly (no generic equality over a value type of another assembly, rule 89).
    private static bool Same(in TextStyle a, in TextStyle b) => a.Argb == b.Argb && a.Bold == b.Bold && a.Italic == b.Italic;

    private sealed class Entry(StyledLine line, TextStyle style)
    {
        public StyledLine Line { get; } = line;

        public TextStyle Style { get; } = style;

        public long Frame { get; set; }

        public Entry? Next { get; set; }
    }

    // A line drawn run by run: kept as its layout, so the surface is not asked again until the line or the key changes.
    private sealed class ByRuns : IDisposable
    {
        public static readonly ByRuns Instance = new();

        public void Dispose()
        {
        }
    }
}
