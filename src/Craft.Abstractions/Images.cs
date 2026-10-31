// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>Where <see cref="IEditorHost.ReadImageAsync"/> takes a picture from.</summary>
public enum EditorImageSource
{
    /// <summary>The clipboard (a screenshot just taken, an image copied).</summary>
    Clipboard,

    /// <summary>A file the user picks.</summary>
    File,
}

/// <summary>
/// A picture a plug-in received from the user (pasted, dropped, picked): its encoded bytes as they came (PNG, JPEG, ...;
/// never decoded here), its media type and a name to show. A chat panel sends it to a model as an "image" message part.
/// </summary>
/// <param name="Name">What to show ("screenshot.png", "Clipboard image").</param>
/// <param name="MediaType">Its media type ("image/png"), or null when not known.</param>
/// <param name="Data">Its encoded bytes.</param>
public sealed record EditorImage(string Name, string? MediaType, ReadOnlyMemory<byte> Data)
{
    /// <summary>The file extensions taken as pictures (lower case, with the dot).</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"];

    /// <summary>Whether <paramref name="path"/> names a picture by its extension.</summary>
    public static bool IsImagePath(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path).ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>The media type of a picture's extension ("image/png"), or null.</summary>
    public static string? MediaTypeOf(string path) => Path.GetExtension(path ?? "").ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".tif" or ".tiff" => "image/tiff",
        _ => null,
    };

    /// <summary>Reads the picture at <paramref name="path"/> (off the caller's thread when awaited from the UI).</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static async Task<EditorImage> FromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return new EditorImage(Path.GetFileName(path), MediaTypeOf(path), bytes);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({MediaType ?? "image"}, {Data.Length:N0} bytes)";
}
