// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>The roles a model plays in the editor (<see cref="IModelRoleSource"/>): chat, inline completion, the agent.</summary>
public static class ModelRoles
{
    /// <summary>The chat panel and the code actions.</summary>
    public const string Chat = "chat";

    /// <summary>Inline completion (ghost text).</summary>
    public const string Completion = "completion";

    /// <summary>The agent.</summary>
    public const string Agent = "agent";

    /// <summary>Every role, in order.</summary>
    public static IReadOnlyList<string> All { get; } = [Chat, Completion, Agent];
}

/// <summary>A model name a workspace defines ("craft:coder"), what it stands for, and the roles it is the default of.</summary>
/// <param name="Name">The name as a model setting takes it ("craft:coder").</param>
/// <param name="Target">What it stands for (a folder, store:name, a .gguf file, a Hugging Face id).</param>
/// <param name="Roles">The roles it is the workspace's default for (<see cref="ModelRoles"/>).</param>
public sealed record NamedModel(string Name, string Target, IReadOnlyList<string> Roles);

/// <summary>
/// Where the models a workspace pins come from (the workspace models plug-in's ".craft/model.json"): the default model of
/// each role (<see cref="ModelRoles"/>) and the names it defines, for a workspace folder. Registered in
/// <see cref="ModelRoleSources"/>; a model plug-in asks <see cref="ModelRoleSources.ModelFor"/> when the user set no model
/// for a role, and the Models page lists <see cref="Names"/>.
/// </summary>
public interface IModelRoleSource : IKind
{
    /// <summary>The model name the workspace gives <paramref name="role"/>, or null when it gives none.</summary>
    /// <param name="role">A role (<see cref="ModelRoles"/>).</param>
    /// <param name="workspaceFolder">The workspace's folder, or null for none open.</param>
    /// <exception cref="InvalidDataException">The workspace's file is there but does not read (the message names it and why).</exception>
    string? ModelFor(string role, string? workspaceFolder);

    /// <summary>The names the workspace defines (none when it defines none).</summary>
    /// <exception cref="InvalidDataException">The workspace's file is there but does not read.</exception>
    IReadOnlyList<NamedModel> Names(string? workspaceFolder);

    /// <summary>The file it reads for <paramref name="workspaceFolder"/> (".craft/model.json"), or null.</summary>
    string? FileFor(string? workspaceFolder) => null;
}

/// <summary>
/// The sources of workspace model defaults, by name (ignoring case). Craft's library registers none; the workspace models
/// plug-in registers "workspace". A source that is not registered fails with "not registered".
/// </summary>
public static class ModelRoleSources
{
    private static readonly KindTable<IModelRoleSource> Table = new(nameof(ModelRoleSources), "Model role source", "add it with ModelRoleSources.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a source reads a workspace's own file, whose names no other source knows");

    /// <summary>Registers <paramref name="source"/> under its name.</summary>
    public static void Register(IModelRoleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Table.Register(source, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the source of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered sources, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IModelRoleSource> All => Table.Kinds;

    /// <summary>The source of that name, or null.</summary>
    public static IModelRoleSource? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The source of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IModelRoleSource Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The first model a registered source gives <paramref name="role"/> for the workspace, or null when none does.</summary>
    /// <exception cref="InvalidDataException">A source's file does not read.</exception>
    public static string? ModelFor(string role, string? workspaceFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        foreach (var source in Table.Kinds)
        {
            if (source.ModelFor(role, workspaceFolder) is { Length: > 0 } model)
            {
                return model;
            }
        }

        return null;
    }
}

/// <summary>
/// What the models measured while they ran (from Idrak's telemetry: the status bar's telemetry plug-in reports it): the
/// last generation's speed and time to its first token, the device it ran on and the device memory in use there. Every
/// value not measured is null, so nothing is shown rather than a guess.
/// </summary>
public sealed record ModelMeasures
{
    /// <summary>Nothing measured.</summary>
    public static ModelMeasures None { get; } = new();

    /// <summary>The last generation's speed, tokens per second.</summary>
    public double? TokensPerSecond { get; init; }

    /// <summary>The last generation's time to its first token.</summary>
    public TimeSpan? FirstToken { get; init; }

    /// <summary>The device the last generation ran on ("cuda:0").</summary>
    public string? Device { get; init; }

    /// <summary>The bytes of device memory the library holds in use on that device.</summary>
    public long? DeviceMemoryBytes { get; init; }

    /// <summary>The peak of those bytes since the device started.</summary>
    public long? DeviceMemoryPeakBytes { get; init; }

    /// <summary>The tokens generated since the editor started.</summary>
    public long TokensGenerated { get; init; }
}

/// <summary>
/// The latest <see cref="ModelMeasures"/>, for the shell's model chip, status bar and Models page beside what a provider
/// reports itself (<see cref="ModelProviderStatus"/>): a plug-in that reads Idrak's telemetry reports here, from any
/// thread; <see cref="Changed"/> is raised on the reporting thread (the shell marshals to its UI thread).
/// </summary>
public static class ModelTelemetry
{
    private static ModelMeasures s_current = ModelMeasures.None;

    /// <summary>The latest measures (<see cref="ModelMeasures.None"/> before any).</summary>
    public static ModelMeasures Current => Volatile.Read(ref s_current);

    /// <summary>Raised after <see cref="Report"/> changed <see cref="Current"/>.</summary>
    public static event EventHandler? Changed;

    /// <summary>Sets the latest measures and raises <see cref="Changed"/> when they differ.</summary>
    public static void Report(ModelMeasures measures)
    {
        ArgumentNullException.ThrowIfNull(measures);
        if (Interlocked.Exchange(ref s_current, measures) != measures)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Forgets every measure (a reporter that stops).</summary>
    public static void Clear() => Report(ModelMeasures.None);
}
