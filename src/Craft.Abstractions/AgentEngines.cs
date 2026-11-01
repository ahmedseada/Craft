// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>A task an agent engine runs (<see cref="IAgentEngine.RunAsync"/>).</summary>
/// <param name="Model">The engine's model the user picked ("Claude Code (claude-work)").</param>
/// <param name="Task">What the user asked.</param>
/// <param name="Folder">The folder it works in: the agent panel's working copy of the open folder (the user's files change only through the diffs they accept).</param>
public sealed record AgentEngineRequest(string Model, string Task, string Folder)
{
    /// <summary>The engine's own session to continue (a follow-up message), or null for a new one.</summary>
    public string? Resume { get; init; }

    /// <summary>The open folder the working copy was made of, or null.</summary>
    public string? Workspace { get; init; }
}

/// <summary>What one step of an engine's run is, as the agent panel draws it.</summary>
public enum AgentEngineStep
{
    /// <summary>Text the engine wrote (appended to its streaming message).</summary>
    Text,

    /// <summary>A tool it called (<see cref="AgentEngineEvent.Text"/> the tool's name, <see cref="AgentEngineEvent.Detail"/> its input in a line).</summary>
    ToolCall,

    /// <summary>A tool's result (<see cref="AgentEngineEvent.Text"/> the tool's name, <see cref="AgentEngineEvent.Detail"/> its result, <see cref="AgentEngineEvent.Ok"/>).</summary>
    ToolResult,

    /// <summary>A quiet line (the engine's session started, its cost and time).</summary>
    Note,

    /// <summary>Something that failed.</summary>
    Error,
}

/// <summary>One step of an engine's run.</summary>
/// <param name="Step">What it is.</param>
/// <param name="Text">Its text.</param>
public sealed record AgentEngineEvent(AgentEngineStep Step, string Text)
{
    /// <summary>A second text (a tool's input or result in a line), or null.</summary>
    public string? Detail { get; init; }

    /// <summary>What opens under the step (a tool's whole input or output), or null.</summary>
    public string? Details { get; init; }

    /// <summary>Whether a tool's call succeeded, or null.</summary>
    public bool? Ok { get; init; }

    /// <summary>The tool call's id, so its result completes its step, or null.</summary>
    public string? Id { get; init; }
}

/// <summary>How an engine's run ended.</summary>
/// <param name="End">What the agent panel says at its end ("Done in 6 turns · 41 s").</param>
/// <param name="Session">The engine's session, for a follow-up (<see cref="AgentEngineRequest.Resume"/>), or null.</param>
public sealed record AgentEngineResult(string End, string? Session)
{
    /// <summary>Whether it failed (the panel says so as an error).</summary>
    public bool Failed { get; init; }
}

/// <summary>
/// An outside agent the agent panel runs in place of a local model (the Claude Code plug-in's "claude-code"), registered
/// in <see cref="AgentEngines"/>: the user picks one of its <see cref="Models"/> at the composer's model chip, and the
/// agent panel hands it each task with its working copy of the open folder; what it changes there is offered as diffs the
/// user accepts. Its steps stream to the panel through the report given to <see cref="RunAsync"/>.
/// </summary>
public interface IAgentEngine : IKind
{
    /// <summary>The models the chip lists for the agent ("Claude Code", "Claude Code (claude-work)"); none where the engine cannot run.</summary>
    IReadOnlyList<string> Models { get; }

    /// <summary>Why <paramref name="model"/> cannot run now (the program missing, a version too old), with what to do; null when it can.</summary>
    string? Check(string model) => null;

    /// <summary>
    /// Runs <paramref name="request"/> to its end, off the UI thread, reporting each step to <paramref name="report"/>
    /// (any thread); <paramref name="cancellationToken"/> stops it with its process tree.
    /// </summary>
    Task<AgentEngineResult> RunAsync(AgentEngineRequest request, Action<AgentEngineEvent> report, CancellationToken cancellationToken = default);
}

/// <summary>A request to the agent panel to run a task with an engine's model (<see cref="AgentEngines.Ask"/>).</summary>
/// <param name="model">The engine's model.</param>
/// <param name="task">The task; empty only shows the panel with that model for its next message.</param>
public sealed class AgentEngineAskEventArgs(string model, string task) : EventArgs
{
    /// <summary>The engine's model.</summary>
    public string Model { get; } = model;

    /// <summary>The task, or "".</summary>
    public string Task { get; } = task;

    /// <summary>Set by the panel that took it.</summary>
    public bool Taken { get; set; }
}

/// <summary>
/// The agent engines, by name (ignoring case). None is the library's; the Claude Code plug-in registers "claude-code".
/// <see cref="FindModel"/> says which engine a model's name belongs to; <see cref="Ask"/> asks the agent panel to run a
/// task with one (a report's "Ask Claude Code").
/// </summary>
public static class AgentEngines
{
    private static readonly KindTable<IAgentEngine> Table = new(nameof(AgentEngines), "Agent engine", "add it with AgentEngines.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "an engine runs a task the user gave, in its working copy");

    /// <summary>Raised (on the UI thread) when a task is asked of an engine's model: the agent panel takes it.</summary>
    public static event EventHandler<AgentEngineAskEventArgs>? Asked;

    /// <summary>Raised when an engine's models changed (accounts found): the chip lists them again.</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="engine"/> under its name.</summary>
    public static void Register(IAgentEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Table.Register(engine, System.Reflection.Assembly.GetCallingAssembly());
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Removes the engine of that name; false when none was registered.</summary>
    public static bool Unregister(string name)
    {
        bool removed = Table.Unregister(name);
        if (removed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered engines, in order.</summary>
    public static IReadOnlyList<IAgentEngine> All => Table.Kinds;

    /// <summary>The engine of that name, or null.</summary>
    public static IAgentEngine? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The engine of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IAgentEngine Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>Every engine's models, in order.</summary>
    public static IReadOnlyList<string> Models
    {
        get
        {
            var models = new List<string>();
            foreach (var engine in Table.Kinds)
            {
                models.AddRange(engine.Models);
            }

            return models;
        }
    }

    /// <summary>The engine that lists <paramref name="model"/> (ignoring case), or null: a local model's name.</summary>
    public static IAgentEngine? FindModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        foreach (var engine in Table.Kinds)
        {
            foreach (var name in engine.Models)
            {
                if (name.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return engine;
                }
            }
        }

        return null;
    }

    /// <summary>Asks the agent panel to run <paramref name="task"/> with <paramref name="model"/> (on the UI thread); false when no panel took it.</summary>
    public static bool Ask(string model, string task)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var ask = new AgentEngineAskEventArgs(model, task ?? "");
        Asked?.Invoke(null, ask);
        return ask.Taken;
    }

    /// <summary>Says an engine's models changed (its plug-in found accounts).</summary>
    public static void RaiseChanged() => Changed?.Invoke(null, EventArgs.Empty);
}
