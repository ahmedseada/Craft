// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Editor.Shell;

/// <summary>The shell's pages that open as tabs in the editor area (<see cref="PageTabs"/>).</summary>
public static class ShellPageIds
{
    /// <summary>The settings page (Ctrl+Comma).</summary>
    public const string Settings = "settings";

    /// <summary>The Models and devices page.</summary>
    public const string Models = "models";

    /// <summary>The Plug-ins page.</summary>
    public const string Plugins = "plugins";
}

/// <summary>A page open as a tab of the editor area.</summary>
public sealed class PageTab
{
    internal PageTab(string id, string title)
    {
        Id = id;
        Title = title;
    }

    /// <summary>Its id (<see cref="ShellPageIds"/>, or a plug-in's).</summary>
    public string Id { get; }

    /// <summary>Its tab's title.</summary>
    public string Title { get; }

    /// <summary>The view that shows it (the app's; disposed by the app when the tab closes).</summary>
    public object? View { get; set; }
}

/// <summary>
/// The pages (settings, Models and devices, Plug-ins) open as tabs beside the documents' tabs, in the editor area: never
/// over the window, so the title bar's menus, the rail and the status bar stay in reach while one is open. One tab per
/// page: opening an open one selects it. While a page is active it shows in the editor's place; a document tab chosen (or
/// opened) shows the document again, the page tab staying until it is closed. Used on the UI thread.
/// </summary>
public sealed class PageTabs
{
    private readonly List<PageTab> _tabs = [];

    /// <summary>The open pages, in the order they opened.</summary>
    public IReadOnlyList<PageTab> Tabs => _tabs;

    /// <summary>The page shown in the editor area, or null when a document (or nothing) shows.</summary>
    public PageTab? Active { get; private set; }

    /// <summary>Raised when a page opened, closed, or the one shown changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a page closed (its view may be disposed now).</summary>
    public event EventHandler<PageTab>? Closed;

    /// <summary>The open page <paramref name="id"/>, or null.</summary>
    public PageTab? Find(string id) => _tabs.Find(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    /// <summary>Opens the page <paramref name="id"/> as a tab titled <paramref name="title"/> (the open one when it is open) and shows it.</summary>
    public PageTab Open(string id, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);
        var tab = Find(id);
        if (tab is null)
        {
            tab = new PageTab(id, title);
            _tabs.Add(tab);
        }
        else if (ReferenceEquals(tab, Active))
        {
            return tab;
        }

        Active = tab;
        Changed?.Invoke(this, EventArgs.Empty);
        return tab;
    }

    /// <summary>Shows <paramref name="tab"/> (an open page).</summary>
    public void Activate(PageTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (_tabs.Contains(tab) && !ReferenceEquals(tab, Active))
        {
            Active = tab;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Closes the page <paramref name="id"/>; false when it is not open. The document shows again when it was the one shown.</summary>
    public bool Close(string id)
    {
        if (Find(id) is not { } tab)
        {
            return false;
        }

        _tabs.Remove(tab);
        if (ReferenceEquals(tab, Active))
        {
            Active = null;
        }

        Closed?.Invoke(this, tab);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The document shows again in the editor area (the page tabs stay open).</summary>
    public void ShowDocument()
    {
        if (Active is not null)
        {
            Active = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Closes every page.</summary>
    public void CloseAll()
    {
        while (_tabs.Count > 0)
        {
            Close(_tabs[^1].Id);
        }
    }
}
