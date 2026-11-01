// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// "ai" in <see cref="CommitMessageProviders"/> (X part 8, the owner: "using the current loaded models with settings"): a
/// commit message written by a model already loaded, through its role's existing lease (never a second copy, never a load
/// of its own) and that role's own temperature, context and reply limit. "loaded" takes the first loaded of the chat's, the
/// agent's and the completion's model; "chat", "agent", "completion" that role's; a model's name the role that has it
/// loaded. Nothing loaded: <see cref="Plan"/> says so and names the command that loads the chat's model (note 23: a load
/// only when the user asks). The message is a draft: this provider never commits.
/// </summary>
public sealed class AiCommitMessages(Func<AiSettings> settings, AiModels models) : ICommitMessageProvider
{
    /// <summary>The provider's name.</summary>
    public const string ProviderName = "ai";

    private static readonly string[] Roles = [ModelRoleKeys.Chat, ModelRoleKeys.Agent, ModelRoleKeys.Completion];

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "the AI plug-in: a model already loaded writes it, with its role's own settings";

    /// <inheritdoc />
    public IReadOnlyList<CommitModelChoice> Models()
    {
        var now = settings();
        var choices = new List<CommitModelChoice>(Roles.Length);
        foreach (var role in Roles)
        {
            var resolved = now.RoleOf(role);
            if (resolved.Model.Length > 0)
            {
                choices.Add(new CommitModelChoice(role, $"{Title(role)} · {resolved.Model}", models.IsLoaded(role)));
            }
        }

        choices.Sort(static (a, b) => b.Loaded.CompareTo(a.Loaded));
        return choices;
    }

    /// <inheritdoc />
    public CommitModelPlan Plan(string choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var now = settings();
        string? role = RoleFor(now, choice.Trim());
        if (role is not null)
        {
            var resolved = now.RoleOf(role);
            return new CommitModelPlan(resolved.Model, Title(role).ToLowerInvariant(), null) { ContextTokens = resolved.Context };
        }

        string why = choice.Trim().ToLowerInvariant() switch
        {
            "loaded" or "" => "No model is loaded: load the chat model, or pick a model at the chat's composer (a model loads only when you ask).",
            "chat" or "agent" or "completion" => $"The {choice.Trim().ToLowerInvariant()} model is not loaded (commitModel={choice.Trim()}): load it, or set commitModel to loaded.",
            _ => $"{choice.Trim()} is not loaded by any role (commitModel): load it, or set commitModel to loaded.",
        };
        return new CommitModelPlan(null, null, why) { LoadCommand = AiPlugin.LoadModelCommand };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> WriteAsync(CommitMessageRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = settings();
        string role = RoleFor(now, request.Model.Trim()) ?? throw new InvalidOperationException(Plan(request.Model).Why);
        var resolved = now.RoleOf(role);
        // The role's lease as it is: the model is loaded, so this takes the copy it holds.
        var lease = role == ModelRoleKeys.Completion
            ? await models.CompletionAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException(Plan(request.Model).Why)
            : await models.RoleAsync(role, cancellationToken).ConfigureAwait(false);
        var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template, so it cannot write a commit message: pick a chat model.");
        var messages = new List<ChatMessage>
        {
            new("system", request.Instructions),
            new("user", Prompt(request)),
        };
        var options = new GenerationOptions
        {
            NumPredict = request.MaxTokens > 0 ? request.MaxTokens : resolved.MaxTokens,
            NumCtx = lease.ContextLength,
            Temperature = resolved.Temperature,
        };
        await foreach (var chunk in chat.StreamAsync(new ChatRequest(messages, Options: options), cancellationToken).ConfigureAwait(false))
        {
            if (chunk.Delta.Content is { Length: > 0 } piece)
            {
                yield return piece;
            }
        }
    }

    /// <summary>What the model is given: the recent subjects (when any), then the changes.</summary>
    public static string Prompt(CommitMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = new System.Text.StringBuilder();
        if (request.Examples.Count > 0)
        {
            text.Append("Recent subjects in this repository:\n");
            foreach (var subject in request.Examples)
            {
                text.Append("- ").Append(subject).Append('\n');
            }

            text.Append('\n');
        }

        return text.Append("The changes:\n").Append(request.Diff).ToString();
    }

    // The loaded role a choice takes: "loaded" the first loaded of chat, agent, completion; a role's key that role when
    // loaded; a model's name the first loaded role whose model it is. Null when none.
    private string? RoleFor(AiSettings now, string choice) => LoadedRole(now, models, choice);

    /// <summary>
    /// The loaded role <paramref name="choice"/> takes ("loaded": the first loaded of chat, agent, completion; a role's key:
    /// that role when loaded; a model's name: the first loaded role whose model it is), or null when none (X part 8; the
    /// review's choice, X part 11, reads it the same way).
    /// </summary>
    internal static string? LoadedRole(AiSettings now, AiModels models, string choice)
    {
        switch (choice.ToLowerInvariant())
        {
            case "loaded" or "":
                foreach (var role in Roles)
                {
                    if (now.RoleOf(role).Model.Length > 0 && models.IsLoaded(role))
                    {
                        return role;
                    }
                }

                return null;
            case ModelRoleKeys.Chat or ModelRoleKeys.Agent or ModelRoleKeys.Completion:
                string key = choice.ToLowerInvariant();
                return models.IsLoaded(key) ? key : null;
            default:
                foreach (var role in Roles)
                {
                    if (string.Equals(now.RoleOf(role).Model, choice, StringComparison.OrdinalIgnoreCase) && models.IsLoaded(role))
                    {
                        return role;
                    }
                }

                return null;
        }
    }

    internal static string Title(string role) => role switch
    {
        ModelRoleKeys.Chat => "Chat",
        ModelRoleKeys.Agent => "Agent",
        _ => "Completion",
    };
}
