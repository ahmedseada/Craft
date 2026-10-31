// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// One project a <see cref="IWorkspaceReader"/> found: its sources, references and compiler settings, as plain data (any
/// language's semantic layer builds its own model from it). Paths are full paths.
/// </summary>
public sealed record WorkspaceProject
{
    /// <summary>The project's name (its assembly's name).</summary>
    public required string Name { get; init; }

    /// <summary>The project file, or null for a project the reader made up (loose files).</summary>
    public string? ProjectFile { get; init; }

    /// <summary>The project's folder.</summary>
    public required string Folder { get; init; }

    /// <summary>The target framework it was read for ("net10.0"), or null.</summary>
    public string? TargetFramework { get; init; }

    /// <summary>The language version ("latest", "14"), or null for the compiler's default.</summary>
    public string? LanguageVersion { get; init; }

    /// <summary>The nullable context ("enable", "disable", "warnings", "annotations"), or null.</summary>
    public string? Nullable { get; init; }

    /// <summary>Whether unsafe code is allowed.</summary>
    public bool AllowUnsafe { get; init; }

    /// <summary>Whether warnings are reported as errors.</summary>
    public bool TreatWarningsAsErrors { get; init; }

    /// <summary>Whether documentation comments are checked (a documentation file is generated).</summary>
    public bool DocumentationDiagnostics { get; init; }

    /// <summary>The warning level, or null for the compiler's default.</summary>
    public int? WarningLevel { get; init; }

    /// <summary>The warnings not reported ("CS1701").</summary>
    public IReadOnlyList<string> NoWarn { get; init; } = [];

    /// <summary>Whether it builds a program (an executable) rather than a library.</summary>
    public bool IsExecutable { get; init; }

    /// <summary>The preprocessor symbols defined.</summary>
    public IReadOnlyList<string> DefineConstants { get; init; } = [];

    /// <summary>The namespaces imported in every file (the SDK's implicit usings and the project's <c>Using</c> items).</summary>
    public IReadOnlyList<string> GlobalUsings { get; init; } = [];

    /// <summary>The assemblies that see its internals (<c>InternalsVisibleTo</c> items).</summary>
    public IReadOnlyList<string> InternalsVisibleTo { get; init; } = [];

    /// <summary>Its source files.</summary>
    public IReadOnlyList<string> SourceFiles { get; init; } = [];

    /// <summary>The assemblies it references (metadata: files on disk).</summary>
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>The project files it references (each one also in <see cref="WorkspaceModel.Projects"/>).</summary>
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];

    /// <summary>What the reader could not do as the build would (no restore found, a condition not evaluated), for the user.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>What a reader made of a folder: its projects (a referenced project before the one that references it).</summary>
/// <param name="Reader">The reader's name.</param>
/// <param name="Root">The folder it read.</param>
/// <param name="Projects">The projects.</param>
public sealed record WorkspaceModel(string Reader, string Root, IReadOnlyList<WorkspaceProject> Projects);

/// <summary>What a reader is asked: the folder, the file that needs a project (or null for every project), and the settings.</summary>
/// <param name="Root">The workspace folder (a full path), or null when the editor has none: the reader then looks upward from <paramref name="FilePath"/>.</param>
/// <param name="FilePath">The file a project is needed for, or null.</param>
/// <param name="Settings">The settings (the configuration to read the project for, say).</param>
public sealed record WorkspaceReadRequest(string? Root, string? FilePath, LanguageSettings Settings);

/// <summary>
/// How a folder becomes projects, registered in <see cref="WorkspaceReaders"/>: "csproj" reads MSBuild project files
/// itself (no MSBuild, no SDK needed), "loose" wraps stray files as one project. A language's semantic layer tries the
/// readers of <see cref="LanguageSettings.WorkspaceReaders"/> in order; the first that answers wins. Called off the UI
/// thread; it reads files and may take a while (it is called once per workspace, again after a release).
/// </summary>
public interface IWorkspaceReader : IKind
{
    /// <summary>The projects for <paramref name="request"/>, or null when this reader does not apply (no project file found).</summary>
    WorkspaceModel? Read(WorkspaceReadRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The workspace readers, by name (ignoring case). Craft's library registers none; the C# plug-in registers "csproj" and
/// "loose". An unknown name fails with "not registered".
/// </summary>
public static class WorkspaceReaders
{
    private static readonly KindTable<IWorkspaceReader> Table = new(nameof(WorkspaceReaders), "Workspace reader", "add it with WorkspaceReaders.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a reader is called once per workspace");

    /// <summary>Registers <paramref name="reader"/> under its name.</summary>
    public static void Register(IWorkspaceReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Table.Register(reader, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's reader of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The reader of that name, or null.</summary>
    public static IWorkspaceReader? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The reader of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IWorkspaceReader Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// The first answer of the readers named in <paramref name="request"/>'s settings, in order; null when none applies.
    /// A name that is not registered fails.
    /// </summary>
    /// <exception cref="NotRegisteredException">A reader the settings name is not registered.</exception>
    public static WorkspaceModel? Read(WorkspaceReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var name in request.Settings.WorkspaceReaders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Get(name).Read(request, cancellationToken) is { } model)
            {
                return model;
            }
        }

        return null;
    }
}
