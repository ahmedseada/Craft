// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Craft.CSharp;

/// <summary>
/// One Roslyn workspace per workspace folder, shared by the sessions of its open C# files (counted; the last to close
/// disposes it). Built on first need from the <see cref="WorkspaceReaders"/> the settings name, in order (each project
/// once; the projects of a file found later are added then); its <see cref="Solution"/> is an immutable value swapped
/// under a lock, so a request reads the one it got while the next change makes another. An open file's document holds
/// its session's live text (<see cref="SnapshotSourceText"/>); every other file is read from disk when Roslyn first
/// needs it. <see cref="Release"/> drops it all (the next request builds it again). Metadata references are read once
/// per process and shared by every workspace. Never used on the UI thread.
/// </summary>
internal sealed class CSharpWorkspace
{
    private static readonly Lock RegistryGate = new();
    private static readonly Dictionary<string, CSharpWorkspace> Open = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, (DateTime Written, PortableExecutableReference Reference, long Bytes)> References = new(StringComparer.Ordinal);
    private static readonly Lock HostGate = new();
    private static MefHostServices? s_host;

    private readonly string _key;
    private readonly string? _root;
    private readonly LanguageSettings _settings;
    private readonly SemaphoreSlim _mutate = new(1, 1);
    private readonly Dictionary<object, DocumentId> _owners = [];
    private readonly Dictionary<string, ProjectId> _projectFiles = new(Workspaces.CsprojWorkspaceReader.PathComparer);
    private readonly Dictionary<string, ProjectId> _madeUp = new(StringComparer.Ordinal);
    private readonly HashSet<string> _referenced = new(StringComparer.Ordinal);
    private AdhocWorkspace? _workspace;
    private Solution? _solution;
    private int _users;
    private int _releaseRequested;
    private int _generation;
    private string? _reader;
    private long _referenceBytes;
    private long _heapBytes;
    private TimeSpan _buildTime;
    private IEditorActivities _activities = NoActivities.Instance;

    private CSharpWorkspace(string key, string? root, LanguageSettings settings)
    {
        _key = key;
        _root = root;
        _settings = settings;
    }

    /// <summary>The folder it is of (null for documents of no folder).</summary>
    public string? Root => _root;

    /// <summary>How many times it was built.</summary>
    public int Generation => Volatile.Read(ref _generation);

    /// <summary>The current solution, or null while released.</summary>
    public Solution? Solution => Volatile.Read(ref _solution);

    /// <summary>The workspace of <paramref name="root"/> for <paramref name="settings"/>, its user count raised (give it back with <see cref="Leave"/>).</summary>
    public static CSharpWorkspace Enter(string? root, LanguageSettings settings, IEditorActivities? activities = null)
    {
        var key = string.Join('|', root ?? "", settings.Configuration, string.Join(',', settings.WorkspaceReaders), settings.SourceGenerators ? "generators" : "");
        lock (RegistryGate)
        {
            if (!Open.TryGetValue(key, out var workspace))
            {
                workspace = new CSharpWorkspace(key, root, settings);
                Open.Add(key, workspace);
            }

            workspace._users++;
            if (activities is not null and not NoActivities)
            {
                workspace._activities = activities;            // the latest session's host shows its builds
            }

            return workspace;
        }
    }

    /// <summary>Roslyn's MEF host services, composed on first need and again after <see cref="ReleaseHost"/>.</summary>
    internal static MefHostServices HostServices()
    {
        lock (HostGate)
        {
            return s_host ??= CreateHost();
        }
    }

    /// <summary>Whether the MEF host is composed now.</summary>
    internal static bool HostComposed
    {
        get
        {
            lock (HostGate)
            {
                return s_host is not null;
            }
        }
    }

    /// <summary>
    /// Drops the MEF host (its export provider and the services it made): a workspace built after this composes it again.
    /// A workspace that still runs keeps the host it was built with until it is released.
    /// </summary>
    internal static void ReleaseHost()
    {
        lock (HostGate)
        {
            s_host = null;
        }
    }

    /// <summary>
    /// The memory-pressure call, from any thread without blocking it: every open workspace drops its solution before its
    /// next request (or on the thread pool, whichever comes first), with the shared references. The MEF host stays: it
    /// holds some 2 MB and composing it again costs 0.3-0.6 s (measured, "Measured (E5)" in the plan).
    /// </summary>
    public static void RequestReleaseAll()
    {
        CSharpWorkspace[] all;
        lock (RegistryGate)
        {
            all = [.. Open.Values];
        }

        foreach (var workspace in all)
        {
            workspace.RequestRelease();
        }

        References.Clear();
        Workspaces.ReferenceAssemblies.Clear();
    }

    /// <summary>Releases every open workspace and the shared references (the MEF host stays; <see cref="ReleaseHost"/> drops it).</summary>
    public static void ReleaseAll()
    {
        CSharpWorkspace[] all;
        lock (RegistryGate)
        {
            all = [.. Open.Values];
        }

        foreach (var workspace in all)
        {
            workspace.Release();
        }
    }

    /// <summary>A user leaves without blocking its thread (the session's close): <see cref="Leave"/> awaiting the gate.</summary>
    public async Task LeaveAsync(object owner)
    {
        await _mutate.WaitAsync().ConfigureAwait(false);
        try
        {
            DetachHeld(owner);
        }
        finally
        {
            _mutate.Release();
        }

        if (LeaveRegistry())
        {
            await _mutate.WaitAsync().ConfigureAwait(false);
            try
            {
                ReleaseHeld();
            }
            finally
            {
                _mutate.Release();
            }

            References.Clear();
            Workspaces.ReferenceAssemblies.Clear();
        }
    }

    // The user count down; true when this was the last (the workspace goes from the registry).
    private bool LeaveRegistry()
    {
        lock (RegistryGate)
        {
            if (--_users > 0)
            {
                return false;
            }

            Open.Remove(_key);
            return true;
        }
    }

    /// <summary>A user leaves; the last one disposes the workspace. It waits for a running build (the tests and the bench; the app's sessions use <see cref="LeaveAsync"/>).</summary>
    public void Leave(object owner)
    {
        Detach(owner);
        lock (RegistryGate)
        {
            if (--_users > 0)
            {
                return;
            }

            Open.Remove(_key);
        }

        Release();
    }

    /// <summary>Drops the solution and the workspace (the next request builds them again), and the shared references.</summary>
    public void Release()
    {
        _mutate.Wait();
        try
        {
            ReleaseHeld();
        }
        finally
        {
            _mutate.Release();
        }

        References.Clear();
        Workspaces.ReferenceAssemblies.Clear();
    }

    /// <summary>
    /// Asks for a release from any thread without waiting: the next request (or the thread pool, whichever comes first)
    /// drops the solution before it goes on, so a request made after this call never sees the old one.
    /// </summary>
    public void RequestRelease()
    {
        Volatile.Write(ref _releaseRequested, 1);
        _ = Task.Run(async () =>
        {
            await _mutate.WaitAsync().ConfigureAwait(false);       // a pool thread is never held while a build runs
            try
            {
                ReleaseIfRequested();
            }
            finally
            {
                _mutate.Release();
            }
        });
    }

    /// <summary>Called holding the mutation gate: performs an asked-for release.</summary>
    private void ReleaseIfRequested()
    {
        if (Interlocked.Exchange(ref _releaseRequested, 0) == 1)
        {
            ReleaseHeld();
            References.Clear();
            Workspaces.ReferenceAssemblies.Clear();
        }
    }

    /// <summary>What it holds now.</summary>
    public SemanticTelemetry Telemetry(TimeSpan lastDiagnostics)
    {
        var solution = Solution;
        if (solution is null)
        {
            return SemanticTelemetry.None with { WorkspaceRoot = _root, LastDiagnosticsTime = lastDiagnostics, Generation = Generation };
        }

        int documents = 0;
        var references = new HashSet<MetadataReference>();
        foreach (var project in solution.Projects)
        {
            documents += project.DocumentIds.Count;
            references.UnionWith(project.MetadataReferences);
        }

        return new SemanticTelemetry(_root, _reader, solution.ProjectIds.Count, documents, references.Count, Volatile.Read(ref _referenceBytes),
            Volatile.Read(ref _heapBytes), _buildTime, lastDiagnostics, Generation);
    }

    /// <summary>
    /// The document of <paramref name="owner"/> (a session; its file <paramref name="path"/>, null for a new document)
    /// with <paramref name="text"/> as its text (null: the text it has). The solution with that text becomes the current
    /// one when <paramref name="keep"/>; else only the answer holds it (a request on an older snapshot). Builds the
    /// workspace first when it is not built. Null when no reader gives the file a project.
    /// </summary>
    public async Task<(Document Document, int Generation)?> DocumentAsync(object owner, string? path, SourceText? text, bool keep, CancellationToken cancellationToken)
    {
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The build itself is not cancelled by a request's token (a keystroke would never let it finish).
            ReleaseIfRequested();
            if (_solution is null)
            {
                Build(path);
            }

            var id = Resolve(owner, path, text);
            if (id is null)
            {
                return null;
            }

            var solution = _solution!;
            if (text is not null)
            {
                solution = solution.WithDocumentText(id, text, PreservationMode.PreserveIdentity);
                if (keep)
                {
                    Volatile.Write(ref _solution, solution);
                }
            }

            return solution.GetDocument(id) is { } document ? (document, _generation) : null;
        }
        finally
        {
            _mutate.Release();
        }
    }

    /// <summary>Builds the projects of the folder (the bench's cold and warm workspace creation): every project the readers find for <paramref name="path"/>.</summary>
    public async Task<Solution> BuildAsync(string? path, CancellationToken cancellationToken)
    {
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReleaseIfRequested();
            if (_solution is null)
            {
                Build(path);
            }

            return _solution!;
        }
        finally
        {
            _mutate.Release();
        }
    }

    /// <summary>The owner's document goes back to the file's text on disk (or out, for a document of no project file).</summary>
    private void Detach(object owner)
    {
        _mutate.Wait();
        try
        {
            DetachHeld(owner);
        }
        finally
        {
            _mutate.Release();
        }
    }

    // Called holding the mutation gate.
    private void DetachHeld(object owner)
    {
        {
            if (!_owners.Remove(owner, out var id) || _solution is not { } solution || solution.GetDocument(id) is not { } document)
            {
                return;
            }

            bool madeUp = _madeUp.ContainsValue(document.Project.Id);
            if (madeUp || document.FilePath is null || !File.Exists(document.FilePath))
            {
                Volatile.Write(ref _solution, solution.RemoveDocument(id));
            }
            else
            {
                Volatile.Write(ref _solution, solution.WithDocumentTextLoader(id, new FileTextLoader(document.FilePath, null), PreservationMode.PreserveValue));
            }
        }
    }

    private void ReleaseHeld()
    {
        _owners.Clear();
        _projectFiles.Clear();
        _madeUp.Clear();
        Volatile.Write(ref _solution, null);
        _workspace?.Dispose();
        _workspace = null;
        _reader = null;
    }

    private void Build(string? path)
    {
        // Phase U: the build shows in the status bar (the readers, the projects, the references), however it ends.
        string name = _root is null ? "loose files" : Path.GetFileName(_root);
        using var activity = _activities.Start($"C#: building the workspace of {name}", ActivityKinds.Build);
        activity.Report(null, "reading the projects (" + string.Join(", ", _settings.WorkspaceReaders) + ")");
        try
        {
            long start = Stopwatch.GetTimestamp();
            long heap = GC.GetTotalMemory(false);
            _workspace = new AdhocWorkspace(HostServices());
            Volatile.Write(ref _solution, _workspace.CurrentSolution);
            Volatile.Write(ref _referenceBytes, 0);
            _referenced.Clear();
            Interlocked.Increment(ref _generation);
            AddProjectsFor(path);
            _buildTime = Stopwatch.GetElapsedTime(start);
            Volatile.Write(ref _heapBytes, Math.Max(0, GC.GetTotalMemory(false) - heap));
            var built = Volatile.Read(ref _solution);
            activity.Complete(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{built?.ProjectIds.Count ?? 0} projects, {_referenced.Count} references, by {_reader ?? "no reader"}"));
        }
        catch (Exception error)
        {
            activity.Fail("the workspace could not be built: " + error.Message, error);
            throw;
        }
    }

    /// <summary>The owner's document id: known, found by its path, or added by the readers (a made-up project takes a file none has).</summary>
    private DocumentId? Resolve(object owner, string? path, SourceText? text)
    {
        if (_owners.TryGetValue(owner, out var known) && _solution!.ContainsDocument(known))
        {
            return known;
        }

        DocumentId? id = path is null ? null : Find(path);
        if (id is null && path is not null)
        {
            AddProjectsFor(path);
            id = Find(path);
        }

        if (id is null)
        {
            // In no project: a project the readers made up (loose files) takes it, as a document of its own.
            if (_madeUp.Count == 0)
            {
                AddProjectsFor(path, madeUpOnly: true);
            }

            if (_madeUp.Values.FirstOrDefault() is { } project)
            {
                id = DocumentId.CreateNewId(project, path ?? "untitled");
                var name = path is null ? "untitled.cs" : Path.GetFileName(path);
                var initial = text ?? SourceText.From("");
                var solution = _solution!.AddDocument(DocumentInfo.Create(id, name, filePath: path,
                    loader: TextLoader.From(TextAndVersion.Create(initial, VersionStamp.Create(), path ?? name))));
                Volatile.Write(ref _solution, solution);
            }
        }

        if (id is not null)
        {
            _owners[owner] = id;
        }

        return id;
    }

    private DocumentId? Find(string path)
    {
        var ids = _solution!.GetDocumentIdsWithFilePath(path);
        return ids.IsDefaultOrEmpty ? null : ids[0];
    }

    /// <summary>Runs the readers in order for <paramref name="path"/>; adds the projects of the first that answers (or of the first made-up one).</summary>
    private void AddProjectsFor(string? path, bool madeUpOnly = false)
    {
        var request = new WorkspaceReadRequest(_root, path, _settings);
        foreach (var name in _settings.WorkspaceReaders)
        {
            var model = WorkspaceReaders.Get(name).Read(request, CancellationToken.None);
            if (model is null || (madeUpOnly && model.Projects.All(p => p.ProjectFile is not null)))
            {
                continue;
            }

            _reader ??= model.Reader;
            Add(model, request.FilePath);
            return;
        }
    }

    private void Add(WorkspaceModel model, string? path)
    {
        var solution = _solution!;
        foreach (var project in model.Projects)
        {
            if (project.ProjectFile is { } file ? _projectFiles.ContainsKey(file) : _madeUp.ContainsKey(model.Reader + "|" + project.Name))
            {
                continue;
            }

            var id = ProjectId.CreateNewId(project.Name);
            if (project.ProjectFile is { } projectFile)
            {
                _projectFiles[projectFile] = id;
            }
            else
            {
                _madeUp[model.Reader + "|" + project.Name] = id;
            }

            solution = solution.AddProject(Info(id, project, path));
        }

        Volatile.Write(ref _solution, solution);
    }

    private ProjectInfo Info(ProjectId id, WorkspaceProject project, string? openPath)
    {
        var parse = new CSharpParseOptions(
            LanguageVersionOf(project.LanguageVersion),
            project.DocumentationDiagnostics ? DocumentationMode.Diagnose : DocumentationMode.Parse,
            SourceCodeKind.Regular,
            project.DefineConstants);
        var specific = new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal);
        foreach (var id2 in project.NoWarn)
        {
            specific[id2] = ReportDiagnostic.Suppress;
        }

        var compilation = new CSharpCompilationOptions(
            project.IsExecutable ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: project.AllowUnsafe,
            nullableContextOptions: NullableOf(project.Nullable),
            warningLevel: project.WarningLevel ?? 4,
            generalDiagnosticOption: project.TreatWarningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default,
            specificDiagnosticOptions: specific);

        var documents = new List<DocumentInfo>(project.SourceFiles.Count + 1);
        foreach (var file in project.SourceFiles)
        {
            if (project.ProjectFile is null && openPath is not null && Workspaces.CsprojWorkspaceReader.PathComparer.Equals(file, openPath))
            {
                // A made-up project's file is added as the owner's document by Resolve, with its live text.
                continue;
            }

            documents.Add(DocumentInfo.Create(DocumentId.CreateNewId(id, file), Path.GetFileName(file), filePath: file, loader: new FileTextLoader(file, null)));
        }

        if (Generated(project) is { } generated)
        {
            var generatedPath = Path.Combine(project.Folder, "obj", "Craft.GlobalUsings.g.cs");
            documents.Add(DocumentInfo.Create(DocumentId.CreateNewId(id, generatedPath), "Craft.GlobalUsings.g.cs", filePath: generatedPath,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(generated), VersionStamp.Create(), generatedPath)), isGenerated: true));
        }

        var references = new List<MetadataReference>(project.References.Count);
        foreach (var file in project.References)
        {
            if (Reference(file) is { } reference)
            {
                references.Add(reference);
            }
        }

        var projectReferences = new List<ProjectReference>();
        foreach (var referenced in project.ProjectReferences)
        {
            if (_projectFiles.TryGetValue(referenced, out var referencedId))
            {
                projectReferences.Add(new ProjectReference(referencedId));
            }
        }

        // The analyzers' source generators run in the compilation (Roslyn runs every generator of a project's analyzer
        // references when it makes the compilation); their diagnostic analyzers are never run, so only the generator part
        // of each counts. The same reference per assembly for every project and workspace (AnalyzerLoader, per process).
        var analyzers = new List<AnalyzerReference>();
        if (_settings.SourceGenerators)
        {
            foreach (var file in project.Analyzers)
            {
                if (AnalyzerLoader.Reference(file) is { } analyzer)
                {
                    analyzers.Add(analyzer);
                }
            }
        }

        return ProjectInfo.Create(id, VersionStamp.Create(), project.Name, project.Name, LanguageNames.CSharp, filePath: project.ProjectFile,
            compilationOptions: compilation, parseOptions: parse, documents: documents, projectReferences: projectReferences, metadataReferences: references,
            analyzerReferences: analyzers);
    }

    /// <summary>The project's global usings and InternalsVisibleTo attributes, as the SDK generates them into obj.</summary>
    private static string? Generated(WorkspaceProject project)
    {
        if (project.GlobalUsings.Count == 0 && project.InternalsVisibleTo.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder("// <auto-generated/>\n");
        foreach (var entry in project.GlobalUsings)
        {
            if (entry.StartsWith("static ", StringComparison.Ordinal))
            {
                text.Append("global using static global::").Append(entry.AsSpan(7)).Append(";\n");
            }
            else if (entry.IndexOf(" = ", StringComparison.Ordinal) is var eq and > 0)
            {
                text.Append("global using ").Append(entry.AsSpan(0, eq)).Append(" = global::").Append(entry.AsSpan(eq + 3)).Append(";\n");
            }
            else
            {
                text.Append("global using global::").Append(entry).Append(";\n");
            }
        }

        foreach (var friend in project.InternalsVisibleTo)
        {
            text.Append("[assembly: global::System.Runtime.CompilerServices.InternalsVisibleTo(\"").Append(friend).Append("\")]\n");
        }

        return text.ToString();
    }

    private PortableExecutableReference? Reference(string file)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(file);
            if (References.TryGetValue(file, out var cached) && cached.Written == written)
            {
                if (_referenced.Add(file))
                {
                    Interlocked.Add(ref _referenceBytes, cached.Bytes);
                }

                return cached.Reference;
            }

            var xml = Path.ChangeExtension(file, ".xml");
            var reference = MetadataReference.CreateFromFile(file, documentation: File.Exists(xml) ? XmlDocumentationProvider.CreateFromFile(xml) : null);
            long bytes = new FileInfo(file).Length;
            References[file] = (written, reference, bytes);
            if (_referenced.Add(file))
            {
                Interlocked.Add(ref _referenceBytes, bytes);
            }

            return reference;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return null;
        }
    }

    private static LanguageVersion LanguageVersionOf(string? text) =>
        text is not null && LanguageVersionFacts.TryParse(text, out var version) ? version : LanguageVersion.Default;

    private static NullableContextOptions NullableOf(string? text) => text?.ToUpperInvariant() switch
    {
        "ENABLE" or "TRUE" => NullableContextOptions.Enable,
        "WARNINGS" => NullableContextOptions.Warnings,
        "ANNOTATIONS" => NullableContextOptions.Annotations,
        _ => NullableContextOptions.Disable,
    };

    /// <summary>
    /// Roslyn's MEF host, composed once per process from the plug-in's own Roslyn assemblies: they are loaded through
    /// this assembly's load context (the plug-in's, when the app loaded it from its folder), never the host's.
    /// </summary>
    private static MefHostServices CreateHost()
    {
        var context = AssemblyLoadContext.GetLoadContext(typeof(CSharpWorkspace).Assembly) ?? AssemblyLoadContext.Default;
        var assemblies = new List<Assembly> { typeof(Workspace).Assembly, typeof(CSharpSyntaxTree).Assembly };
        foreach (var name in (ReadOnlySpan<string>)["Microsoft.CodeAnalysis.CSharp.Workspaces", "Microsoft.CodeAnalysis.Features", "Microsoft.CodeAnalysis.CSharp.Features"])
        {
            assemblies.Add(context.LoadFromAssemblyName(new AssemblyName(name)));
        }

        return MefHostServices.Create(assemblies);
    }
}
