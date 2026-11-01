// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The language sessions of the open documents, shared and counted (the X restart's split editor): every view of one
/// document in one language and settings takes the same session (<see cref="Acquire"/>), so a document shown in two
/// editor groups is parsed, classified and checked once; the session is disposed when its last view lets it go. Each
/// view keeps its own carets, scroll and folds (its own <see cref="EditorModel"/> over the shared session). Used on the UI
/// thread.
/// </summary>
public sealed class LanguageSessionPool
{
    private readonly List<Entry> _entries = [];

    /// <summary>The sessions open now.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// The session of <paramref name="document"/> in <paramref name="language"/> with <paramref name="context"/>: the open
    /// one when a view holds it (its count goes up), else a new one opened over the document's buffer. Null for plain
    /// text (no language). Dispose the lease when the view lets it go.
    /// </summary>
    public LanguageSessionLease? Acquire(ITextDocument document, ILanguage? language, LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        if (language is null)
        {
            return null;
        }

        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Document, document) && ReferenceEquals(entry.Language, language) && Equals(entry.Context, context))
            {
                entry.Uses++;
                return new LanguageSessionLease(this, entry);
            }
        }

        var opened = new Entry(document, language, context, language.Open(document.Buffer, context)) { Uses = 1 };
        _entries.Add(opened);
        return new LanguageSessionLease(this, opened);
    }

    /// <summary>
    /// Another hold on the session a view of <paramref name="document"/> has open (the first, whatever its settings: a
    /// peek of an open document shares it), or null when none is open.
    /// </summary>
    public LanguageSessionLease? AcquireOpen(ITextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Document, document))
            {
                entry.Uses++;
                return new LanguageSessionLease(this, entry);
            }
        }

        return null;
    }

    /// <summary>How many views hold a session of <paramref name="document"/> (0 when none is open).</summary>
    public int UsesOf(ITextDocument document)
    {
        int uses = 0;
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Document, document))
            {
                uses += entry.Uses;
            }
        }

        return uses;
    }

    /// <summary>The sessions open over <paramref name="document"/>'s buffer.</summary>
    public int SessionsOf(ITextDocument document)
    {
        int sessions = 0;
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Document, document))
            {
                sessions++;
            }
        }

        return sessions;
    }

    internal void Release(Entry entry)
    {
        if (--entry.Uses > 0)
        {
            return;
        }

        _entries.Remove(entry);
        entry.Session.Dispose();
    }

    internal sealed class Entry(ITextDocument document, ILanguage language, LanguageOpenContext context, ILanguageSession session)
    {
        public ITextDocument Document { get; } = document;

        public ILanguage Language { get; } = language;

        public LanguageOpenContext Context { get; } = context;

        public ILanguageSession Session { get; } = session;

        public int Uses { get; set; }
    }
}

/// <summary>A view's hold on a shared language session (<see cref="LanguageSessionPool.Acquire"/>); disposing it lets the session go once.</summary>
public sealed class LanguageSessionLease : IDisposable
{
    private readonly LanguageSessionPool _pool;
    private readonly LanguageSessionPool.Entry _entry;
    private bool _released;

    internal LanguageSessionLease(LanguageSessionPool pool, LanguageSessionPool.Entry entry)
    {
        _pool = pool;
        _entry = entry;
    }

    /// <summary>The session.</summary>
    public ILanguageSession Session => _entry.Session;

    /// <summary>The views holding it now (this one included while it is held).</summary>
    public int Uses => _entry.Uses;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _pool.Release(_entry);
    }
}

/// <summary>
/// A language session with its syntactic layer only (the X restart's peek of a closed file): the colours of the text and
/// its scopes, never the semantic layer (no diagnostics, completion or workspace), whatever the session underneath can
/// do. It owns the session it wraps.
/// </summary>
public sealed class SyntacticLanguageSession : ILanguageSession
{
    private readonly ILanguageSession _inner;

    /// <summary>Wraps <paramref name="inner"/> (disposed with this).</summary>
    public SyntacticLanguageSession(ILanguageSession inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>
    /// Opens <paramref name="language"/> over <paramref name="buffer"/> for colours only: no workspace (none is given), the
    /// semantic layer deferred for any text and semantic colours off, then wrapped so nothing semantic is reachable.
    /// </summary>
    public static SyntacticLanguageSession Open(ILanguage language, ITextBuffer buffer, string? filePath, LanguageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(settings);
        var colours = settings with { SemanticMaxCharacters = 1, SemanticColorsDelayMilliseconds = -1, SourceGenerators = false };
        return new SyntacticLanguageSession(language.Open(buffer, new LanguageOpenContext(filePath, null, colours)));
    }

    /// <summary>The session underneath (the tests check what it holds).</summary>
    public ILanguageSession Inner => _inner;

    /// <inheritdoc />
    public event EventHandler? Reclassified
    {
        add => _inner.Reclassified += value;
        remove => _inner.Reclassified -= value;
    }

    /// <inheritdoc />
    public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into) => _inner.Classify(snapshot, start, length, into);

    /// <inheritdoc />
    public bool WaitUntilClassified(ITextSnapshot snapshot, TimeSpan timeout) => _inner.WaitUntilClassified(snapshot, timeout);

    /// <inheritdoc />
    public IReadOnlyList<ScopeName> ScopeAt(ITextSnapshot snapshot, int position) => _inner.ScopeAt(snapshot, position);

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
