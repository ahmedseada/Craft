// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The plug-ins' conversation panels (<see cref="IConversationPanel"/>: the AI plug-in's chat and agent run) of an
/// <see cref="EditorHost"/> as the approved design's chat column (docs/design/Main.dc.html, Agent.dc.html; the page shows it
/// as a sheet in the narrow layout, PhoneChat.dc.html): a tab per panel, its title and subtitle and header buttons, its
/// entries (the user's bubbles, the model's text, tool rows with a check, code blocks with their buttons and Copy, approval
/// cards, the changed files), its status line, its context chips (a tap removes one) and the input with Send. A streamed
/// reply replaces one entry's view per piece; anything else rebuilds the panel. Colours only from the theme. Any panel a
/// plug-in adds through <see cref="IEditorHost.AddPanel"/> that is an <see cref="IConversationPanel"/> shows here.
/// </summary>
public sealed class ConversationsView : ContentView, IDisposable
{
    private readonly EditorHost _host;
    private readonly string _codeFont;
    private readonly HorizontalStackLayout _tabs = new() { Spacing = 4 };
    private readonly Label _title = new() { FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Label _subtitle = new() { FontSize = 12, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly HorizontalStackLayout _headerActions = new() { Spacing = 6 };
    private readonly VerticalStackLayout _entries = new() { Spacing = 12, Padding = new Thickness(14) };
    private readonly ScrollView _scroll;
    private readonly Label _status = new() { FontSize = 12, Padding = new Thickness(14, 0) };
    private readonly FlexLayout _chips = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly Microsoft.Maui.Controls.Editor _input = new() { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 44 };
    private readonly Button _send = new() { Text = "→", WidthRequest = 44, HeightRequest = 44, Padding = 0, FontAttributes = FontAttributes.Bold };
    private IConversationPanel? _shown;
    private EditorColors _colors = EditorThemes.Get(EditorThemes.Dark).Colors;
    private bool _disposed;

    /// <summary>The conversation panels of <paramref name="host"/>; code in <paramref name="codeFont"/>.</summary>
    public ConversationsView(EditorHost host, string codeFont)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _codeFont = codeFont;
        _scroll = new ScrollView { Content = _entries };
        _send.Clicked += (_, _) => Send();
        _input.Completed += (_, _) => Send();
        SemanticProperties.SetDescription(_send, "Send");
        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
            Padding = new Thickness(14, 4),
            MinimumHeightRequest = 40,
        };
        header.Add(_title, 0, 0);
        header.Add(_subtitle, 1, 0);
        header.Add(_headerActions, 2, 0);
        var inputRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        inputRow.Add(_input, 0, 0);
        inputRow.Add(_send, 1, 0);
        var footer = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(12), Children = { _chips, inputRow } };
        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
            },
        };
        layout.Add(_tabs, 0, 0);
        layout.Add(header, 0, 1);
        layout.Add(_scroll, 0, 2);
        layout.Add(_status, 0, 3);
        layout.Add(footer, 0, 4);
        Content = layout;
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.Drop += OnDrop;
        layout.GestureRecognizers.Add(drop);
        host.PanelsChanged += OnPanelsChanged;
        OnPanelsChanged(this, EventArgs.Empty);
    }

    /// <summary>Whether the host has a conversation panel to show.</summary>
    public bool HasPanels => Panels().Any();

    /// <summary>The panel shown, or null.</summary>
    public IConversationPanel? Shown => _shown;

    /// <summary>Raised when <see cref="HasPanels"/> may have changed.</summary>
    public event EventHandler? PanelsChanged;

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

    /// <summary>Puts the keyboard's focus in the input.</summary>
    public void FocusInput() => _input.Focus();

    /// <summary>
    /// How a drop's picture is read (the shell sets it to the platform's <see cref="IShellPlatform.ReadDroppedImageAsync"/>);
    /// null: a drop attaches nothing.
    /// </summary>
    public Func<DropEventArgs, Task<EditorImage?>>? ReadDrop { get; set; }

    /// <summary>
    /// Attaches <paramref name="image"/> to the panel shown (<see cref="IConversationPanel.AttachImage"/>); null when it was
    /// attached, else why not (no panel shown, or the panel's refusal).
    /// </summary>
    public string? Attach(EditorImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return _shown is { } panel ? panel.AttachImage(image) : "No chat is shown to attach the picture to.";
    }

    // A picture dropped on the chat column goes to the panel shown; a drop with none, or a refusal, is said.
    private async void OnDrop(object? sender, DropEventArgs e)
    {
        if (ReadDrop is not { } read)
        {
            return;
        }

        EditorImage? image;
        try
        {
            image = await read(e);
        }
#pragma warning disable CA1031 // A drop that cannot be read is said; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Warning, "The dropped item could not be read as a picture.", error);
            return;
        }

        if (image is null)
        {
            await _host.ShowMessageAsync("Not a picture", $"Drop a picture ({string.Join(", ", EditorImage.Extensions)}) on the chat to send it with your next message.");
            return;
        }

        if (Attach(image) is { } refusal)
        {
            await _host.ShowMessageAsync("Picture not attached", refusal);
        }
    }

    /// <summary>Takes the theme's colours.</summary>
    public void ApplyTheme(ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _colors = theme.Colors;
        BackgroundColor = C(_colors.Surface);
        _title.TextColor = C(_colors.Foreground);
        _subtitle.TextColor = C(_colors.SecondaryText);
        _status.TextColor = C(_colors.SecondaryText);
        _input.TextColor = C(_colors.Foreground);
        _input.BackgroundColor = C(_colors.PopupBackground);
        _input.PlaceholderColor = C(_colors.SecondaryText);
        _send.BackgroundColor = C(_colors.Accent);
        _send.TextColor = C(_colors.Ground);
        RebuildTabs();
        RebuildAll();
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

        _shown = panel;
        if (panel is not null)
        {
            panel.ConversationChanged += OnConversationChanged;
        }

        RebuildTabs();
        RebuildAll();
    }

    private void OnConversationChanged(object? sender, ConversationChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _shown) || _shown is not { } panel)
        {
            return;
        }

        switch (e.Change)
        {
            case ConversationChange.EntryAdded when e.Index == _entries.Children.Count && e.Index < panel.Entries.Count:
                _entries.Children.Add(EntryView(panel.Entries[e.Index]));
                ScrollToEnd();
                break;
            case ConversationChange.EntryUpdated when e.Index >= 0 && e.Index < _entries.Children.Count && e.Index < panel.Entries.Count:
                _entries.Children[e.Index] = EntryView(panel.Entries[e.Index]);
                ScrollToEnd();
                break;
            default:
                RebuildAll();
                break;
        }
    }

    private void ScrollToEnd()
    {
        if (_entries.Children.Count > 0 && _entries.Children[^1] is Element last)
        {
            _ = _scroll.ScrollToAsync(last, ScrollToPosition.End, animated: false);
        }
    }

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        var panels = Panels().ToList();
        _tabs.IsVisible = panels.Count > 1;
        foreach (var panel in panels)
        {
            bool active = ReferenceEquals(panel, _shown);
            var tab = new Button
            {
                Text = panel.Title.Length > 24 ? panel.Title[..23] + "…" : panel.Title,
                FontSize = 12,
                Padding = new Thickness(10, 4),
                MinimumHeightRequest = 32,
                BackgroundColor = active ? C(_colors.CurrentLine) : Colors.Transparent,
                TextColor = active ? C(_colors.Foreground) : C(_colors.SecondaryText),
                BorderColor = active ? C(_colors.Accent) : Colors.Transparent,
                BorderWidth = active ? 1 : 0,
            };
            var target = panel;
            tab.Clicked += (_, _) => Select(target);
            _tabs.Children.Add(tab);
        }
    }

    private void RebuildAll()
    {
        _entries.Children.Clear();
        _chips.Children.Clear();
        _headerActions.Children.Clear();
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
        _status.Text = panel.Status ?? "";
        _status.IsVisible = panel.Status is { Length: > 0 };
        _input.Placeholder = panel.Placeholder;
        foreach (var action in panel.HeaderActions)
        {
            _headerActions.Children.Add(ActionButton(action));
        }

        foreach (var entry in panel.Entries)
        {
            _entries.Children.Add(EntryView(entry));
        }

        foreach (var chip in panel.Chips)
        {
            var label = new Label
            {
                Text = chip.Text + "  ×",
                FontSize = 12,
                TextColor = C(_colors.Foreground),
                VerticalTextAlignment = TextAlignment.Center,
            };
            var border = new Border
            {
                Content = label,
                Padding = new Thickness(8, 3),
                Margin = new Thickness(0, 0, 6, 6),
                BackgroundColor = C(_colors.PopupBackground),
                Stroke = C(_colors.Border),
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
            };
            SemanticProperties.SetDescription(border, $"Context: {chip.Text}. Tap to remove.");
            var tap = new TapGestureRecognizer();
            string id = chip.Id;
            tap.Tapped += (_, _) => panel.RemoveChip(id);
            border.GestureRecognizers.Add(tap);
            _chips.Children.Add(border);
        }

        ScrollToEnd();
    }

    private View EntryView(ConversationEntry entry)
    {
        var text = C(_colors.Foreground);
        var secondary = C(_colors.SecondaryText);
        switch (entry.Role)
        {
            case ConversationRole.User:
                return new Border
                {
                    Content = new Label { Text = entry.Detail is null ? entry.Text : $"{entry.Text}\n{entry.Detail}", TextColor = text, LineHeight = 1.4 },
                    Padding = new Thickness(12, 10),
                    HorizontalOptions = LayoutOptions.End,
                    MaximumWidthRequest = 520,
                    BackgroundColor = C(_colors.Selection),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10, 10, 10, 2) },
                };
            case ConversationRole.Assistant:
                return new Label { Text = entry.Text, TextColor = text, LineHeight = 1.45 };
            case ConversationRole.Note:
                return new Label { Text = entry.Text, TextColor = secondary, FontSize = 12 };
            case ConversationRole.Error:
                return new Label { Text = entry.Text, TextColor = C(_colors.Error), FontSize = 12 };
            case ConversationRole.Section:
                return Stack(new Label { Text = entry.Text, TextColor = secondary, FontSize = 11, FontAttributes = FontAttributes.Bold, CharacterSpacing = 0.6 }, entry);
            case ConversationRole.Tool:
                var mark = new Label
                {
                    Text = entry.Succeeded switch { true => "✓", false => "✗", null => "…" },
                    TextColor = entry.Succeeded == false ? C(_colors.Error) : C(_colors.Success),
                    FontAttributes = FontAttributes.Bold,
                };
                var line = new Label { FontSize = 12, TextColor = secondary, LineBreakMode = LineBreakMode.WordWrap };
                line.FormattedText = new FormattedString
                {
                    Spans =
                    {
                        new Span { Text = "Called " },
                        new Span { Text = entry.Text, TextColor = text, FontAttributes = FontAttributes.Bold },
                        new Span { Text = entry.Detail is { Length: > 0 } detail ? " " + detail : "" },
                    },
                };
                return Card(new HorizontalStackLayout { Spacing = 8, Children = { mark, line } }, C(_colors.Border), null);
            case ConversationRole.File:
                var file = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                file.Add(new Label { Text = entry.Text, TextColor = text, LineBreakMode = LineBreakMode.MiddleTruncation, VerticalTextAlignment = TextAlignment.Center }, 0, 0);
                file.Add(new Label { Text = entry.Detail ?? "", TextColor = C(_colors.Success), FontSize = 12, VerticalTextAlignment = TextAlignment.Center }, 1, 0);
                return Stack(file, entry);
            case ConversationRole.Approval:
                var card = new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        new Label { Text = "The agent wants to run a command", TextColor = text, FontAttributes = FontAttributes.Bold },
                        Code(entry.Text),
                        new Label { Text = entry.Detail ?? "", TextColor = secondary, FontSize = 12 },
                    },
                };
                AddActions(card, entry);
                return Card(card, C(_colors.Warning), C(_colors.PopupBackground));
            case ConversationRole.Code:
            default:
                var block = new VerticalStackLayout { Spacing = 0, Children = { Code(entry.Text) } };
                var buttons = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(8) };
                foreach (var action in entry.Actions)
                {
                    buttons.Children.Add(ActionButton(action));
                }

                if (entry.CopyText is { } copy)
                {
                    var button = new Button
                    {
                        Text = "Copy",
                        FontSize = 12,
                        MinimumHeightRequest = 28,
                        Padding = new Thickness(12, 0),
                        BackgroundColor = Colors.Transparent,
                        TextColor = text,
                        BorderColor = C(_colors.PopupBorder),
                        BorderWidth = 1,
                    };
                    button.Clicked += async (_, _) => await Clipboard.Default.SetTextAsync(copy);
                    buttons.Children.Add(button);
                }

                if (buttons.Children.Count > 0)
                {
                    block.Children.Add(buttons);
                }

                return Card(block, C(_colors.Border), null, padding: 0);
        }
    }

    private Label Code(string code) => new()
    {
        Text = code.TrimEnd('\n'),
        FontFamily = _codeFont,
        FontSize = 12,
        LineHeight = 1.5,
        TextColor = C(_colors.Foreground),
        BackgroundColor = C(_colors.Ground),
        Padding = new Thickness(12, 10),
        LineBreakMode = LineBreakMode.CharacterWrap,
    };

    private VerticalStackLayout Stack(View head, ConversationEntry entry)
    {
        var stack = new VerticalStackLayout { Spacing = 6, Children = { head } };
        AddActions(stack, entry);
        return stack;
    }

    private void AddActions(Layout stack, ConversationEntry entry)
    {
        if (entry.Actions.Count == 0)
        {
            return;
        }

        var row = new HorizontalStackLayout { Spacing = 8 };
        foreach (var action in entry.Actions)
        {
            row.Children.Add(ActionButton(action));
        }

        stack.Children.Add(row);
    }

    private static Border Card(View content, Color stroke, Color? fill, double padding = 10) => new()
    {
        Content = content,
        Padding = new Thickness(padding),
        Stroke = stroke,
        StrokeThickness = 1,
        BackgroundColor = fill ?? Colors.Transparent,
        StrokeShape = new RoundRectangle { CornerRadius = 8 },
    };

    private Button ActionButton(PanelAction action)
    {
        var button = new Button
        {
            Text = action.Label,
            FontSize = 12,
            MinimumHeightRequest = 28,
            Padding = new Thickness(12, 0),
            FontAttributes = action.Primary ? FontAttributes.Bold : FontAttributes.None,
            BackgroundColor = action.Primary ? C(_colors.Accent) : Colors.Transparent,
            TextColor = action.Primary ? C(_colors.Ground) : C(_colors.Foreground),
            BorderColor = action.Primary ? C(_colors.Accent) : C(_colors.PopupBorder),
            BorderWidth = 1,
        };
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await action.RunAsync(CancellationToken.None);
            }
#pragma warning disable CA1031 // A plug-in's action that throws is logged; the editor goes on.
            catch (Exception error)
#pragma warning restore CA1031
            {
                _host.Log(EditorLogLevel.Error, $"'{action.Label}' failed.", error);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private void Send()
    {
        if (_shown is not { } panel || string.IsNullOrWhiteSpace(_input.Text))
        {
            return;
        }

        string text = _input.Text;
        _input.Text = "";
        _ = SubmitAsync(panel, text);
    }

    private async Task SubmitAsync(IConversationPanel panel, string text)
    {
        try
        {
            await panel.SubmitAsync(text);
        }
#pragma warning disable CA1031 // A plug-in's failure is logged; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Error, $"{panel.Title}: the message was not taken.", error);
        }
    }

    private static Color C(uint argb) => ThemeColors.Of(argb);
}
