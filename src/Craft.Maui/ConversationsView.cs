// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Diagnostics;
using Craft.Editor;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>A context menu the conversation view asks the shell to open: which, on what, and where in the anchor.</summary>
public sealed class ConversationMenuEventArgs(string menu, ContextMenuTarget target, VisualElement anchor, Point at) : EventArgs
{
    /// <summary>The menu (<see cref="EditorContextMenus.Conversation"/>, <see cref="EditorContextMenus.ConversationPanel"/>).</summary>
    public string Menu { get; } = menu;

    /// <summary>What it acts on.</summary>
    public ContextMenuTarget Target { get; } = target;

    /// <summary>The view the point is in.</summary>
    public VisualElement Anchor { get; } = anchor;

    /// <summary>The point in <see cref="Anchor"/>.</summary>
    public Point At { get; } = at;
}

/// <summary>
/// The plug-ins' conversation panels (<see cref="IConversationPanel"/>: the AI plug-in's chat and agent run) as one
/// conversation view (X part 10; the approved design's chat column, docs/design/Main.dc.html and Agent.dc.html): a header
/// with a tab per panel, the title and subtitle, the past conversations, the panel's own buttons, its menu (copy and export
/// the conversation), Expand and Close; the transcript as one selectable read-only text in Craft's own editor surface
/// (<see cref="ConversationTranscript"/>: drag across messages, Shift+click, Ctrl+A, Ctrl+C as Markdown, only the shown lines
/// laid out), "Jump to latest" while the user reads above a streaming reply; the composer (the context chips, the message
/// box growing to <c>CRAFT_COMPOSER_MAX_LINES</c>, the "/" and "@" picker, Send, Stop, the model chip). The logic is the
/// headless <see cref="ConversationController"/>; this view only draws it. Colours only from the theme. It is a drop target
/// for files (the shown panel takes them).
/// </summary>
public sealed class ConversationsView : ContentView, IDisposable
{
    private readonly EditorHost _host;
    private readonly HorizontalStackLayout _tabs = new() { Spacing = 2 };
    private readonly Label _title = new() { FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Label _subtitle = new() { FontSize = 12, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly HorizontalStackLayout _headerActions = new() { Spacing = 2, VerticalOptions = LayoutOptions.Center };
    private readonly EditorView _transcriptView;
    private readonly Grid _body = new();
    private readonly EmptyStateView _empty;
    private readonly Border _jump;
    private readonly Label _status = new() { FontSize = 12, Padding = new Thickness(14, 2), LineBreakMode = LineBreakMode.WordWrap };
    private readonly FlexLayout _chips = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly VerticalStackLayout _suggestions = new() { Spacing = 0, IsVisible = false };
    private readonly ComposerEditor _input = new() { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 40 };
    private readonly Button _send;
    private readonly Button _stop;
    private readonly CraftInput _inputBox;
    private readonly Button _history;
    private readonly Button _menu;
    private readonly Button _expand;
    private readonly Button _close;
    private readonly Label _editing = new() { FontSize = 12, IsVisible = false };
    private readonly VerticalStackLayout _footer = new() { Spacing = 8, Padding = new Thickness(12, 8, 12, 12) };
    private readonly ConversationListView _list;
    private bool _syncing;
    private bool _expanded;
    private ShellPalette _palette;
    private IConversationPanel? _shown;
    private EditorColors _colors = EditorThemes.Get(EditorThemes.Dark).Colors;
    private bool _disposed;

    /// <summary>The conversation panels of <paramref name="host"/>; the transcript in <paramref name="codeFont"/>.</summary>
    public ConversationsView(EditorHost host, string codeFont, UndoBar? undo = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        var palette = new ShellPalette(EditorThemes.Get(EditorThemes.Dark), host.Options);
        _palette = palette;
        Controller = new ConversationController(host, host.Options, new MauiEditorClipboard(failed: error => host.Log(EditorLogLevel.Warning, "The clipboard failed.", error)), undo);
        Controller.Notice += (_, text) => Notice?.Invoke(this, text);
        Controller.MenuRequested += (_, message) => OpenMessageMenu(message.Entry, null);
        Controller.LinkRequested += (_, url) => _ = OpenLinkAsync(url);
        Controller.ComposerFocusRequested += (_, _) => FocusInput();
        Controller.Composer.TextChanged += (_, _) => SyncInput();
        Controller.Composer.SuggestionsChanged += (_, _) => DrawSuggestions();
        Controller.Composer.Symbols = () => SymbolsSource?.Invoke() ?? [];

        // The transcript: Craft's editor surface, read-only, no line numbers, the chat's colours on the panel's surface.
        _transcriptView = new EditorView
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            Theme = ChatClasses.Theme(palette.Theme),
            Options = ConversationTranscript.EditorOptions(host.Options with { FontFamily = codeFont }),
            KeyHandler = new TranscriptKeys(this),
        };
        _transcriptView.ModelChanged += (_, _) => Controller.Transcript.Attach(_transcriptView.Model);
        _transcriptView.Open(Controller.Transcript.Document, Controller.Transcript.Language);
        _transcriptView.ContextRequested += OnTranscriptContext;

        _empty = new EmptyStateView(palette);
        _empty.ActionChosen += (_, action) =>
        {
            if (action.CommandId is { } command)
            {
                _ = _host.ExecuteCommandAsync(command);
            }
        };
        var jumpLabel = palette.Label("↓  Jump to latest", palette.Text, palette.SmallSize);
        _jump = new Border
        {
            Content = jumpLabel,
            Padding = new Thickness(12, 5),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 10),
            IsVisible = false,
        };
        palette.States(_jump, InteractionKind.Flat);
        var jumpTap = new TapGestureRecognizer();
        jumpTap.Tapped += (_, _) => Controller.Transcript.JumpToLatest();
        _jump.GestureRecognizers.Add(jumpTap);
        SemanticProperties.SetDescription(_jump, "Jump to the latest message and follow it");
        _list = new ConversationListView(this, palette) { IsVisible = false };
        _body.Add(_transcriptView);
        _body.Add(_empty);
        _body.Add(_jump);
        _body.Add(_list);
        Controller.Transcript.FollowChanged += (_, _) => _jump.IsVisible = Controller.Transcript.ShowsJump;
        Controller.List.Changed += (_, _) => _list.Rebuild();

        // The header: the panels' tabs, the title, its buttons.
        _history = Icon(palette, "☰", "Past conversations", () => ShowList(!_list.IsVisible));
        _menu = Icon(palette, "⋯", "More: copy or export the conversation", OpenPanelMenu);
        _expand = Icon(palette, "⤢", "Expand over the editor (Ctrl+Shift+Alt+M)", () => ExpandRequested?.Invoke(this, EventArgs.Empty));
        _close = Icon(palette, "✕", "Close the chat (Ctrl+Alt+I opens it again)", () => CloseRequested?.Invoke(this, EventArgs.Empty));
        var titles = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { _title, _subtitle } };
        var header = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
            ],
            ColumnSpacing = 2,
            Padding = new Thickness(14, 6, 6, 6),
            MinimumHeightRequest = 44,
        };
        header.Add(titles, 0, 0);
        header.Add(_headerActions, 1, 0);
        header.Add(_history, 2, 0);
        header.Add(_menu, 3, 0);
        header.Add(_expand, 4, 0);
        header.Add(_close, 5, 0);

        // The composer: chips, the picker, the box (one border, note 12) with Send and Stop, the model chip.
        _inputBox = new CraftInput(palette, _input, radius: 8, multiline: true);
        ModelChip = new ModelChip(palette);
        _send = palette.Primary("Send", 32);
        _send.Clicked += (_, _) => _ = SendAsync();
        _stop = palette.SecondaryButton("Stop", 32);
        _stop.Clicked += (_, _) => _ = Controller.Composer.StopAsync();
        SemanticProperties.SetDescription(_send, "Send the message (Enter)");
        SemanticProperties.SetDescription(_stop, "Stop the reply");
        var keys = new ComposerInput(() => _ = SendAsync(), InsertNewLine, () =>
        {
            if (Controller.Composer.EditingEntry >= 0)
            {
                Controller.Composer.CancelEdit();
                UpdateEditing();
                return;
            }

            if (_expanded)
            {
                ExpandRequested?.Invoke(this, EventArgs.Empty);   // Escape restores the column
                return;
            }

            LeaveRequested?.Invoke(this, EventArgs.Empty);
        });
        _input.RouteKey = (key, modifiers) => RouteComposerKey(key, modifiers, keys);
        _input.TextChanged += (_, _) =>
        {
            if (!_syncing)
            {
                Controller.Composer.Typed(_input.Text ?? "", _input.CursorPosition);
                UpdateButtons();
            }
        };
        var buttons = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.End, Children = { _stop, _send } };
        var below = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        below.Add(ModelChip, 0, 0);
        below.Add(buttons, 1, 0);
        _footer.Children.Add(_editing);
        _footer.Children.Add(_chips);
        _footer.Children.Add(_suggestions);
        _footer.Children.Add(_inputBox);
        _footer.Children.Add(below);

        var layout = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
            ],
        };
        layout.Add(_tabs, 0, 0);
        layout.Add(header, 0, 1);
        layout.Add(_body, 0, 2);
        layout.Add(_status, 0, 3);
        layout.Add(_footer, 0, 4);
        // X part 5: a file dragged from the tree, or files from the system's file manager, dropped on the view go to the
        // shown panel (IConversationPanel.AcceptDropAsync); the composer shows the accent while a drag is over it.
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += (_, e) =>
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            _footer.BackgroundColor = _palette.Selection;
        };
        drop.DragLeave += (_, _) => _footer.BackgroundColor = Colors.Transparent;
        drop.Drop += async (_, e) =>
        {
            _footer.BackgroundColor = Colors.Transparent;
            await OnDropAsync(e);
        };
        layout.GestureRecognizers.Add(drop);
        Content = layout;
        host.PanelsChanged += OnPanelsChanged;
        OnPanelsChanged(this, EventArgs.Empty);
    }

    /// <summary>The view's logic (the transcript, the composer, the past conversations).</summary>
    public ConversationController Controller { get; }

    /// <summary>The composer's model chip (the shell sets its text and handles its tap).</summary>
    public ModelChip ModelChip { get; }

    /// <summary>Reads the paths a drop from outside the app carries (the shell sets it: <c>IShellPlatform.DroppedPathsAsync</c>), or null for none.</summary>
    public Func<DropEventArgs, Task<IReadOnlyList<string>>>? DroppedPaths { get; set; }

    /// <summary>The open documents' symbols for "@" (the shell sets it), or null.</summary>
    public Func<IReadOnlyList<ComposerSuggestion>>? SymbolsSource { get; set; }

    /// <summary>Lists the composer's "@" picker again (the active editor's symbols landed).</summary>
    public void RefreshMentions() => Controller.Composer.RefreshSuggestions();

    /// <summary>The window's key handler, for the transcript's keys it does not take (Ctrl+J, the palette).</summary>
    public Craft.Editor.Abstractions.IEditorKeyHandler? WindowKeys { get; set; }

    /// <summary>Raised with what the view says after an action (a copy, a drop), for the status bar.</summary>
    public event EventHandler<string>? Notice;

    /// <summary>Raised to open a context menu (a message's, the panel's).</summary>
    public event EventHandler<ConversationMenuEventArgs>? MenuRequested;

    /// <summary>Raised when the header's Expand (or Escape while expanded) asks to expand or restore.</summary>
    public event EventHandler? ExpandRequested;

    /// <summary>Whether <paramref name="drop"/> landed on the view while it shows (the window's own drop leaves it to the view).</summary>
    public bool Holds(DropEventArgs drop)
    {
        ArgumentNullException.ThrowIfNull(drop);
        return IsVisible && Width > 0 && drop.GetPosition(this) is { } at && at.X >= 0 && at.Y >= 0 && at.X < Width && at.Y < Height;
    }

    /// <summary>Whether the view shows expanded (the button's glyph and tooltip, Escape restores).</summary>
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            _expand.Text = value ? "⤡" : "⤢";
            ToolTipProperties.SetText(_expand, value ? "Restore the chat column (Escape)" : "Expand over the editor (Ctrl+Shift+Alt+M)");
            SemanticProperties.SetDescription(_expand, value ? "Restore the chat column" : "Expand the chat over the editor");
        }
    }

    // A drop on the view: the tree's file (its path in the drag's properties), else what the system's file manager dropped.
    private async Task OnDropAsync(DropEventArgs e)
    {
        try
        {
            if (_shown is not { } panel)
            {
                return;
            }

            IReadOnlyList<string> paths = e.Data.Properties.TryGetValue(FolderTreeView.DragPathKey, out var dragged) && dragged is string path
                ? [path]
                : DroppedPaths is { } read ? await read(e) : [];
            if (paths.Count == 0)
            {
                return;
            }

            e.Handled = true;
            if (await ConversationDrops.DropAsync(panel, paths) is { } said)
            {
                Notice?.Invoke(this, said);
            }
        }
#pragma warning disable CA1031 // An async drop handler: nothing may leave it (the owner's note 20).
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the chat column: a drop", error);
        }
    }

    /// <summary>Takes <paramref name="palette"/>'s colours and fonts (the composer, its chip, the header's buttons, the list).</summary>
    public void ApplyPalette(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _inputBox.Apply(palette);
        _input.FontFamily = palette.Font();
        _input.FontSize = palette.FontSize;
        _input.MaximumHeightRequest = (palette.FontSize * 1.45 * Math.Max(1, palette.Options.ComposerMaxLines)) + 16;
        ModelChip.Apply(palette);
        _title.FontFamily = palette.Font(FontWeight.SemiBold);
        _subtitle.FontFamily = palette.Font();
        _list.Apply(palette);
        _jump.BackgroundColor = palette.RaisedHigh;
        _jump.Stroke = palette.BorderStrong;
        Controller.Options = palette.Options;
        _transcriptView.Options = ConversationTranscript.EditorOptions(palette.Options with { FontFamily = _transcriptView.Options.FontFamily });
        DrawSuggestions();
    }

    /// <summary>Whether the host has a conversation panel to show.</summary>
    public bool HasPanels => Panels().Any();

    /// <summary>The panel shown, or null.</summary>
    public IConversationPanel? Shown => _shown;

    /// <summary>Raised when <see cref="HasPanels"/> may have changed.</summary>
    public event EventHandler? PanelsChanged;

    /// <summary>Raised when another panel is shown (its model role may differ: the chip follows it).</summary>
    public event EventHandler? ShownChanged;

    /// <summary>Raised when the close button is clicked (the shell closes the column and keeps that per workspace).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Shows the panel <paramref name="id"/> ("ai.chat", "ai.agent"); false when none has that id.</summary>
    public bool Show(string id)
    {
        if (Panels().FirstOrDefault(p => p.Id == id) is not { } panel)
        {
            return false;
        }

        Select(panel);
        return true;
    }

    /// <summary>Shows or hides the past conversations in place of the transcript.</summary>
    public void ShowList(bool show)
    {
        _list.IsVisible = show && Controller.List.IsAvailable;
        if (_list.IsVisible)
        {
            _list.Rebuild();
            _list.FocusSearch();
        }
    }

    /// <summary>The model's state changed: the empty panel says the model again.</summary>
    public void ModelChanged()
    {
        if (_shown is { Entries.Count: 0 })
        {
            Refresh();
        }
    }

    /// <summary>The close button's tooltip (the keys in effect for showing or hiding the chat).</summary>
    public void SetCloseTooltip(string text) => ToolTipProperties.SetText(_close, text);

    /// <summary>Puts the keyboard's focus in the input.</summary>
    public void FocusInput() => _input.Focus();

    /// <summary>Raised when the composer's leave key (Escape) asks to give the keyboard back to the editor.</summary>
    public event EventHandler? LeaveRequested;

    /// <summary>Takes the theme's colours.</summary>
    public void ApplyTheme(ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _colors = theme.Colors;
        Controller.Transcript.Colors = ChatClasses.Theme(theme).Colors;
        _transcriptView.Theme = ChatClasses.Theme(theme);
        BackgroundColor = C(_colors.Surface);
        _title.TextColor = C(_colors.Foreground);
        _subtitle.TextColor = C(_colors.SecondaryText);
        _status.TextColor = C(_colors.SecondaryText);
        _editing.TextColor = C(_colors.Accent);
        foreach (var button in (ReadOnlySpan<Button>)[_history, _menu, _expand, _close])
        {
            button.TextColor = C(_colors.SecondaryText);
        }

        RebuildTabs();
        Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.PanelsChanged -= OnPanelsChanged;
        if (_shown is not null)
        {
            _shown.ConversationChanged -= OnConversationChanged;
        }

        Controller.Dispose();
        _transcriptView.Dispose();
    }

    private IEnumerable<IConversationPanel> Panels() => _host.Panels.OfType<IConversationPanel>();

    private void OnPanelsChanged(object? sender, EventArgs e)
    {
        if (_shown is null || !Panels().Contains(_shown))
        {
            Select(Panels().FirstOrDefault());
        }

        RebuildTabs();
        PanelsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Select(IConversationPanel? panel)
    {
        if (_shown is not null)
        {
            _shown.ConversationChanged -= OnConversationChanged;
        }

        bool changed = !ReferenceEquals(_shown, panel);
        _shown = panel;
        if (panel is not null)
        {
            panel.ConversationChanged += OnConversationChanged;
        }

        Controller.Panel = panel;
        _list.IsVisible = false;
        RebuildTabs();
        Refresh();
        if (changed)
        {
            ShownChanged?.Invoke(this, EventArgs.Empty);      // the model chip shows the shown panel's role (note 25)
        }
    }

    // The transcript writes itself; the view redraws only its chrome (title, status, chips, buttons, empty state).
    private void OnConversationChanged(object? sender, ConversationChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _shown) && (e.Change == ConversationChange.Reset || _empty.IsVisible))
        {
            Refresh();
        }
    }

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        var panels = Panels().ToList();
        _tabs.IsVisible = panels.Count > 1;
        _tabs.Padding = new Thickness(10, 6, 10, 0);
        foreach (var panel in panels)
        {
            bool active = ReferenceEquals(panel, _shown);
            var tab = _palette.Flat(panel.Title.Length > 24 ? panel.Title[..23] + "…" : panel.Title, active ? C(_colors.Foreground) : C(_colors.SecondaryText), 30, _palette.SmallSize);
            tab.Padding = new Thickness(12, 0);
            tab.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
            tab.BorderColor = active ? C(_colors.Accent) : Colors.Transparent;
            tab.BorderWidth = active ? 1 : 0;
            var target = panel;
            tab.Clicked += (_, _) => Select(target);
            _tabs.Children.Add(tab);
        }
    }

    private void Refresh()
    {
        _headerActions.Children.Clear();
        _chips.Children.Clear();
        if (_shown is not { } panel)
        {
            _title.Text = "";
            _subtitle.Text = "";
            _status.Text = "";
            _input.Placeholder = "";
            IsVisible = false;
            return;
        }

        IsVisible = true;
        _title.Text = panel.Title.Length > 40 ? panel.Title[..39] + "…" : panel.Title;
        _subtitle.Text = panel.Subtitle ?? "";
        _subtitle.IsVisible = _subtitle.Text.Length > 0;
        _status.Text = panel.Status ?? "";
        _status.IsVisible = panel.Status is { Length: > 0 };
        _input.Placeholder = panel.Placeholder + (panel.SlashCommands.Count > 0 ? "  (/ for commands, @ for files)" : "");
        _history.IsVisible = Controller.List.IsAvailable;
        foreach (var action in panel.HeaderActions)
        {
            if (action.Label == "Stop")
            {
                continue;                                // Stop is the composer's, beside Send
            }

            var button = _palette.Flat(action.Label, C(_colors.SecondaryText), 30, _palette.SmallSize);
            button.Padding = new Thickness(10, 0);
            var run = action;
            button.Clicked += async (_, _) =>
            {
                try
                {
                    await run.RunAsync(CancellationToken.None);
                }
#pragma warning disable CA1031 // A header button's failure is logged; the view goes on.
                catch (Exception error)
#pragma warning restore CA1031
                {
                    CallbackGuard.Report("the chat: a header button", error);
                }
            };
            _headerActions.Children.Add(button);
        }

        _empty.IsVisible = panel.Entries.Count == 0;
        if (_empty.IsVisible)
        {
            // Phase X: before the first message, what the panel does and which model answers (the first entry replaces it).
            _empty.Show(ShellEmptyStates.Conversation(panel, ShellText.ModelStatus()?.Status));
        }

        foreach (var chip in panel.Chips)
        {
            _chips.Children.Add(Chip(panel, chip));
        }

        _chips.IsVisible = panel.Chips.Count > 0;
        UpdateButtons();
        UpdateEditing();
    }

    // A context chip: removable by a tap; a chip that cannot go is greyed and struck through, its reason in the tooltip.
    private Border Chip(IConversationPanel panel, ContextChip chip)
    {
        bool greyed = chip.Problem is not null;
        var label = new Label
        {
            Text = chip.Text + "  ×",
            FontSize = _palette.SmallSize,
            FontFamily = _palette.Font(),
            TextColor = C(greyed ? _colors.SecondaryText : _colors.Foreground),
            TextDecorations = greyed ? TextDecorations.Strikethrough : TextDecorations.None,
            VerticalTextAlignment = TextAlignment.Center,
        };
        var border = new Border
        {
            Content = label,
            Padding = new Thickness(9, 3),
            Margin = new Thickness(0, 0, 6, 6),
            BackgroundColor = greyed ? Colors.Transparent : C(_colors.Raised),
            Stroke = C(_colors.Border),
            StrokeShape = new RoundRectangle { CornerRadius = 11 },
            Opacity = greyed ? 0.7 : 1,
        };
        _palette.States(border, InteractionKind.Flat);
        ToolTipProperties.SetText(border, chip.Problem ?? "Remove from the context");
        SemanticProperties.SetDescription(border, greyed ? $"Context: {chip.Text}. Not sent: {chip.Problem} Tap to remove." : $"Context: {chip.Text}. Tap to remove.");
        var tap = new TapGestureRecognizer();
        string id = chip.Id;
        tap.Tapped += (_, _) => panel.RemoveChip(id);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private void UpdateButtons()
    {
        bool busy = _shown?.IsBusy == true;
        _stop.IsVisible = busy;
        _send.Text = busy && _shown?.ShowsTimeline == true ? "Add note" : Controller.Composer.EditingEntry >= 0 ? "Resend" : "Send";
        _send.IsEnabled = !busy || _shown?.ShowsTimeline == true;
    }

    private void UpdateEditing()
    {
        _editing.IsVisible = Controller.Composer.EditingEntry >= 0;
        _editing.Text = "Editing your message: Send replaces it and what came after it (Escape cancels).";
        UpdateButtons();
    }

    // The box shows the composer's text (a recalled message, a quote, a picked row), the caret where it says.
    private void SyncInput()
    {
        _syncing = true;
        try
        {
            if (!string.Equals(_input.Text, Controller.Composer.Text, StringComparison.Ordinal))
            {
                _input.Text = Controller.Composer.Text;
            }

            _input.CursorPosition = Controller.Composer.Caret;
        }
        finally
        {
            _syncing = false;
        }

        UpdateEditing();
    }

    // The composer's keys: the picker's and the history's first, then the composer's bindings (Enter sends).
    private bool RouteComposerKey(EditorKey key, KeyModifiers modifiers, ComposerInput keys)
    {
        var composer = Controller.Composer;
        if (!string.Equals(composer.Text, _input.Text ?? "", StringComparison.Ordinal) || composer.Caret != _input.CursorPosition)
        {
            composer.Typed(_input.Text ?? "", _input.CursorPosition);
        }

        return composer.HandleKey(key, modifiers) || _host.HandleKey(key, modifiers, keys);
    }

    private void DrawSuggestions()
    {
        _suggestions.Children.Clear();
        var rows = Controller.Composer.Suggestions;
        _suggestions.IsVisible = rows.Count > 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool chosen = i == Controller.Composer.Selected;
            var text = new FormattedString();
            text.Spans.Add(new Span { Text = row.Label, TextColor = C(_colors.Foreground), FontFamily = _palette.Font(FontWeight.Medium) });
            if (row.Detail is { Length: > 0 } detail)
            {
                text.Spans.Add(new Span { Text = "   " + detail, TextColor = C(_colors.SecondaryText) });
            }

            var label = new Label { FormattedText = text, FontSize = _palette.SmallSize, LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = TextAlignment.Center };
            var item = new Border
            {
                Content = label,
                Padding = new Thickness(10, 5),
                StrokeThickness = 0,
                BackgroundColor = chosen ? C(_colors.InterfaceSelection) : Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
            };
            _palette.States(item, InteractionKind.Row);
            int index = i;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                Controller.Composer.Accept(index);
                FocusInput();
            };
            item.GestureRecognizers.Add(tap);
            _suggestions.Children.Add(item);
        }
    }

    private void InsertNewLine()
    {
        int at = Math.Clamp(_input.CursorPosition, 0, _input.Text?.Length ?? 0);
        _input.Text = (_input.Text ?? "").Insert(at, "\n");
        _input.CursorPosition = at + 1;
    }

    private async Task SendAsync()
    {
        try
        {
            Controller.Composer.Typed(_input.Text ?? "", _input.CursorPosition);
            Controller.Transcript.JumpToLatest();
            await Controller.Composer.SendAsync();
        }
#pragma warning disable CA1031 // A send that fails is logged; the composer keeps going.
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the chat: send", error);
        }
    }

    // A right-click on the transcript: the menu of the message under the pointer.
    private void OnTranscriptContext(object? sender, TappedEventArgs e)
    {
        if (_transcriptView.Model is not { } model || e.GetPosition(_transcriptView) is not { } at)
        {
            return;
        }

        float x = (float)at.X < model.TextLeft ? model.TextLeft : (float)at.X + model.ScrollX;
        int position = model.PositionFromPoint(x, (float)at.Y + model.ScrollY);
        int entry = Controller.Transcript.EntryAt(position);
        if (entry >= 0)
        {
            OpenMessageMenu(entry, at);
        }
    }

    // A message's menu at `at` in the transcript, else under its first line.
    private void OpenMessageMenu(int entry, Point? at)
    {
        var where = at;
        if (where is null && _transcriptView.Model is { } model && Controller.Transcript.FirstLineOf(entry) is >= 0 and var line)
        {
            where = new Point(Math.Max(24, _transcriptView.Width - 180), ((line + 1) * model.Metrics.LineHeight) - model.ScrollY);
        }

        MenuRequested?.Invoke(this, new ConversationMenuEventArgs(EditorContextMenus.Conversation,
            new ContextMenuTarget(EditorContextMenus.Conversation, null, false, new ConversationMessage(Controller, entry)), _transcriptView, where ?? new Point(24, 24)));
    }

    private void OpenPanelMenu() =>
        MenuRequested?.Invoke(this, new ConversationMenuEventArgs(EditorContextMenus.ConversationPanel,
            new ContextMenuTarget(EditorContextMenus.ConversationPanel, null, false, Controller), _menu, new Point(0, _menu.Height)));

    private async Task OpenLinkAsync(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(uri);
            }
        }
#pragma warning disable CA1031 // A link that cannot open is said.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Notice?.Invoke(this, $"The link could not open: {error.Message}");
        }
    }

    private static Button Icon(ShellPalette palette, string glyph, string tooltip, Action run)
    {
        var button = palette.Flat(glyph, palette.Secondary, 30, palette.FontSize + 1);
        button.WidthRequest = 32;
        button.Padding = new Thickness(0);
        ToolTipProperties.SetText(button, tooltip);
        SemanticProperties.SetDescription(button, tooltip);
        button.Clicked += (_, _) => run();
        return button;
    }

    private static Color C(uint argb) => ThemeColors.Of(argb);

    // The transcript's keys: Escape restores the expanded view, then the controller's (Ctrl+C as Markdown), the editor's and the window's.
    private sealed class TranscriptKeys(ConversationsView owner) : Craft.Editor.Abstractions.IEditorKeyHandler
    {
        private Craft.Editor.Abstractions.IEditorKeyHandler? _inner;

        public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor)
        {
            if (key == EditorKey.Escape && modifiers == KeyModifiers.None && owner._expanded)
            {
                owner.ExpandRequested?.Invoke(owner, EventArgs.Empty);
                return true;
            }

            _inner ??= owner.Controller.TranscriptKeys(() => owner.WindowKeys);
            return _inner.HandleKey(key, modifiers, editor);
        }
    }

    /// <summary>The past conversations in place of the transcript: a search, New, the rows (open, rename in place, delete with Undo).</summary>
    private sealed class ConversationListView : ContentView
    {
        private readonly ConversationsView _owner;
        private readonly Entry _search;
        private readonly CraftInput _searchBox;
        private readonly VerticalStackLayout _rows = new() { Spacing = 2 };
        private readonly Button _new;
        private ShellPalette _palette;
        private string? _renaming;

        public ConversationListView(ConversationsView owner, ShellPalette palette)
        {
            _owner = owner;
            _palette = palette;
            _search = palette.Input("Search conversations", 30);
            _search.TextChanged += (_, _) => owner.Controller.List.Search = _search.Text ?? "";
            _searchBox = palette.Field(_search);
            _new = palette.Primary("New", 30);
            _new.Clicked += (_, _) =>
            {
                owner.Controller.List.New();
                owner.ShowList(false);
                owner.FocusInput();
            };
            var top = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
            top.Add(_searchBox, 0, 0);
            top.Add(_new, 1, 0);
            Content = new Grid
            {
                RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
                Padding = new Thickness(12),
                RowSpacing = 10,
                Children = { top },
            };
            var scroll = new ScrollView { Content = _rows };
            ((Grid)Content).Add(scroll, 0, 1);
        }

        public void Apply(ShellPalette palette)
        {
            _palette = palette;
            _searchBox.Apply(palette);
            BackgroundColor = palette.Surface;
            Rebuild();
        }

        public void FocusSearch() => _search.Focus();

        public void Rebuild()
        {
            if (!IsVisible)
            {
                return;
            }

            _rows.Children.Clear();
            var list = _owner.Controller.List;
            var now = DateTimeOffset.Now;
            foreach (var row in list.Rows)
            {
                _rows.Children.Add(Row(row, string.Equals(row.Id, list.Current, StringComparison.Ordinal), now));
            }

            if (_rows.Children.Count == 0)
            {
                _rows.Children.Add(_palette.Label(list.Search.Length > 0 ? "No conversation holds that." : "No conversation yet: the first message starts one.", _palette.Secondary, _palette.SmallSize));
            }
        }

        private Border Row(ConversationSummary row, bool current, DateTimeOffset now)
        {
            var list = _owner.Controller.List;
            View title;
            if (string.Equals(_renaming, row.Id, StringComparison.Ordinal))
            {
                var field = _palette.Input("Title", 28);
                field.Text = row.Title;
                field.Completed += (_, _) =>
                {
                    _renaming = null;
                    list.Rename(row.Id, field.Text ?? "");
                    Rebuild();
                };
                title = _palette.Field(field);
                Dispatcher.DispatchLabelled("chat: rename in place", () => field.Focus());
            }
            else
            {
                title = _palette.Label(row.Title.Length > 0 ? row.Title : "Untitled", current ? _palette.Accent : _palette.Text, _palette.FontSize, current ? FontWeight.SemiBold : FontWeight.Regular);
            }

            var when = _palette.Label(ConversationList.When(row.Time, now) + " · " + row.Messages + (row.Messages == 1 ? " message" : " messages"), _palette.Secondary, _palette.CaptionSize);
            var rename = _palette.Flat("Rename", _palette.Secondary, 26, _palette.CaptionSize);
            rename.Clicked += (_, _) =>
            {
                _renaming = row.Id;
                Rebuild();
            };
            var delete = _palette.Flat("Delete", _palette.Error, 26, _palette.CaptionSize);
            delete.Clicked += (_, _) =>
            {
                list.Delete(row.Id);
                Rebuild();
            };
            var grid = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
                RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)],
                Padding = new Thickness(10, 6),
                ColumnSpacing = 4,
            };
            grid.Add(title, 0, 0);
            grid.Add(when, 0, 1);
            grid.Add(rename, 1, 0);
            grid.Add(delete, 2, 0);
            Grid.SetRowSpan(rename, 2);
            Grid.SetRowSpan(delete, 2);
            var item = new Border { Content = grid, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 6 } };
            _palette.States(item, InteractionKind.Row);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                list.Open(row.Id);
                _owner.ShowList(false);
            };
            item.GestureRecognizers.Add(tap);
            return item;
        }
    }
}

/// <summary>
/// The composer's text box: a multi-line editor whose keys the platform hands to <see cref="RouteKey"/> before the box
/// takes them (Windows: a PreviewKeyDown on its TextBox, <c>Platforms/Windows/ComposerKeys.cs</c>), so Enter sends and
/// Shift+Enter makes a line through the composer's bindings, and the picker and the history take Up, Down and Tab; a key
/// the route declines stays the box's. It never sends on focus loss.
/// </summary>
public sealed class ComposerEditor : Microsoft.Maui.Controls.Editor
{
    /// <summary>Takes a key with its modifiers: true when a binding took it (the box then ignores it).</summary>
    public Func<EditorKey, KeyModifiers, bool>? RouteKey { get; set; }
}
