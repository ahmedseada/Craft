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
/// The plug-ins' panels of an <see cref="EditorHost"/>, one under the other in a scrolling column: each its title and its
/// items (a click on an item with a command runs it). It hides itself when there is no panel.
/// </summary>
public sealed class PanelsView : ScrollView, IDisposable
{
    private readonly EditorHost _host;
    private readonly VerticalStackLayout _stack = new() { Spacing = 4, Padding = new Thickness(8) };
    private readonly List<IEditorPanel> _shown = [];
    private Color _textColor = Colors.Gray;

    /// <summary>The panels of <paramref name="host"/>.</summary>
    public PanelsView(EditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Content = _stack;
        host.PanelsChanged += OnPanelsChanged;
        Rebuild();
    }

    /// <summary>The text's colour.</summary>
    public Color TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;
            Rebuild();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _host.PanelsChanged -= OnPanelsChanged;
        Unsubscribe();
    }

    private void OnPanelsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnPanelChanged(object? sender, EventArgs e) => Rebuild();

    private void Unsubscribe()
    {
        foreach (var panel in _shown)
        {
            panel.Changed -= OnPanelChanged;
        }

        _shown.Clear();
    }

    private void Rebuild()
    {
        Unsubscribe();
        _stack.Children.Clear();
        foreach (var panel in _host.Panels)
        {
            if (panel is IConversationPanel)
            {
                continue;   // the chat column's (ConversationsView)
            }

            panel.Changed += OnPanelChanged;
            _shown.Add(panel);
            _stack.Children.Add(new Label { Text = panel.Title, FontAttributes = FontAttributes.Bold, TextColor = _textColor, Margin = new Thickness(0, 6, 0, 2) });
            foreach (var item in panel.Items)
            {
                var label = new Label
                {
                    Text = item.Detail is null ? item.Text : $"{item.Text}  {item.Detail}",
                    FontSize = 12,
                    TextColor = _textColor,
                    LineBreakMode = LineBreakMode.TailTruncation,
                };
                if (item.CommandId is { } command)
                {
                    var tap = new TapGestureRecognizer();
                    tap.Tapped += (_, _) => _ = _host.ExecuteCommandAsync(command);
                    label.GestureRecognizers.Add(tap);
                }

                _stack.Children.Add(label);
            }
        }

        IsVisible = _shown.Count > 0;
    }
}

/// <summary>
/// The host's questions as pages and dialogs: a proposed edit is shown as a diff (<see cref="ProposalDiffPage"/>) and
/// applied only on Accept; a message is an alert.
/// </summary>
/// <param name="page">The page the views are shown over.</param>
/// <param name="theme">The theme in use (the shell's), read each time a view opens; null: the light or dark built-in by the app's theme.</param>
public sealed class MauiEditorPresenter(Page page, Func<ITheme>? theme = null) : IEditorPresenter
{
    private readonly Page _page = page ?? throw new ArgumentNullException(nameof(page));

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
    public Task ShowMessageAsync(string title, string message) => _page.DisplayAlertAsync(title, message, "OK");

    /// <inheritdoc />
    /// <remarks>
    /// An action sheet whose title holds the question, its message and its facts, one a line, and one button per answer;
    /// dismissing it gives the question's <see cref="EditorQuestion.Dismissed"/>. (The Agent page's own card is phase A2's.)
    /// </remarks>
    public async Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (question.Choices.Count == 0)
        {
            return question.Dismissed;
        }

        var shown = question.Choices.ToArray();
        string title = string.Join('\n', [question.Title, question.Message, .. question.Details]);
        string? picked = await _page.DisplayActionSheetAsync(title, "Cancel", null, shown);
        cancellationToken.ThrowIfCancellationRequested();
        int index = picked is null ? -1 : Array.IndexOf(shown, picked);
        return index >= 0 ? index : question.Dismissed;
    }
}

/// <summary>The command palette: the commands that can run now, by title, in a list; the one picked runs.</summary>
public static class CommandPalette
{
    /// <summary>Shows the palette on <paramref name="page"/> for <paramref name="host"/>; true when a command was picked and ran.</summary>
    public static async Task<bool> ShowAsync(Page page, EditorHost host)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(host);
        var commands = new List<IEditorCommand>();
        foreach (var command in host.Commands)
        {
            bool can;
            try
            {
                can = command.CanExecute(host);
            }
#pragma warning disable CA1031 // A command whose check throws is left out of the list, never a crash.
            catch (Exception)
#pragma warning restore CA1031
            {
                can = false;
            }

            if (can)
            {
                commands.Add(command);
            }
        }

        if (commands.Count == 0)
        {
            await page.DisplayAlertAsync("Commands", "No command can run now.", "OK");
            return false;
        }

        commands.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
        var titles = commands.Select(c => Chorded(c)).ToArray();
        var picked = await page.DisplayActionSheetAsync("Commands", "Cancel", null, titles);
        int index = picked is null ? -1 : Array.IndexOf(titles, picked);
        return index >= 0 && await host.ExecuteCommandAsync(commands[index].Name);
    }

    // "Title (Ctrl+Shift+W)" when a binding runs it.
    private static string Chorded(IEditorCommand command)
    {
        foreach (var binding in EditorKeyBindings.All)
        {
            if (binding.CommandId.Equals(command.Name, StringComparison.OrdinalIgnoreCase))
            {
                return $"{command.Title} ({binding.Name})";
            }
        }

        return command.Title;
    }
}
