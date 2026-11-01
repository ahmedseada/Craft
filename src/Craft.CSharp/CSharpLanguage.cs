// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Craft.CSharp;

/// <summary>
/// C# on Roslyn: "csharp" opens ".cs" files as regular code, "csharp-script" opens ".csx" files as scripts
/// (<see cref="Script"/>; a language of its own, since <see cref="ILanguage.Open(ITextBuffer)"/> is not told the file).
/// A session parses the buffer off the UI thread (incrementally, from its last tree) and classifies the lines asked for
/// with <see cref="CSharpClassifier"/> from the last tree that landed; opened with a <see cref="LanguageOpenContext"/>, it
/// is also the document of its workspace's Roslyn solution: diagnostics, completion, signature help, hover, go to
/// definition and formatting (<see cref="ISemanticLanguageSession"/>).
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

    /// <summary>Registers regular C# and C# script in <see cref="EditorLanguages"/>, and the "csproj" and "loose" workspace readers.</summary>
    public static void Register()
    {
        EditorLanguages.Register(new CSharpLanguage());
        EditorLanguages.Register(Script);
        FoldingRangeProviders.Register(new CSharpFolding());
        SnippetProviders.Register(new CSharpSnippets());
        CodeLensProviders.Register(new CSharpCodeLens());
        WorkspaceReaders.Register(new Workspaces.CsprojWorkspaceReader());
        WorkspaceReaders.Register(new Workspaces.LooseWorkspaceReader());
    }

    /// <summary>Removes both languages and both readers.</summary>
    public static void Unregister()
    {
        EditorLanguages.Unregister(RegularName);
        EditorLanguages.Unregister(ScriptName);
        FoldingRangeProviders.Unregister(CSharpFolding.ProviderName);
        SnippetProviders.Unregister(CSharpSnippets.ProviderName);
        CodeLensProviders.Unregister(CSharpCodeLens.ProviderName);
        WorkspaceReaders.Unregister(Workspaces.CsprojWorkspaceReader.ReaderName);
        WorkspaceReaders.Unregister(Workspaces.LooseWorkspaceReader.ReaderName);
    }

    /// <summary>Releases every C# workspace and the shared references (the host's memory-pressure call); each is built again on its next request.</summary>
    public static void ReleaseWorkspaces() => CSharpWorkspace.ReleaseAll();

    /// <summary>
    /// The host's memory-pressure call (<see cref="ILanguage.ReleaseMemory"/>): every C# workspace and the shared references
    /// are dropped off the calling thread; the sessions stay and build what they need again.
    /// </summary>
    public void ReleaseMemory() => CSharpWorkspace.RequestReleaseAll();

    /// <inheritdoc />
    /// <remarks>
    /// The folder's workspace (shared with any session open on it, else built for this search and released after it: a
    /// cold search pays for reading the projects), Roslyn's declaration search over it. Empty for C# script.
    /// </remarks>
    public Task<IReadOnlyList<SymbolLocation>> FindSymbolsAsync(string workspaceRoot, string query, int maxResults, LanguageSettings settings, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);
        ArgumentNullException.ThrowIfNull(settings);
        if (Kind == SourceCodeKind.Script)
        {
            return Task.FromResult<IReadOnlyList<SymbolLocation>>([]);
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        return Task.Run(async () =>
        {
            var owner = new object();
            var workspace = CSharpWorkspace.Enter(root, settings);
            try
            {
                var solution = await workspace.BuildAsync(null, cancellationToken).ConfigureAwait(false);
                return await CSharpSemantics.FindSymbolsAsync(solution, query.Trim(), maxResults, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await workspace.LeaveAsync(owner).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    ILanguageSession ILanguage.Open(ITextBuffer buffer) => Open(buffer);

    /// <inheritdoc />
    ILanguageSession ILanguage.Open(ITextBuffer buffer, LanguageOpenContext context) => Open(buffer, context);

    /// <summary>
    /// A session over <paramref name="buffer"/>; its first full parse starts at once, off the calling thread. It knows no
    /// file or workspace: its semantic layer answers requests (a loose document on the running framework) and computes
    /// diagnostics only when asked (<see cref="CSharpLanguageSession.EnableSemantics"/>).
    /// </summary>
    public CSharpLanguageSession Open(ITextBuffer buffer) => new(buffer, ParseOptions, null);

    /// <summary>
    /// A session over <paramref name="buffer"/>, the document of <paramref name="context"/>'s file in its workspace: its
    /// first full parse starts at once, and its diagnostics follow the changes (off the calling thread).
    /// </summary>
    public CSharpLanguageSession Open(ITextBuffer buffer, LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new(buffer, ParseOptions, context);
    }
}

/// <summary>
/// C# on one buffer, parsed off the UI thread. The session keeps the last parse that landed (its tree and the snapshot
/// it is the text of, swapped as one immutable value) and one worker on the thread pool: <see cref="ITextBuffer.Changed"/>
/// (on the UI thread) records the change, composed with the others not parsed yet into one covering range, and wakes the
/// worker, which parses the newest snapshot from its last tree in one incremental step however many versions arrived
/// meanwhile, again while newer changes are pending, then sleeps (no thread is held while idle). Each landed parse raises
/// <see cref="Reclassified"/> on the worker's thread. <see cref="Classify"/> never waits: for a snapshot newer than the
/// last parse it classifies the parse's tree through the pending change (spans before and after it kept, shifted; the
/// changed text left plain). The editor waits up to its budget with <see cref="WaitUntilClassified"/>. A parse that
/// throws is kept in <see cref="ParseError"/> (the last tree stays in use); the next change parses the file afresh.
/// </summary>
public sealed partial class CSharpLanguageSession : ISemanticLanguageSession
{
    private readonly ITextBuffer _buffer;
    private readonly CSharpParseOptions _options;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _resume = new(true);
    private readonly CancellationTokenSource _stop = new();
    private Parsed? _parsed;
    private ITextSnapshot _latest;
    private TextChangeRange? _inflight;
    private bool _inflightUnknown;
    private TextChangeRange? _pending;
    private bool _pendingUnknown;
    private bool _full = true;
    private bool _working;
    private bool _disposed;
    private bool _released;
    // Made only when someone waits (WhenIdle) while the worker parses, never on the keystroke's path (X part 9's speed fix:
    // before, each keystroke that started the worker made a new source and its task).
    private TaskCompletionSource? _idle;
    private Exception? _error;
    private TokenSpanBuffer? _scratch;

    internal CSharpLanguageSession(ITextBuffer buffer, CSharpParseOptions options, LanguageOpenContext? context)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _options = options;
        _latest = buffer.Current;
        _context = context ?? LanguageOpenContext.None;
        _automatic = context is not null && options.Kind == SourceCodeKind.Regular;
        buffer.Changed += OnChanged;
        lock (_gate)
        {
            Kick();
            if (_automatic && !IsSemanticDeferred)
            {
                ScheduleDiagnostics(immediately: true);
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Reclassified;

    /// <summary>The last tree that landed (of <see cref="Snapshot"/>), or null before the first parse lands.</summary>
    public SyntaxTree? Tree => Volatile.Read(ref _parsed)?.Tree;

    /// <summary>The snapshot <see cref="Tree"/> is the text of, or null before the first parse lands.</summary>
    public ITextSnapshot? Snapshot => Volatile.Read(ref _parsed)?.Text.Snapshot;

    /// <summary>The last landed tree with the snapshot it is the text of, read together (null before the first parse lands).</summary>
    internal (SyntaxTree Tree, ITextSnapshot Snapshot)? LastParse => Volatile.Read(ref _parsed) is { } parsed ? (parsed.Tree, parsed.Text.Snapshot) : null;

    /// <summary>What the last landed parse cost on the worker (the first full parse, then each incremental one).</summary>
    public TimeSpan LastUpdate => Volatile.Read(ref _parsed)?.Elapsed ?? TimeSpan.Zero;

    /// <summary>The parses landed since the session opened, the first included.</summary>
    public int Updates => Volatile.Read(ref _parsed)?.Number ?? 0;

    /// <summary>The error the last parse threw (or a <see cref="Reclassified"/> handler), or null when it succeeded.</summary>
    public Exception? ParseError => Volatile.Read(ref _error);

    /// <summary>Whether the worker is parsing (or about to).</summary>
    public bool IsParsing
    {
        get
        {
            lock (_gate)
            {
                return _working;
            }
        }
    }

    /// <summary>
    /// How many idle sources (the parse worker's, the diagnostics' and the colours') were made since the session opened: each
    /// is made only when someone waits on one (the tests' check that a keystroke makes none).
    /// </summary>
    internal int IdleSourcesMade => Volatile.Read(ref _idleSourcesMade);

    private int _idleSourcesMade;

    /// <summary>Completes when the worker has nothing left to parse (or the session is disposed and the worker stopped).</summary>
    public Task WhenIdle()
    {
        lock (_gate)
        {
            if (!_working)
            {
                return Task.CompletedTask;
            }

            if (_idle is null)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Interlocked.Increment(ref _idleSourcesMade);
            }

            return _idle.Task;
        }
    }

    /// <summary>Test hook: holds the worker before its next parse until <see cref="ResumeParsing"/>.</summary>
    internal void PauseParsing() => _resume.Reset();

    /// <summary>Test hook: lets a held worker go on.</summary>
    internal void ResumeParsing() => _resume.Set();

    /// <summary>Test hook: called on the worker before each parse (a test throws from it).</summary>
    internal Action? BeforeParse { get; set; }

    /// <inheritdoc />
    public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(into);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (start < 0 || length < 0 || start > snapshot.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, $"The range of {length} characters at {start} runs outside the text of {snapshot.Length}.");
        }

        if (!ClassifyWithColors(snapshot, start, length, into))
        {
            ClassifySyntactic(snapshot, start, length, into);
        }
    }

    /// <inheritdoc />
    void ILanguageSession.ClassifySyntactic(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(into);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (start < 0 || length < 0 || start > snapshot.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, $"The range of {length} characters at {start} runs outside the text of {snapshot.Length}.");
        }

        ClassifySyntactic(snapshot, start, length, into);       // the minimap's window: never Roslyn's semantic colours, never a pass asked for
    }

    /// <summary>The syntactic layer's spans of the range (the last parse, through the change still being parsed).</summary>
    private void ClassifySyntactic(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
    {
        var parsed = Volatile.Read(ref _parsed);
        if (parsed is not null && IsOf(snapshot, parsed))
        {
            CSharpClassifier.Classify(parsed.Root, start, length, into);
            return;
        }

        if (ReferenceEquals(snapshot.Buffer, _buffer))
        {
            TextChangeRange? change = null;
            bool newest;
            bool unknown = false;
            lock (_gate)
            {
                parsed = _parsed;
                newest = snapshot.Version == _latest.Version;
                if (newest)
                {
                    change = SnapshotSourceText.Compose(_inflight, _pending);
                    unknown = _inflightUnknown || _pendingUnknown;
                }
            }

            if (newest)
            {
                // Newer than the last parse: its tree, through the change still being parsed (nothing yet before the first).
                if (parsed is not null && !unknown)
                {
                    if (change is { } through)
                    {
                        ClassifyThrough(parsed.Root, through, start, length, into);
                    }
                    else
                    {
                        CSharpClassifier.Classify(parsed.Root, start, length, into);
                    }
                }

                return;
            }

            if (parsed is null || snapshot.Version > parsed.Text.Snapshot.Version)
            {
                // A version between the last parse and the newest: no change is kept for it; drawn plain.
                return;
            }
        }

        CSharpClassifier.Classify(CSharpSyntaxTree.ParseText(new SnapshotSourceText(snapshot), _options).GetRoot(), start, length, into);
    }

    /// <inheritdoc />
    public bool WaitUntilClassified(ITextSnapshot snapshot, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Volatile.Read(ref _parsed) is { } parsed && IsOf(snapshot, parsed))
        {
            return true;
        }

        if (!ReferenceEquals(snapshot.Buffer, _buffer))
        {
            return true;
        }

        bool forever = timeout == Timeout.InfiniteTimeSpan;
        if (!forever && timeout <= TimeSpan.Zero)
        {
            return false;
        }

        long deadline = forever ? long.MaxValue : Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        lock (_gate)
        {
            while (!_disposed && _working && (_parsed is null || _parsed.Text.Snapshot.Version < snapshot.Version))
            {
                if (forever)
                {
                    Monitor.Wait(_gate);
                    continue;
                }

                long left = deadline - Stopwatch.GetTimestamp();
                if (left <= 0)
                {
                    break;
                }

                Monitor.Wait(_gate, TimeSpan.FromSeconds((double)left / Stopwatch.Frequency));
            }

            return _parsed is { } landed && landed.Text.Snapshot.Version >= snapshot.Version;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopSemantics();
            _stop.Cancel();
            _resume.Set();
            Monitor.PulseAll(_gate);
            if (!_working)
            {
                ReleaseHandles();
            }
        }

        _buffer.Changed -= OnChanged;
    }

    private static bool IsOf(ITextSnapshot snapshot, Parsed parsed)
    {
        var of = parsed.Text.Snapshot;
        return ReferenceEquals(snapshot, of) || (ReferenceEquals(snapshot.Buffer, of.Buffer) && snapshot.Version == of.Version);
    }

    private void OnChanged(object? sender, TextChangedEventArgs change)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            bool continuous = change.Before.Version == _latest.Version && ReferenceEquals(change.Before.Buffer, _buffer);
            if (continuous)
            {
                _pending = SnapshotSourceText.Compose(_pending, SnapshotSourceText.Collapse(change.Edits));
            }
            else
            {
                // A change this session did not see the start of: what changed is unknown, so the file is parsed afresh.
                _pendingUnknown = true;
                _full = true;
            }

            _latest = change.After;
            Kick();
            NoteSemanticChange(change, continuous);
        }
    }

    /// <summary>Starts the worker when it is not running (called holding the gate).</summary>
    private void Kick()
    {
        if (_working || _disposed)
        {
            return;
        }

        _working = true;

        _ = Task.Run(Work);
    }

    private void Work()
    {
        while (true)
        {
            _resume.Wait();
            ITextSnapshot target;
            Parsed? from;
            TextChangeRange? range;
            bool full;
            lock (_gate)
            {
                if (_disposed || (!_full && _parsed is not null && _parsed.Text.Snapshot.Version == _latest.Version))
                {
                    Stop();
                    return;
                }

                target = _latest;
                from = _parsed;
                full = _full || from is null;
                range = _pending;
                _inflight = _pending;
                _inflightUnknown = _pendingUnknown;
                _pending = null;
                _pendingUnknown = false;
                _full = false;
            }

            Parsed? landed = null;
            Exception? error = null;
            long start = Stopwatch.GetTimestamp();
            try
            {
                BeforeParse?.Invoke();
                landed = Parse(target, full ? null : from, range, start);
            }
            catch (Exception e)
            {
                error = e;
            }

            lock (_gate)
            {
                _inflight = null;
                if (landed is not null)
                {
                    _inflightUnknown = false;
                    Volatile.Write(ref _parsed, landed);
                    Volatile.Write(ref _error, null);
                    Monitor.PulseAll(_gate);
                }
                else
                {
                    // The last tree stays; the change it missed goes back in front of the pending one, and the next change
                    // parses the file afresh (no retry loop on a parse that keeps throwing).
                    _pending = SnapshotSourceText.Compose(range, _pending);
                    _pendingUnknown |= _inflightUnknown;
                    _inflightUnknown = false;
                    _full = true;
                    if (!_disposed)
                    {
                        Volatile.Write(ref _error, error);
                    }

                    Stop();
                    return;
                }
            }

            try
            {
                if (!_disposed)
                {
                    Reclassified?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception e)
            {
                Volatile.Write(ref _error, e);
            }
        }
    }

    private Parsed Parse(ITextSnapshot target, Parsed? from, TextChangeRange? range, long start)
    {
        SnapshotSourceText text;
        SyntaxTree tree;
        if (from is not null && range is { } change)
        {
            text = SnapshotSourceText.Since(target, from.Text.Snapshot.Version, change);
            tree = from.Tree.WithChangedText(text);
        }
        else
        {
            text = new SnapshotSourceText(target);
            tree = CSharpSyntaxTree.ParseText(text, _options, cancellationToken: _stop.Token);
        }

        var root = tree.GetRoot(_stop.Token);
        return new Parsed(text, tree, root, Stopwatch.GetElapsedTime(start), (from?.Number ?? Volatile.Read(ref _parsed)?.Number ?? 0) + 1);
    }

    /// <summary>The worker ends (called holding the gate): idle, waiters woken, and the handles released once disposed.</summary>
    private void Stop()
    {
        _working = false;
        _idle?.TrySetResult();
        _idle = null;
        Monitor.PulseAll(_gate);
        if (_disposed)
        {
            ReleaseHandles();
        }
    }

    private void ReleaseHandles()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _stop.Dispose();
        _resume.Dispose();
        Interlocked.Exchange(ref _scratch, null)?.Dispose();
        Interlocked.Exchange(ref _colorScratch, null)?.Dispose();
        _idle?.TrySetResult();
        _idle = null;
    }

    /// <summary>
    /// Classifies <paramref name="root"/> (an older version's tree) for a range of the newest snapshot, through
    /// <paramref name="change"/> (older to newest, in the older positions): the part before the change as it is, the part
    /// after it shifted by the change's delta, and no span that overlaps or touches the changed text.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ClassifyThrough(SyntaxNode root, TextChangeRange change, int start, int length, TokenSpanBuffer into)
    {
        int at = change.Span.Start;
        int oldEnd = change.Span.End;
        int newEnd = at + change.NewLength;
        int delta = change.NewLength - change.Span.Length;
        int end = start + length;
        var scratch = Interlocked.Exchange(ref _scratch, null) ?? new TokenSpanBuffer(256);
        try
        {
            if (start < at)
            {
                scratch.Clear();
                CSharpClassifier.Classify(root, start, Math.Min(end, at) - start, scratch);
                foreach (var span in scratch.Spans)
                {
                    if (span.End < at)
                    {
                        into.Add(span.Start, span.Length, span.Class);
                    }
                }
            }

            if (end > newEnd || (start > newEnd && length == 0))
            {
                int from = Math.Max(start, newEnd);
                scratch.Clear();
                CSharpClassifier.Classify(root, from - delta, end - from, scratch);
                foreach (var span in scratch.Spans)
                {
                    if (span.Start > oldEnd)
                    {
                        into.Add(span.Start + delta, span.Length, span.Class);
                    }
                }
            }
        }
        finally
        {
            scratch.Clear();
            if (Interlocked.CompareExchange(ref _scratch, scratch, null) is not null)
            {
                scratch.Dispose();
            }
        }
    }

    /// <summary>A landed parse: the text it is of, its tree and root, what it cost and its number since the session opened.</summary>
    private sealed record Parsed(SnapshotSourceText Text, SyntaxTree Tree, SyntaxNode Root, TimeSpan Elapsed, int Number);
}
