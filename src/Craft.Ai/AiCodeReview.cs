// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// "ai" in <see cref="CodeReviewProviders"/> (X part 11 item 4, Source Control's "Review changes"): the changes reviewed by
/// a model already loaded, as <c>reviewModel</c> picks it (chat by default; loaded, chat, agent, completion or a model's
/// name, as the commit message's choice), through that role's existing lease (no load of its own, no second copy) and its
/// temperature and reply limit (<c>reviewMaxTokens</c> when set). The model writes one finding a line
/// ("path:line: severity: message — reason"); each is read once its line is whole and streamed back. It changes nothing.
/// </summary>
public sealed class AiCodeReview(Func<AiSettings> settings, AiModels models) : ICodeReviewProvider
{
    /// <summary>The provider's name.</summary>
    public const string ProviderName = "ai";

    /// <summary>The plug-in's own instructions (<c>reviewPrompt</c> replaces them).</summary>
    public const string DefaultPrompt =
        "You review code changes before they are committed. Look for bugs, crashes, security problems, leaks, wrong edge cases and "
        + "code that does not do what it seems to; mention style only when it hides a problem. For each finding write one line, exactly: "
        + "PATH:LINE: SEVERITY: MESSAGE — REASON, where PATH is the file as the diff names it, LINE the number shown before the changed "
        + "line, SEVERITY one of error (a bug), warning (a risk) or info (a suggestion), MESSAGE what is wrong in a few words and REASON "
        + "why it matters. Write nothing else. When there is nothing to report, write: No findings.";

    private const string Quotes = "`\"'*";
    private const string Separators = ": -–—";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "the AI plug-in: a model already loaded reviews the changes, with its role's own settings";

    /// <inheritdoc />
    public CodeReviewPlan Plan()
    {
        var now = settings();
        string choice = now.ReviewModel.Trim();
        if (AiCommitMessages.LoadedRole(now, models, choice) is { } role)
        {
            var resolved = now.RoleOf(role);
            return new CodeReviewPlan(resolved.Model, AiCommitMessages.Title(role).ToLowerInvariant(), null)
            {
                ContextTokens = resolved.Context,
                ReplyTokens = now.ReviewMaxTokens > 0 ? now.ReviewMaxTokens : resolved.MaxTokens,
            };
        }

        string why = choice.ToLowerInvariant() switch
        {
            "loaded" or "" => "No model is loaded: load the chat model, or pick a model at the chat's composer (a model loads only when you ask).",
            "chat" or "agent" or "completion" => $"The {choice.ToLowerInvariant()} model is not loaded (reviewModel={choice}, CRAFT_PLUGIN_AI_REVIEWMODEL): load it, or set reviewModel to loaded.",
            _ => $"{choice} is not loaded by any role (reviewModel, CRAFT_PLUGIN_AI_REVIEWMODEL): load it, or set reviewModel to loaded.",
        };
        return new CodeReviewPlan(null, null, why) { LoadCommand = choice.Equals(ModelRoleKeys.Agent, StringComparison.OrdinalIgnoreCase) ? AiPlugin.LoadAgentModelCommand : AiPlugin.LoadModelCommand };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ReviewFinding> ReviewAsync(CodeReviewRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = settings();
        string role = AiCommitMessages.LoadedRole(now, models, now.ReviewModel.Trim()) ?? throw new InvalidOperationException(Plan().Why);
        var resolved = now.RoleOf(role);
        var lease = role == ModelRoleKeys.Completion
            ? await models.CompletionAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException(Plan().Why)
            : await models.RoleAsync(role, cancellationToken).ConfigureAwait(false);
        var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template, so it cannot review: pick a chat model (reviewModel).");
        var messages = new List<ChatMessage>
        {
            new("system", now.ReviewPrompt.Length > 0 ? now.ReviewPrompt : DefaultPrompt),
            new("user", Prompt(request)),
        };
        var options = new GenerationOptions
        {
            NumPredict = now.ReviewMaxTokens > 0 ? now.ReviewMaxTokens : resolved.MaxTokens,
            NumCtx = lease.ContextLength,
            Temperature = resolved.Temperature,
        };
        var partial = new StringBuilder();
        await foreach (var chunk in chat.StreamAsync(new ChatRequest(messages, Options: options), cancellationToken).ConfigureAwait(false))
        {
            if (chunk.Delta.Content is not { Length: > 0 } piece)
            {
                continue;
            }

            partial.Append(piece);
            string all = partial.ToString();
            int at = 0;
            int newline;
            var found = new List<ReviewFinding>();
            while ((newline = all.IndexOf('\n', at)) >= 0)
            {
                if (Parse(all.AsSpan(at, newline - at)) is { } finding)
                {
                    found.Add(finding);
                }

                at = newline + 1;
            }

            partial.Clear().Append(all.AsSpan(at));
            foreach (var finding in found)
            {
                yield return finding;
            }
        }

        if (Parse(partial.ToString()) is { } last)
        {
            yield return last;
        }
    }

    /// <summary>What the model is given: the changes, and which files did not fit.</summary>
    public static string Prompt(CodeReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = new StringBuilder();
        text.Append(request.AllChanges ? "Nothing is staged: these are every change of the working copy.\n" : "These are the staged changes.\n");
        if (request.Summarised.Count > 0)
        {
            text.Append("These files did not fit and are given by name only (do not review them): ").Append(string.Join(", ", request.Summarised)).Append(".\n");
        }

        return text.Append("Each kept or added line has its line number before a '|'.\n\n").Append(request.Diff).ToString();
    }

    /// <summary>
    /// A finding from one line of the model's reply ("src/A.cs:12: warning: message — reason", with a list mark, quotes or
    /// a column allowed), or null when the line is none ("No findings.", prose).
    /// </summary>
    public static ReviewFinding? Parse(ReadOnlySpan<char> line)
    {
        var text = line.Trim();
        // A list mark ("- ", "* ", "1. ") and quotes around the path.
        if (text.Length > 1 && text[0] is '-' or '*' or '•' && text[1] == ' ')
        {
            text = text[2..].TrimStart();
        }

        int digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
        {
            digits++;
        }

        if (digits > 0 && digits + 1 < text.Length && text[digits] is '.' or ')' && text[digits + 1] == ' ')
        {
            text = text[(digits + 2)..].TrimStart();
        }

        text = text.TrimStart(Quotes);
        // PATH:LINE — the first ':' followed by digits after a non-empty path.
        int colon = -1;
        for (int i = 1; i < text.Length - 1; i++)
        {
            if (text[i] == ':' && char.IsAsciiDigit(text[i + 1]))
            {
                colon = i;
                break;
            }
        }

        if (colon < 0)
        {
            return null;
        }

        string path = text[..colon].Trim().Trim(Quotes).ToString().Replace('\\', '/');
        if (path.Length == 0 || path.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        int end = colon + 1;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        if (!int.TryParse(text[(colon + 1)..end], NumberStyles.None, CultureInfo.InvariantCulture, out int lineNumber))
        {
            return null;
        }

        var rest = text[end..];
        // An optional column (":5"), then the separator before the severity.
        if (rest.Length > 1 && rest[0] == ':' && char.IsAsciiDigit(rest[1]))
        {
            int skip = 1;
            while (skip < rest.Length && char.IsAsciiDigit(rest[skip]))
            {
                skip++;
            }

            rest = rest[skip..];
        }

        rest = rest.TrimStart(Quotes).TrimStart(Separators).TrimStart();
        rest = rest.TrimStart("[(");
        int word = 0;
        while (word < rest.Length && char.IsAsciiLetter(rest[word]))
        {
            word++;
        }

        var severity = SeverityOf(rest[..word]);
        if (severity is null)
        {
            return null;
        }

        rest = rest[word..].TrimStart("])").TrimStart(Separators).Trim();
        if (rest.IsEmpty)
        {
            return null;
        }

        string message = rest.ToString();
        string reason = "";
        foreach (var separator in (ReadOnlySpan<string>)[" — ", " – ", " -- ", " | ", " - because ", "; because "])
        {
            int at = message.IndexOf(separator, StringComparison.Ordinal);
            if (at > 0)
            {
                reason = message[(at + separator.Length)..].Trim();
                message = message[..at].Trim();
                break;
            }
        }

        return new ReviewFinding(path, lineNumber, severity.Value, message.TrimEnd('.').Trim(), reason);
    }

    private static DiagnosticSeverity? SeverityOf(ReadOnlySpan<char> word)
    {
        if (word.Equals("error", StringComparison.OrdinalIgnoreCase) || word.Equals("bug", StringComparison.OrdinalIgnoreCase)
            || word.Equals("critical", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticSeverity.Error;
        }

        if (word.Equals("warning", StringComparison.OrdinalIgnoreCase) || word.Equals("warn", StringComparison.OrdinalIgnoreCase)
            || word.Equals("risk", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticSeverity.Warning;
        }

        return word.Equals("info", StringComparison.OrdinalIgnoreCase) || word.Equals("note", StringComparison.OrdinalIgnoreCase)
            || word.Equals("suggestion", StringComparison.OrdinalIgnoreCase) || word.Equals("nit", StringComparison.OrdinalIgnoreCase)
            || word.Equals("style", StringComparison.OrdinalIgnoreCase)
            ? DiagnosticSeverity.Info
            : null;
    }
}
