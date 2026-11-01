// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// The parsed paths of file icons' layers (W: rich icons), each made once per size it is drawn at (docs/optimization.md
/// rule 75) by the platform's parser (MAUI Graphics' <c>PathBuilder</c> on Windows and Android; a test's fake here), so a
/// warm frame finds every path and allocates nothing (rule 63). A layer is found by its identity (an icon's layers live as
/// long as its registration), a size by its value. The UI thread's; not thread-safe. Bounded: past
/// <see cref="Limit"/> paths it starts again (an icon set replaced many times, sizes changed by zooming).
/// </summary>
/// <typeparam name="TPath">The platform's path.</typeparam>
public sealed class IconPathCache<TPath>
    where TPath : class
{
    private readonly Func<string, float, TPath> _build;
    private readonly Dictionary<Key, TPath> _paths = new(KeyComparer.Instance);

    /// <summary>A cache that makes a path with <paramref name="build"/>: the layer's path data, and the scale from its box to the size drawn.</summary>
    public IconPathCache(Func<string, float, TPath> build, int limit = 4096)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        _build = build;
        Limit = limit;
    }

    /// <summary>How many paths it keeps at most before it starts again.</summary>
    public int Limit { get; }

    /// <summary>How many paths were made (parsed) since it was created: a warm frame adds none.</summary>
    public int Parses { get; private set; }

    /// <summary>How many paths it holds.</summary>
    public int Count => _paths.Count;

    /// <summary>The path of <paramref name="layer"/> of <paramref name="shape"/> drawn in a square of <paramref name="size"/>, made the first time.</summary>
    public TPath Get(FileIconShape shape, FileIconLayer layer, float size)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(layer);
        var key = new Key(layer, size);
        if (_paths.TryGetValue(key, out var path))
        {
            return path;
        }

        if (_paths.Count >= Limit)
        {
            _paths.Clear();
        }

        path = _build(layer.Path, size / shape.ViewBox);
        Parses++;
        _paths.Add(key, path);
        return path;
    }

    /// <summary>Forgets every path (an icon theme was replaced).</summary>
    public void Clear() => _paths.Clear();

    private readonly record struct Key(FileIconLayer Layer, float Size);

    // By the layer's identity: hashing its path data per lookup would cost a walk of the string each frame.
    private sealed class KeyComparer : IEqualityComparer<Key>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals(Key x, Key y) => ReferenceEquals(x.Layer, y.Layer) && x.Size == y.Size;

        public int GetHashCode(Key key) => HashCode.Combine(RuntimeHelpers.GetHashCode(key.Layer), key.Size);
    }
}
