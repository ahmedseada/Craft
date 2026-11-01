// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Editor;
using Craft.Editor.Activities;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The editor control: a <see cref="ScrollView"/> (both ways, for the platform's scroll bars and wheel) whose content is a
/// spacer of the editor's content size, with a <see cref="GraphicsView"/> kept over the viewport (translated by the
/// scroll offsets) that draws the visible lines. The pointer comes through the GraphicsView's interactions, the keyboard
/// through the platform's <see cref="ITextInputAdapter"/> (Platforms/Windows on Windows). Set <see cref="Document"/> (and
/// <see cref="Language"/>, <see cref="Theme"/>, <see cref="Options"/>) to edit; each change of those but the theme makes a
/// new <see cref="EditorModel"/>.
/// </summary>
public sealed class EditorView : ContentView, IDisposable
{
    private readonly ScrollView _scroll;
    private readonly Grid _root;
    private readonly MinimapView _minimap;
    private readonly Grid _content;
    private readonly GraphicsView _graphics;
    private readonly GraphicsView _caretLayer;
    private readonly MauiTextMeasurer _measurer = new();
    private readonly EditorDrawable _drawable;
    private readonly CaretDrawable _caretDrawable;
    private readonly MauiEditorClipboard _clipboard = MauiEditorClipboard.Shared;
    private readonly InputForwarder _input;
    private IDisposable? _inputConnection;
    private IDispatcherTimer? _blink;
    private EditorModel? _model;
    private ITextDocument? _document;
    private ILanguage? _language;
    private ITheme? _theme;
    private CraftOptions _options = new();
    private string? _workspaceRoot;
    private UiWorkLabels? _labels;
    private readonly SmoothScroll _smooth = new();
    private IDispatcherTimer? _scrollTimer;
    private bool _caretOn = true;
    private bool _reclassifying;
    private bool _disposed;
    private bool _readOnly;
    private bool _lineNumbers = true;
    private bool _focusedView = true;
    private bool _showsMinimap;
    private LanguageSessionLease? _lease;
    private ILanguageSession? _givenSession;
    private bool _sessionGiven;

    /// <summary>An empty editor (set <see cref="Document"/> to show one).</summary>
    public EditorView()
    {
        _drawable = new EditorDrawable(_measurer) { CaretsOnOwnLayer = true };
        _drawable.Drawn += OnDrawn;
        _caretDrawable = new CaretDrawable(_drawable);
        _input = new InputForwarder(this);
        _graphics = new GraphicsView
        {
            Drawable = _drawable,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
        };
        // B1: the carets on a transparent layer of their own over the text (the pointer goes through it to the text's
        // view), so the blink repaints the carets and no text.
        _caretLayer = new GraphicsView
        {
            Drawable = _caretDrawable,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
        };
        _content = new Grid
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
        };
        _content.Children.Add(_graphics);
        _content.Children.Add(_caretLayer);
        _scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Both,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Default,
            VerticalScrollBarVisibility = ScrollBarVisibility.Default,
            Content = _content,
        };
        // The X restart, piece 2b: the minimap beside the text (its own view, so the text's frame never draws it), shown when
        // CRAFT_MINIMAP is on and the view is wide enough; the overview ruler then sits at its right edge.
        _minimap = new MinimapView { IsVisible = false, VerticalOptions = LayoutOptions.Fill };
        _root = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        _root.Add(_scroll, 0, 0);
        _root.Add(_minimap, 1, 0);
        Content = _root;
        SizeChanged += (_, _) => UpdateMinimap();

        _scroll.Scrolled += OnScrolled;
        _scroll.SizeChanged += OnViewportSizeChanged;
        _graphics.StartInteraction += OnStartInteraction;
        _graphics.DragInteraction += OnDragInteraction;
        _graphics.EndInteraction += OnEndInteraction;
        _graphics.MoveHoverInteraction += OnMoveHoverInteraction;
        _graphics.EndHoverInteraction += OnEndHoverInteraction;
        _graphics.HandlerChanged += OnGraphicsHandlerChanged;
        // Phase X: a right-click opens the editor's context menu (the selection kept when the click was inside it).
        var context = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
        context.Tapped += OnContextTapped;
        _graphics.GestureRecognizers.Add(context);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The editor of the current document, or null when there is none.</summary>
    public EditorModel? Model => _model;

    /// <summary>The paint's counters (run strings built, lines drawn).</summary>
    public EditorRenderer Renderer => _drawable.Renderer;

    /// <summary>The last text frame's time in milliseconds (B1's telemetry).</summary>
    public double LastFrameMilliseconds => _drawable.LastFrameMilliseconds;

    /// <summary>Whether the last text frame laid text out or built runs (cold) rather than drawing only what was kept (warm).</summary>
    public bool LastFrameCold => _drawable.LastFrameCold;

    /// <summary>
    /// Drops the drawn lines and the layouts kept for them (B1: memory pressure); the next frame lays out the lines it
    /// shows. On the UI thread.
    /// </summary>
    public void ReleaseDrawingMemory()
    {
        _drawable.ReleaseLayouts();
        Invalidate();
    }

    /// <summary>The document being edited (the caller keeps owning it).</summary>
    public ITextDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            Rebuild();
        }
    }

    /// <summary>The document's language, or null for plain text.</summary>
    public ILanguage? Language
    {
        get => _language;
        set
        {
            _language = value;
            Rebuild();
        }
    }

    /// <summary>The colours (the options' theme when null).</summary>
    public ITheme? Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            if (_model is not null && value is not null)
            {
                _model.Theme = value;
            }
        }
    }

    /// <summary>The labels of the UI work (phase U's watchdog names a key, a paint or a pointer that blocks the window), or null.</summary>
    public UiWorkLabels? Labels
    {
        get => _labels;
        set
        {
            _labels = value;
            _drawable.Labels = value;
        }
    }

    /// <summary>Whether the text is read-only (<see cref="EditorModel.IsReadOnly"/>, the bottom panel's Output); carried to each new model.</summary>
    public bool IsReadOnly
    {
        get => _readOnly;
        set
        {
            _readOnly = value;
            if (_model is not null)
            {
                _model.IsReadOnly = value;
            }
        }
    }

    /// <summary>Whether the line numbers are drawn (<see cref="EditorModel.ShowLineNumbers"/>); carried to each new model.</summary>
    public bool ShowLineNumbers
    {
        get => _lineNumbers;
        set
        {
            _lineNumbers = value;
            if (_model is not null)
            {
                _model.ShowLineNumbers = value;
            }
        }
    }

    /// <summary>
    /// The pool the document's language session is taken from (the split editor: views of one document share one session;
    /// each view keeps its own carets, scroll and folds), or null for a session of the view's own.
    /// </summary>
    public LanguageSessionPool? Sessions { get; set; }

    /// <summary>
    /// Whether this view is the focused group's (the X restart): only that view's caret blinks; another shows no caret and
    /// redraws nothing on the blink.
    /// </summary>
    public bool IsFocusedView
    {
        get => _focusedView;
        set
        {
            if (_focusedView == value)
            {
                return;
            }

            _focusedView = value;
            _caretOn = value;
            _drawable.CaretVisible = value;
            if (value)
            {
                RestartBlink();
            }
            else
            {
                _blink?.Stop();
            }

            _caretLayer.Invalidate();
        }
    }

    /// <summary>
    /// Whether this view may show the minimap (the X restart, piece 2b): the documents' editors do; the peek, the output and
    /// the chat's transcript do not. It shows while <c>CRAFT_MINIMAP</c> is on and the view is at least
    /// <c>CRAFT_MINIMAP_MIN_WIDTH</c> wide (<see cref="MinimapRenderer.Shows"/>).
    /// </summary>
    public bool ShowsMinimap
    {
        get => _showsMinimap;
        set
        {
            _showsMinimap = value;
            UpdateMinimap();
        }
    }

    /// <summary>The minimap's view (its renderer's counters).</summary>
    public MinimapView Minimap => _minimap;

    /// <summary>Raised with a short message for the status bar (a completion applied at some carets only).</summary>
    public event EventHandler<string>? StatusRequested;

    /// <summary>Raised when the pointer presses the text (before the editor acts on it): the page focuses the view's group.</summary>
    public event EventHandler? Engaged;

    /// <summary>Raised after a double click on the text (the peek opens its definition).</summary>
    public event EventHandler? DoubleClicked;

    /// <summary>Raised on the UI thread when a new model replaced the last (a new document, language or options).</summary>
    public event EventHandler? ModelChanged;

    /// <summary>Where the document's language shows its long work (the host's activities), or null; set before <see cref="Open(ITextDocument?, ILanguage?)"/>.</summary>
    public IEditorActivities? Activities { get; set; }

    /// <summary>The host the editor's code lenses run through (X part 9: a test's Run), or null; set before <see cref="Open(ITextDocument?, ILanguage?)"/>.</summary>
    public IEditorHost? Host { get; set; }

    /// <summary>The open folder the document belongs to (its language's workspace), or null; a change makes a new model.</summary>
    public string? WorkspaceRoot
    {
        get => _workspaceRoot;
        set
        {
            _workspaceRoot = value;
            Rebuild();
        }
    }

    /// <summary>The settings.</summary>
    public CraftOptions Options
    {
        get => _options;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _options = value;
            Rebuild();
            RestartBlink();
        }
    }

    /// <summary>
    /// Where a key goes first (an <see cref="EditorHost"/>: the user's and the plug-ins' key bindings around the editor's
    /// own keys); null sends keys to the editor alone.
    /// </summary>
    public Craft.Editor.Abstractions.IEditorKeyHandler? KeyHandler { get; set; }

    /// <summary>Raised on Ctrl+S.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Raised on Ctrl+F and Ctrl+H.</summary>
    public event EventHandler<FindRequestedEventArgs>? FindRequested;

    /// <summary>Raised when the caret or the selection moved.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Raised after each frame, outside it (the status bar reads the model's telemetry then).</summary>
    public event EventHandler? Painted;

    /// <summary>Raised on the UI thread when the model's diagnostics changed (the problems list and the counts follow).</summary>
    public event EventHandler? DiagnosticsChanged;

    /// <summary>Raised on a right-click, with where: the page opens the editor's context menu (<see cref="EditorContextMenus.Editor"/>).</summary>
    public event EventHandler<TappedEventArgs>? ContextRequested;

    /// <summary>Raised on F12 when the definition is in another file (the app opens it) or only in metadata.</summary>
    public event EventHandler<DefinitionRequestedEventArgs>? DefinitionRequested;

    /// <summary>Raised on Alt+F12 with the definition to peek (the page shows the overlay under the caret's line).</summary>
    public event EventHandler<DefinitionRequestedEventArgs>? PeekRequested;

    /// <summary>Releases the language's semantic memory (its workspace), as on the platform's memory-pressure signal; it is rebuilt on the next request.</summary>
    public void ReleaseSemanticMemory() => _model?.SemanticSession?.Release();

    /// <summary>Shows <paramref name="document"/> in <paramref name="language"/> (null for plain text), making one new model.</summary>
    public void Open(ITextDocument? document, ILanguage? language)
    {
        _document = document;
        _language = language;
        _sessionGiven = false;
        _givenSession = null;
        Rebuild();
    }

    /// <summary>
    /// Shows <paramref name="document"/> with <paramref name="session"/>, a session the view neither opens nor disposes (the
    /// peek's: an open document's shared session, or a closed file's syntactic one); null for none.
    /// </summary>
    public void Open(ITextDocument? document, ILanguage? language, ILanguageSession? session)
    {
        _document = document;
        _language = language;
        _sessionGiven = true;
        _givenSession = session;
        Rebuild();
    }

    /// <summary>Redraws (the text and the caret layer; the minimap only when its frame would differ).</summary>
    public void Invalidate()
    {
        _graphics.Invalidate();
        _caretLayer.Invalidate();
        _minimap.Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _blink?.Stop();
        _scrollTimer?.Stop();
        _inputConnection?.Dispose();
        _inputConnection = null;
        DetachModel();
        _drawable.Dispose();
        _minimap.Dispose();
    }

    private void Rebuild()
    {
        DetachModel();
        if (_document is null)
        {
            Invalidate();
            return;
        }

        EditorModel model;
        if (_sessionGiven)
        {
            model = new EditorModel(_document, _givenSession, _language, _options, _theme, _measurer, _clipboard, workspaceRoot: _workspaceRoot, activities: Activities);
        }
        else if (Sessions is { } pool)
        {
            // The split editor: one session per document, shared by its views (counted; the last view lets it go).
            _lease = pool.Acquire(_document, _language, EditorModel.OpenContext(_document, _workspaceRoot, _options, Activities));
            model = new EditorModel(_document, _lease?.Session, _language, _options, _theme, _measurer, _clipboard, workspaceRoot: _workspaceRoot, activities: Activities);
        }
        else
        {
            model = new EditorModel(_document, _language, _options, _theme, _measurer, _clipboard, workspaceRoot: _workspaceRoot, activities: Activities);
        }

        model.IsReadOnly = _readOnly;
        model.ShowLineNumbers = _lineNumbers;
        model.Host = Host;
        model.Dispatch = action => Dispatcher.DispatchLabelled("editor: a language's answer", action);
        model.DiagnosticsChanged += OnModelDiagnosticsChanged;
        model.DefinitionRequested += OnModelDefinitionRequested;
        model.PeekRequested += OnModelPeekRequested;
        model.Invalidated += OnModelInvalidated;
        model.CaretMoved += OnModelCaretMoved;
        model.ScrollRequested += OnModelScrollRequested;
        model.ContentSizeChanged += OnModelContentSizeChanged;
        model.SaveRequested += OnModelSaveRequested;
        model.FindRequested += OnModelFindRequested;
        model.Reclassified += OnModelReclassified;
        model.StatusRequested += OnModelStatusRequested;
        _model = model;
        _drawable.Model = model;
        UpdateMinimap();
        model.UpdateViewport((float)_scroll.ScrollX, (float)_scroll.ScrollY, (float)_scroll.Width, (float)_scroll.Height);
        UpdateContentSize();
        Invalidate();
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DetachModel()
    {
        if (_model is not { } model)
        {
            return;
        }

        model.Invalidated -= OnModelInvalidated;
        model.CaretMoved -= OnModelCaretMoved;
        model.ScrollRequested -= OnModelScrollRequested;
        model.ContentSizeChanged -= OnModelContentSizeChanged;
        model.SaveRequested -= OnModelSaveRequested;
        model.FindRequested -= OnModelFindRequested;
        model.Reclassified -= OnModelReclassified;
        model.StatusRequested -= OnModelStatusRequested;
        model.DiagnosticsChanged -= OnModelDiagnosticsChanged;
        model.DefinitionRequested -= OnModelDefinitionRequested;
        model.PeekRequested -= OnModelPeekRequested;
        model.Dispatch = null;
        _drawable.Model = null;
        _minimap.Model = null;
        _model = null;
        model.Dispose();
        _lease?.Dispose();                               // after the model: the session goes with its last view
        _lease = null;
    }

    // The minimap shows beside the text while it is on, this view may show it and the view is wide enough (the layout
    // follows the measured width); the model then leaves the ruler to it.
    private void UpdateMinimap()
    {
        bool show = _showsMinimap && _model is not null && MinimapRenderer.Shows(_options, (float)Width);
        _minimap.WidthRequest = MinimapRenderer.WidthOf(_options);
        _minimap.IsVisible = show;
        _minimap.Model = show ? _model : null;
        if (_model is { } model)
        {
            model.MinimapShown = show;
        }
    }

    private void OnModelStatusRequested(object? sender, string message) => StatusRequested?.Invoke(this, message);

    private void UpdateContentSize()
    {
        double viewportWidth = Math.Max(0, _scroll.Width);
        double viewportHeight = Math.Max(0, _scroll.Height);
        double width = viewportWidth;
        double height = viewportHeight;
        if (_model is { } model)
        {
            width = Math.Max(width, model.ContentWidth);
            height = Math.Max(height, model.ContentHeight);
        }

        _content.WidthRequest = width;
        _content.HeightRequest = height;
        _graphics.WidthRequest = viewportWidth;
        _graphics.HeightRequest = viewportHeight;
        _caretLayer.WidthRequest = viewportWidth;
        _caretLayer.HeightRequest = viewportHeight;
        _drawable.ViewportWidth = (float)viewportWidth;
        _drawable.ViewportHeight = (float)viewportHeight;
    }

    private void OnContextTapped(object? sender, TappedEventArgs e)
    {
        _model?.KeepSelectionForContextMenu();
        ContextRequested?.Invoke(this, e);
    }

    // The wheel (phase X's smooth scrolling): the turn adds to where the scroll is going, and the offsets move there on
    // the ease-out curve over CRAFT_SCROLL_MS; with reduced motion, or 0, at once. CRAFT_SCROLL_WHEEL_LINES a notch.
    private bool OnWheel(double deltaX, double deltaY, bool precise)
    {
        if (_model is not { } model)
        {
            return false;
        }

        double line = model.Metrics.LineHeight;
        int lines = _options.ScrollWheelLines;
        double dy = SmoothScroll.WheelPixels(deltaY, lines, line);
        if (deltaY != 0 && model.WheelOverPopup((float)dy))
        {
            return true;                                 // the X restart: the wheel over a hover card scrolls the card
        }

        double dx = -SmoothScroll.WheelPixels(deltaX, lines, model.Metrics.CellWidth * 2);
        ScrollSmoothlyBy(dx, dy);
        return true;
    }

    private void ScrollSmoothlyBy(double dx, double dy)
    {
        if (!_smooth.IsMoving)
        {
            _smooth.Jump(_scroll.ScrollX, _scroll.ScrollY);
        }

        var (maxX, maxY) = MaxScroll();
        _smooth.ScrollBy(dx, dy, maxX, maxY, TimeProvider.System.GetTimestamp());
        StartSmoothScroll();
    }

    private void ScrollSmoothlyTo(double x, double y)
    {
        if (!_smooth.IsMoving)
        {
            _smooth.Jump(_scroll.ScrollX, _scroll.ScrollY);
        }

        var (maxX, maxY) = MaxScroll();
        _smooth.ScrollTo(x, y, maxX, maxY, TimeProvider.System.GetTimestamp());
        StartSmoothScroll();
    }

    private (double X, double Y) MaxScroll() => _model is { } model
        ? (Math.Max(0, Math.Max(_scroll.Width, model.ContentWidth) - _scroll.Width), Math.Max(0, Math.Max(_scroll.Height, model.ContentHeight) - _scroll.Height))
        : (0, 0);

    private void StartSmoothScroll()
    {
        if (Motion.ScrollDuration(_options) <= 0)
        {
            _smooth.Step(TimeProvider.System.GetTimestamp(), 0, TimeProvider.System);
            EndSmoothScroll();
            return;
        }

        if (_scrollTimer is null)
        {
            _scrollTimer = Dispatcher.CreateTimer();
            _scrollTimer.Interval = TimeSpan.FromMilliseconds(16);
            _scrollTimer.Tick += OnScrollTick;
        }

        if (!_scrollTimer.IsRunning)
        {
            _scrollTimer.Start();
        }
    }

    // A frame of the smooth scroll: the drawn offsets move (only the lines they show are laid out and drawn); the scroll
    // view's own offset (its bars) follows once the move ends. Nothing is allocated per frame.
    private void OnScrollTick(object? sender, EventArgs e)
    {
        using var label = _labels?.Enter("editor: smooth scroll");
        bool moving = _smooth.Step(TimeProvider.System.GetTimestamp(), Motion.ScrollDuration(_options), TimeProvider.System);
        ShowOffsets((float)_smooth.X, (float)_smooth.Y);
        if (!moving)
        {
            EndSmoothScroll();
        }
    }

    private void EndSmoothScroll()
    {
        _scrollTimer?.Stop();
        ShowOffsets((float)_smooth.X, (float)_smooth.Y);
        _ = _scroll.ScrollToAsync(_smooth.X, _smooth.Y, false);
    }

    private void ShowOffsets(float x, float y)
    {
        _drawable.ScrollX = x;
        _drawable.ScrollY = y;
        _model?.UpdateViewport(x, y, (float)_scroll.Width, (float)_scroll.Height);
        Invalidate();
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        if (_smooth.IsMoving && _scrollTimer?.IsRunning == true)
        {
            // The scroll bar moved while a smooth scroll ran: the user's drag wins.
            _scrollTimer.Stop();
        }

        _smooth.Jump(e.ScrollX, e.ScrollY);
        _graphics.TranslationX = e.ScrollX;
        _graphics.TranslationY = e.ScrollY;
        _caretLayer.TranslationX = e.ScrollX;
        _caretLayer.TranslationY = e.ScrollY;
        _drawable.ScrollX = (float)e.ScrollX;
        _drawable.ScrollY = (float)e.ScrollY;
        _model?.UpdateViewport((float)e.ScrollX, (float)e.ScrollY, (float)_scroll.Width, (float)_scroll.Height);
        Invalidate();
    }

    private void OnViewportSizeChanged(object? sender, EventArgs e)
    {
        UpdateContentSize();
        _model?.UpdateViewport((float)_scroll.ScrollX, (float)_scroll.ScrollY, (float)_scroll.Width, (float)_scroll.Height);
        Invalidate();
    }

    private void OnModelReclassified(object? sender, EventArgs e)
    {
        // Raised on the language's parse thread: the repaint happens on the UI thread, for the model still shown.
        var model = sender as EditorModel;
        Dispatcher.DispatchLabelled("editor: new colours", () =>
        {
            if (model is not null && ReferenceEquals(model, _model))
            {
                _reclassifying = true;
                try
                {
                    model.ApplyReclassification();
                }
                finally
                {
                    _reclassifying = false;
                }
            }
        });
    }

    private void OnModelInvalidated(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("editor: redraw", () => OnModelInvalidated(sender, e));
            return;
        }

        if (_reclassifying)
        {
            // New colours only: the caret keeps its blink.
            Invalidate();
            return;
        }

        _caretOn = _focusedView;
        _drawable.CaretVisible = _focusedView;
        RestartBlink();
        Invalidate();
    }

    private void OnDrawn(object? sender, EventArgs e) => Dispatcher.DispatchLabelled("editor: painted", RaisePainted);

    private void RaisePainted()
    {
        if (_model?.IsAnimating == true)
        {
            Invalidate();                                // the completion list unfolding: one more frame
        }

        Painted?.Invoke(this, EventArgs.Empty);
    }

    private void OnModelCaretMoved(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("editor: the caret moved", () => CaretMoved?.Invoke(this, EventArgs.Empty));
            return;
        }

        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private void OnModelSaveRequested(object? sender, EventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void OnModelFindRequested(object? sender, FindRequestedEventArgs e) => FindRequested?.Invoke(this, e);

    private void OnModelContentSizeChanged(object? sender, EventArgs e)
    {
        if (_drawable.IsDrawing)
        {
            // A line drawn wider than any before: resize after the frame, never inside it.
            Dispatcher.DispatchLabelled("editor: content size", UpdateContentSize);
        }
        else
        {
            UpdateContentSize();
        }
    }

    private void OnModelScrollRequested(object? sender, EditorScrollEventArgs e)
    {
        UpdateContentSize();
        double x = e.X;
        double y = e.Y;
        // A jump of more than a line (Page Down, Go to line, a search hit) scrolls smoothly; the line typing pushes in
        // view stays at once, so typing at the edge never lags.
        double line = _model?.Metrics.LineHeight ?? 0;
        double from = _smooth.IsMoving ? _smooth.TargetY : _scroll.ScrollY;
        bool smooth = !e.Instant && Motion.ScrollDuration(_options) > 0 && (Math.Abs(y - from) > line * 1.5 || Math.Abs(x - (_smooth.IsMoving ? _smooth.TargetX : _scroll.ScrollX)) > line * 8);
        Dispatcher.DispatchLabelled("editor: scroll", () =>
        {
            if (smooth)
            {
                ScrollSmoothlyTo(x, y);
            }
            else
            {
                _scrollTimer?.Stop();
                _smooth.Jump(x, y);
                _ = _scroll.ScrollToAsync(x, y, false);
            }
        });
    }

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (_model is null || e.Touches.Length == 0)
        {
            return;
        }

        var modifiers = _inputConnection is IKeyModifierSource source ? source.CurrentModifiers : KeyModifiers.None;
        var point = e.Touches[0];
        using var label = _labels?.Enter("editor: pointer pressed");
        Engaged?.Invoke(this, EventArgs.Empty);
        if (_model is not { } model)
        {
            return;
        }

        model.PointerPressed(point.X, point.Y, modifiers);
        if (model.ClickCount == 2)
        {
            DoubleClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (_model is null || e.Touches.Length == 0)
        {
            return;
        }

        var point = e.Touches[0];
        using var label = _labels?.Enter("editor: pointer moved");
        _model.PointerMoved(point.X, point.Y);
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        using var label = _labels?.Enter("editor: pointer released");
        _model?.PointerReleased();
    }

    private void OnMoveHoverInteraction(object? sender, TouchEventArgs e)
    {
        if (_model is null || e.Touches.Length == 0)
        {
            return;
        }

        var point = e.Touches[0];
        if (_inputConnection is IKeyModifierSource source)
        {
            // X part 9: CRAFT_INLAY_HINTS "ctrl-held" (the platform tells no lone modifier key; the pointer's moves and the keys read it).
            _model.ControlHeld = (source.CurrentModifiers & KeyModifiers.Control) != 0;
        }

        _model.PointerHovered(point.X, point.Y);
    }

    private void OnEndHoverInteraction(object? sender, EventArgs e) => _model?.PointerExited();

    // A buffer changed off the UI thread (an edit applied from a worker) moves the diagnostics there: the page's Problems
    // panel and status bar are WinUI objects, touched only on the UI thread (a fatal COM error otherwise).
    private void OnModelDiagnosticsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("editor: the diagnostics changed", () => DiagnosticsChanged?.Invoke(this, EventArgs.Empty));
            return;
        }

        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnModelDefinitionRequested(object? sender, DefinitionRequestedEventArgs e) => DefinitionRequested?.Invoke(this, e);

    private void OnModelPeekRequested(object? sender, DefinitionRequestedEventArgs e) => PeekRequested?.Invoke(this, e);

    // A key or text that reached a view of a group not focused (the platform's focus moved there): its group takes the focus.
    private void EngageFromKeyboard()
    {
        if (!_focusedView)
        {
            Engaged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnGraphicsHandlerChanged(object? sender, EventArgs e)
    {
        _inputConnection?.Dispose();
        _inputConnection = null;
#if WINDOWS
        if (_graphics.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
        {
            _inputConnection = new WinUI.WindowsTextInputAdapter().Attach(element, _input);
        }
#endif
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_focusedView)
        {
            RestartBlink();
        }
    }

    private void OnUnloaded(object? sender, EventArgs e) => _blink?.Stop();

    private void RestartBlink()
    {
        int ms = _options.CaretBlinkMilliseconds;
        if (ms <= 0 || !_focusedView)
        {
            _blink?.Stop();
            return;
        }

        if (_blink is null)
        {
            _blink = Dispatcher.CreateTimer();
            _blink.Tick += OnBlink;
        }

        _blink.Stop();
        _blink.Interval = TimeSpan.FromMilliseconds(ms);
        _blink.Start();
    }

    // B1: a blink repaints the caret layer only; while a popup or ghost text shows, the text frame draws the carets (under
    // them), so it is that frame that repaints.
    private void OnBlink(object? sender, EventArgs e)
    {
        _caretOn = !_caretOn;
        _drawable.CaretVisible = _caretOn;
        if (_model is { } model && EditorRenderer.CaretsOnTextLayer(model))
        {
            _graphics.Invalidate();
        }

        _caretLayer.Invalidate();
    }

    /// <summary>The keyboard's sink: the current model, whichever it is when the key arrives.</summary>
    private sealed class InputForwarder(EditorView view) : IEditorInputSink, IEditorScrollSink
    {
        public bool OnWheel(double deltaX, double deltaY, bool precise)
        {
            using var label = view._labels?.Enter("editor: wheel");
            return view.OnWheel(deltaX, deltaY, precise);
        }

        private static readonly Dictionary<EditorKey, string> KeyLabels = Enum.GetValues<EditorKey>().Distinct().ToDictionary(k => k, k => "editor: key " + k.ToString());

        public bool OnKey(EditorKey key, KeyModifiers modifiers)
        {
            using var label = view._labels?.Enter(KeyLabel(key));
            view.EngageFromKeyboard();
            if (view._model is { } model)
            {
                model.ControlHeld = (modifiers & KeyModifiers.Control) != 0;
            }

            return view.KeyHandler is { } handler ? handler.HandleKey(key, modifiers, view._model) : view._model?.OnKey(key, modifiers) ?? false;
        }

        public void OnText(string text)
        {
            using var label = view._labels?.Enter("editor: typed text");
            view.EngageFromKeyboard();
            view._model?.OnText(text);
        }

        // The key's label, made once per key (a keystroke allocates nothing for it).
        private static string KeyLabel(EditorKey key) => KeyLabels.TryGetValue(key, out var label) ? label : "editor: key";
    }
}
