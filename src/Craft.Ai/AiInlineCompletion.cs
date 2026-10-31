// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>How the last completion prompt was laid out: by the model's fill-in-the-middle format, or the prefix alone.</summary>
/// <param name="Format">The format's name (Idrak's FillInMiddleFormats), or null for prefix-only completion.</param>
/// <param name="Model">The model's name.</param>
/// <param name="Reason">Why prefix-only (the model gives no tokenizer, or no registered format has its tokens), or null.</param>
public sealed record CompletionMode(string? Format, string Model, string? Reason)
{
    /// <summary>The status bar's line.</summary>
    public string Text => Format is null ? $"Completion: prefix-only ({Reason})" : $"Completion: fill-in-the-middle ({Format})";
}

/// <summary>
/// The AI plug-in's inline completion ("ai", plan 1 phase A2): the text around the caret laid out by the model's
/// fill-in-the-middle format, detected from its tokenizer (Idrak's <c>FillInMiddleFormats.Detect</c>; none: the prefix alone,
/// and the status bar says so), streamed from its <see cref="ITextModel"/> with the model's stop tokens. The prompt keeps
/// what a kept cache (<c>TextGenerator.KeepCache</c>) can reuse: the prefix starts at a block boundary that moves only
/// when the caret has moved a block on, so consecutive requests share their text up to the format's
/// <see cref="IFillInMiddlePrompt.PrefixEnd"/>; the suffix (<see cref="AiSettings.CompletionSuffixCharacters"/>) is what a
/// prefix-first format processes again each request. The prefix's budget comes from the model's context window. The
/// request stops when the editor cancels it (the generator checks the token at each step), at the token limit, or at
/// the line limit.
/// </summary>
public sealed class AiInlineCompletion : IInlineCompletionProvider
{
    /// <summary>The provider's name.</summary>
    public const string ProviderName = "ai";

    // Characters per token assumed before the tokenizer has counted (code runs 3-4); the tokenizer's count corrects it.
    private const int CharactersPerToken = 3;
    private readonly Func<AiSettings> _settings;
    private readonly AiModels _models;
    private readonly Func<bool> _active;
    private ModelLease? _detectedFor;
    private CompletionMode? _mode;
    private string? _unavailable;
    private (string Key, string Reason)? _generationFailure;

    /// <summary>A provider over <paramref name="models"/>, reading <paramref name="settings"/> at each request, answering while <paramref name="active"/>.</summary>
    public AiInlineCompletion(Func<AiSettings> settings, AiModels models, Func<bool>? active = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _active = active ?? (static () => true);
    }

    /// <summary>Where a request shows while it writes (the host's activities); none by default.</summary>
    public IEditorActivities Activities { get; set; } = NoActivities.Instance;

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "Ghost text from the AI plug-in's model: fill-in-the-middle by the model's format, else the prefix alone";

    /// <inheritdoc />
    public int DelayMilliseconds => _settings().CompletionDelayMilliseconds;

    /// <summary>How the last prompt was laid out, or null before the first.</summary>
    public CompletionMode? Mode => _mode;

    /// <summary>Raised (on the request's thread) when <see cref="Mode"/> changed.</summary>
    public event EventHandler<CompletionMode>? ModeChanged;

    /// <summary>Raised (on the request's thread) when a completion ended: its statistics (prompt and cached tokens, rates).</summary>
    public event EventHandler<GenerationStats>? Completed;

    /// <summary>
    /// Raised (on the asking thread) when whether completion can answer changed: why not (no model loaded, its load or its
    /// last generation failed), or null when it can again. Said once per reason, quietly (the status bar), never a dialog.
    /// </summary>
    public event EventHandler<string?>? Unavailable;

    /// <summary>Why completion does not answer now, or null when it does.</summary>
    public string? UnavailableReason => _unavailable;

    /// <inheritdoc />
    public bool Accepts(InlineCompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = _settings();
        if (!_active() || !settings.Completion)
        {
            return false;
        }

        // Never a model loaded for completion behind the user's back: only one it may use now (its own small one, loaded on
        // its first request when completionLoad allows it, or the chat model while loaded); a failure stops the asking.
        string? reason = _generationFailure is { } failure && failure.Key == FailureKey(settings) ? failure.Reason : _models.CompletionUnavailable;
        if (!string.Equals(reason, _unavailable, StringComparison.Ordinal))
        {
            _unavailable = reason;
            Unavailable?.Invoke(this, reason);
        }

        return reason is null;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<InlineCompletionPiece> CompleteAsync(InlineCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = _settings();
        if (await _models.CompletionAsync(cancellationToken).ConfigureAwait(false) is not { } lease)
        {
            yield break;                                                // no model to use now: Accepts says why
        }

        var model = lease.Text ?? throw new NotSupportedException($"The model {lease.Model} gives no text model, so it cannot complete text.");
        var (prompt, stops) = Layout(request, lease, settings);
        var options = new GenerationOptions
        {
            NumPredict = settings.CompletionMaxTokens,
            NumCtx = lease.ContextLength,
            Temperature = settings.CompletionTemperature,
            ChunkSize = settings.CompletionChunk,
            Stop = stops,
        };

        int lines = 0;
        int generated = 0;
        // What the ghost showed, for the AI events' sinks (the learning plug-in); gathered only while one listens.
        var shown = AiEvents.Listening ? new StringBuilder() : null;
        // Phase U: a request that runs long shows in the status bar while it writes (never kept: the next key ends it).
        using var activity = Activities.Start("Completion", ActivityKinds.Generation, keep: false);
        try
        {
            await foreach (var chunk in Guarded(model.StreamAsync(prompt, options, cancellationToken), settings, cancellationToken).ConfigureAwait(false))
            {
                activity.Report(null, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Max(generated, chunk.Generated)} tokens"));
                if (chunk.Done && chunk.Stats is { } stats)
                {
                    Completed?.Invoke(this, stats);
                }

                string text = chunk.Text;
                int tokens = chunk.Generated > generated ? chunk.Generated - generated : text.Length > 0 ? 1 : 0;
                generated = Math.Max(generated, chunk.Generated);
                if (text.Length == 0 && tokens == 0)
                {
                    continue;
                }

                // The line limit: the piece up to the line break that would start one line too many, then the request ends.
                int cut = -1;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\n' && ++lines >= settings.CompletionMaxLines)
                    {
                        cut = i;
                        break;
                    }
                }

                if (cut >= 0)
                {
                    shown?.Append(text, 0, cut);
                    yield return new InlineCompletionPiece(text[..cut], tokens);
                    yield break;
                }

                shown?.Append(text);
                yield return new InlineCompletionPiece(text, tokens);
                if (chunk.Done)
                {
                    yield break;
                }
            }
        }
        finally
        {
            if (shown is { Length: > 0 })
            {
                AiEvents.Answered(new AiAnswer(AiEvents.NewId(), AiAnswerKinds.Completion, prompt, shown.ToString())
                {
                    Plugin = AiSettings.PluginName,
                    Model = lease.Model,
                    Snapshot = request.Snapshot,
                    Position = request.Position,
                    FilePath = request.FilePath,
                    LanguageName = request.LanguageName,
                    WorkspaceFolder = request.WorkspaceFolder,
                });
            }
        }
    }

    // The model's stream with its failure kept: an out-of-memory one (or any) stops completion asking on every pause until
    // the completion settings change, said once in the status bar; the editor's controller keeps the error too.
    private async IAsyncEnumerable<GenerationChunk> Guarded(IAsyncEnumerable<GenerationChunk> stream, AiSettings settings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chunks = stream.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool more;
                try
                {
                    more = await chunks.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OperationCanceledException && AiErrors.IsOutOfMemory(error))
                {
                    _generationFailure = (FailureKey(settings), "Completion stopped: " + AiErrors.Describe(error, settings, forCompletion: true));
                    throw;
                }

                if (!more)
                {
                    yield break;
                }

                yield return chunks.Current;
            }
        }
        finally
        {
            await chunks.DisposeAsync().ConfigureAwait(false);
        }
    }

    // What a generation failure is kept for: the completion settings of the model (another model, device or weights asks again).
    private static string FailureKey(AiSettings settings) =>
        string.Join('\n', settings.CompletionModel, settings.Model, settings.Device, settings.Weights, settings.Context.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The prompt for <paramref name="request"/> and its stops: the model's fill-in-the-middle format over the prefix and
    /// suffix, or the prefix alone (stopping at a blank line) when the model has none; <see cref="Mode"/> says which.
    /// </summary>
    public (string Prompt, IReadOnlyList<string> Stop) Layout(InlineCompletionRequest request, ModelLease lease, AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(settings);
        var format = Detect(lease);
        var snapshot = request.Snapshot;
        int caret = Math.Clamp(request.Position, 0, snapshot.Length);
        string suffix = format is null ? "" : Suffix(snapshot, caret, settings.CompletionSuffixCharacters);
        int prefixStart = PrefixStart(snapshot, caret, PrefixBudget(lease, settings, suffix));
        string prefix = snapshot.GetText(prefixStart, caret - prefixStart);
        if (format is null)
        {
            return (prefix, ["\n\n"]);
        }

        var context = new FillInMiddleContext(lease.Tokenizer!, prefix, suffix) { Path = RelativePath(request) };
        var prompt = format.Create(context);
        return (prompt.Text, prompt.Stop);
    }

    /// <summary>
    /// Where the prefix starts for a caret at <paramref name="caret"/> and a budget of <paramref name="budget"/> characters:
    /// the first line start at or after <c>caret - budget</c> rounded up to a block of a quarter of the budget, so it stays
    /// put while the caret moves within a block (a kept cache reuses the prompt), and never more than the budget back.
    /// </summary>
    public static int PrefixStart(ITextSnapshot snapshot, int caret, int budget)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (budget <= 0)
        {
            return caret;
        }

        if (caret <= budget)
        {
            return 0;
        }

        int block = Math.Max(256, budget / 4);
        long from = caret - (long)budget;
        int start = (int)Math.Min(caret, (from + block - 1) / block * block);
        int line = snapshot.GetLineIndex(start);
        var text = snapshot.GetLine(line);
        if (text.Start == start)
        {
            return start;
        }

        return line + 1 < snapshot.LineCount && snapshot.GetLine(line + 1).Start <= caret ? snapshot.GetLine(line + 1).Start : start;
    }

    private static string Suffix(ITextSnapshot snapshot, int caret, int limit)
    {
        int length = Math.Min(limit, snapshot.Length - caret);
        if (length <= 0)
        {
            return "";
        }

        // Whole lines when the limit cuts one (the model reads a line's start and end together).
        if (caret + length < snapshot.Length)
        {
            int end = caret + length;
            int line = snapshot.GetLineIndex(end);
            int start = snapshot.GetLine(line).Start;
            if (start > caret)
            {
                length = start - caret;
            }
        }

        return snapshot.GetText(caret, length);
    }

    // The prefix's budget in characters: the setting, else the model's context less the answer and the suffix (counted by
    // the tokenizer when the model gives one), at the characters per token assumed.
    private static int PrefixBudget(ModelLease lease, AiSettings settings, string suffix)
    {
        if (settings.CompletionPrefixCharacters > 0)
        {
            return settings.CompletionPrefixCharacters;
        }

        int suffixTokens = suffix.Length == 0 ? 0 : lease.Tokenizer?.Encode(suffix).Count ?? suffix.Length;
        long tokens = (long)lease.ContextLength - settings.CompletionMaxTokens - suffixTokens - 16;
        return (int)Math.Clamp(tokens * CharactersPerToken, 0, int.MaxValue);
    }

    private IFillInMiddleFormat? Detect(ModelLease lease)
    {
        if (ReferenceEquals(_detectedFor, lease) && _mode is { } known)
        {
            return known.Format is null ? null : FillInMiddleFormats.Get(known.Format);
        }

        CompletionMode mode;
        IFillInMiddleFormat? format = null;
        if (lease.Tokenizer is not { } tokenizer)
        {
            mode = new CompletionMode(null, lease.Model, "the model gives no tokenizer to detect its fill-in-the-middle format from");
        }
        else if (FillInMiddleFormats.Detect(tokenizer) is { } name)
        {
            format = FillInMiddleFormats.Get(name);
            mode = new CompletionMode(name, lease.Model, null);
        }
        else
        {
            mode = new CompletionMode(null, lease.Model, string.Create(CultureInfo.InvariantCulture,
                $"{lease.Model} has no fill-in-the-middle format among those registered: {(FillInMiddleFormats.Names.Count == 0 ? "none" : string.Join(", ", FillInMiddleFormats.Names))}"));
        }

        _detectedFor = lease;
        bool changed = _mode != mode;
        _mode = mode;
        if (changed)
        {
            ModeChanged?.Invoke(this, mode);
        }

        return format;
    }

    private static string? RelativePath(InlineCompletionRequest request)
    {
        if (request.FilePath is not { } path)
        {
            return null;
        }

        return (request.WorkspaceFolder is { } root ? Path.GetRelativePath(root, path) : Path.GetFileName(path)).Replace('\\', '/');
    }
}
