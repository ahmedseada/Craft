// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Craft.CSharp;

/// <summary>
/// The session's semantic layer (<see cref="ISemanticLanguageSession"/>). The document lives in its workspace's Roslyn
/// solution (<see cref="CSharpWorkspace"/>, shared by the folder's open files) with the buffer's own text: each request
/// first brings it to the snapshot asked for, in one incremental step from the version the solution holds (the changes
/// between them composed into one range, as the syntactic worker does), never copying the text. Diagnostics run on the
/// thread pool after a pause in the changes (a timer reset by each change, so a keystroke allocates nothing for it), the
/// running pass cancelled by the next change; a document above the settings' size waits for
/// <see cref="EnableSemantics"/>. Every request runs on the thread pool, so a call from the UI thread returns at once.
/// </summary>
public sealed partial class CSharpLanguageSession
{
    private readonly LanguageOpenContext _context;
    private readonly bool _automatic;
    private readonly SemaphoreSlim _documentGate = new(1, 1);
    private CSharpWorkspace? _workspace;
    private int _workspaceGeneration = -1;
    private int _workspaceVersion = -1;
    private TextChangeRange? _workspacePending;
    private bool _workspaceUnknown;
    private DiagnosticSet _diagnostics = DiagnosticSet.Empty;
    private ITimer? _diagnosticsTimer;
    private CancellationTokenSource? _diagnosticsRun;
    private bool _diagnosticsArmed;
    private bool _diagnosticsRunning;
    private bool _diagnosticsAgain;
    private TaskCompletionSource _diagnosticsIdle = Completed();
    private bool _semanticsEnabled;
    private long _lastDiagnosticsTicks;
    private int _diagnosticsPasses;
    private Exception? _semanticError;

    /// <inheritdoc />
    public event EventHandler? DiagnosticsChanged;

    /// <inheritdoc />
    public DiagnosticSet Diagnostics => Volatile.Read(ref _diagnostics);

    /// <summary>The file and workspace the session was opened with.</summary>
    public LanguageOpenContext Context => _context;

    /// <inheritdoc />
    public bool IsSemanticDeferred
    {
        get
        {
            int max = _context.Settings.SemanticMaxCharacters;
            return !Volatile.Read(ref _semanticsEnabled) && max > 0 && Volatile.Read(ref _latest).Length > max;
        }
    }

    /// <inheritdoc />
    public SemanticTelemetry Telemetry => Volatile.Read(ref _workspace)?.Telemetry(TimeSpan.FromTicks(Volatile.Read(ref _lastDiagnosticsTicks)))
        ?? SemanticTelemetry.None with { LastDiagnosticsTime = TimeSpan.FromTicks(Volatile.Read(ref _lastDiagnosticsTicks)) };

    /// <summary>The diagnostics passes that finished since the session opened.</summary>
    public int DiagnosticsPasses => Volatile.Read(ref _diagnosticsPasses);

    /// <summary>The error the last semantic work threw (a diagnostics pass, a reader), or null.</summary>
    public Exception? SemanticError => Volatile.Read(ref _semanticError);

    /// <summary>Completes when no diagnostics pass is due or running (the tests' and the benches' idle hook).</summary>
    public Task WhenDiagnosticsIdle()
    {
        lock (_gate)
        {
            return _diagnosticsIdle.Task;
        }
    }

    /// <inheritdoc />
    public void EnableSemantics()
    {
        lock (_gate)
        {
            Volatile.Write(ref _semanticsEnabled, true);
            ScheduleDiagnostics(immediately: true);
        }
    }

    /// <inheritdoc />
    public Task<CompletionList?> GetCompletionsAsync(ITextSnapshot snapshot, int position, CompletionTrigger trigger, CancellationToken cancellationToken)
    {
        Validate(snapshot, position);
        if (trigger.Kind != CompletionTriggerKind.Invoke && IsSemanticDeferred)
        {
            return Task.FromResult<CompletionList?>(null);
        }

        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpSemantics.CompletionsAsync(document, snapshot, position, trigger, cancellationToken).ConfigureAwait(false)
            : null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CompletionItem> ResolveCompletionAsync(CompletionList list, CompletionItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(item);
        return item.NeedsResolve ? Task.Run(() => CSharpSemantics.ResolveAsync(list, item, cancellationToken), cancellationToken) : Task.FromResult(item);
    }

    /// <inheritdoc />
    public Task<SignatureHelp?> GetSignatureHelpAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken)
    {
        Validate(snapshot, position);
        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpSemantics.SignatureHelpAsync(document, position, cancellationToken).ConfigureAwait(false)
            : null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<HoverInfo?> GetHoverAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken)
    {
        Validate(snapshot, position);
        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpSemantics.HoverAsync(document, position, cancellationToken).ConfigureAwait(false)
            : null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DefinitionLocation?> GetDefinitionAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken)
    {
        Validate(snapshot, position);
        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpSemantics.DefinitionAsync(document, position, cancellationToken).ConfigureAwait(false)
            : null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TextEdit>> FormatAsync(ITextSnapshot snapshot, int start, int length, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TextSpan? span = null;
        if (length >= 0)
        {
            if (start < 0 || start > snapshot.Length - length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), start, $"The range of {length} characters at {start} runs outside the text of {snapshot.Length}.");
            }

            span = new TextSpan(start, length);
        }

        string? newLine = snapshot.LineCount > 1 ? (snapshot.GetLine(0).BreakLength == 2 ? "\r\n" : snapshot[snapshot.GetLine(0).End] == '\r' ? "\r" : "\n") : null;
        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpSemantics.FormatAsync(document, span, _context.Settings, newLine, cancellationToken).ConfigureAwait(false)
            : (IReadOnlyList<TextEdit>)[], cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The workspace's document for <paramref name="snapshot"/>, forked with the edits (<c>Document.WithText</c>: a new
    /// solution beside the workspace's, which is not changed) and diagnosed as the live pass is (plan phase A1, the diff
    /// view's check). Null for a script, which has no semantic layer here yet.
    /// </remarks>
    public Task<DiagnosticSet?> DiagnoseEditsAsync(ITextSnapshot snapshot, IReadOnlyList<TextEdit> edits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(edits);
        var changes = new TextChange[edits.Count];
        for (int i = 0; i < changes.Length; i++)
        {
            changes[i] = new TextChange(new TextSpan(edits[i].Start, edits[i].Length), edits[i].Text);
        }

        return Task.Run(async () =>
        {
            if (await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is not { } document)
            {
                return null;
            }

            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var items = await CSharpSemantics.DiagnosticsAsync(document.WithText(text.WithChanges(changes)), cancellationToken).ConfigureAwait(false);
            return (DiagnosticSet?)new DiagnosticSet(null, items);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void Release()
    {
        lock (_gate)
        {
            _workspaceGeneration = -1;
            _workspaceVersion = -1;
            _workspacePending = null;
        }

        Volatile.Read(ref _workspace)?.RequestRelease();
    }

    /// <summary>The workspace's solution with this document at its current text (the bench builds it with this).</summary>
    internal async Task<Document?> CurrentDocumentAsync(CancellationToken cancellationToken) =>
        await Task.Run(() => DocumentForAsync(Volatile.Read(ref _latest), cancellationToken), cancellationToken).ConfigureAwait(false);

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    private void Validate(ITextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(snapshot.Buffer, _buffer))
        {
            throw new ArgumentException("The snapshot is not of this session's buffer.", nameof(snapshot));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, snapshot.Length);
    }

    /// <summary>The workspace folder: the context's, else the folder of the file's nearest project, else the file's folder.</summary>
    private string? RootFor()
    {
        if (_context.WorkspaceRoot is { } root)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }

        if (_context.FilePath is not { } file)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        return Workspaces.CsprojWorkspaceReader.FindProject(folder, null) is { } project ? Path.GetDirectoryName(project) : folder;
    }

    /// <summary>Called holding the gate, on each buffer change: the change joins the one the workspace has not seen, and diagnostics are due again.</summary>
    private void NoteSemanticChange(TextChangedEventArgs change, bool continuous)
    {
        NoteColorsChange(change, continuous);
        if (_workspaceVersion >= 0)
        {
            if (continuous)
            {
                _workspacePending = SnapshotSourceText.Compose(_workspacePending, SnapshotSourceText.Collapse(change.Edits));
            }
            else
            {
                _workspaceUnknown = true;
            }
        }

        if (_automatic && !IsSemanticDeferred)
        {
            ScheduleDiagnostics(immediately: false);
        }
    }

    /// <summary>Called holding the gate: cancels the running pass and (re)starts the pause before the next.</summary>
    private void ScheduleDiagnostics(bool immediately)
    {
        if (_disposed)
        {
            return;
        }

        _diagnosticsRun?.Cancel();
        _diagnosticsArmed = true;
        if (_diagnosticsIdle.Task.IsCompleted)
        {
            _diagnosticsIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _diagnosticsTimer ??= TimeProvider.System.CreateTimer(static state => ((CSharpLanguageSession)state!).OnDiagnosticsDue(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        int delay = immediately ? 0 : Math.Max(0, _context.Settings.DiagnosticsDelayMilliseconds);
        _diagnosticsTimer.Change(TimeSpan.FromMilliseconds(delay), Timeout.InfiniteTimeSpan);
    }

    private void OnDiagnosticsDue()
    {
        CancellationTokenSource run;
        ITextSnapshot snapshot;
        lock (_gate)
        {
            if (_disposed || !_diagnosticsArmed)
            {
                return;
            }

            if (_diagnosticsRunning)
            {
                // The running pass was cancelled by the change that armed this one: it starts this one when it ends.
                _diagnosticsAgain = true;
                return;
            }

            _diagnosticsArmed = false;
            _diagnosticsRunning = true;
            run = new CancellationTokenSource();
            _diagnosticsRun = run;
            snapshot = _latest;
        }

        _ = RunDiagnosticsAsync(snapshot, run);
    }

    private async Task RunDiagnosticsAsync(ITextSnapshot snapshot, CancellationTokenSource run)
    {
        bool changed = false;
        try
        {
            long start = Stopwatch.GetTimestamp();
            var document = await DocumentForAsync(snapshot, run.Token).ConfigureAwait(false);
            if (document is not null)
            {
                var items = await CSharpSemantics.DiagnosticsAsync(document, run.Token).ConfigureAwait(false);
                Volatile.Write(ref _diagnostics, new DiagnosticSet(snapshot, items));
                Volatile.Write(ref _lastDiagnosticsTicks, Stopwatch.GetElapsedTime(start).Ticks);
                Volatile.Write(ref _semanticError, null);
                Interlocked.Increment(ref _diagnosticsPasses);
                changed = true;
            }
        }
        catch (OperationCanceledException)
        {
        }
#pragma warning disable CA1031 // A failed pass is kept in SemanticError; the editor goes on, the next change tries again.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Volatile.Write(ref _semanticError, error);
        }

        if (changed && !_disposed)
        {
            try
            {
                DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
            }
#pragma warning disable CA1031 // A subscriber's failure is kept, never thrown into the worker.
            catch (Exception error)
#pragma warning restore CA1031
            {
                Volatile.Write(ref _semanticError, error);
            }
        }

        bool again;
        lock (_gate)
        {
            _diagnosticsRunning = false;
            if (ReferenceEquals(_diagnosticsRun, run))
            {
                _diagnosticsRun = null;
            }

            run.Dispose();
            again = _diagnosticsAgain && _diagnosticsArmed && !_disposed;
            _diagnosticsAgain = false;
            if (!again && (!_diagnosticsArmed || _disposed))
            {
                _diagnosticsIdle.TrySetResult();
            }
        }

        if (again)
        {
            OnDiagnosticsDue();
        }
    }

    /// <summary>
    /// The workspace's document at <paramref name="snapshot"/>: for the newest snapshot, the solution is brought to it
    /// (in one incremental step from the version it holds) and kept; for an older one, a solution made for this answer only.
    /// </summary>
    private async Task<Document?> DocumentForAsync(ITextSnapshot snapshot, CancellationToken cancellationToken)
    {
        await _documentGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_options.Kind != SourceCodeKind.Regular)
            {
                // Scripts (#r, top-level code across submissions) have no workspace model here yet: syntactic only.
                return null;
            }

            var workspace = Volatile.Read(ref _workspace);
            if (workspace is null)
            {
                workspace = CSharpWorkspace.Enter(RootFor(), _context.Settings);
                Volatile.Write(ref _workspace, workspace);
            }

            for (int attempt = 0; ; attempt++)
            {
                SourceText? text;
                bool keep;
                int generation = workspace.Generation;
                lock (_gate)
                {
                    keep = snapshot.Version == _latest.Version;
                    bool inSync = workspace.Solution is not null && _workspaceGeneration == generation && _workspaceVersion >= 0 && !_workspaceUnknown;
                    if (keep && inSync && snapshot.Version == _workspaceVersion)
                    {
                        text = null;
                    }
                    else if (keep && inSync && _workspacePending is { } pending)
                    {
                        text = SnapshotSourceText.Since(snapshot, _workspaceVersion, pending);
                    }
                    else
                    {
                        text = new SnapshotSourceText(snapshot);
                    }

                    if (keep)
                    {
                        // From here the solution holds this version; a change from now on composes against it.
                        _workspaceVersion = snapshot.Version;
                        _workspacePending = null;
                        _workspaceUnknown = false;
                    }
                }

                (Document Document, int Generation)? found;
                try
                {
                    found = await workspace.DocumentAsync(this, _context.FilePath, text, keep, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    if (keep)
                    {
                        lock (_gate)
                        {
                            _workspaceUnknown = true;
                        }
                    }

                    throw;
                }

                if (found is not { } result)
                {
                    return null;
                }

                if (text is null && result.Generation != generation && attempt == 0)
                {
                    // Rebuilt meanwhile (a release): the new solution has the file's disk text; give it the buffer's.
                    lock (_gate)
                    {
                        _workspaceUnknown = true;
                    }

                    continue;
                }

                if (keep)
                {
                    lock (_gate)
                    {
                        _workspaceGeneration = result.Generation;
                    }
                }

                return result.Document;
            }
        }
        finally
        {
            _documentGate.Release();
        }
    }

    /// <summary>Called holding the gate from Dispose: no pass runs again, and the workspace is left off the calling thread.</summary>
    private void StopSemantics()
    {
        _diagnosticsRun?.Cancel();
        _diagnosticsArmed = false;
        _diagnosticsTimer?.Dispose();
        _diagnosticsTimer = null;
        StopColors();
        if (!_diagnosticsRunning)
        {
            _diagnosticsIdle.TrySetResult();
        }

        if (Interlocked.Exchange(ref _workspace, null) is { } workspace)
        {
            _ = Task.Run(() => workspace.Leave(this));
        }
    }
}
