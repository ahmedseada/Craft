// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>An open document in a tab: its text document (the tabs own it), the host's view of it, its title.</summary>
public sealed class DocumentTab
{
    internal DocumentTab(ITextDocument document, IEditorDocument hostDocument, ILanguage? language, int untitledNumber)
    {
        Document = document;
        HostDocument = hostDocument;
        Language = language;
        UntitledNumber = untitledNumber;
        WasDirty = document.IsDirty;
        LastPath = document.FilePath;
        Title = "";
    }

    /// <summary>The document (disposed when the tab closes).</summary>
    public ITextDocument Document { get; }

    /// <summary>The document as the editor host and its plug-ins see it.</summary>
    public IEditorDocument HostDocument { get; internal set; }

    /// <summary>Its language, or null for plain text.</summary>
    public ILanguage? Language { get; internal set; }

    /// <summary>Its file, or null for a new document.</summary>
    public string? FilePath => Document.FilePath;

    /// <summary>
    /// The file a new document is made for and not yet written (a plug-in's new file, such as the agent's): Save writes it
    /// there, creating its folders, without asking; null once saved, and for every other tab.
    /// </summary>
    public string? SuggestedPath { get; internal set; }

    /// <summary>Whether it has unsaved changes.</summary>
    public bool IsDirty => Document.IsDirty;

    /// <summary>What the tab shows: the file's name (with its folder's when another tab has the same name), or "untitled-N".</summary>
    public string Title { get; internal set; }

    /// <summary>The number of a new document's "untitled-N" (0 for one opened from a file).</summary>
    public int UntitledNumber { get; }

    /// <summary>The UI's own object for the tab (its editor view); the tabs never read it.</summary>
    public object? View { get; set; }

    internal bool WasDirty { get; set; }

    internal string? LastPath { get; set; }

    internal EventHandler? StateHandler { get; set; }
}

/// <summary>
/// The open documents as tabs, in the order they opened, and the active one, kept in step with an
/// <see cref="EditorHost"/>: a tab added opens its document in the host, the active tab is the host's active document, a
/// tab removed closes it in the host and disposes its document. A file opens in one tab only (<see cref="Find"/>).
/// <see cref="TabChanged"/> is raised when a tab's title or dirty marker changes (not on every edit), so the view redraws
/// a tab only then. Asking about unsaved changes is the shell's (<see cref="ShellModel.CloseTabAsync"/>); <see cref="Remove"/>
/// closes at once. Used on the UI thread.
/// </summary>
public sealed class DocumentTabs : IDisposable
{
    private readonly EditorHost _host;
    private readonly List<DocumentTab> _tabs = [];
    private int _nextUntitled = 1;

    /// <summary>The tabs of <paramref name="host"/>.</summary>
    public DocumentTabs(EditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>The tabs, in the order they opened.</summary>
    public IReadOnlyList<DocumentTab> Tabs => _tabs;

    /// <summary>The active tab, or null when none is open.</summary>
    public DocumentTab? Active { get; private set; }

    /// <summary>The active tab's index, or -1.</summary>
    public int ActiveIndex => Active is null ? -1 : _tabs.IndexOf(Active);

    /// <summary>Whether any tab has unsaved changes.</summary>
    public bool HasUnsaved => _tabs.Exists(t => t.IsDirty);

    /// <summary>Raised when a tab was added or removed (the list changed).</summary>
    public event EventHandler? TabsChanged;

    /// <summary>Raised when <see cref="Active"/> changed.</summary>
    public event EventHandler? ActiveChanged;

    /// <summary>Raised when a tab's title or dirty marker changed.</summary>
    public event EventHandler<DocumentTab>? TabChanged;

    /// <summary>The tab of the file <paramref name="path"/>, or null.</summary>
    public DocumentTab? Find(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        return _tabs.Find(t => t.FilePath is { } file && PathKeys.Comparer.Equals(file, full));
    }

    /// <summary>
    /// Opens <paramref name="document"/> (the tabs own it from now on) in a new tab in <paramref name="language"/>, after
    /// the others, and makes it active when <paramref name="activate"/>.
    /// </summary>
    public DocumentTab Add(ITextDocument document, ILanguage? language, string? languageName = null, bool activate = true) =>
        Add(document, language, languageName, activate, suggestedPath: null);

    /// <summary>
    /// Opens the new <paramref name="document"/> made for the file <paramref name="suggestedPath"/>, not written yet (its
    /// <see cref="DocumentTab.SuggestedPath"/>; titled with that file's name), as <see cref="Add(ITextDocument, ILanguage?, string?, bool)"/> does.
    /// </summary>
    public DocumentTab AddFor(ITextDocument document, string suggestedPath, ILanguage? language, bool activate = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedPath);
        return Add(document, language, null, activate, Path.GetFullPath(suggestedPath));
    }

    /// <summary>The new document's tab made for <paramref name="path"/> (<see cref="DocumentTab.SuggestedPath"/>), or null.</summary>
    public DocumentTab? FindSuggested(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        return _tabs.Find(t => t.FilePath is null && t.SuggestedPath is { } suggested && PathKeys.Comparer.Equals(suggested, full));
    }

    private DocumentTab Add(ITextDocument document, ILanguage? language, string? languageName, bool activate, string? suggestedPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        var hostDocument = _host.Open(document, language, languageName);
        var tab = new DocumentTab(document, hostDocument, language, document.FilePath is null ? _nextUntitled++ : 0) { SuggestedPath = suggestedPath };
        tab.StateHandler = (_, _) => OnStateChanged(tab);
        document.StateChanged += tab.StateHandler;
        _tabs.Add(tab);
        Retitle();
        TabsChanged?.Invoke(this, EventArgs.Empty);
        if (activate || Active is null)
        {
            Activate(tab);
        }

        return tab;
    }

    /// <summary>Makes <paramref name="tab"/> the active one (and the host's active document).</summary>
    public void Activate(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!_tabs.Contains(tab))
        {
            throw new ArgumentException("The tab is not open.", nameof(tab));
        }

        _host.SetActive(tab.HostDocument);
        if (!ReferenceEquals(Active, tab))
        {
            Active = tab;
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Moves <paramref name="tab"/> to <paramref name="index"/> (a tab dragged along the strip; clamped to the tabs), the
    /// others keeping their order; false when it is not open or already there. The active tab stays active.
    /// </summary>
    public bool Move(DocumentTab tab, int index)
    {
        ArgumentNullException.ThrowIfNull(tab);
        int from = _tabs.IndexOf(tab);
        if (from < 0)
        {
            return false;
        }

        int to = Math.Clamp(index, 0, _tabs.Count - 1);
        if (to == from)
        {
            return false;
        }

        _tabs.RemoveAt(from);
        _tabs.Insert(to, tab);
        TabsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Moves the dragged tab <paramref name="dragged"/> onto <paramref name="target"/>'s place (a drop on a tab): after it
    /// when it came from the left, before it when from the right, as a strip shows a dragged tab passing another.
    /// </summary>
    public bool MoveOnto(DocumentTab dragged, DocumentTab target)
    {
        ArgumentNullException.ThrowIfNull(dragged);
        ArgumentNullException.ThrowIfNull(target);
        int to = _tabs.IndexOf(target);
        return to >= 0 && !ReferenceEquals(dragged, target) && Move(dragged, to);
    }

    /// <summary>Activates the next tab (after the last, the first); false when there are fewer than two.</summary>
    public bool Next() => Step(1);

    /// <summary>Activates the previous tab (before the first, the last); false when there are fewer than two.</summary>
    public bool Previous() => Step(-1);

    /// <summary>
    /// Closes <paramref name="tab"/> at once (its unsaved changes are lost: ask first): closes its document in the host and
    /// disposes it. When it was active, the tab after it (else before it) becomes active.
    /// </summary>
    public void Remove(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        int index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        bool wasActive = ReferenceEquals(Active, tab);
        _tabs.RemoveAt(index);
        tab.Document.StateChanged -= tab.StateHandler;
        _host.Close(tab.HostDocument);
        tab.Document.Dispose();
        Retitle();
        TabsChanged?.Invoke(this, EventArgs.Empty);
        if (wasActive)
        {
            Active = null;
            if (_tabs.Count > 0)
            {
                Activate(_tabs[Math.Min(index, _tabs.Count - 1)]);
            }
            else
            {
                _host.SetActive(null);
                ActiveChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Gives <paramref name="tab"/> the language <paramref name="language"/> (after Save As to a new extension): its
    /// document is closed in the host and opened again in that language, so the plug-ins see the change.
    /// </summary>
    public void SetLanguage(DocumentTab tab, ILanguage? language, string? languageName = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!_tabs.Contains(tab) || ReferenceEquals(tab.Language, language))
        {
            return;
        }

        bool wasActive = ReferenceEquals(Active, tab);
        var selection = tab.HostDocument.Selection;
        _host.Close(tab.HostDocument);
        tab.HostDocument = _host.Open(tab.Document, language, languageName);
        _host.UpdateSelection(tab.HostDocument, selection);
        tab.Language = language;
        if (wasActive)
        {
            _host.SetActive(tab.HostDocument);
        }

        TabChanged?.Invoke(this, tab);
    }

    /// <summary>Closes every tab at once (unsaved changes are lost: ask first).</summary>
    public void Dispose()
    {
        while (_tabs.Count > 0)
        {
            Remove(_tabs[^1]);
        }
    }

    private bool Step(int delta)
    {
        if (_tabs.Count < 2)
        {
            return false;
        }

        int index = Math.Max(0, ActiveIndex);
        Activate(_tabs[(index + delta + _tabs.Count) % _tabs.Count]);
        return true;
    }

    // Raised on every edit; the view hears of it only when the dirty marker or the file changed.
    private void OnStateChanged(DocumentTab tab)
    {
        bool dirty = tab.IsDirty;
        var path = tab.FilePath;
        if (dirty == tab.WasDirty && string.Equals(path, tab.LastPath, StringComparison.Ordinal))
        {
            return;
        }

        tab.WasDirty = dirty;
        if (!string.Equals(path, tab.LastPath, StringComparison.Ordinal))
        {
            tab.LastPath = path;
            if (path is not null)
            {
                tab.SuggestedPath = null;                    // written: its file is its own now
            }

            Retitle();
        }

        TabChanged?.Invoke(this, tab);
    }

    // A file's name, with its folder's name when another tab shows the same file name.
    private void Retitle()
    {
        foreach (var tab in _tabs)
        {
            string title;
            if (tab.FilePath is null && tab.SuggestedPath is { } suggested)
            {
                title = Path.GetFileName(suggested);
            }
            else if (tab.FilePath is not { } path)
            {
                title = "untitled-" + tab.UntitledNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                var name = Path.GetFileName(path);
                bool shared = _tabs.Exists(other => !ReferenceEquals(other, tab) && other.FilePath is { } p && PathKeys.Comparer.Equals(Path.GetFileName(p), name));
                title = shared && Path.GetFileName(Path.GetDirectoryName(path)) is { Length: > 0 } folder ? $"{name} — {folder}" : name;
            }

            if (!string.Equals(title, tab.Title, StringComparison.Ordinal))
            {
                bool first = tab.Title.Length == 0;
                tab.Title = title;
                if (!first)
                {
                    TabChanged?.Invoke(this, tab);
                }
            }
        }
    }
}
