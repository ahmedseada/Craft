// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using Craft.Abstractions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Craft.Maui.WinUI;

/// <summary>
/// Pictures from Windows' data packages (plan phase P8): the clipboard's (a screenshot taken with Win+Shift+S lands there
/// as a bitmap; a copied image file as a storage item) and a drop's (its <c>DragEventArgs.DataView</c>). The package is
/// read on the UI thread, as WinRT requires; the bytes are read asynchronously.
/// </summary>
internal static class WindowsImages
{
    /// <summary>The clipboard's picture, or null.</summary>
    public static Task<EditorImage?> FromClipboardAsync(CancellationToken cancellationToken)
    {
        DataPackageView view = Clipboard.GetContent();
        return FromViewAsync(view, "Clipboard image", cancellationToken);
    }

    /// <summary>A drop's picture, or null.</summary>
    public static Task<EditorImage?> FromDropAsync(Microsoft.Maui.Controls.DropEventArgs drop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drop);
        DataPackageView? view = drop.PlatformArgs?.DragEventArgs?.DataView;
        return view is null ? Task.FromResult<EditorImage?>(null) : FromViewAsync(view, "Dropped image", cancellationToken);
    }

    // A picture file among the package's storage items first (its name and type kept), else its bitmap.
    private static async Task<EditorImage?> FromViewAsync(DataPackageView view, string name, CancellationToken cancellationToken)
    {
        if (view.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await view.GetStorageItemsAsync();
            foreach (IStorageItem item in items)
            {
                if (item is StorageFile file && EditorImage.IsImagePath(file.Name))
                {
                    using Stream stream = await file.OpenStreamForReadAsync();
                    return new EditorImage(file.Name, EditorImage.MediaTypeOf(file.Name), await ReadAllAsync(stream, cancellationToken));
                }
            }
        }

        if (view.Contains(StandardDataFormats.Bitmap))
        {
            RandomAccessStreamReference reference = await view.GetBitmapAsync();
            using IRandomAccessStreamWithContentType source = await reference.OpenReadAsync();
            string? mediaType = string.IsNullOrWhiteSpace(source.ContentType) ? null : source.ContentType;
            using Stream stream = source.AsStreamForRead();
            return new EditorImage(name, mediaType, await ReadAllAsync(stream, cancellationToken));
        }

        return null;
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }
}
#endif
