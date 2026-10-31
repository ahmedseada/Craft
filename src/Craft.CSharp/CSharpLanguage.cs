// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Craft.CSharp;

/// <summary>
/// C# on Roslyn's parser: "csharp" opens ".cs" files as regular code, "csharp-script" opens ".csx" files as scripts
/// (<see cref="Script"/>; <see cref="ILanguage.Open"/> is not told the file, so the script kind is a language of its
/// own). A session keeps the buffer's latest syntax tree, re-parsed incrementally on each change, and classifies the
/// lines asked for with <see cref="CSharpClassifier"/>.
/// </summary>
public sealed class CSharpLanguage : ILanguage
{
    /// <summary>The name of regular C#.</summary>
    public const string RegularName = "csharp";

    /// <summary>The name of C# script.</summary>
    public const string ScriptName = "csharp-script";

    private static readonly LanguageRules CSharpRules = new("//", "/*", "*/", [new('(', ')'), new('[', ']'), new('{', '}')]);

    /// <summary>Regular C# ("csharp", ".cs").</summary>
    public CSharpLanguage() : this(SourceCodeKind.Regular)
    {
    }

    /// <summary>C# of <paramref name="kind"/>: regular ("csharp", ".cs") or script ("csharp-script", ".csx").</summary>
    public CSharpLanguage(SourceCodeKind kind)
    {
        Kind = kind;
        ParseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, kind);
        Name = kind == SourceCodeKind.Script ? ScriptName : RegularName;
        Extensions = kind == SourceCodeKind.Script ? [".csx"] : [".cs"];
    }

    /// <summary>C# script ("csharp-script", ".csx").</summary>
    public static CSharpLanguage Script { get; } = new(SourceCodeKind.Script);

    /// <summary>Whether it parses regular code or scripts.</summary>
    public SourceCodeKind Kind { get; }

    /// <summary>The parse options: the latest language version, documentation comments parsed.</summary>
    public CSharpParseOptions ParseOptions { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary => Kind == SourceCodeKind.Script ? "C# script, on Roslyn's parser" : "C#, on Roslyn's parser";

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; }

    /// <inheritdoc />
    public LanguageRules Rules => CSharpRules;

    /// <summary>Registers regular C# and C# script in <see cref="EditorLanguages"/>.</summary>
    public static void Register()
    {
        EditorLanguages.Register(new CSharpLanguage());
        EditorLanguages.Register(Script);
    }

    /// <summary>Removes both from <see cref="EditorLanguages"/>.</summary>
    public static void Unregister()
    {
        EditorLanguages.Unregister(RegularName);
        EditorLanguages.Unregister(ScriptName);
    }

    /// <inheritdoc />
    ILanguageSession ILanguage.Open(ITextBuffer buffer) => Open(buffer);

    /// <summary>A session over <paramref name="buffer"/>, its text parsed now.</summary>
    public CSharpLanguageSession Open(ITextBuffer buffer) => new(buffer, ParseOptions);
}

/// <summary>
/// C# on one buffer: the latest <see cref="SyntaxTree"/>, re-parsed incrementally on each <see cref="ITextBuffer.Changed"/>
/// (on the thread that changed the buffer, the UI thread; <see cref="LastUpdate"/> says what it cost), and the syntactic
/// classification of a range.
/// </summary>
public sealed class CSharpLanguageSession : ILanguageSession
{
    private readonly ITextBuffer _buffer;
    private readonly CSharpParseOptions _options;
    private SnapshotSourceText _text;
    private SyntaxTree _tree;
    private SyntaxNode _root;
    private bool _disposed;

    internal CSharpLanguageSession(ITextBuffer buffer, CSharpParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _options = options;
        long start = Stopwatch.GetTimestamp();
        _text = new SnapshotSourceText(buffer.Current);
        _tree = CSharpSyntaxTree.ParseText(_text, options);
        _root = _tree.GetRoot();
        LastUpdate = Stopwatch.GetElapsedTime(start);
        buffer.Changed += OnChanged;
    }

    /// <summary>The latest tree, of <see cref="Snapshot"/>.</summary>
    public SyntaxTree Tree => _tree;

    /// <summary>The snapshot <see cref="Tree"/> is the text of.</summary>
    public ITextSnapshot Snapshot => _text.Snapshot;

    /// <summary>What the last parse cost (the first full parse, then each incremental one).</summary>
    public TimeSpan LastUpdate { get; private set; }

    /// <summary>The parses since the session opened, the first included.</summary>
    public int Updates { get; private set; } = 1;

    /// <inheritdoc />
    public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (start < 0 || length < 0 || start > snapshot.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, $"The range of {length} characters at {start} runs outside the text of {snapshot.Length}.");
        }

        var root = ReferenceEquals(snapshot, _text.Snapshot) || (ReferenceEquals(snapshot.Buffer, _buffer) && snapshot.Version == _text.Snapshot.Version)
            ? _root
            : CSharpSyntaxTree.ParseText(new SnapshotSourceText(snapshot), _options).GetRoot();
        CSharpClassifier.Classify(root, start, length, into);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _buffer.Changed -= OnChanged;
        }
    }

    private void OnChanged(object? sender, TextChangedEventArgs change)
    {
        long start = Stopwatch.GetTimestamp();
        if (ReferenceEquals(change.Before, _text.Snapshot))
        {
            _text = SnapshotSourceText.FromChange(change);
            _tree = _tree.WithChangedText(_text);
        }
        else
        {
            // A change this session did not see the start of: parse the text afresh.
            _text = new SnapshotSourceText(change.After);
            _tree = CSharpSyntaxTree.ParseText(_text, _options);
        }

        _root = _tree.GetRoot();
        LastUpdate = Stopwatch.GetElapsedTime(start);
        Updates++;
    }
}
