// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// The open menu of the window without a UI (phase X): a title-bar menu or a context menu (<see cref="EditorContextMenus"/>),
/// its rows (<see cref="ShellMenus.Rows"/>, each a registered command with the keys in effect and whether it can run on
/// what the menu was opened on), the row the keyboard is on, and its keys: Up and Down (and Home, End) move over the rows
/// that can run, a letter goes to the next row it starts, Enter or Space runs the row, Escape closes; a chord closes the
/// menu and goes on to the key bindings. A picked row closes the menu and runs its command through the host, with
/// <see cref="EditorContextMenus.Target"/> set while the command starts, so the command acts on the row, tab or text the
/// menu was opened on. Nothing runs until a row is picked. Used on the UI thread.
/// </summary>
public sealed class MenuModel : IEditorKeyHandler
{
    private readonly EditorHost _host;
    private IReadOnlyList<MenuRow> _rows = [];

    /// <summary>The menus of <paramref name="host"/>'s commands.</summary>
    public MenuModel(EditorHost host) => _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>The menu open ("File", <see cref="EditorContextMenus.Tree"/>), or null.</summary>
    public string? Menu { get; private set; }

    /// <summary>What the menu was opened on, or null (a title-bar menu, or the active document).</summary>
    public ContextMenuTarget? Target { get; private set; }

    /// <summary>The open menu's rows.</summary>
    public IReadOnlyList<MenuRow> Rows => _rows;

    /// <summary>The row the keyboard is on (-1 for none: a menu opened by the pointer starts with none).</summary>
    public int Focused { get; private set; } = -1;

    /// <summary>Whether a menu is open.</summary>
    public bool IsOpen => Menu is not null;

    /// <summary>Raised when a menu opened or closed (the view draws it again).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the keyboard's row moved (the view moves its highlight).</summary>
    public event EventHandler? FocusMoved;

    /// <summary>
    /// Opens <paramref name="menu"/> on <paramref name="target"/>: its rows as they are now (each command asked whether it
    /// can run on the target); with <paramref name="focusFirst"/> (the keyboard opened it) on its first row that can run.
    /// False when it has no rows.
    /// </summary>
    public bool Open(string menu, ContextMenuTarget? target = null, bool focusFirst = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menu);
        var before = EditorContextMenus.Target;
        EditorContextMenus.Target = target;
        try
        {
            _rows = ShellMenus.Rows(menu, _host);
        }
        finally
        {
            EditorContextMenus.Target = before;
        }

        Menu = menu;
        Target = target;
        Focused = -1;
        if (focusFirst)
        {
            Focused = Next(-1, 1);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return _rows.Count > 0;
    }

    /// <summary>Closes the menu (nothing runs).</summary>
    public void Close()
    {
        if (Menu is null)
        {
            return;
        }

        Menu = null;
        Target = null;
        _rows = [];
        Focused = -1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the keyboard's row on <paramref name="index"/> (the pointer moved over it); a row that cannot run is not taken.</summary>
    public void SetFocus(int index)
    {
        if (index >= -1 && index < _rows.Count && (index < 0 || _rows[index].Enabled) && index != Focused)
        {
            Focused = index;
            FocusMoved?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Moves the keyboard's row <paramref name="step"/> rows that can run on (negative: back), wrapping; the row now, or -1 when none can run.</summary>
    public int MoveFocus(int step)
    {
        int next = Next(Focused, step);
        if (next != Focused)
        {
            Focused = next;
            FocusMoved?.Invoke(this, EventArgs.Empty);
        }

        return Focused;
    }

    /// <summary>
    /// Runs row <paramref name="index"/>: the menu closes first, then its command runs through the host with
    /// <see cref="EditorContextMenus.Target"/> set to the menu's target while it starts. False for a row that cannot run.
    /// </summary>
    public async Task<bool> RunAsync(int index)
    {
        if (index < 0 || index >= _rows.Count || !_rows[index].Enabled)
        {
            return false;
        }

        var row = _rows[index];
        var target = Target;
        Close();
        var before = EditorContextMenus.Target;
        EditorContextMenus.Target = target;
        Task<bool> run;
        try
        {
            run = _host.ExecuteCommandAsync(row.CommandId);    // the command reads the target before its first await
        }
        finally
        {
            EditorContextMenus.Target = before;
        }

        return await run.ConfigureAwait(true);
    }

    /// <summary>The menu's keys while one is open (see the type); false with none open, or for a chord that goes on to the bindings.</summary>
    public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor)
    {
        if (!IsOpen)
        {
            return false;
        }

        bool plain = (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0;
        if (!plain)
        {
            Close();
            return false;
        }

        switch (key)
        {
            case EditorKey.Escape:
                Close();
                return true;
            case EditorKey.Down or EditorKey.Tab when (modifiers & KeyModifiers.Shift) == 0 || key == EditorKey.Down:
                MoveFocus(1);
                return true;
            case EditorKey.Up or EditorKey.Tab:
                MoveFocus(-1);
                return true;
            case EditorKey.Home:
                Focused = -1;
                MoveFocus(1);
                return true;
            case EditorKey.End:
                Focused = _rows.Count;
                MoveFocus(-1);
                return true;
            case EditorKey.Enter or EditorKey.Space:
                if (Focused >= 0)
                {
                    _ = RunAsync(Focused);
                }

                return true;
            case var letterKey when LetterOf(letterKey) is char letter:
                for (int step = 1; step <= _rows.Count; step++)
                {
                    int i = (Math.Max(Focused, -1) + step + _rows.Count) % _rows.Count;
                    if (_rows[i].Enabled && _rows[i].Title.Length > 0 && char.ToLowerInvariant(_rows[i].Title[0]) == letter)
                    {
                        SetFocus(i);
                        break;
                    }
                }

                return true;
            default:
                return true;                                     // the menu has the keyboard while it is open
        }
    }

    /// <summary>The window's key handler with the open menu first, then <paramref name="inner"/> (the dialogs and the bindings).</summary>
    public IEditorKeyHandler Before(IEditorKeyHandler inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new First(this, inner);
    }

    // The letter a letter key names (the enum's names of the letters are the letters), or null.
    private static char? LetterOf(EditorKey key)
    {
        string name = key.ToString();
        return name.Length == 1 && char.IsAsciiLetter(name[0]) ? char.ToLowerInvariant(name[0]) : null;
    }

    private int Next(int from, int step)
    {
        int count = _rows.Count;
        if (count == 0)
        {
            return -1;
        }

        int at = from;
        for (int tried = 0; tried < count; tried++)
        {
            at = at < 0 && step < 0 ? count - 1 : (((at + step) % count) + count) % count;
            if (_rows[at].Enabled)
            {
                return at;
            }
        }

        return -1;
    }

    private sealed class First(MenuModel menu, IEditorKeyHandler inner) : IEditorKeyHandler
    {
        public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor) =>
            menu.HandleKey(key, modifiers, editor) || inner.HandleKey(key, modifiers, editor);
    }
}

/// <summary>
/// The tooltips of the shell's icon-only controls (phase X): the action's name and, when a key runs it, its keys in
/// effect (<see cref="EditorHost.KeysOf"/>), so a remap on the Keyboard page shows in the tooltip at once.
/// </summary>
public static class ShellTooltips
{
    /// <summary>"Explorer (Ctrl+B)", or "Explorer" when no key runs <paramref name="commandId"/>.</summary>
    public static string For(EditorHost host, string title, string? commandId)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(title);
        return commandId is not null && host.KeysOf(commandId) is { } keys ? $"{title} ({keys})" : title;
    }
}
