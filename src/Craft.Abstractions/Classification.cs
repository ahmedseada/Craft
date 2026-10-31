// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;

namespace Craft.Abstractions;

/// <summary>A classified range of a snapshot: its start, its length and its class (one of <see cref="TokenClasses"/>, or a language's own).</summary>
/// <param name="Start">The position of its first character.</param>
/// <param name="Length">Its characters.</param>
/// <param name="Class">Its class name; a theme maps it to a style. A shared constant, never a string made per span.</param>
public readonly record struct TokenSpan(int Start, int Length, string Class)
{
    /// <summary>The position after its last character.</summary>
    public int End => Start + Length;
}

/// <summary>
/// The token classes, named as Roslyn's <c>ClassificationTypeNames</c> names them, so a semantic layer and a theme read
/// the same names. A language may add classes of its own; a theme that does not know one draws it as plain text.
/// </summary>
public static class TokenClasses
{
    /// <summary>Plain text.</summary>
    public const string Text = "text";

    /// <summary>A keyword.</summary>
    public const string Keyword = "keyword";

    /// <summary>A control-flow keyword (if, return, while).</summary>
    public const string ControlKeyword = "keyword - control";

    /// <summary>An identifier not classified further.</summary>
    public const string Identifier = "identifier";

    /// <summary>A string or character literal.</summary>
    public const string String = "string";

    /// <summary>A verbatim string.</summary>
    public const string VerbatimString = "string - verbatim";

    /// <summary>An escape sequence inside a string.</summary>
    public const string StringEscape = "string - escape character";

    /// <summary>A number.</summary>
    public const string Number = "number";

    /// <summary>A comment.</summary>
    public const string Comment = "comment";

    /// <summary>The text of a documentation comment.</summary>
    public const string DocComment = "xml doc comment - text";

    /// <summary>A documentation comment's delimiters and tags.</summary>
    public const string DocCommentTag = "xml doc comment - delimiter";

    /// <summary>An operator.</summary>
    public const string Operator = "operator";

    /// <summary>Punctuation (braces, commas, semicolons).</summary>
    public const string Punctuation = "punctuation";

    /// <summary>A preprocessor directive's keyword.</summary>
    public const string PreprocessorKeyword = "preprocessor keyword";

    /// <summary>A preprocessor directive's text.</summary>
    public const string PreprocessorText = "preprocessor text";

    /// <summary>Code excluded by a preprocessor condition.</summary>
    public const string ExcludedCode = "excluded code";

    /// <summary>A class's name.</summary>
    public const string ClassName = "class name";

    /// <summary>A record class's name.</summary>
    public const string RecordClassName = "record class name";

    /// <summary>A struct's name.</summary>
    public const string StructName = "struct name";

    /// <summary>A record struct's name.</summary>
    public const string RecordStructName = "record struct name";

    /// <summary>An interface's name.</summary>
    public const string InterfaceName = "interface name";

    /// <summary>An enum's name.</summary>
    public const string EnumName = "enum name";

    /// <summary>A delegate's name.</summary>
    public const string DelegateName = "delegate name";

    /// <summary>A type parameter's name.</summary>
    public const string TypeParameterName = "type parameter name";

    /// <summary>A method's name.</summary>
    public const string MethodName = "method name";

    /// <summary>A namespace's name.</summary>
    public const string NamespaceName = "namespace name";

    /// <summary>A property's name.</summary>
    public const string PropertyName = "property name";

    /// <summary>A field's name.</summary>
    public const string FieldName = "field name";

    /// <summary>A parameter's name.</summary>
    public const string ParameterName = "parameter name";

    /// <summary>A local's name.</summary>
    public const string LocalName = "local name";
}

/// <summary>
/// A growable list of <see cref="TokenSpan"/>s over a pooled array: a classifier writes into it, the renderer reads
/// <see cref="Spans"/>. Clear it to reuse it; dispose it to return the array. Not thread-safe.
/// </summary>
public sealed class TokenSpanBuffer : IDisposable
{
    private TokenSpan[] _items;
    private int _count;

    /// <summary>An empty buffer with room for <paramref name="capacity"/> spans before it grows.</summary>
    public TokenSpanBuffer(int capacity = 256) => _items = ArrayPool<TokenSpan>.Shared.Rent(Math.Max(16, capacity));

    /// <summary>The spans added since the last <see cref="Clear"/>, in the order they were added.</summary>
    public ReadOnlySpan<TokenSpan> Spans => _items.AsSpan(0, _count);

    /// <summary>How many spans it holds.</summary>
    public int Count => _count;

    /// <summary>Adds a span (a span of length 0 is dropped).</summary>
    public void Add(int start, int length, string tokenClass)
    {
        if (length <= 0)
        {
            return;
        }

        if (_count == _items.Length)
        {
            Grow();
        }

        _items[_count++] = new TokenSpan(start, length, tokenClass);
    }

    /// <summary>Empties it, keeping its array.</summary>
    public void Clear() => _count = 0;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_items.Length > 0)
        {
            ArrayPool<TokenSpan>.Shared.Return(_items, clearArray: true);
            _items = [];
            _count = 0;
        }
    }

    private void Grow()
    {
        var bigger = ArrayPool<TokenSpan>.Shared.Rent(_items.Length * 2);
        _items.AsSpan(0, _count).CopyTo(bigger);
        ArrayPool<TokenSpan>.Shared.Return(_items, clearArray: true);
        _items = bigger;
    }
}
