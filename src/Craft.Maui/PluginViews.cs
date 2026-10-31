// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Craft.Maui;

/// <summary>
/// The plug-ins' status items of an <see cref="EditorHost"/>, as labels in a wrapping row (the left end's, a spacer, the
/// right end's); a click on an item with a command runs it. It follows the host's items and each item's text.
/// </summary>
public sealed class StatusItemsView : FlexLayout, IDisposable
{
    private readonly EditorHost _host;
    private readonly List<(IStatusItem Item, Label Label)> _shown = [];
    private Color _textColor = Colors.Gray;

    /// <summary>The items of <paramref name="host"/>.</summary>
    public StatusItemsView(EditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Wrap = FlexWrap.Wrap;
        AlignItems = FlexAlignItems.Center;
        host.StatusItemsChanged += OnItemsChanged;
        Rebuild();
    }

    /// <summary>The labels' colour.</summary>
    public Color TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;
            foreach (var (_, label) in _shown)
            {
                label.TextColor = value;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _host.StatusItemsChanged -= OnItemsChanged;
        ClearItems();
    }

    private void OnItemsChanged(object? sender, EventArgs e) => Rebuild();

    private void ClearItems()
    {
        foreach (var (item, _) in _shown)
        {
            item.Changed -= OnItemChanged;
        }

        _shown.Clear();
        Children.Clear();
    }

    private void Rebuild()
    {
        ClearItems();
        bool right = false;
        foreach (var item in _host.StatusItems)
        {
            if (!right && item.Alignment == StatusAlignment.Right)
            {
                right = true;
                var spacer = new BoxView { Color = Colors.Transparent, HeightRequest = 1 };
                SetGrow((Microsoft.Maui.IView)spacer, 1);
                Children.Add(spacer);
            }

            var label = new Label { Text = item.Text, FontSize = 12, Margin = new Thickness(0, 0, 16, 0), TextColor = _textColor, VerticalTextAlignment = TextAlignment.Center };
            ToolTipProperties.SetText(label, item.Tooltip ?? "");
            if (item.CommandId is { } command)
            {
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => _ = _host.ExecuteCommandAsync(command);
                label.GestureRecognizers.Add(tap);
            }

            item.Changed += OnItemChanged;
            _shown.Add((item, label));
            Children.Add(label);
        }
    }

    private void OnItemChanged(object? sender, EventArgs e)
    {
        foreach (var (item, label) in _shown)
        {
            if (ReferenceEquals(item, sender))
            {
                label.Text = item.Text;
                ToolTipProperties.SetText(label, item.Tooltip ?? "");
            }
        }
    }
}

/// <summary>
/// A plug-in panel (<see cref="IEditorPanel"/>, not a conversation) as a side view of the rail (phase U2: there is no fixed
/// column of panels): its title, then its items in a scrolling column (a click on an item with a command runs it). It is
/// drawn again when the panel raises <see cref="IEditorPanel.Changed"/>, through the dispatcher when that comes off the UI thread.
/// </summary>
public sealed class PanelView : ContentView, IDisposable
{
    private readonly EditorHost _host;
    private readonly VerticalStackLayout _stack = new() { Spacing = 4, Padding = new Thickness(12, 8) };
    private readonly Label _title = new() { FontAttributes = FontAttributes.Bold, Margin = new Thickness(12, 10, 12, 2) };
    private Color _textColor = Colors.Gray;
    private Color _secondary = Colors.Gray;

    /// <summary>The view of <paramref name="panel"/>; its commands run through <paramref name="host"/>.</summary>
    public PanelView(EditorHost host, IEditorPanel panel)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(panel);
        _host = host;
        Panel = panel;
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        grid.Add(_title, 0, 0);
        grid.Add(new ScrollView { Content = _stack }, 0, 1);
        Content = grid;
        panel.Changed += OnPanelChanged;
        Rebuild();
    }

    /// <summary>The panel shown.</summary>
    public IEditorPanel Panel { get; }

    /// <summary>Takes the text colours.</summary>
    public void SetColors(Color text, Color secondary)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(secondary);
        _textColor = text;
        _secondary = secondary;
        Rebuild();
    }

    /// <inheritdoc />
    public void Dispose() => Panel.Changed -= OnPanelChanged;

    private void OnPanelChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("plug-ins page: rebuild", Rebuild);
        }
        else
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _title.Text = Panel.Title;
        _title.TextColor = _textColor;
        _stack.Children.Clear();
        foreach (var item in Panel.Items)
        {
            var text = new Label { Text = item.Text, FontSize = 12, TextColor = _textColor, LineBreakMode = LineBreakMode.WordWrap };
            var row = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(0, 2) };
            row.Children.Add(text);
            if (item.Detail is { } detail)
            {
                row.Children.Add(new Label { Text = detail, FontSize = 11, TextColor = _secondary, LineBreakMode = LineBreakMode.WordWrap });
            }

            if (item.CommandId is { } command)
            {
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => _ = _host.ExecuteCommandAsync(command);
                row.GestureRecognizers.Add(tap);
                ToolTipProperties.SetText(row, EditorCommands.Find(command)?.Title ?? command);
            }

            _stack.Children.Add(row);
        }
    }
}

/// <summary>
/// The host's questions as pages and Craft's dialogs: a proposed edit is shown as a diff (<see cref="ProposalDiffPage"/>)
/// and applied only on Accept; a message and a question are a <see cref="CraftDialog"/> (never the platform's message box).
/// </summary>
/// <param name="page">The page the diff view is shown over.</param>
/// <param name="dialog">The window's dialogs.</param>
/// <param name="theme">The theme in use (the shell's), read each time a view opens; null: the light or dark built-in by the app's theme.</param>
public sealed class MauiEditorPresenter(Page page, CraftDialog dialog, Func<ITheme>? theme = null) : IEditorPresenter
{
    private readonly Page _page = page ?? throw new ArgumentNullException(nameof(page));
    private readonly CraftDialog _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));

    /// <inheritdoc />
    /// <remarks>
    /// The diff-and-accept view (<see cref="ProposalDiffPage"/>, plan phase A1) as a modal page: Accept applies, Reject,
    /// closing it or a cancelled request leaves the document as it was.
    /// </remarks>
    public async Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken)
    {
        var answers = await ChooseHunksAsync(proposal, cancellationToken);
        return answers.Count > 0 && answers.All(a => a);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> ChooseHunksAsync(EditProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var view = new ProposalDiffPage(proposal, Theme());
        await using var cancel = cancellationToken.Register(view.Reject);
        await _page.Navigation.PushModalAsync(view);
        var answers = await view.Answer;
        if (_page.Navigation.ModalStack.Contains(view))
        {
            await _page.Navigation.PopModalAsync();
        }

        return answers;
    }

    private ITheme Theme() => theme?.Invoke()
        ?? EditorThemes.Get(Application.Current?.RequestedTheme == AppTheme.Light ? EditorThemes.Light : EditorThemes.Dark);

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message) => _dialog.ShowMessageAsync(title, message);

    /// <inheritdoc />
    /// <remarks>
    /// A Craft dialog: the title, the message, the facts in the code font (a command's program, folder, limits: the
    /// sandbox's approval), one button per answer (a list when there are more than three); Escape, or the request's
    /// cancellation, gives the question's <see cref="EditorQuestion.Dismissed"/>.
    /// </remarks>
    public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken) => _dialog.AskAsync(question, cancellationToken);
}
