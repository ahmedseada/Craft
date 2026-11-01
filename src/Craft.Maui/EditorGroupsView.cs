// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// One editor group's pane (the X restart's split editor): its tab strip, a slot for the breadcrumb (the focused pane's),
/// and its editor area, which takes drops: a tab (or a file from the tree) dragged over it shows where it would go, the
/// centre (into the group) or the right or bottom half (a new group there), and goes there when dropped. A press anywhere
/// in it raises <see cref="Engaged"/> (the page focuses the group). No platform code.
/// </summary>
public sealed class EditorGroupPane : Grid
{
    private readonly Border _zone;
    private EditorDropZone _shownZone;
    private ShellPalette _palette;

    /// <summary>The pane of <paramref name="group"/> with its strip.</summary>
    public EditorGroupPane(EditorGroup group, TabStripView strip, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(strip);
        ArgumentNullException.ThrowIfNull(palette);
        Group = group;
        Strip = strip;
        _palette = palette;
        RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)];
        Empty = palette.Label("Drop a tab here, or open a file (Ctrl+P).", palette.Secondary);
        Empty.HorizontalTextAlignment = TextAlignment.Center;
        Empty.VerticalTextAlignment = TextAlignment.Center;
        Empty.LineBreakMode = LineBreakMode.WordWrap;
        Empty.Margin = new Thickness(24);
        // The drop zone: the accent's tint inside its line, rounded, over the part of the editor a drop would take.
        _zone = new Border
        {
            IsVisible = false,
            InputTransparent = true,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Margin = new Thickness(6),
        };
        Stack.Add(Area);
        Stack.Add(_zone);
        this.Add(strip, 0, 0);
        this.Add(BreadcrumbSlot, 0, 1);
        this.Add(Stack, 0, 2);
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += OnDragOver;
        drop.DragLeave += (_, _) => ShowZone(EditorDropZone.None);
        drop.Drop += OnDrop;
        Stack.GestureRecognizers.Add(drop);
        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += (_, _) => Engaged?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(pointer);
        Apply(palette);
    }

    /// <summary>The group.</summary>
    public EditorGroup Group { get; }

    /// <summary>Its tab strip.</summary>
    public TabStripView Strip { get; }

    /// <summary>Where the breadcrumb goes while this is the focused pane.</summary>
    public ContentView BreadcrumbSlot { get; } = new();

    /// <summary>The editor area and what floats over it (the find overlay and the peek while this is the focused pane).</summary>
    public Grid Stack { get; } = new();

    /// <summary>The editor area's content: the active tab's editor, a page, or <see cref="Empty"/>.</summary>
    public ContentView Area { get; } = new();

    /// <summary>What an empty group says.</summary>
    public Label Empty { get; }

    /// <summary>The editor it shows, or null.</summary>
    public EditorView? Shown { get; set; }

    /// <summary>Raised when the pointer presses inside the pane.</summary>
    public event EventHandler? Engaged;

    /// <summary>Raised when a tab is dropped on a zone of the editor area.</summary>
    public event EventHandler<(DocumentTab Tab, EditorDropZone Zone)>? TabDropped;

    /// <summary>Raised when a file from the tree is dropped on a zone of the editor area.</summary>
    public event EventHandler<(string Path, EditorDropZone Zone)>? FileDropped;

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        Area.BackgroundColor = palette.Surface;
        Empty.TextColor = palette.Secondary;
        _zone.Stroke = palette.Accent;
        _zone.BackgroundColor = palette.Accent.WithAlpha(0.16f);
        Strip.Apply(palette);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!Carries(e.Data))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        var at = e.GetPosition(Stack);
        ShowZone(at is { } point ? EditorGroups.ZoneAt(point.X, point.Y, Stack.Width, Stack.Height) : EditorDropZone.Centre);
    }

    private void OnDrop(object? sender, DropEventArgs e)
    {
        var at = e.GetPosition(Stack);
        var zone = at is { } point ? EditorGroups.ZoneAt(point.X, point.Y, Stack.Width, Stack.Height) : EditorDropZone.Centre;
        ShowZone(EditorDropZone.None);
        if (zone == EditorDropZone.None)
        {
            return;
        }

        if (e.Data.Properties.TryGetValue(TabStripView.DragTabKey, out var value) && value is DocumentTab tab)
        {
            e.Handled = true;
            TabDropped?.Invoke(this, (tab, zone));
        }
        else if (e.Data.Properties.TryGetValue(FolderTreeView.DragPathKey, out var path) && path is string file)
        {
            e.Handled = true;
            FileDropped?.Invoke(this, (file, zone));
        }
    }

    private static bool Carries(DataPackage data) =>
        data.Properties.ContainsKey(TabStripView.DragTabKey) || data.Properties.ContainsKey(FolderTreeView.DragPathKey);

    // The zone's look over the part a drop would take: all of it, its right half, or its bottom half; it fades in.
    private void ShowZone(EditorDropZone zone)
    {
        if (zone == _shownZone)
        {
            return;
        }

        _shownZone = zone;
        if (zone == EditorDropZone.None)
        {
            _zone.IsVisible = false;
            return;
        }

        _zone.HorizontalOptions = zone == EditorDropZone.Right ? LayoutOptions.End : LayoutOptions.Fill;
        _zone.VerticalOptions = zone == EditorDropZone.Down ? LayoutOptions.End : LayoutOptions.Fill;
        _zone.WidthRequest = zone == EditorDropZone.Right ? Math.Max(0, (Stack.Width / 2) - 6) : -1;
        _zone.HeightRequest = zone == EditorDropZone.Down ? Math.Max(0, (Stack.Height / 2) - 6) : -1;
        _ = ShellMotion.ShowAsync(_zone, 0);
    }
}

/// <summary>
/// The splitter between two editor groups: a one-unit line in a six-unit grip, the accent under the pointer and while
/// dragged. Its drag is s3's (B8): <see cref="SplitterModel"/> keeps the first group's size within what leaves each group
/// <see cref="EditorGroupsView.MinimumSize"/> (no snapping closed: a group closes with its tabs), a double-click shares
/// the room in halves, and with the platform's pointer (<see cref="IPointerCursor"/>, Windows) the resize arrows show and
/// the drag captures the pointer; elsewhere MAUI's pan drags it.
/// </summary>
public sealed class GroupSplitter : Grid, IPointerDrag
{
    private const double GripThickness = 6;
    private readonly BoxView _line = new();
    private readonly bool _across;
    private readonly SplitterModel _model;
    private PanGestureRecognizer? _pan;
    private IDisposable? _pointerHook;
    private IPointerCursor? _pointer;
    private Color _lineColor = Colors.Gray;
    private Color _hoverColor = Colors.Orange;
    private bool _hovered;
    private double _pair;

    /// <summary>A splitter between groups side by side (<paramref name="across"/>: a vertical line) or stacked (a horizontal line).</summary>
    public GroupSplitter(bool across)
    {
        _across = across;
        _model = new SplitterModel(1, across) { Snap = false };
        if (across)
        {
            WidthRequest = GripThickness;
            _line.WidthRequest = 1;
            _line.HorizontalOptions = LayoutOptions.Center;
            _line.VerticalOptions = LayoutOptions.Fill;
        }
        else
        {
            HeightRequest = GripThickness;
            _line.HeightRequest = 1;
            _line.VerticalOptions = LayoutOptions.Center;
            _line.HorizontalOptions = LayoutOptions.Fill;
        }

        BackgroundColor = Colors.Transparent;
        Add(_line);
        UsePan();
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => SetHovered(true);
        pointer.PointerExited += (_, _) => SetHovered(_model.Dragging);
        GestureRecognizers.Add(pointer);
        SemanticProperties.SetDescription(this, across ? "Drag to share the width between the editor groups; double-click shares it in halves" : "Drag to share the height between the editor groups; double-click shares it in halves");
        ToolTipProperties.SetText(this, "Drag to resize the groups · double-click to share in halves");
    }

    /// <summary>Whether it splits side by side groups.</summary>
    public bool Across => _across;

    /// <summary>The two groups' views either side, asked when a drag starts; null leaves the splitter still.</summary>
    public Func<(View First, View Second)?>? Neighbours { get; set; }

    /// <summary>The least a drag leaves either group.</summary>
    public double MinimumSize { get; set; } = 120;

    /// <summary>
    /// The platform's pointer (the resize arrows and a captured drag, <see cref="IShellPlatform.PointerCursor"/>); null
    /// keeps MAUI's pan gesture.
    /// </summary>
    public IPointerCursor? Pointer
    {
        get => _pointer;
        set
        {
            if (ReferenceEquals(value, _pointer))
            {
                return;
            }

            _pointerHook?.Dispose();
            _pointerHook = null;
            _pointer = value;
            if (value is null)
            {
                UsePan();
                return;
            }

            if (_pan is not null)
            {
                GestureRecognizers.Remove(_pan);
                _pan = null;
            }

            _pointerHook = value.Attach(this, _across ? PointerShape.ResizeWestEast : PointerShape.ResizeNorthSouth, this);
        }
    }

    /// <summary>Raised on each step of a drag with the first group's share of the two (0-1).</summary>
    public event EventHandler<double>? Resizing;

    /// <summary>Raised once when a drag (or a double-click) ended, with the first group's share.</summary>
    public event EventHandler<double>? Resized;

    /// <summary>The line's colour and its colour under the pointer and while dragged.</summary>
    public void SetColors(Color line, Color hover)
    {
        _lineColor = line;
        _hoverColor = hover;
        _line.Color = _hovered ? hover : line;
    }

    /// <inheritdoc />
    void IPointerDrag.Pressed(double x, double y)
    {
        if (Start())
        {
            _model.Begin(_across ? x : y);
        }
    }

    /// <inheritdoc />
    void IPointerDrag.Moved(double x, double y) => Report(_model.Move(_across ? x : y), ended: false);

    /// <inheritdoc />
    void IPointerDrag.Released(double x, double y)
    {
        if (_model.Dragging)
        {
            Report(_model.End(), ended: true);
        }
    }

    /// <inheritdoc />
    void IPointerDrag.Lost()
    {
        if (_model.Dragging)
        {
            Report(_model.Cancel(), ended: true);
        }
    }

    /// <inheritdoc />
    void IPointerDrag.DoubleClicked()
    {
        if (Start())
        {
            Report(_model.Reset(), ended: true);
        }
    }

    private void UsePan()
    {
        if (_pan is not null)
        {
            return;
        }

        _pan = new PanGestureRecognizer();
        _pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    if (Start())
                    {
                        _model.Begin(0);
                    }

                    break;
                case GestureStatus.Running:
                    Report(_model.Move(_across ? e.TotalX : e.TotalY), ended: false);
                    break;
                case GestureStatus.Completed:
                    Report(_model.End(), ended: true);
                    break;
                case GestureStatus.Canceled:
                    Report(_model.Cancel(), ended: true);
                    break;
            }
        };
        GestureRecognizers.Add(_pan);
    }

    // A drag starts from the two groups' sizes now: each keeps at least the minimum (half the room when there is less).
    private bool Start()
    {
        if (Neighbours?.Invoke() is not { } pair)
        {
            return false;
        }

        double first = _across ? pair.First.Width : pair.First.Height;
        double second = _across ? pair.Second.Width : pair.Second.Height;
        _pair = first + second;
        if (!(_pair > 0))
        {
            return false;
        }

        double least = Math.Min(MinimumSize, _pair / 2);
        _model.Clamp = size => Math.Clamp(size, least, _pair - least);
        _model.DefaultSize = _pair / 2;
        _model.SetSize(Math.Max(1, first));
        SetHovered(true);
        return true;
    }

    private void Report(SplitterState state, bool ended)
    {
        if (!(_pair > 0))
        {
            return;
        }

        double share = state.Size / _pair;
        if (ended)
        {
            SetHovered(false);
            Resized?.Invoke(this, share);
        }
        else
        {
            Resizing?.Invoke(this, share);
        }
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        _line.Color = hovered ? _hoverColor : _lineColor;
    }
}

/// <summary>
/// The editor area's groups as the layout says (<see cref="EditorGroups.Root"/>): a grid per split, each child's share a
/// star size, a <see cref="GroupSplitter"/> between neighbours; each group's <see cref="EditorGroupPane"/> is made once and
/// placed again when the layout changes (its editors are not made again). In the narrow layout only the focused pane shows.
/// </summary>
public sealed class EditorGroupsView : ContentView
{
    private readonly EditorGroups _groups;
    private readonly Func<EditorGroup, EditorGroupPane> _paneOf;
    private readonly List<GroupSplitter> _splitters = [];
    private ShellPalette _palette;

    /// <summary>The view of <paramref name="groups"/>, its panes from <paramref name="paneOf"/>.</summary>
    public EditorGroupsView(EditorGroups groups, Func<EditorGroup, EditorGroupPane> paneOf, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(paneOf);
        ArgumentNullException.ThrowIfNull(palette);
        _groups = groups;
        _paneOf = paneOf;
        _palette = palette;
    }

    /// <summary>Whether only the focused group shows (the narrow layout).</summary>
    public bool Narrow { get; set; }

    /// <summary>The least a drag leaves a group, device-independent units (<c>CRAFT_PANEL_MIN_SIZE</c>).</summary>
    public double MinimumSize { get; set; } = 120;

    /// <summary>The platform's pointer for the splitters (the resize arrows and a captured drag), or null for MAUI's pan.</summary>
    public IPointerCursor? Pointer { get; set; }

    /// <summary>Raised once when a splitter's drag ended (the layout's file follows).</summary>
    public event EventHandler? Resized;

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        foreach (var splitter in _splitters)
        {
            splitter.SetColors(palette.Border, palette.Accent);
        }
    }

    /// <summary>Lays the panes out as the groups' layout is now.</summary>
    public void Rebuild()
    {
        foreach (var group in _groups.Groups)
        {
            Detach(_paneOf(group));
        }

        _splitters.Clear();
        Content = Narrow ? _paneOf(_groups.Focused) : Build(_groups.Root);
    }

    private static void Detach(View view)
    {
        if (view.Parent is Layout layout)
        {
            layout.Remove(view);
        }
        else if (view.Parent is ContentView holder)
        {
            holder.Content = null;
        }
    }

    private View Build(EditorGroupNode node)
    {
        if (node.Group is { } group)
        {
            return _paneOf(group);
        }

        bool across = node.Orientation == GroupOrientation.Row;
        var grid = new Grid();
        for (int i = 0; i < node.Children.Count; i++)
        {
            var size = new GridLength(Math.Max(0.0001, node.Weights[i]), GridUnitType.Star);
            if (across)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(size));
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition(size));
            }

            var child = Build(node.Children[i]);
            if (across)
            {
                grid.Add(child, i * 2, 0);
            }
            else
            {
                grid.Add(child, 0, i * 2);
            }

            if (i + 1 < node.Children.Count)
            {
                if (across)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                }
                else
                {
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                }

                var splitter = new GroupSplitter(across);
                splitter.SetColors(_palette.Border, _palette.Accent);
                int index = i;
                Wire(splitter, grid, node, index);
                _splitters.Add(splitter);
                if (across)
                {
                    grid.Add(splitter, (i * 2) + 1, 0);
                }
                else
                {
                    grid.Add(splitter, 0, (i * 2) + 1);
                }
            }
        }

        return grid;
    }

    // A drag shares the room of the two neighbours (s3's splitter model keeps each at least MinimumSize): the grid's star
    // sizes follow live, the groups' shares are set once at the end.
    private void Wire(GroupSplitter splitter, Grid grid, EditorGroupNode node, int index)
    {
        splitter.MinimumSize = MinimumSize;
        splitter.Pointer = Pointer;
        splitter.Neighbours = () => grid.Children.Count > (index * 2) + 2 && grid.Children[index * 2] is View first && grid.Children[(index * 2) + 2] is View second
            ? (first, second)
            : null;
        splitter.Resizing += (_, share) =>
        {
            double weights = node.Weights[index] + node.Weights[index + 1];
            SetSize(grid, splitter.Across, index * 2, weights * share);
            SetSize(grid, splitter.Across, (index * 2) + 2, weights * (1 - share));
        };
        splitter.Resized += (_, share) =>
        {
            double weights = node.Weights[index] + node.Weights[index + 1];
            SetSize(grid, splitter.Across, index * 2, weights * share);
            SetSize(grid, splitter.Across, (index * 2) + 2, weights * (1 - share));
            _groups.Resize(node, index, share);
            Resized?.Invoke(this, EventArgs.Empty);
        };
    }

    private static void SetSize(Grid grid, bool across, int at, double weight)
    {
        var size = new GridLength(Math.Max(0.0001, weight), GridUnitType.Star);
        if (across)
        {
            grid.ColumnDefinitions[at].Width = size;
        }
        else
        {
            grid.RowDefinitions[at].Height = size;
        }
    }
}

/// <summary>
/// The peek definition overlay (the X restart's Alt+F12): a card under the caret's line holding a read-only editor of the
/// definition (the peek's document and session; the name selected and in view), a header with the file and line, Open and
/// close. Escape closes it, Enter or a double click opens the definition in the focused group (<see cref="PeekKeys"/>).
/// </summary>
public sealed class PeekView : ContentView, IDisposable
{
    private const double HeaderHeight = 32;
    private readonly Border _card;
    private readonly Label _title;
    private readonly Label _detail;
    private readonly Button _open;
    private readonly Button _close;
    private bool _placed;

    /// <summary>The overlay of <paramref name="peek"/>, its editor made with <paramref name="configure"/> (options, theme, keys).</summary>
    public PeekView(PeekSession peek, ShellPalette palette, Action<EditorView> configure, int lines)
    {
        ArgumentNullException.ThrowIfNull(peek);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(configure);
        Peek = peek;
        Editor = new EditorView { IsReadOnly = true };
        configure(Editor);
        Editor.Open(peek.Document, peek.Language, peek.Session);
        double lineHeight = Editor.Model?.Metrics.LineHeight ?? 18;
        Editor.HeightRequest = Math.Max(3, lines) * lineHeight;
        _title = palette.Label(peek.Title, palette.Text, palette.SmallSize, FontWeight.Medium);
        _title.VerticalTextAlignment = TextAlignment.Center;
        _detail = palette.Label(peek.IsSyntacticOnly ? "colours only: the file is not open" : "", palette.Secondary, palette.CaptionSize);
        _detail.VerticalTextAlignment = TextAlignment.Center;
        _detail.LineBreakMode = LineBreakMode.TailTruncation;
        _open = palette.Flat("Open", palette.Accent, 24, palette.SmallSize);
        _open.Clicked += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _close = palette.Flat("×", palette.Secondary, 24, palette.SmallSize + 2);
        _close.WidthRequest = 28;
        _close.Clicked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        ToolTipProperties.SetText(_open, "Open the definition in the focused group (Enter)");
        ToolTipProperties.SetText(_close, "Close the peek (Escape)");
        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 10,
            Padding = new Thickness(12, 0, 4, 0),
            HeightRequest = HeaderHeight,
        };
        header.Add(_title, 0, 0);
        header.Add(_detail, 1, 0);
        header.Add(_open, 2, 0);
        header.Add(_close, 3, 0);
        var open = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        open.Tapped += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        header.GestureRecognizers.Add(open);
        Editor.DoubleClicked += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        var body = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)] };
        body.Add(header, 0, 0);
        body.Add(palette.Line(), 0, 1);
        body.Add(Editor, 0, 2);
        _card = new Border
        {
            Content = body,
            Padding = new Thickness(0),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Opacity = 0.28f, Radius = 16, Offset = new Point(0, 6) },
        };
        Content = _card;
        VerticalOptions = LayoutOptions.Start;
        HorizontalOptions = LayoutOptions.Fill;
        Margin = new Thickness(8, 0, 8, 0);
        Apply(palette);
        // The name selected and scrolled into view once the editor has its size.
        Editor.SizeChanged += (_, _) =>
        {
            if (!_placed && Editor.Model is { } model && model.ViewportHeight > 0)
            {
                _placed = true;
                model.Select(Math.Min(model.Snapshot.Length, peek.Start + peek.Length), peek.Start);
                model.EnsureCaretVisible();
            }
        };
    }

    /// <summary>The peek shown.</summary>
    public PeekSession Peek { get; }

    /// <summary>The read-only editor of the definition.</summary>
    public EditorView Editor { get; }

    /// <summary>The overlay's height: its header, its line and its editor's lines.</summary>
    public double OverlayHeight => HeaderHeight + 1 + Editor.HeightRequest + 2;

    /// <summary>Raised by Enter, a double click or Open.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised by Escape or ×.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _card.BackgroundColor = palette.Raised;
        _card.Stroke = palette.Accent.WithAlpha(0.6f);
        _title.TextColor = palette.Text;
        _detail.TextColor = palette.Secondary;
    }

    /// <summary>Raises the request a key maps to (<see cref="PeekSession.ActionFor"/>); true when it was the peek's.</summary>
    public bool OnKey(EditorKey key, KeyModifiers modifiers)
    {
        switch (PeekSession.ActionFor(key, modifiers))
        {
            case PeekAction.Close:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case PeekAction.Open:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Editor.Dispose();
        Peek.Dispose();
    }
}

/// <summary>The keys of the peek's editor: Escape and Enter are the peek's, the rest go where the window's keys go.</summary>
public sealed class PeekKeys(Func<PeekView?> peek, IEditorKeyHandler inner) : IEditorKeyHandler
{
    /// <inheritdoc />
    public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor) =>
        (peek() is { } view && view.OnKey(key, modifiers)) || inner.HandleKey(key, modifiers, editor);
}
