// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Idrak.Abstraction;
using Idrak.Abstraction.Formats;

namespace Craft.Plugins.WorkspaceModels;

/// <summary>
/// Idrak's model source "craft" (in <see cref="ModelSources"/>, a new kind of name): <c>craft:NAME</c> is the name the
/// workspace's <c>.craft/model.json</c> gives, resolved to what it stands for by the other registered sources (a folder or a
/// .gguf file relative to the workspace, store:NAME, a Hugging Face id, another craft:NAME). The workspace is the editor's
/// open folder while the plug-in is active there, else <c>CRAFT_PLUGIN_WORKSPACEMODELS_WORKSPACE</c>, else the current
/// folder (the idrak tool run in the project). A name the file does not give fails with "not registered", naming the file
/// and the names it has; nothing falls back to another name.
/// </summary>
public sealed class WorkspaceModelSource : IModelSource
{
    /// <summary>The source's name.</summary>
    public const string SourceName = "craft";

    /// <summary>The names' prefix.</summary>
    public const string Prefix = "craft:";

    private const int MostLinks = 8;

    private static string? s_workspace;
    private static string? s_file;

    private WorkspaceModelSource()
    {
    }

    /// <summary>The source.</summary>
    public static WorkspaceModelSource Instance { get; } = new();

    /// <inheritdoc />
    public string Name => SourceName;

    /// <summary>The workspace the names are read from now (the editor's folder while the plug-in is active there), or null.</summary>
    public static string? Workspace
    {
        get => Volatile.Read(ref s_workspace)
               ?? (Environment.GetEnvironmentVariable(WorkspaceModelSettings.Variable("workspace")) is { Length: > 0 } set ? set : Environment.CurrentDirectory);
        internal set => Volatile.Write(ref s_workspace, value);
    }

    /// <summary>The model file's path in a workspace (the setting <c>file</c>).</summary>
    public static string File
    {
        get => Volatile.Read(ref s_file)
               ?? (Environment.GetEnvironmentVariable(WorkspaceModelSettings.Variable("file")) is { Length: > 0 } set ? set : WorkspaceModelFile.DefaultPath);
        internal set => Volatile.Write(ref s_file, value);
    }

    /// <summary>Back to the defaults (the plug-in deactivated): the environment's workspace and file.</summary>
    internal static void Reset()
    {
        Volatile.Write(ref s_workspace, null);
        Volatile.Write(ref s_file, null);
    }

    /// <inheritdoc />
    public bool CanResolve(string model) => model is not null && model.StartsWith(Prefix, StringComparison.Ordinal) && model.Length > Prefix.Length;

    /// <inheritdoc />
    public string Resolve(string model, ModelSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string target = Target(model, Workspace);
        var source = ModelSources.For(target)
                     ?? throw new NotRegisteredException(nameof(ModelSources), target, ModelSources.Names,
                         $"'{model}' stands for '{target}', which no model source resolves (registered: {string.Join(", ", ModelSources.Names)}).");
        return source.Resolve(target, options);
    }

    /// <summary>
    /// What <paramref name="model"/> (<c>craft:NAME</c>) stands for in <paramref name="workspace"/>, its links followed: a
    /// relative path that is there becomes full.
    /// </summary>
    /// <exception cref="NotRegisteredException">The workspace's file does not give the name (or the workspace has no file).</exception>
    /// <exception cref="InvalidDataException">The file does not read, or its names link in a circle.</exception>
    public static string Target(string model, string? workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        string name = model;
        for (int link = 0; link < MostLinks; link++)
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return Place(name, workspace);
            }

            string key = name[Prefix.Length..];
            var file = workspace is null ? null : WorkspaceModelFile.For(workspace, File);
            if (file is null || !file.Models.TryGetValue(key, out var target))
            {
                string where = workspace is null ? "no workspace is open" : Path.Combine(workspace, File);
                var known = file?.Models.Keys.Select(k => Prefix + k).ToList() ?? [];
                throw new NotRegisteredException("workspace model names", name, known, file is null
                    ? $"The model name '{name}' is not registered: {where} {(workspace is null ? "" : "does not exist")} (a workspace names its models in .craft/model.json under \"models\")."
                    : $"The model name '{name}' is not registered in {where} (names there: {(known.Count == 0 ? "none" : string.Join(", ", known))}); add it under \"models\".");
            }

            name = target;
        }

        throw new InvalidDataException($"'{model}': its names link more than {MostLinks} times (a circle in {File}?).");
    }

    // A relative folder or file of the workspace (that is there) as a full path; any other name as it is.
    private static string Place(string target, string? workspace)
    {
        if (workspace is null || Path.IsPathFullyQualified(target) || target.Contains(':', StringComparison.Ordinal))
        {
            return target;
        }

        string full = Path.GetFullPath(Path.Combine(workspace, target));
        return Directory.Exists(full) || System.IO.File.Exists(full) ? full : target;
    }
}
