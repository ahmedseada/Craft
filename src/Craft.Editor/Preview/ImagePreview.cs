// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;

namespace Craft.Editor.Preview;

/// <summary>An image file's format and size, read from its first bytes (no decoder: PNG's header, GIF's screen, BMP's, JPEG's frame, ICO's entries).</summary>
public static class ImageFormats
{
    /// <summary>The extensions the image preview opens (with their dot).</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg"];

    /// <summary>Whether <paramref name="path"/> is an image the preview opens (by its extension).</summary>
    public static bool IsImage(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path.AsSpan());
        foreach (var known in Extensions)
        {
            if (extension.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The format ("PNG", "JPEG", "GIF", "BMP", "ICO") and size in pixels of <paramref name="bytes"/>; false when they are none of these.</summary>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out string format, out int width, out int height)
    {
        format = "";
        width = height = 0;
        if (bytes.Length >= 24 && bytes.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            format = "PNG";
            width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..]);
            height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..]);
            return true;
        }

        if (bytes.Length >= 10 && (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)))
        {
            format = "GIF";
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]);
            return true;
        }

        if (bytes.Length >= 26 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            format = "BMP";
            int header = BinaryPrimitives.ReadInt32LittleEndian(bytes[14..]);
            if (header == 12)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]);
                height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]);
            }
            else
            {
                width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]);
                height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]));     // negative: rows top-down
            }

            return true;
        }

        if (bytes.Length >= 6 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0)
        {
            // ICO: the largest of its images (a 0 in an entry means 256).
            int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
            if (count == 0 || bytes.Length < 6 + (16 * count))
            {
                return false;
            }

            format = "ICO";
            for (int i = 0; i < count; i++)
            {
                var entry = bytes.Slice(6 + (16 * i), 16);
                int w = entry[0] == 0 ? 256 : entry[0];
                int h = entry[1] == 0 ? 256 : entry[1];
                if (w * h > width * height)
                {
                    (width, height) = (w, h);
                }
            }

            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            // JPEG: the segments up to a start of frame (SOF0-SOF15 but DHT, JPG and DAC), whose height and width it holds.
            int at = 2;
            while (at + 9 < bytes.Length)
            {
                if (bytes[at] != 0xFF)
                {
                    at++;
                    continue;
                }

                byte marker = bytes[at + 1];
                if (marker == 0xFF)
                {
                    at++;
                    continue;
                }

                if (marker is >= 0xD0 and <= 0xD9 || marker == 0x01)
                {
                    at += 2;                                  // markers without a length
                    continue;
                }

                int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    format = "JPEG";
                    height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..]);
                    width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..]);
                    return true;
                }

                at += 2 + length;
            }

            format = "JPEG";
            return true;
        }

        return false;
    }
}

/// <summary>A shape of a simple SVG drawing: its path (SVG path data, what MAUI Graphics' path builder reads), its fill and stroke.</summary>
/// <param name="PathData">The path, in the drawing's user units.</param>
/// <param name="Fill">Its fill, 0xAARRGGBB, or null for none; <see cref="SvgDrawing.CurrentColor"/> for the text's colour.</param>
/// <param name="Stroke">Its stroke, or null for none.</param>
/// <param name="StrokeWidth">The stroke's width.</param>
public sealed record SvgShape(string PathData, uint? Fill, uint? Stroke, float StrokeWidth);

/// <summary>
/// A simple SVG file as shapes (X part 9's image preview draws it with MAUI Graphics' path parser): its size, its view box
/// and its paths, rectangles, circles, ellipses, lines, polylines and polygons with their fills and strokes (inherited from
/// groups). What it does not draw (text, gradients, transforms, images, filters, masks, styles, uses) is named in
/// <see cref="Unsupported"/>, and the preview says so instead of drawing it wrong.
/// </summary>
public sealed record SvgDrawing(float Width, float Height, float ViewX, float ViewY, float ViewWidth, float ViewHeight, IReadOnlyList<SvgShape> Shapes)
{
    /// <summary>The fill or stroke "currentColor": the text's colour where it is drawn.</summary>
    public const uint CurrentColor = 0x00FFFFFEu;

    /// <summary>What it uses that the preview does not draw ("text, linearGradient"), or null when it draws whole.</summary>
    public string? Unsupported { get; init; }

    private static readonly string[] Skipped = ["title", "desc", "metadata", "defs", "svg", "g"];

    /// <summary>Reads <paramref name="bytes"/> (an SVG file's UTF-8).</summary>
    /// <exception cref="XmlException">It is not XML.</exception>
    public static SvgDrawing Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var shapes = new List<SvgShape>();
        var unsupported = new SortedSet<string>(StringComparer.Ordinal);
        var styles = new Stack<(uint? Fill, uint? Stroke, float Width)>();
        styles.Push((0xFF000000u, null, 1f));
        float width = 0, height = 0, vx = 0, vy = 0, vw = 0, vh = 0;
        bool root = true;
        int defs = -1;                                        // the depth of an open <defs>: what it defines is drawn only where used
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = true });
        while (reader.Read())
        {
            if (defs >= 0)
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == defs)
                {
                    defs = -1;
                }

                continue;
            }

            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.LocalName is "g" or "svg" or "a" && styles.Count > 1)
                {
                    styles.Pop();
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            string name = reader.LocalName;
            if (root)
            {
                if (name != "svg")
                {
                    throw new XmlException($"The file's root is <{name}>, not <svg>.");
                }

                root = false;
                width = Length(reader.GetAttribute("width"));
                height = Length(reader.GetAttribute("height"));
                if (reader.GetAttribute("viewBox") is { } box && Numbers(box) is { Count: 4 } view)
                {
                    (vx, vy, vw, vh) = (view[0], view[1], view[2], view[3]);
                }
            }

            if (reader.GetAttribute("transform") is not null)
            {
                unsupported.Add("transform");
            }

            if (reader.GetAttribute("style") is not null || reader.GetAttribute("class") is not null)
            {
                unsupported.Add("style");
            }

            var parent = styles.Peek();
            var style = (Fill: Paint(reader.GetAttribute("fill"), parent.Fill, unsupported), Stroke: Paint(reader.GetAttribute("stroke"), parent.Stroke, unsupported),
                Width: reader.GetAttribute("stroke-width") is { } w ? Length(w) : parent.Width);
            if (reader.GetAttribute("opacity") is { } opacity && float.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out float alpha))
            {
                style.Fill = Alpha(style.Fill, alpha);
                style.Stroke = Alpha(style.Stroke, alpha);
            }

            if (name is "g" or "svg" or "a")
            {
                if (!reader.IsEmptyElement)
                {
                    styles.Push(style);
                }

                continue;
            }

            string? path = name switch
            {
                "path" => reader.GetAttribute("d"),
                "rect" => Rect(reader),
                "circle" => Ellipse(F(reader, "cx"), F(reader, "cy"), F(reader, "r"), F(reader, "r")),
                "ellipse" => Ellipse(F(reader, "cx"), F(reader, "cy"), F(reader, "rx"), F(reader, "ry")),
                "line" => string.Create(CultureInfo.InvariantCulture, $"M{F(reader, "x1")} {F(reader, "y1")}L{F(reader, "x2")} {F(reader, "y2")}"),
                "polyline" or "polygon" => Poly(reader.GetAttribute("points"), name == "polygon"),
                _ => null,
            };
            if (path is null)
            {
                if (Array.IndexOf(Skipped, name) < 0 && !name.StartsWith("sodipodi", StringComparison.Ordinal))
                {
                    unsupported.Add(name);
                }

                if (name == "defs" && !reader.IsEmptyElement)
                {
                    defs = reader.Depth;                      // gradients and patterns defined: said where used (a url() paint)
                }

                continue;
            }

            if (path.Length > 0)
            {
                shapes.Add(new SvgShape(path, style.Fill, style.Stroke, style.Width));
            }
        }

        if (vw <= 0 || vh <= 0)
        {
            (vw, vh) = (width > 0 ? width : 100, height > 0 ? height : 100);
        }

        return new SvgDrawing(width > 0 ? width : vw, height > 0 ? height : vh, vx, vy, vw, vh, shapes)
        {
            Unsupported = unsupported.Count == 0 ? null : string.Join(", ", unsupported),
        };
    }

    private static float F(XmlReader reader, string name) => Length(reader.GetAttribute(name));

    private static string Rect(XmlReader reader)
    {
        float x = F(reader, "x"), y = F(reader, "y"), w = F(reader, "width"), h = F(reader, "height");
        return w <= 0 || h <= 0 ? "" : string.Create(CultureInfo.InvariantCulture, $"M{x} {y}H{x + w}V{y + h}H{x}Z");
    }

    private static string Ellipse(float cx, float cy, float rx, float ry) =>
        rx <= 0 || ry <= 0 ? "" : string.Create(CultureInfo.InvariantCulture, $"M{cx - rx} {cy}A{rx} {ry} 0 1 0 {cx + rx} {cy}A{rx} {ry} 0 1 0 {cx - rx} {cy}Z");

    private static string Poly(string? points, bool closed)
    {
        var numbers = Numbers(points ?? "");
        if (numbers.Count < 4)
        {
            return "";
        }

        var text = new StringBuilder();
        for (int i = 0; i + 1 < numbers.Count; i += 2)
        {
            text.Append(i == 0 ? 'M' : 'L').Append(numbers[i].ToString(CultureInfo.InvariantCulture)).Append(' ').Append(numbers[i + 1].ToString(CultureInfo.InvariantCulture));
        }

        return closed ? text.Append('Z').ToString() : text.ToString();
    }

    private static List<float> Numbers(string text)
    {
        var numbers = new List<float>();
        foreach (var part in text.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                numbers.Add(value);
            }
        }

        return numbers;
    }

    // "12", "12px", "12.5pt": the number (a percentage or another unit taken as user units).
    private static float Length(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var span = text.AsSpan().Trim();
        int end = 0;
        while (end < span.Length && (char.IsAsciiDigit(span[end]) || span[end] is '.' or '-' or '+' or 'e' or 'E'))
        {
            end++;
        }

        return float.TryParse(span[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;
    }

    private static uint? Alpha(uint? color, float alpha) =>
        color is { } c && c != CurrentColor ? (c & 0x00FFFFFFu) | ((uint)Math.Clamp(alpha * ((c >> 24) & 0xFF), 0, 255) << 24) : color;

    // A paint: none, a colour (#rgb, #rrggbb, rgb(), a few names), currentColor, inherit (the parent's); url(#...) is a gradient or pattern.
    private static uint? Paint(string? text, uint? parent, SortedSet<string> unsupported)
    {
        if (text is null || text == "inherit")
        {
            return parent;
        }

        text = text.Trim();
        if (text == "none" || text == "transparent")
        {
            return null;
        }

        if (text == "currentColor")
        {
            return CurrentColor;
        }

        if (text.StartsWith("url(", StringComparison.Ordinal))
        {
            unsupported.Add("gradient or pattern");
            return parent;
        }

        if (text[0] == '#' && uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint hex))
        {
            return text.Length switch
            {
                4 => 0xFF000000u | ((hex >> 8 & 0xF) * 0x110000u) | ((hex >> 4 & 0xF) * 0x1100u) | ((hex & 0xF) * 0x11u),
                7 => 0xFF000000u | hex,
                9 => (hex >> 8) | ((hex & 0xFF) << 24),
                _ => parent,
            };
        }

        if (text.StartsWith("rgb(", StringComparison.Ordinal) && Numbers(text[4..].TrimEnd(')')) is { Count: 3 } rgb)
        {
            return 0xFF000000u | ((uint)Math.Clamp(rgb[0], 0, 255) << 16) | ((uint)Math.Clamp(rgb[1], 0, 255) << 8) | (uint)Math.Clamp(rgb[2], 0, 255);
        }

        uint? named = text.ToLowerInvariant() switch
        {
            "black" => 0xFF000000u,
            "white" => 0xFFFFFFFFu,
            "red" => 0xFFFF0000u,
            "green" => 0xFF008000u,
            "blue" => 0xFF0000FFu,
            "yellow" => 0xFFFFFF00u,
            "orange" => 0xFFFFA500u,
            "gray" or "grey" => 0xFF808080u,
            "purple" => 0xFF800080u,
            _ => null,
        };
        if (named is null)
        {
            unsupported.Add("colour '" + text + "'");
            return parent;
        }

        return named;
    }
}

/// <summary>How an image is shown: fitted to the view, or at a zoom.</summary>
public enum ImageZoomMode
{
    /// <summary>Fitted to the view (never larger than its own size).</summary>
    Fit,

    /// <summary>At <see cref="ImagePreview.Zoom"/>.</summary>
    Zoom,
}

/// <summary>
/// An image file's preview tab (X part 9, item 20): png, jpg, gif, bmp, ico and svg read off the UI thread (up to
/// <c>CRAFT_IMAGE_PREVIEW_MAX_MB</c>), its format, size in pixels and bytes for the status bar, fitted to the view or zoomed
/// (steps of a quarter, an eighth to 32 times), an SVG drawn from its shapes when it is simple (else said). Headless; the
/// view draws it.
/// </summary>
public sealed class ImagePreview
{
    private static readonly double[] Steps = [0.125, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32];

    private ImagePreview(string path)
    {
        Path = path;
    }

    /// <summary>The file.</summary>
    public string Path { get; }

    /// <summary>Its format ("PNG", "SVG"), or "" when not known.</summary>
    public string Format { get; private set; } = "";

    /// <summary>Its size in pixels (an SVG's in its units), 0 when not known.</summary>
    public int Width { get; private set; }

    /// <summary>Its height.</summary>
    public int Height { get; private set; }

    /// <summary>Its bytes on disk.</summary>
    public long Length { get; private set; }

    /// <summary>Its bytes (a raster image's, for the view's decoder); empty when it was not read.</summary>
    public ReadOnlyMemory<byte> Bytes { get; private set; }

    /// <summary>An SVG's drawing, or null.</summary>
    public SvgDrawing? Svg { get; private set; }

    /// <summary>Why it is not shown (too large, unreadable, not an image), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Fitted or zoomed.</summary>
    public ImageZoomMode Mode { get; private set; } = ImageZoomMode.Fit;

    /// <summary>The zoom when <see cref="Mode"/> is <see cref="ImageZoomMode.Zoom"/> (1: its own size).</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>Raised when the zoom changed.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Reads <paramref name="path"/> off the calling thread (at most <paramref name="maxBytes"/>).</summary>
    public static Task<ImagePreview> LoadAsync(string path, long maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Task.Run(() => Load(path, maxBytes), cancellationToken);
    }

    /// <summary>Reads <paramref name="path"/> (on the calling thread: the tests', and <see cref="LoadAsync"/>'s worker).</summary>
    public static ImagePreview Load(string path, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        var preview = new ImagePreview(System.IO.Path.GetFullPath(path));
        try
        {
            var info = new FileInfo(preview.Path);
            if (!info.Exists)
            {
                preview.Problem = "The file is not there any more.";
                return preview;
            }

            preview.Length = info.Length;
            if (info.Length > maxBytes)
            {
                preview.Problem = string.Create(CultureInfo.InvariantCulture, $"{Size(info.Length)} is larger than the preview reads ({Size(maxBytes)}, CRAFT_IMAGE_PREVIEW_MAX_MB).");
                return preview;
            }

            var bytes = File.ReadAllBytes(preview.Path);
            if (System.IO.Path.GetExtension(preview.Path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                preview.Format = "SVG";
                try
                {
                    var svg = SvgDrawing.Read(bytes);
                    preview.Svg = svg;
                    (preview.Width, preview.Height) = ((int)MathF.Round(svg.Width), (int)MathF.Round(svg.Height));
                    if (svg.Unsupported is { } what)
                    {
                        preview.Problem = $"This SVG uses what the preview does not draw ({what}): open it as text, or in a browser.";
                    }
                }
                catch (XmlException e)
                {
                    preview.Problem = $"The SVG does not parse: {e.Message}";
                }

                return preview;
            }

            if (!ImageFormats.TryRead(bytes, out var format, out int width, out int height))
            {
                preview.Problem = "The file is not an image this preview knows (PNG, JPEG, GIF, BMP, ICO, SVG).";
                return preview;
            }

            (preview.Format, preview.Width, preview.Height, preview.Bytes) = (format, width, height, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            preview.Problem = $"The file could not be read: {e.Message}";
        }

        return preview;
    }

    /// <summary>The scale it is drawn at in a view of <paramref name="viewWidth"/> by <paramref name="viewHeight"/>: fitted (never above 1) or the zoom.</summary>
    public double ScaleFor(double viewWidth, double viewHeight)
    {
        if (Mode == ImageZoomMode.Zoom)
        {
            return Zoom;
        }

        if (Width <= 0 || Height <= 0 || viewWidth <= 0 || viewHeight <= 0)
        {
            return 1;
        }

        return Math.Min(1, Math.Min(viewWidth / Width, viewHeight / Height));
    }

    /// <summary>Zooms in a step (from the scale shown now, <paramref name="shown"/>).</summary>
    public void ZoomIn(double shown) => SetZoom(Steps.FirstOrDefault(s => s > shown + 1e-6, Steps[^1]));

    /// <summary>Zooms out a step.</summary>
    public void ZoomOut(double shown) => SetZoom(Steps.LastOrDefault(s => s < shown - 1e-6, Steps[0]));

    /// <summary>Its own size (100%).</summary>
    public void ActualSize() => SetZoom(1);

    /// <summary>Fitted to the view.</summary>
    public void FitToView()
    {
        Mode = ImageZoomMode.Fit;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What the status bar shows: "PNG · 640 × 480 · 12.3 KB · 100%" (the scale shown).</summary>
    public string StatusText(double shown)
    {
        var text = new StringBuilder();
        text.Append(Format.Length > 0 ? Format : "Image");
        if (Width > 0 && Height > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" · {Width} × {Height}");
        }

        text.Append(" · ").Append(Size(Length));
        text.Append(CultureInfo.InvariantCulture, $" · {Math.Round(shown * 100)}%");
        if (Mode == ImageZoomMode.Fit)
        {
            text.Append(" (fit)");
        }

        return text.ToString();
    }

    /// <summary>A byte count as people read it ("12.3 KB").</summary>
    public static string Size(long bytes) => bytes < 1024 ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
        : bytes < 1024 * 1024 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB");

    private void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(zoom, Steps[0], Steps[^1]);
        Mode = ImageZoomMode.Zoom;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }
}
