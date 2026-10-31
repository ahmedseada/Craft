// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.CSharp.Workspaces;

/// <summary>
/// "loose": stray C# files, in no project, as one project of their own, "Loose files": the running framework's
/// references, the SDK's implicit usings, nullable enabled, the latest language version. The file asked for is its one
/// source; the semantic layer adds each other stray file that opens.
/// </summary>
public sealed class LooseWorkspaceReader : IWorkspaceReader
{
    /// <summary>Its name in <see cref="WorkspaceReaders"/>.</summary>
    public const string ReaderName = "loose";

    /// <summary>The loose project's name.</summary>
    public const string ProjectName = "Loose files";

    /// <inheritdoc />
    public string Name => ReaderName;

    /// <inheritdoc />
    public string Summary => "Stray C# files as one project on the running framework's references";

    /// <inheritdoc />
    public WorkspaceModel? Read(WorkspaceReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var framework = ReferenceAssemblies.For(null);
        var folder = request.Root ?? (request.FilePath is { } file ? Path.GetDirectoryName(Path.GetFullPath(file))! : Directory.GetCurrentDirectory());
        var project = new WorkspaceProject
        {
            Name = ProjectName,
            Folder = folder,
            TargetFramework = ReferenceAssemblies.RunningFramework,
            Nullable = "enable",
            LanguageVersion = "latest",
            GlobalUsings = ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"],
            SourceFiles = request.FilePath is { } path ? [Path.GetFullPath(path)] : [],
            References = framework.Files,
            Analyzers = framework.Analyzers,
            Notes = ["a stray file, in no project: the running framework's references", .. framework.Notes],
        };
        return new WorkspaceModel(ReaderName, folder, [project]);
    }
}
