// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Preview;
using Craft.Editor.Spelling;
using Craft.Editor.Testing;
using Craft.Editor.Tools;

namespace Craft.Editor.Shell;

/// <summary>The commands of X part 9's tools.</summary>
public static class ToolCommands
{
    /// <summary>Markdown: Open preview (Ctrl+Shift+V).</summary>
    public const string MarkdownPreview = "craft.markdown.preview";

    /// <summary>Markdown: Open preview to the side (Ctrl+K V).</summary>
    public const string MarkdownPreviewBeside = "craft.markdown.previewBeside";

    /// <summary>Test: Run all tests.</summary>
    public const string RunAllTests = "craft.tests.runAll";

    /// <summary>Test: Run failed tests.</summary>
    public const string RunFailedTests = "craft.tests.runFailed";

    /// <summary>Test: Run the test at the caret.</summary>
    public const string RunTestAtCaret = "craft.tests.runAtCaret";

    /// <summary>View: TODO list (the bottom panel's tab).</summary>
    public const string TodoList = "craft.todo.show";

    /// <summary>Image: Open as text (an SVG's source).</summary>
    public const string OpenImageAsText = "craft.image.openAsText";
}

// X part 9, the tools (items 18-22): the Test explorer (a rail side view), the TODO list (a bottom panel tab), the Markdown
// and image previews (page tabs of the editor area) and the spell check (the shared checker, its quick fixes).
public sealed partial class ShellModel
{
    /// <summary>The prefix of a Markdown preview's page id (then the source's full path).</summary>
    public const string MarkdownPagePrefix = "preview:";

    /// <summary>The prefix of an image preview's page id (then the image's full path).</summary>
    public const string ImagePagePrefix = "image:";

    private readonly Dictionary<string, MarkdownPreview> _markdownPreviews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImagePreview> _imagePreviews = new(StringComparer.Ordinal);
    private IDisposable? _testsPanel;
    private IDisposable? _imageStatusItem;
    private StatusItem? _imageStatus;
    private bool _spellingRegistered;
    private bool _openAsText;

    /// <summary>The Test explorer (the rail's Testing).</summary>
    public TestExplorerView Tests { get; private set; } = null!;

    /// <summary>The TODO list (the bottom panel's TODO tab).</summary>
    public TodoListView Todo { get; private set; } = null!;

    /// <summary>The open Markdown previews, by page id.</summary>
    public IReadOnlyDictionary<string, MarkdownPreview> MarkdownPreviews => _markdownPreviews;

    /// <summary>The open image previews (once read), by page id.</summary>
    public IReadOnlyDictionary<string, ImagePreview> ImagePreviews => _imagePreviews;

    /// <summary>The status bar's text for the image shown (its format, size, bytes and zoom), or null when none shows.</summary>
    public string? ImageStatusText => _imageStatus?.Text;

    /// <summary>
    /// Opens the preview of the Markdown document <paramref name="tab"/> (the active one when null) as a page tab; with
    /// <paramref name="beside"/>, the source stays in view beside it (its own group to the right). Null when there is no
    /// Markdown document.
    /// </summary>
    public PageTab? OpenMarkdownPreview(DocumentTab? tab = null, bool beside = false)
    {
        tab ??= Groups.Focused.Tabs.Active ?? Tabs.Active;
        if (tab is null || !IsMarkdown(tab))
        {
            Host.ShowStatus("Markdown preview", "Open a Markdown file (.md) first: the preview renders the document you edit.");
            return null;
        }

        string id = MarkdownPagePrefix + (tab.FilePath ?? "untitled-" + tab.UntitledNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!_markdownPreviews.TryGetValue(id, out var preview))
        {
            preview = new MarkdownPreview(action => Host.Dispatch("markdown preview: a render landed", action), () => Options);
            preview.Attach(Host, tab.HostDocument);
            _markdownPreviews[id] = preview;
        }

        if (beside && ReferenceEquals(Groups.GroupOf(tab), Groups.Groups[0]))
        {
            // The pages show in the first group: the source is shown in a group on its right too (one document in two tabs)
            // and the preview takes the first group's place.
            Groups.Split(Groups.Groups[0], SplitDirection.Right, tab, move: false);
        }

        return Pages.Open(id, "Preview " + tab.Title);
    }

    /// <summary>Opens the image <paramref name="path"/> in a preview page tab, read off the UI thread; completes when it is read.</summary>
    public async Task<PageTab> OpenImageAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        string id = ImagePagePrefix + full;
        var page = Pages.Open(id, Path.GetFileName(full));
        Recent.Add(full, Core.Workspaces.RecentKind.File);
        if (!_imagePreviews.ContainsKey(id))
        {
            var image = await ImagePreview.LoadAsync(full, (long)Options.ImagePreviewMaxMegabytes * 1024 * 1024).ConfigureAwait(true);
            if (Pages.Find(id) is not null)
            {
                _imagePreviews[id] = image;
                ImageChanged?.Invoke(this, id);
                ShowImageStatus(image.Problem is null ? image.StatusText(image.ScaleFor(0, 0)) : image.Problem);
            }
        }

        return page;
    }

    /// <summary>Raised (with the page id) when an image preview was read: its view draws it.</summary>
    public event EventHandler<string>? ImageChanged;

    /// <summary>Shows <paramref name="text"/> (an image's format, size and zoom) in the status bar while an image page shows; null takes it away.</summary>
    public void ShowImageStatus(string? text)
    {
        if (text is null)
        {
            _imageStatusItem?.Dispose();
            _imageStatusItem = null;
            _imageStatus = null;
            return;
        }

        if (_imageStatus is null)
        {
            _imageStatus = new StatusItem("craft.image", text, StatusAlignment.Right, 50) { Tooltip = "The image's format, size in pixels, bytes and zoom" };
            _imageStatusItem = Host.AddStatusItem(_imageStatus);
        }
        else
        {
            _imageStatus.Text = text;
        }
    }

    /// <summary>Opens the file <paramref name="path"/> as text (an SVG's source) whatever its kind.</summary>
    public async Task<DocumentTab?> OpenAsTextAsync(string path, CancellationToken cancellationToken = default)
    {
        _openAsText = true;
        try
        {
            return await OpenFileAsync(path, cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _openAsText = false;
        }
    }

    // OpenFileAsync's question: an image opens in its preview (not as text) unless asked as text.
    private bool OpensAsImage(string full) => !_openAsText && ImageFormats.IsImage(full);

    private static bool IsMarkdown(DocumentTab tab) =>
        tab.Language?.Name.Equals("markdown", StringComparison.OrdinalIgnoreCase) == true
        || (tab.FilePath ?? tab.SuggestedPath) is { } path && (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase));

    private void CreateTools()
    {
        Tests = new TestExplorerView(Host, Run, () => Workspace?.Root, () => Options);
        Todo = new TodoListView(Host, () => Workspace?.Root, () => Options);
        _testsPanel = Host.AddPanel(Tests);
        Pages.Closed += OnToolPageClosed;
        Pages.Changed += OnToolPagesChanged;
        WorkspaceChanged += OnToolsWorkspaceChanged;
        OptionsChanged += OnToolsOptionsChanged;
        SpellChecker.Shared.Post = action => Host.Dispatch("spelling: what the checker knows changed", action);
        ConfigureSpelling();
        if (CodeActionProviders.Find(SpellingCodeActions.ProviderName) is null)
        {
            CodeActionProviders.Register(new SpellingCodeActions(Host, SpellChecker.Shared));
            _spellingRegistered = true;
        }

        if (Workspace is not null)
        {
            OnToolsWorkspaceChanged(this, EventArgs.Empty);
        }
    }

    private void ConfigureSpelling() => SpellChecker.Shared.Configure(Options, Host.DataFolder, Workspace?.Root);

    private void OnToolsWorkspaceChanged(object? sender, EventArgs e)
    {
        Tests.WorkspaceChanged();
        Todo.WorkspaceChanged();
        ConfigureSpelling();
    }

    private void OnToolsOptionsChanged(object? sender, EventArgs e) => ConfigureSpelling();

    private void OnToolPageClosed(object? sender, PageTab page)
    {
        if (_markdownPreviews.Remove(page.Id, out var preview))
        {
            preview.Dispose();
        }

        _imagePreviews.Remove(page.Id);
    }

    private void OnToolPagesChanged(object? sender, EventArgs e)
    {
        if (Pages.Active is { } page && _imagePreviews.TryGetValue(page.Id, out var image))
        {
            ShowImageStatus(image.Problem ?? image.StatusText(image.ScaleFor(0, 0)));
        }
        else if (Pages.Active is not { } shown || !shown.Id.StartsWith(ImagePagePrefix, StringComparison.Ordinal))
        {
            ShowImageStatus(null);
        }
    }

    // The test at the caret of the active document: the nearest test declared at or above the caret's line in its file.
    private Task RunTestAtCaretAsync(CancellationToken cancellationToken)
    {
        if (Workspace is not { } workspace || Host.ActiveDocument is not { FilePath: { } path } document)
        {
            return Task.CompletedTask;
        }

        string relative = WorkspaceFiles.Relative(workspace.Root, Path.GetFullPath(path));
        int line = document.Document.Buffer.Current.GetLineIndex(Math.Min(document.Selection.Caret, document.Document.Buffer.Current.Length));
        var test = Tests.Tests.Where(t => string.Equals(t.File, relative, StringComparison.OrdinalIgnoreCase) && t.Line <= line).MaxBy(t => t.Line);
        if (test is null)
        {
            Host.ShowStatus("Tests", "No test is declared at or above the caret in this file.");
            return Task.CompletedTask;
        }

        return Tests.RunAsync([test], all: false, cancellationToken);
    }

    private void RegisterToolCommands()
    {
        Command(ToolCommands.MarkdownPreview, "Markdown: Open preview", (_, _) =>
        {
            OpenMarkdownPreview();
            return Task.CompletedTask;
        }, _ => Tabs.Active is { } tab && IsMarkdown(tab), null);
        Command(ToolCommands.MarkdownPreviewBeside, "Markdown: Open preview to the side", (_, _) =>
        {
            OpenMarkdownPreview(beside: true);
            return Task.CompletedTask;
        }, _ => Tabs.Active is { } tab && IsMarkdown(tab), null);
        // The preview's keys act in the text editor only (a terminal keeps Ctrl+Shift+V for its paste).
        foreach (var (keys, id) in (ReadOnlySpan<(string, string)>)[("Ctrl+Shift+V", ToolCommands.MarkdownPreview), ("Ctrl+K V", ToolCommands.MarkdownPreviewBeside)])
        {
            var (first, then) = KeyBinding.ParseKeys(keys);
            var binding = new KeyBinding(first, id, Then: then, When: KeyContext.TextEditor);
            EditorKeyBindings.Register(binding);
            _bindings.Add(binding);
        }
        Command(ToolCommands.RunAllTests, "Test: Run all tests", async (_, token) => await Tests.RunAllAsync(token).ConfigureAwait(true), _ => Tests.Tests.Count > 0, null);
        Command(ToolCommands.RunFailedTests, "Test: Run failed tests", async (_, token) => await Tests.RunFailedAsync(token).ConfigureAwait(true), _ => Tests.FailedCount > 0, null);
        Command(ToolCommands.RunTestAtCaret, "Test: Run the test at the caret", (_, token) => RunTestAtCaretAsync(token), _ => Tests.Tests.Count > 0, null);
        Command(ToolCommands.TodoList, "View: TODO list", (_, _) => View(ShellViewNames.Todo), null, null);
        Command(ToolCommands.OpenImageAsText, "Image: Open as text", async (_, _) =>
        {
            if (Pages.Active is { } page && page.Id.StartsWith(ImagePagePrefix, StringComparison.Ordinal))
            {
                await OpenAsTextAsync(page.Id[ImagePagePrefix.Length..]).ConfigureAwait(true);
            }
        }, _ => Pages.Active is { } page && page.Id.StartsWith(ImagePagePrefix, StringComparison.Ordinal), null);
        Menu(EditorMenus.Run, ToolCommands.RunAllTests, 3, 0);
        Menu(EditorMenus.Run, ToolCommands.RunFailedTests, 3, 1);
        Menu(EditorMenus.Run, ToolCommands.RunTestAtCaret, 3, 2);
        Menu(EditorMenus.View, ToolCommands.TodoList, 6, 0);
        Menu(EditorMenus.View, ToolCommands.MarkdownPreview, 6, 1);
        Menu(EditorMenus.View, ToolCommands.MarkdownPreviewBeside, 6, 2);
        Rail(new RailItem(TestExplorerView.PanelId, "Testing", SideViewCommand(TestExplorerView.PanelId), RailIcons.Test, 16));
    }

    private void DisposeTools()
    {
        WorkspaceChanged -= OnToolsWorkspaceChanged;
        OptionsChanged -= OnToolsOptionsChanged;
        Pages.Closed -= OnToolPageClosed;
        Pages.Changed -= OnToolPagesChanged;
        _testsPanel?.Dispose();
        Tests?.Dispose();
        Todo?.Dispose();
        foreach (var preview in _markdownPreviews.Values)
        {
            preview.Dispose();
        }

        _markdownPreviews.Clear();
        _imagePreviews.Clear();
        ShowImageStatus(null);
        if (_spellingRegistered)
        {
            CodeActionProviders.Unregister(SpellingCodeActions.ProviderName);
            _spellingRegistered = false;
        }

        if (SpellChecker.Shared.Post is not null)
        {
            SpellChecker.Shared.Post = null;
        }
    }
}
