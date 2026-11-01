// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Editor;
using Craft.Editor.Preview;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls.Shapes;

namespace Craft.Maui;

/// <summary>
/// A Markdown document's preview tab (X part 9, item 19): a header with the source's name and its buttons, then the
/// rendered text in Craft's own editor surface, read-only (<see cref="MarkdownPreview"/>: the chat's renderer, code
/// coloured, only the shown lines drawn), following the source editor's scroll; a click on a link opens it (an image in
/// its preview, a file in its tab, an address in the browser), a code block's Copy copies it.
/// </summary>
public sealed class MarkdownPreviewView : ContentView, IDisposable
{
    private readonly ShellModel _shell;
    private readonly MarkdownPreview _preview;
    private readonly EditorView _view;
    private readonly Label _title;
    private readonly Button _source;
    private EditorModel? _model;
    private EditorModel? _followed;

    /// <summary>The view of <paramref name="preview"/> in <paramref name="shell"/>.</summary>
    public MarkdownPreviewView(ShellModel shell, MarkdownPreview preview, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _title = palette.Label("Preview · " + System.IO.Path.GetFileName(preview.SourcePath ?? "untitled"), palette.Text, palette.SmallSize, FontWeight.SemiBold);
        _title.VerticalOptions = LayoutOptions.Center;
        _source = palette.Flat("Open the source", palette.Secondary, 26, palette.SmallSize);
        _source.Padding = new Thickness(10, 0);
        ToolTipProperties.SetText(_source, "Show the Markdown file this preview renders");
        _source.Clicked += (_, _) => ShowSource();
        _view = new EditorView
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            Theme = MarkdownPreview.Theme(palette.Theme),
            Options = Options(palette.Options),
        };
        _view.ModelChanged += (_, _) => AttachModel(_view.Model);
        _view.Open(preview.Document, preview.Language);
        _preview.Colors = palette.Theme.Colors;
        _preview.Updated += OnUpdated;
        _shell.Groups.ActiveChanged += OnGroupsChanged;
        _shell.Groups.GroupsChanged += OnGroupsChanged2;
        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Padding = new Thickness(16, 6),
            BackgroundColor = palette.Surface,
        };
        header.Add(_title, 0, 0);
        header.Add(_source, 1, 0);
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        grid.Add(header, 0, 0);
        grid.Add(_view, 0, 1);
        Content = grid;
        SemanticProperties.SetDescription(this, "Markdown preview");
        FollowSource();
    }

    /// <summary>Takes <paramref name="palette"/>'s colours and settings.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _view.Theme = MarkdownPreview.Theme(palette.Theme);
        _view.Options = Options(palette.Options with { FontFamily = _view.Options.FontFamily });
        _preview.Colors = palette.Theme.Colors;
        _title.TextColor = palette.Text;
        _source.TextColor = palette.Secondary;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _preview.Updated -= OnUpdated;
        _shell.Groups.ActiveChanged -= OnGroupsChanged;
        _shell.Groups.GroupsChanged -= OnGroupsChanged2;
        AttachModel(null);
        Follow(null);
        _view.Dispose();
    }

    private static CraftOptions Options(CraftOptions options) => options with { BottomMarginLines = 0, ScrollMarginColumns = 0, Minimap = false, InlineCompletionProvider = "none" };

    private void AttachModel(EditorModel? model)
    {
        if (_model is { } old)
        {
            old.PointerActivity -= OnPointer;
            old.ViewportChanged -= OnViewportChanged;
            old.LineBackdrop = null;
        }

        _model = model;
        if (model is not null)
        {
            model.LineBackdrop = _preview.Backdrop;
            model.PointerActivity += OnPointer;
            model.ViewportChanged += OnViewportChanged;
        }
    }

    // The preview wraps at its width in cells.
    private void OnViewportChanged(object? sender, EventArgs e)
    {
        if (_model is { } model && model.Metrics.CellWidth > 0)
        {
            int columns = (int)((model.ViewportWidth - model.TextLeft) / model.Metrics.CellWidth) - 1;
            if (columns >= 20)
            {
                _preview.Columns = columns;
            }
        }
    }

    private void OnUpdated(object? sender, EventArgs e) => Sync();

    private void OnGroupsChanged(object? sender, EditorGroup e) => FollowSource();

    private void OnGroupsChanged2(object? sender, EventArgs e) => FollowSource();

    // The source's editor (its tab's view in any group): its scroll moves the preview.
    private void FollowSource()
    {
        EditorModel? source = null;
        foreach (var tab in _shell.Groups.AllTabs)
        {
            if (ReferenceEquals(tab.HostDocument, _preview.Source) && tab.View is EditorView { Model: { } model })
            {
                source = model;
                break;
            }
        }

        Follow(source);
    }

    private void Follow(EditorModel? source)
    {
        if (ReferenceEquals(source, _followed))
        {
            return;
        }

        if (_followed is { } old)
        {
            old.ViewportChanged -= OnSourceScrolled;
        }

        _followed = source;
        if (source is not null)
        {
            source.ViewportChanged += OnSourceScrolled;
        }
    }

    private void OnSourceScrolled(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        if (_followed is { } source && _model is { } model)
        {
            _preview.Follow(source, model);
        }
    }

    private void ShowSource()
    {
        foreach (var group in _shell.Groups.Groups)
        {
            foreach (var tab in group.Tabs.Tabs)
            {
                if (ReferenceEquals(tab.HostDocument, _preview.Source))
                {
                    _shell.Groups.Focus(group);
                    group.Tabs.Activate(tab);
                    _shell.Pages.ShowDocument();
                    return;
                }
            }
        }
    }

    // A click: a link opens (an image in its preview, a file in its tab, an address in the browser); Copy copies.
    private void OnPointer(object? sender, EditorPointerEventArgs e)
    {
        if (e.Kind != EditorPointerKind.Click || _preview.ActionAt(e.Position) is not { } action)
        {
            return;
        }

        if (action.Kind == "copy")
        {
            _ = MauiEditorClipboard.Shared.SetTextAsync(action.Text);
            _shell.Host.ShowStatus("Markdown preview", "Copied the code block.");
            return;
        }

        if (_preview.FileOf(action.Text) is { } file)
        {
            _ = ImageFormats.IsImage(file) ? (Task)_shell.OpenImageAsync(file) : _shell.OpenFileAsync(file);
        }
        else if (Uri.TryCreate(action.Text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto))
        {
            _ = Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(uri);
        }
    }
}

/// <summary>
/// An image's preview tab (X part 9, item 20): the image fitted to the tab or zoomed (−, +, 100%, Fit), on a checkered
/// ground; a raster image through MAUI's decoder, a simple SVG drawn from its shapes with MAUI Graphics' path parser (one
/// that uses more is said, with Open as text); its format, size and zoom in the status bar. The file is read off the UI
/// thread (<see cref="ShellModel.OpenImageAsync"/>).
/// </summary>
public sealed class ImagePreviewView : ContentView, IDisposable
{
    private readonly ShellModel _shell;
    private readonly string _pageId;
    private readonly Grid _stage = new();
    private readonly ScrollView _scroll;
    private readonly Label _note;
    private readonly Label _zoomLabel;
    private ShellPalette _palette;
    private ImagePreview? _image;
    private View? _picture;

    /// <summary>The view of the page <paramref name="pageId"/> of <paramref name="shell"/>.</summary>
    public ImagePreviewView(ShellModel shell, string pageId, ShellPalette palette)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _pageId = pageId ?? throw new ArgumentNullException(nameof(pageId));
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        _note = palette.Label("Reading the image…", palette.Secondary);
        _note.HorizontalOptions = LayoutOptions.Center;
        _note.VerticalOptions = LayoutOptions.Center;
        _zoomLabel = palette.Label("", palette.Secondary, palette.SmallSize);
        _zoomLabel.VerticalOptions = LayoutOptions.Center;
        _scroll = new ScrollView { Orientation = ScrollOrientation.Both, Content = _stage };
        var tools = new HorizontalStackLayout { Spacing = 4, Padding = new Thickness(12, 4), VerticalOptions = LayoutOptions.Center };
        tools.Children.Add(Tool("−", "Zoom out", () => Zoom(-1)));
        tools.Children.Add(Tool("+", "Zoom in", () => Zoom(1)));
        tools.Children.Add(Tool("100%", "Its own size", () => _image?.ActualSize()));
        tools.Children.Add(Tool("Fit", "Fit to the tab", () => _image?.FitToView()));
        tools.Children.Add(_zoomLabel);
        var asText = Tool("Open as text", "Open the file's text in a tab (an SVG's source)", () => _ = _shell.OpenAsTextAsync(_pageId[ShellModel.ImagePagePrefix.Length..]));
        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            BackgroundColor = palette.Surface,
        };
        header.Add(tools, 0, 0);
        header.Add(asText, 1, 0);
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)], BackgroundColor = palette.Ground };
        grid.Add(header, 0, 0);
        grid.Add(_scroll, 0, 1);
        grid.Add(_note, 0, 1);
        Content = grid;
        SizeChanged += (_, _) => Layout2();
        _shell.ImageChanged += OnImageChanged;
        SemanticProperties.SetDescription(this, "Image preview");
        Show();
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        _note.TextColor = palette.Secondary;
        _zoomLabel.TextColor = palette.Secondary;
        (_picture as GraphicsView)?.Invalidate();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shell.ImageChanged -= OnImageChanged;
        if (_image is not null)
        {
            _image.ZoomChanged -= OnZoomChanged;
        }
    }

    private Button Tool(string text, string tip, Action run)
    {
        var button = _palette.Flat(text, _palette.Text, 26, _palette.SmallSize);
        button.Padding = new Thickness(10, 0);
        ToolTipProperties.SetText(button, tip);
        button.Clicked += (_, _) => run();
        return button;
    }

    private void OnImageChanged(object? sender, string id)
    {
        if (id == _pageId)
        {
            Show();
        }
    }

    private void Show()
    {
        if (_image is not null || !_shell.ImagePreviews.TryGetValue(_pageId, out var image))
        {
            return;
        }

        _image = image;
        image.ZoomChanged += OnZoomChanged;
        if (image.Problem is { } problem && image.Svg is null)
        {
            _note.Text = problem;
            return;
        }

        if (image.Svg is { } svg)
        {
            if (svg.Unsupported is not null)
            {
                _note.Text = image.Problem;
                return;
            }

            _picture = new GraphicsView { Drawable = new SvgDrawable(svg, () => _palette.Text) };
        }
        else if (!image.Bytes.IsEmpty && System.Runtime.InteropServices.MemoryMarshal.TryGetArray(image.Bytes, out var bytes))
        {
            _picture = new Image { Source = ImageSource.FromStream(() => new MemoryStream(bytes.Array!, bytes.Offset, bytes.Count, writable: false)), Aspect = Aspect.Fill };
        }

        if (_picture is null)
        {
            return;
        }

        _note.IsVisible = false;
        var checkers = new Border
        {
            Content = _picture,
            StrokeThickness = 1,
            Stroke = _palette.Border,
            StrokeShape = new Rectangle(),
            BackgroundColor = _palette.Raised,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        _stage.Children.Clear();
        _stage.Children.Add(checkers);
        Layout2();
    }

    private void OnZoomChanged(object? sender, EventArgs e) => Layout2();

    private void Zoom(int steps)
    {
        if (_image is not { } image)
        {
            return;
        }

        double shown = image.ScaleFor(_scroll.Width, _scroll.Height);
        if (steps > 0)
        {
            image.ZoomIn(shown);
        }
        else
        {
            image.ZoomOut(shown);
        }
    }

    // The picture's size at the scale shown; the status bar says it.
    private void Layout2()
    {
        if (_image is not { } image || _picture is null || image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        double scale = image.ScaleFor(Math.Max(1, _scroll.Width - 32), Math.Max(1, _scroll.Height - 32));
        _picture.WidthRequest = image.Width * scale;
        _picture.HeightRequest = image.Height * scale;
        _stage.MinimumWidthRequest = Math.Max(_scroll.Width, (image.Width * scale) + 32);
        _stage.MinimumHeightRequest = Math.Max(_scroll.Height, (image.Height * scale) + 32);
        string text = image.StatusText(scale);
        _zoomLabel.Text = text;
        if (ReferenceEquals(_shell.Pages.Active?.Id, _pageId) || _shell.Pages.Active?.Id == _pageId)
        {
            _shell.ShowImageStatus(text);
        }
    }

    // A simple SVG's shapes scaled from its view box to the view, each filled and stroked through MAUI Graphics' path parser.
    private sealed class SvgDrawable(SvgDrawing svg, Func<Color> current) : IDrawable
    {
        private readonly List<(PathF Path, SvgShape Shape)> _paths = [.. svg.Shapes.Select(s => (PathBuilder.Build(s.PathData), s))];

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float scale = Math.Min(dirtyRect.Width / svg.ViewWidth, dirtyRect.Height / svg.ViewHeight);
            canvas.SaveState();
            canvas.Scale(scale, scale);
            canvas.Translate(-svg.ViewX, -svg.ViewY);
            foreach (var (path, shape) in _paths)
            {
                if (shape.Fill is { } fill)
                {
                    canvas.FillColor = ColorOf(fill);
                    canvas.FillPath(path);
                }

                if (shape.Stroke is { } stroke)
                {
                    canvas.StrokeColor = ColorOf(stroke);
                    canvas.StrokeSize = shape.StrokeWidth;
                    canvas.DrawPath(path);
                }
            }

            canvas.RestoreState();
        }

        private Color ColorOf(uint argb) => argb == SvgDrawing.CurrentColor ? current()
            : Color.FromRgba((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
    }
}
