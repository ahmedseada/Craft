// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core;

namespace Craft.Editor.Shell;

/// <summary>The shell's two layouts.</summary>
public enum ShellLayoutKind
{
    /// <summary>The rail and its side view (the folder tree, or a plug-in panel) beside the editor, the tabs as a strip, the chat column and the bottom panel, every button.</summary>
    Wide,

    /// <summary>A phone's: the folder tree in place of the editor when shown, the tabs as one switcher, no side panels, few buttons.</summary>
    Narrow,
}

/// <summary>
/// Which layout the shell shows and what is visible in it, chosen from the window's measured width against
/// <see cref="CraftOptions.NarrowWidth"/> (<c>CRAFT_NARROW_WIDTH</c>), never from the platform: a narrow Windows window shows
/// the phone layout. Each layout keeps its own choice of whether the tree shows: in the wide layout it shows beside the
/// editor (on until hidden); in the narrow one it takes the editor's place (shown when a folder opens, hidden when a file
/// is opened from it). Used on the UI thread; <see cref="Changed"/> tells the view to apply it.
/// </summary>
public sealed class ShellLayout
{
    private CraftOptions _options;
    private bool _wideTree = true;
    private bool _narrowTree;
    private bool _hasFolder;

    /// <summary>A layout with <paramref name="options"/>' threshold, wide until a width is measured.</summary>
    public ShellLayout(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>The settings (the threshold and the widths); setting them chooses again for the last width.</summary>
    public CraftOptions Options
    {
        get => _options;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _options = value;
            var kind = Choose(Width, value.NarrowWidth);
            if (kind != Kind)
            {
                Kind = kind;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The layout in use.</summary>
    public ShellLayoutKind Kind { get; private set; }

    /// <summary>Whether it is the narrow layout.</summary>
    public bool IsNarrow => Kind == ShellLayoutKind.Narrow;

    /// <summary>The window's last measured width (0 before the first).</summary>
    public double Width { get; private set; }

    /// <summary>Whether a folder is open (the tree has something to show).</summary>
    public bool HasFolder
    {
        get => _hasFolder;
        set
        {
            if (value == _hasFolder)
            {
                return;
            }

            _hasFolder = value;
            if (value)
            {
                // A folder just opened: the narrow layout shows its tree, so the user sees what opened.
                _narrowTree = true;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether the folder tree shows.</summary>
    public bool TreeVisible => _hasFolder && (IsNarrow ? _narrowTree : _wideTree);

    /// <summary>Whether the editor shows (in the narrow layout the tree takes its place).</summary>
    public bool EditorVisible => !IsNarrow || !TreeVisible;

    /// <summary>
    /// Whether the side view (the tree, or a plug-in panel) is open in the wide layout: the shell keeps it with the panels'
    /// sizes (<see cref="PanelLayout"/>, <c>CRAFT_PANEL_EXPLORER_OPEN</c>); setting it raises <see cref="Changed"/>.
    /// </summary>
    public bool WideTreeOpen
    {
        get => _wideTree;
        set
        {
            if (value != _wideTree)
            {
                _wideTree = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Raised when what shows changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The layout for <paramref name="width"/>: narrow below <paramref name="narrowWidth"/>; wide while no width is measured (0 or less).</summary>
    public static ShellLayoutKind Choose(double width, float narrowWidth) =>
        width > 0 && width < narrowWidth ? ShellLayoutKind.Narrow : ShellLayoutKind.Wide;

    /// <summary>The window measured <paramref name="width"/> wide; true when the layout changed.</summary>
    public bool UpdateWidth(double width)
    {
        Width = width;
        var kind = Choose(width, _options.NarrowWidth);
        if (kind == Kind)
        {
            return false;
        }

        Kind = kind;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Shows or hides the tree in the layout in use.</summary>
    public void ToggleTree()
    {
        if (IsNarrow)
        {
            _narrowTree = !_narrowTree;
        }
        else
        {
            _wideTree = !_wideTree;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A file was opened from the tree: the narrow layout goes back to the editor.</summary>
    public void FileOpenedFromTree()
    {
        if (IsNarrow && _narrowTree)
        {
            _narrowTree = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
