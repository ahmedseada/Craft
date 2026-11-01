// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using Craft.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using MauiFont = Microsoft.Maui.Graphics.Font;
using WindowsColor = Windows.UI.Color;

namespace Craft.Maui.WinUI;

/// <summary>
/// The drawn surfaces' laid-out text on Windows (the owner's third Windows run, B1): MAUI's canvas on Windows is Win2D, and
/// its <c>DrawString</c> makes a text format and a text layout on every call (the 53-68 ms frames). Here a line is laid out
/// once as a Win2D <see cref="CanvasTextLayout"/> (each run's colour, and its family, weight and slant when bold or
/// italic, set on its range) and drawn from it on the frame's drawing session (<see cref="PlatformCanvas.Session"/>) in
/// every frame after; <c>Craft.Editor.TextLayoutCache</c> decides what is kept. Win2D comes with MAUI on Windows
/// (Microsoft.Maui.Graphics depends on Microsoft.Graphics.Win2D): no package added. The text formats are made as MAUI
/// makes its own (the mapping of its internal <c>FontExtensions.ToCanvasTextFormat</c>), so a line draws in the font MAUI's
/// <c>DrawString</c> drew.
/// </summary>
public sealed class Win2DTextLayouts : IDrawnTextLayouts
{
    /// <inheritdoc />
    public IDrawnTextSurface Create(CodeFontFaces faces, float fontSize)
    {
        ArgumentNullException.ThrowIfNull(faces);
        return new Surface(faces, fontSize);
    }

    private sealed class Surface : IDrawnTextSurface
    {
        // Regular, bold, italic, bold italic: index (bold ? 1 : 0) + (italic ? 2 : 0).
        private readonly CanvasTextFormat[] _formats = new CanvasTextFormat[4];
        private CanvasDrawingSession? _session;
        private float _cell = 1;
        private float _line = 1;

        public Surface(CodeFontFaces faces, float size)
        {
            // As MauiEditorCanvas.SetFaces: a face that names a file is that file's weight and style already.
            bool files = faces.AdvanceEm is not null;
            _formats[0] = Format(new MauiFont(faces.Regular, FontWeights.Normal, FontStyleType.Normal), size);
            _formats[1] = Format(new MauiFont(faces.Bold, files ? FontWeights.Normal : FontWeights.Bold, FontStyleType.Normal), size);
            _formats[2] = Format(new MauiFont(faces.Italic, FontWeights.Normal, files ? FontStyleType.Normal : FontStyleType.Italic), size);
            _formats[3] = Format(new MauiFont(faces.BoldItalic, files ? FontWeights.Normal : FontWeights.Bold, files ? FontStyleType.Normal : FontStyleType.Italic), size);
        }

        public bool Begin(ICanvas target, FontMetrics metrics)
        {
            if (target is not PlatformCanvas { Session: { } session })
            {
                return false;
            }

            _session = session;
            _cell = Math.Max(1, metrics.CellWidth);
            _line = Math.Max(1, metrics.LineHeight);
            return true;
        }

        public void End() => _session = null;

        public IDisposable? Lay(StyledLine line, bool wholeLine)
        {
            ArgumentNullException.ThrowIfNull(line);
            var session = _session ?? throw new InvalidOperationException("Win2DTextLayouts: a line is laid out only inside a frame (Begin first).");
            string text = line.Text;
            var runs = line.Runs;
            // The box MAUI's DrawString used for a run: its cells and one more wide, a line high, the text centred in it.
            var first = runs[0].Style;
            var layout = new CanvasTextLayout(session, text, _formats[FaceOf(first)], (line.TextColumns + 1) * _cell, _line);
            try
            {
                for (int i = 0; i < runs.Length; i++)
                {
                    var style = runs[i].Style;
                    int start = line.TextIndexOf(i);
                    int count = runs[i].Text.Length;
                    layout.SetColor(start, count, ColorOf(style.Argb));
                    int face = FaceOf(style);
                    if (face != FaceOf(first))
                    {
                        var format = _formats[face];
                        layout.SetFontFamily(start, count, format.FontFamily);
                        layout.SetFontWeight(start, count, format.FontWeight);
                        layout.SetFontStyle(start, count, format.FontStyle);
                    }
                }

                // A whole line keeps each run at its column only when its glyphs are one cell each (a wide character or a
                // fallback font's glyph is not): else the line is drawn run by run, each run at its column, as before.
                if (wholeLine && runs.Length > 1 && Math.Abs(layout.LayoutBoundsIncludingTrailingWhitespace.Width - (line.TextColumns * _cell)) > _cell / 2)
                {
                    layout.Dispose();
                    return null;
                }

                return layout;
            }
            catch
            {
                layout.Dispose();
                throw;
            }
        }

        public void Draw(IDisposable layout, float x, float top)
        {
            var session = _session ?? throw new InvalidOperationException("Win2DTextLayouts: a layout is drawn only inside a frame (Begin first).");
            session.DrawTextLayout((CanvasTextLayout)layout, x, top, default(WindowsColor));
        }

        public void Dispose()
        {
            _session = null;
            foreach (var format in _formats)
            {
                format?.Dispose();
            }
        }

        private static int FaceOf(TextStyle style) => (style.Bold ? 1 : 0) + (style.Italic ? 2 : 0);

        private static WindowsColor ColorOf(uint argb) => Microsoft.UI.ColorHelper.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        private static CanvasTextFormat Format(MauiFont font, float size)
        {
            // MAUI's own mapping (Microsoft.Maui.Graphics.Platform.FontExtensions.ToCanvasTextFormat, internal; read from its
            // IL): the font's name, the size, the weight clamped to 1-999, the style.
            var format = new CanvasTextFormat
            {
                FontFamily = font.Name,
                FontSize = size,
                FontWeight = new global::Windows.UI.Text.FontWeight { Weight = (ushort)Math.Clamp(font.Weight, 1, 999) },
                FontStyle = font.StyleType switch
                {
                    FontStyleType.Italic => global::Windows.UI.Text.FontStyle.Italic,
                    FontStyleType.Oblique => global::Windows.UI.Text.FontStyle.Oblique,
                    _ => global::Windows.UI.Text.FontStyle.Normal,
                },
            };
            format.WordWrapping = CanvasWordWrapping.NoWrap;
            format.HorizontalAlignment = CanvasHorizontalAlignment.Left;
            format.VerticalAlignment = CanvasVerticalAlignment.Center;
            return format;
        }
    }
}
#endif
