// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Core;
using Craft.Core.Settings;

namespace Craft.Editor.Shell;

/// <summary>The shell's panels that collapse and resize (one <c>SplitPanel</c> each in the window).</summary>
public enum ShellPanel
{
    /// <summary>The rail's side view at the left: the explorer, or a plug-in panel's view (its width).</summary>
    Explorer,

    /// <summary>The chat and agent column at the right (its width).</summary>
    Chat,

    /// <summary>The bottom panel: Problems, Terminal, Output, Idrak telemetry (its height).</summary>
    Bottom,
}

/// <summary>
/// Whether each panel of the wide layout is open and how large it is: read from the options (<c>CRAFT_PANEL_*</c>, the
/// workspace's <c>.craft/settings.json</c> over the user's), changed by a splitter drag (<see cref="Resize"/> while it
/// moves, <see cref="CommitAsync"/> when it ends), a close button, a chevron or a command (<see cref="SetOpenAsync"/>),
/// and written back to the workspace's settings when a folder is open, else to the user's, on the thread pool (the UI
/// thread never waits on the disk). Which side view shows (<see cref="SideView"/>: the explorer or a plug-in panel's id)
/// lives for the run. The narrow layout does not use it. Used on the UI thread.
/// </summary>
public sealed class PanelLayout
{
    /// <summary>The side view that is the explorer (the folder tree).</summary>
    public const string ExplorerView = "explorer";

    private readonly CraftSettings _settings;
    private readonly Action<string, Exception>? _failed;
    private readonly double[] _sizes = new double[3];
    private readonly bool[] _open = new bool[3];

    /// <summary>The panels of <paramref name="options"/>, written into <paramref name="settings"/>; a write that fails is told to <paramref name="failed"/>.</summary>
    public PanelLayout(CraftSettings settings, CraftOptions options, Action<string, Exception>? failed = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _failed = failed;
        Load(options);
    }

    /// <summary>The smallest size a drag leaves a panel, and the least it leaves the editor (<c>CRAFT_PANEL_MIN_SIZE</c>).</summary>
    public double MinimumSize { get; private set; }

    /// <summary>The side view shown at the left: <see cref="ExplorerView"/>, or the id of a plug-in panel (<see cref="ShowSide"/>).</summary>
    public string SideView { get; private set; } = ExplorerView;

    /// <summary>The last write to the settings (the tests wait on it).</summary>
    public Task LastSave { get; private set; } = Task.CompletedTask;

    /// <summary>Raised when a panel opened, closed, was resized, or the side view changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The variable of <paramref name="panel"/>'s size.</summary>
    public static string SizeVariable(ShellPanel panel) => panel switch
    {
        ShellPanel.Explorer => "CRAFT_PANEL_EXPLORER_WIDTH",
        ShellPanel.Chat => "CRAFT_PANEL_CHAT_WIDTH",
        _ => "CRAFT_PANEL_BOTTOM_HEIGHT",
    };

    /// <summary>The variable of whether <paramref name="panel"/> is open.</summary>
    public static string OpenVariable(ShellPanel panel) => panel switch
    {
        ShellPanel.Explorer => "CRAFT_PANEL_EXPLORER_OPEN",
        ShellPanel.Chat => "CRAFT_PANEL_CHAT_OPEN",
        _ => "CRAFT_PANEL_BOTTOM_OPEN",
    };

    /// <summary>The size a splitter's double-click restores for <paramref name="panel"/> (B8): the option's default.</summary>
    public static double DefaultSizeOf(ShellPanel panel)
    {
        var defaults = new CraftOptions();
        return panel switch
        {
            ShellPanel.Explorer => defaults.TreeWidth,
            ShellPanel.Chat => defaults.ChatWidth,
            _ => defaults.BottomPanelHeight,
        };
    }

    /// <summary><paramref name="panel"/>'s width (the explorer, the chat) or height (the bottom panel).</summary>
    public double SizeOf(ShellPanel panel) => _sizes[(int)panel];

    /// <summary>Whether <paramref name="panel"/> is open.</summary>
    public bool IsOpen(ShellPanel panel) => _open[(int)panel];

    /// <summary>Takes the sizes and open states of <paramref name="options"/> (a folder opened, a setting changed).</summary>
    public void Load(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        MinimumSize = options.PanelMinimumSize;
        bool changed = Set(ShellPanel.Explorer, options.TreeWidth, options.ExplorerOpen)
            | Set(ShellPanel.Chat, options.ChatWidth, options.ChatOpen)
            | Set(ShellPanel.Bottom, options.BottomPanelHeight, options.BottomPanelOpen);
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// <paramref name="size"/> kept within what a drag may leave: at least <see cref="MinimumSize"/>, and at most
    /// <paramref name="room"/> (what the panel and the editor share) less <see cref="MinimumSize"/> for the editor.
    /// </summary>
    public double Clamp(double size, double room)
    {
        double min = MinimumSize;
        double max = room > 0 ? Math.Max(min, room - min) : double.MaxValue;
        return Math.Clamp(size, min, max);
    }

    /// <summary>A drag moved <paramref name="panel"/>'s edge: its size now (not written until <see cref="CommitAsync"/>).</summary>
    public void Resize(ShellPanel panel, double size)
    {
        if (double.IsFinite(size) && size > 0 && Math.Abs(size - _sizes[(int)panel]) >= 0.5)
        {
            _sizes[(int)panel] = size;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A drag ended: <paramref name="panel"/>'s size is written to the settings.</summary>
    public Task CommitAsync(ShellPanel panel)
    {
        double lowest = panel == ShellPanel.Chat ? 120 : 40;           // what the variables take (CraftOptions)
        double size = Math.Clamp(Math.Round(_sizes[(int)panel]), lowest, 10_000);
        return Store(SizeVariable(panel), size.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Opens or closes <paramref name="panel"/> and writes it to the settings.</summary>
    public Task SetOpenAsync(ShellPanel panel, bool open)
    {
        if (_open[(int)panel] == open)
        {
            return Task.CompletedTask;
        }

        _open[(int)panel] = open;
        Changed?.Invoke(this, EventArgs.Empty);
        return Store(OpenVariable(panel), open ? "true" : "false");
    }

    /// <summary>Closes <paramref name="panel"/> when open, opens it when closed.</summary>
    public Task ToggleAsync(ShellPanel panel) => SetOpenAsync(panel, !IsOpen(panel));

    /// <summary>Shows <paramref name="view"/> (<see cref="ExplorerView"/> or a plug-in panel's id) as the side view, opening the side.</summary>
    public Task ShowSide(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        if (!string.Equals(view, SideView, StringComparison.Ordinal))
        {
            SideView = view;
            if (IsOpen(ShellPanel.Explorer))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        return SetOpenAsync(ShellPanel.Explorer, true);
    }

    /// <summary>A rail item of side view <paramref name="view"/> was clicked: shown when another view shows or the side is closed, else the side closes.</summary>
    public Task ToggleSide(string view) =>
        IsOpen(ShellPanel.Explorer) && string.Equals(view, SideView, StringComparison.Ordinal) ? SetOpenAsync(ShellPanel.Explorer, false) : ShowSide(view);

    /// <summary>A plug-in panel shown as the side view went away: the explorer takes its place.</summary>
    public void Forget(string view)
    {
        if (string.Equals(view, SideView, StringComparison.Ordinal))
        {
            SideView = ExplorerView;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool Set(ShellPanel panel, double size, bool open)
    {
        int i = (int)panel;
        bool changed = _sizes[i] != size || _open[i] != open;
        _sizes[i] = size;
        _open[i] = open;
        return changed;
    }

    // The workspace's file when a folder is open (sizes are per workspace), else the user's; none: kept for the run only.
    private Task Store(string variable, string value)
    {
        var layer = _settings.Workspace is not null ? SettingLayer.Workspace : SettingLayer.User;
        if ((layer == SettingLayer.User ? _settings.User : _settings.Workspace) is null)
        {
            return Task.CompletedTask;
        }

        LastSave = StoreAsync(layer, variable, value);
        return LastSave;
    }

    private async Task StoreAsync(SettingLayer layer, string variable, string value)
    {
        try
        {
            await _settings.StoreAsync(layer, variable, value).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _failed?.Invoke($"The panel's {variable} was not written: {error.Message}", error);
        }
    }
}
