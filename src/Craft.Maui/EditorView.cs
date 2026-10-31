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
    private readonly Grid _content;
    private readonly GraphicsView _graphics;
    private readonly MauiTextMeasurer _measurer = new();
    private readonly EditorDrawable _drawable;
    private readonly MauiEditorClipboard _clipboard = new();
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
    private bool _caretOn = true;
    private bool _reclassifying;
    private bool _disposed;

    /// <summary>An empty editor (set <see cref="Document"/> to show one).</summary>
    public EditorView()
    {
        _drawable = new EditorDrawable(_measurer);
        _drawable.Drawn += OnDrawn;
        _input = new InputForwarder(this);
        _graphics = new GraphicsView
        {
            Drawable = _drawable,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
        };
        _content = new Grid
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
        };
        _content.Children.Add(_graphics);
        _scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Both,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Default,
            VerticalScrollBarVisibility = ScrollBarVisibility.Default,
            Content = _content,
        };
        Content = _scroll;

        _scroll.Scrolled += OnScrolled;
        _scroll.SizeChanged += OnViewportSizeChanged;
        _graphics.StartInteraction += OnStartInteraction;
        _graphics.DragInteraction += OnDragInteraction;
        _graphics.EndInteraction += OnEndInteraction;
        _graphics.MoveHoverInteraction += OnMoveHoverInteraction;
        _graphics.EndHoverInteraction += OnEndHoverInteraction;
        _graphics.HandlerChanged += OnGraphicsHandlerChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The editor of the current document, or null when there is none.</summary>
    public EditorModel? Model => _model;

    /// <summary>The paint's counters (run strings built, lines drawn).</summary>
    public EditorRenderer Renderer => _drawable.Renderer;

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

    /// <summary>Where the document's language shows its long work (the host's activities), or null; set before <see cref="Open"/>.</summary>
    public IEditorActivities? Activities { get; set; }

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

    /// <summary>Raised on F12 when the definition is in another file (the app opens it) or only in metadata.</summary>
    public event EventHandler<DefinitionRequestedEventArgs>? DefinitionRequested;

    /// <summary>Releases the language's semantic memory (its workspace), as on the platform's memory-pressure signal; it is rebuilt on the next request.</summary>
    public void ReleaseSemanticMemory() => _model?.SemanticSession?.Release();

    /// <summary>Shows <paramref name="document"/> in <paramref name="language"/> (null for plain text), making one new model.</summary>
    public void Open(ITextDocument? document, ILanguage? language)
    {
        _document = document;
        _language = language;
        Rebuild();
    }

    /// <summary>Redraws.</summary>
    public void Invalidate() => _graphics.Invalidate();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _blink?.Stop();
        _inputConnection?.Dispose();
        _inputConnection = null;
        DetachModel();
        _drawable.Dispose();
    }

    private void Rebuild()
    {
        DetachModel();
        if (_document is null)
        {
            Invalidate();
            return;
        }

        var model = new EditorModel(_document, _language, _options, _theme, _measurer, _clipboard, workspaceRoot: _workspaceRoot, activities: Activities);
        model.Dispatch = action => Dispatcher.DispatchLabelled("editor: a language's answer", action);
        model.DiagnosticsChanged += OnModelDiagnosticsChanged;
        model.DefinitionRequested += OnModelDefinitionRequested;
        model.Invalidated += OnModelInvalidated;
        model.CaretMoved += OnModelCaretMoved;
        model.ScrollRequested += OnModelScrollRequested;
        model.ContentSizeChanged += OnModelContentSizeChanged;
        model.SaveRequested += OnModelSaveRequested;
        model.FindRequested += OnModelFindRequested;
        model.Reclassified += OnModelReclassified;
        _model = model;
        _drawable.Model = model;
        model.UpdateViewport((float)_scroll.ScrollX, (float)_scroll.ScrollY, (float)_scroll.Width, (float)_scroll.Height);
        UpdateContentSize();
        Invalidate();
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
        model.DiagnosticsChanged -= OnModelDiagnosticsChanged;
        model.DefinitionRequested -= OnModelDefinitionRequested;
        model.Dispatch = null;
        _drawable.Model = null;
        _model = null;
        model.Dispose();
    }

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
        _drawable.ViewportWidth = (float)viewportWidth;
        _drawable.ViewportHeight = (float)viewportHeight;
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        _graphics.TranslationX = e.ScrollX;
        _graphics.TranslationY = e.ScrollY;
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

        _caretOn = true;
        _drawable.CaretVisible = true;
        RestartBlink();
        Invalidate();
    }

    private void OnDrawn(object? sender, EventArgs e) => Dispatcher.DispatchLabelled("editor: painted", RaisePainted);

    private void RaisePainted() => Painted?.Invoke(this, EventArgs.Empty);

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
        Dispatcher.DispatchLabelled("editor: scroll", () => _ = _scroll.ScrollToAsync(x, y, false));
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
        _model.PointerPressed(point.X, point.Y, modifiers);
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

    private void OnLoaded(object? sender, EventArgs e) => RestartBlink();

    private void OnUnloaded(object? sender, EventArgs e) => _blink?.Stop();

    private void RestartBlink()
    {
        int ms = _options.CaretBlinkMilliseconds;
        if (ms <= 0)
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

    private void OnBlink(object? sender, EventArgs e)
    {
        _caretOn = !_caretOn;
        _drawable.CaretVisible = _caretOn;
        Invalidate();
    }

    /// <summary>The keyboard's sink: the current model, whichever it is when the key arrives.</summary>
    private sealed class InputForwarder(EditorView view) : IEditorInputSink
    {
        private static readonly Dictionary<EditorKey, string> KeyLabels = Enum.GetValues<EditorKey>().Distinct().ToDictionary(k => k, k => "editor: key " + k.ToString());

        public bool OnKey(EditorKey key, KeyModifiers modifiers)
        {
            using var label = view._labels?.Enter(KeyLabel(key));
            return view.KeyHandler is { } handler ? handler.HandleKey(key, modifiers, view._model) : view._model?.OnKey(key, modifiers) ?? false;
        }

        public void OnText(string text)
        {
            using var label = view._labels?.Enter("editor: typed text");
            view._model?.OnText(text);
        }

        // The key's label, made once per key (a keystroke allocates nothing for it).
        private static string KeyLabel(EditorKey key) => KeyLabels.TryGetValue(key, out var label) ? label : "editor: key";
    }
}
