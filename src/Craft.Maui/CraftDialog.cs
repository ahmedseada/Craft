// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// Craft's dialog (the owner's note 10): every question, confirmation, consent, message and choice of the window (the
/// host's and the plug-ins' through <see cref="IEditorPresenter"/>, the shell's through <see cref="IShellUi"/>, the About
/// box, the unsaved-changes question, the sandbox's approval, the pickers) shown as an overlay in the window, never the
/// platform's message box: the backdrop dims the window and takes its pointer, a card in the design's tokens and fonts
/// holds the title, the text, the details (in the code font), an optional text field and the actions (the primary one in
/// the accent, a losing one in the error's colour, the way out plain; a picker's rows as a list). Its behaviour is
/// <see cref="DialogQueue"/>'s (one at a time, the next waiting; Escape cancels, Enter takes the focused or default action,
/// Tab moves the focus among the actions and wraps); the focus goes to the default action when it opens and back to what
/// had it when it closes. On Windows the card keeps the Tab key inside it (<c>WindowsControlStyles</c>, by its
/// <see cref="Element.ClassId"/> <see cref="CardClassId"/>).
/// </summary>
public sealed class CraftDialog : Grid
{
    /// <summary>The card's <see cref="Element.ClassId"/>, by which a platform's styles keep the keyboard's focus in it.</summary>
    public const string CardClassId = "craft-dialog-card";

    /// <summary>The <see cref="Element.ClassId"/> of a picker's rows (left-aligned buttons on a platform that can).</summary>
    public const string RowClassId = "craft-dialog-row";

    private readonly BoxView _backdrop = new() { Color = Color.FromRgba(0, 0, 0, 0.5) };
    private readonly Border _card;
    private readonly Label _title = new() { LineBreakMode = LineBreakMode.WordWrap };
    private readonly Label _text = new() { LineBreakMode = LineBreakMode.WordWrap };
    private readonly VerticalStackLayout _details = new() { Spacing = 2 };
    private readonly Border _detailsBox;
    private readonly Entry _field = new();
    private readonly CraftInput _fieldBox;
    private readonly FlexLayout _buttons = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.End };
    private readonly VerticalStackLayout _rows = new() { Spacing = 2 };
    private readonly ScrollView _list;
    private readonly List<Button> _actions = [];
    private ShellPalette _palette;
    private DialogRequest? _shown;
    private VisualElement? _returnFocus;

    /// <summary>The dialogs of the window, drawn with <paramref name="palette"/>; <paramref name="dispatch"/> is the UI thread's dispatcher.</summary>
    public CraftDialog(ShellPalette palette, Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(dispatch);
        _palette = palette;
        Queue = new DialogQueue(dispatch);
        Queue.Changed += (_, _) => Show();
        Queue.FocusMoved += (_, _) => FocusAction(Queue.Focused);
        IsVisible = false;
        // The backdrop takes the pointer (nothing behind the dialog is clicked) and does nothing with it: a question is
        // answered by its actions or Escape, never by a click beside it.
        _backdrop.GestureRecognizers.Add(new TapGestureRecognizer());
        _field.Completed += (_, _) =>
        {
            if (_shown is { } shown && ReferenceEquals(Queue.Current, shown))
            {
                Queue.Confirm(_field.Text);
            }
        };
        _field.Focused += (_, _) => Queue.SetFocus(-1);
        _fieldBox = new CraftInput(palette, _field);
        _fieldBox.HeightRequest = 34;
        _detailsBox = new Border { Content = _details, Padding = new Thickness(10, 8), StrokeShape = new RoundRectangle { CornerRadius = 6 }, StrokeThickness = 1 };
        _list = new ScrollView { Content = _rows, MaximumHeightRequest = 360 };
        _card = new Border
        {
            ClassId = CardClassId,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 1,
            Padding = new Thickness(20, 18),
            Margin = new Thickness(16),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 460,
            Content = new VerticalStackLayout { Spacing = 12, Children = { _title, _text, _detailsBox, _fieldBox, _list, _buttons } },
        };
        SemanticProperties.SetHeadingLevel(_title, SemanticHeadingLevel.Level1);
        Add(_backdrop);
        Add(_card);
        SizeChanged += (_, _) => _card.WidthRequest = Math.Max(240, Math.Min(_shown?.IsList == true ? 560 : 460, Width - 32));
        Apply(palette);
    }

    /// <summary>The dialogs' behaviour (what the keys do, the order); its <see cref="DialogQueue.Before"/> is the window's key handler.</summary>
    public DialogQueue Queue { get; }

    /// <summary>The view the focus goes back to when a dialog closes and nothing had it before (the active editor), or null.</summary>
    public Func<VisualElement?>? FallbackFocus { get; set; }

    /// <summary>Takes <paramref name="palette"/>'s colours and fonts.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _card.BackgroundColor = palette.Raised;
        _card.Stroke = palette.BorderStrong;
        _card.Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Opacity = 0.45f, Radius = 24, Offset = new Point(0, 8) };
        _title.TextColor = palette.Text;
        _title.FontFamily = palette.Font(FontWeight.SemiBold);
        _title.FontSize = palette.FontSize + 2;
        _text.TextColor = palette.Secondary;
        _text.FontFamily = palette.Font();
        _text.FontSize = palette.FontSize;
        _text.LineHeight = 1.4;
        _detailsBox.BackgroundColor = palette.Ground;
        _detailsBox.Stroke = palette.Border;
        _field.FontFamily = palette.Font();
        _field.FontSize = palette.FontSize;
        _fieldBox.Apply(palette);
        if (_shown is not null)
        {
            Build(_shown);
        }
    }

    /// <summary>A message with OK; completes when it is dismissed.</summary>
    public Task ShowMessageAsync(string title, string message) => Queue.ShowAsync(DialogRequest.Message(title, message));

    /// <summary>A plug-in's question: the index of the answer, or its <see cref="EditorQuestion.Dismissed"/>.</summary>
    public async Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (question.Choices.Count == 0)
        {
            return question.Dismissed;
        }

        var result = await Queue.ShowAsync(DialogRequest.Question(question), cancellationToken);
        return DialogRequest.QuestionAnswer(question, result);
    }

    /// <summary>The unsaved-changes question for the document <paramref name="title"/>.</summary>
    public async Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) =>
        DialogRequest.UnsavedAnswer(await Queue.ShowAsync(DialogRequest.Unsaved(title), cancellationToken));

    /// <summary>One of <paramref name="items"/>: its index, or -1.</summary>
    public async Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default, string text = "")
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Count == 0 ? -1 : (await Queue.ShowAsync(DialogRequest.Pick(title, items, text), cancellationToken)).Index;
    }

    /// <summary>A text to type: the text, or null when it was cancelled.</summary>
    public async Task<string?> PromptAsync(string title, string text, string placeholder, string ok = "OK", CancellationToken cancellationToken = default)
    {
        var result = await Queue.ShowAsync(DialogRequest.Prompt(title, text, placeholder, ok), cancellationToken);
        return result.Index == 0 ? result.Text ?? "" : null;
    }

    private void Show()
    {
        var current = Queue.Current;
        if (current is null)
        {
            _shown = null;
            IsVisible = false;
            var back = _returnFocus is { IsVisible: true, IsEnabled: true } ? _returnFocus : FallbackFocus?.Invoke();
            _returnFocus = null;
            back?.Focus();
            return;
        }

        if (!IsVisible)
        {
            // The focus goes back where it was when the last dialog closes.
            _returnFocus = FocusedElement();
        }

        _shown = current;
        Build(current);
        IsVisible = true;
        _card.WidthRequest = Math.Max(240, Math.Min(current.IsList ? 560 : 460, Width > 0 ? Width - 32 : 460));
        if (current.InputPlaceholder is not null)
        {
            Dispatcher.DispatchLabelled("dialog: focus the field", () => _field.Focus());
        }
        else
        {
            int focused = Queue.Focused;
            Dispatcher.DispatchLabelled("dialog: focus the action", () => FocusAction(focused));
        }
    }

    private void Build(DialogRequest request)
    {
        var p = _palette;
        _title.Text = request.Title;
        _text.Text = request.Text;
        _text.IsVisible = request.Text.Length > 0;
        _details.Children.Clear();
        foreach (var line in request.Details)
        {
            var label = p.Label(line, p.Text, p.SmallSize);
            label.FontFamily = p.Options.FontFamily;
            label.LineBreakMode = LineBreakMode.CharacterWrap;
            _details.Children.Add(label);
        }

        _detailsBox.IsVisible = request.Details.Count > 0;
        _fieldBox.IsVisible = request.InputPlaceholder is not null;
        _field.Placeholder = request.InputPlaceholder ?? "";
        _field.Text = request.InputText;
        _actions.Clear();
        _buttons.Children.Clear();
        _rows.Children.Clear();
        _list.IsVisible = request.IsList;
        for (int i = 0; i < request.Actions.Count; i++)
        {
            var action = request.Actions[i];
            int index = i;
            Button button;
            if (request.IsList && action.Role != DialogActionRole.Cancel)
            {
                button = p.Flat(action.Label, p.Text, 32);
                button.ClassId = RowClassId;
                button.HorizontalOptions = LayoutOptions.Fill;
                button.Padding = new Thickness(10, 0);
                button.LineBreakMode = LineBreakMode.TailTruncation;
                _rows.Children.Add(button);
            }
            else
            {
                button = action.Role switch
                {
                    DialogActionRole.Primary => p.Primary(action.Label, 32),
                    _ => p.SecondaryButton(action.Label, 32),
                };
                if (action.Role == DialogActionRole.Danger)
                {
                    button.TextColor = p.Error;
                }

                button.Margin = new Thickness(8, 0, 0, 0);
                _buttons.Children.Add(button);
            }

            // Only the dialog the button was made for: a key that answered it already (and showed the next) answers nothing more.
            button.Clicked += (_, _) =>
            {
                if (ReferenceEquals(Queue.Current, request))
                {
                    Queue.Answer(index, _field.Text);
                }
            };
            button.Focused += (_, _) =>
            {
                Queue.SetFocus(index);
                MarkFocused(index);
            };
            SemanticProperties.SetDescription(button, action.Label);
            _actions.Add(button);
        }

        if (request.IsList && request.CancelIndex < 0)
        {
            // A picker without a Cancel of its own still says how to leave it.
            var close = p.SecondaryButton("Cancel", 32);
            close.Clicked += (_, _) => Queue.Cancel();
            _buttons.Children.Add(close);
        }

        _buttons.IsVisible = _buttons.Children.Count > 0;
        MarkFocused(Queue.Focused);
    }

    private void FocusAction(int index)
    {
        if (index >= 0 && index < _actions.Count)
        {
            _actions[index].Focus();
            MarkFocused(index);
            if (_shown?.IsList == true)
            {
                _ = _list.ScrollToAsync(_actions[index], ScrollToPosition.MakeVisible, animated: false);
            }
        }
        else if (_shown?.InputPlaceholder is not null)
        {
            _field.Focus();
        }
    }

    // A picker's focused row on the selection's fill, so the arrows' place shows whatever the platform draws for focus.
    private void MarkFocused(int index)
    {
        if (_shown?.IsList != true)
        {
            return;
        }

        for (int i = 0; i < _actions.Count; i++)
        {
            if (_actions[i].ClassId == RowClassId)
            {
                _actions[i].BackgroundColor = i == index ? _palette.Selection : Colors.Transparent;
            }
        }
    }

    // What has the keyboard's focus in the window now (searched once, when a dialog opens).
    private VisualElement? FocusedElement()
    {
        Element? top = this;
        while (top.Parent is { } parent)
        {
            top = parent;
        }

        if (top is not IVisualTreeElement tree)
        {
            return null;
        }

        foreach (var element in tree.GetVisualTreeDescendants())
        {
            if (element is VisualElement { IsFocused: true } focused && !IsInside(focused))
            {
                return focused;
            }
        }

        return null;
    }

    private bool IsInside(Element element)
    {
        for (Element? e = element; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, this))
            {
                return true;
            }
        }

        return false;
    }
}
