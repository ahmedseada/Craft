// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// A diagnostic of a file by line and column (W, contract 6), as a tool reports it: lines and columns from 1, columns in
/// UTF-16 code units (as .NET and JavaScript count), the end exclusive. What a plug-in publishes with
/// <see cref="IEditorHost.PublishDiagnostics"/>, and what Problems lists for a closed file.
/// </summary>
/// <param name="Line">The first line, from 1.</param>
/// <param name="Column">The first column, from 1.</param>
/// <param name="EndLine">The last line, from 1.</param>
/// <param name="EndColumn">The column after the last character, from 1 (equal to <paramref name="Column"/> on its line for a point).</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Id">The tool's id ("no-unused-vars"), or "".</param>
/// <param name="Message">What is wrong.</param>
public readonly record struct FileDiagnostic(int Line, int Column, int EndLine, int EndColumn, DiagnosticSeverity Severity, string Id, string Message)
{
    /// <summary>A point at <paramref name="line"/> and <paramref name="column"/> (from 1).</summary>
    public static FileDiagnostic At(int line, int column, DiagnosticSeverity severity, string id, string message) => new(line, column, line, column, severity, id, message);

    /// <summary>
    /// This diagnostic placed in <paramref name="snapshot"/> (its start and length there, each clamped to the text: a
    /// line past the end is the last line, a column past a line's end is its end), with <paramref name="source"/> and
    /// <paramref name="id"/> as given.
    /// </summary>
    public EditorDiagnostic In(ITextSnapshot snapshot, string? source = null, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        int start = PositionIn(snapshot, Line, Column);
        int end = EndLine < Line || (EndLine == Line && EndColumn <= Column) ? start : Math.Max(start, PositionIn(snapshot, EndLine, EndColumn));
        return new EditorDiagnostic(start, end - start, Severity, id ?? Id, Message) { Source = source };
    }

    /// <summary><paramref name="diagnostic"/>, placed in <paramref name="snapshot"/>, by its line and column (its id and source kept as they are).</summary>
    public static FileDiagnostic Of(ITextSnapshot snapshot, EditorDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        int start = Math.Clamp(diagnostic.Start, 0, snapshot.Length);
        int end = Math.Clamp(diagnostic.End, start, snapshot.Length);
        int line = snapshot.GetLineIndex(start);
        int endLine = snapshot.GetLineIndex(end);
        return new FileDiagnostic(line + 1, start - snapshot.GetLine(line).Start + 1, endLine + 1, end - snapshot.GetLine(endLine).Start + 1,
            diagnostic.Severity, diagnostic.Id, diagnostic.Message);
    }

    /// <summary>The position of <paramref name="line"/> and <paramref name="column"/> (from 1) in <paramref name="snapshot"/>, clamped to the text.</summary>
    public static int PositionIn(ITextSnapshot snapshot, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.LineCount == 0)
        {
            return 0;
        }

        var text = snapshot.GetLine(Math.Clamp(line - 1, 0, snapshot.LineCount - 1));
        if (line > snapshot.LineCount)
        {
            return text.Start + text.Length;
        }

        return text.Start + Math.Clamp(column - 1, 0, text.Length);
    }
}

/// <summary>
/// What a linter found for a folder (W, contract 7): its project folder (where its configuration lives and it runs),
/// the configuration file, the tool's version, and why it cannot run there (or null when it can), with a note to say once
/// (a configuration format kept for compatibility, a deprecated tool).
/// </summary>
/// <param name="Linter">The linter's name.</param>
/// <param name="Folder">The project folder (a full path).</param>
/// <param name="Config">The configuration file (a full path), or null.</param>
/// <param name="Version">The tool's version as installed ("9.39.1"), or null when not known.</param>
/// <param name="Problem">Why it cannot run there (no install, a configuration it no longer reads), or null.</param>
public sealed record LinterProject(string Linter, string Folder, string? Config, string? Version, string? Problem)
{
    /// <summary>A note to say once per workspace ("TSLint is deprecated"), or null.</summary>
    public string? Note { get; init; }

    /// <summary>The variables its runs need (ESLint 9's <c>ESLINT_USE_FLAT_CONFIG=false</c> for an <c>.eslintrc</c>), or none.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>The program it runs (a full path to the tool's script), or null.</summary>
    public string? Program { get; init; }

    /// <summary>Whether it can run.</summary>
    public bool Ready => Problem is null;
}

/// <summary>
/// A lint run (<see cref="ILinter.LintAsync"/>): the project, the files (full paths; none: the whole project), and how it
/// runs: through <paramref name="Sandbox"/> (one of Idrak's <see cref="CommandSandboxes"/>, chosen by whoever drives the
/// linter, which may ask the user first), with <paramref name="Limits"/>, writing what it must into
/// <paramref name="ScratchFolder"/> (the user's data folder's, never the workspace).
/// </summary>
/// <param name="Project">Where it runs.</param>
/// <param name="Files">The files to lint, or none for the whole project.</param>
/// <param name="Sandbox">The sandbox every command goes through.</param>
/// <param name="Limits">The commands' limits (who asks, the time, the root).</param>
/// <param name="ScratchFolder">A folder the linter may write its output into.</param>
public sealed record LintRequest(LinterProject Project, IReadOnlyList<string> Files, ICommandSandbox Sandbox, CommandLimits Limits, string ScratchFolder);

/// <summary>
/// One fix a linter offers: a replacement in the text it linted (UTF-16 positions), what it does, and the diagnostic it
/// fixes. A fix is only ever a proposal: applied, it is a diff the user accepts, one undo step.
/// </summary>
/// <param name="Start">The first character replaced, in the text linted.</param>
/// <param name="Length">The characters replaced.</param>
/// <param name="Text">What replaces them.</param>
/// <param name="Title">What it does ("Fix: eslint(semi)", "Remove unused variable 'x'.").</param>
/// <param name="Diagnostic">The index of the diagnostic it fixes in its file's list, or -1.</param>
public sealed record LintFix(int Start, int Length, string Text, string Title, int Diagnostic)
{
    /// <summary>A suggestion (applied only on its own, by the user's choice), not an automatic fix.</summary>
    public bool Suggestion { get; init; }

    /// <summary>The end (exclusive).</summary>
    public int End => Start + Length;
}

/// <summary>A linted file: its path (a full path), its diagnostics by line and column, and its fixes.</summary>
/// <param name="Path">The file.</param>
/// <param name="Diagnostics">Its diagnostics, in the tool's order.</param>
/// <param name="Fixes">Its fixes and suggestions, in the positions of the text linted.</param>
public sealed record LintFileResult(string Path, IReadOnlyList<FileDiagnostic> Diagnostics, IReadOnlyList<LintFix> Fixes);

/// <summary>A lint run's result: each file it linted (the clean ones too, with none), or why it did not lint.</summary>
/// <param name="Files">The files linted.</param>
public sealed record LintResult(IReadOnlyList<LintFileResult> Files)
{
    /// <summary>Why the run gave no result (refused, a configuration error, the tool's own failure), or null.</summary>
    public string? Problem { get; init; }

    /// <summary>A run that did not lint, and why.</summary>
    public static LintResult Failed(string problem) => new([]) { Problem = problem };
}

/// <summary>
/// A linter (W, contract 7) in <see cref="Linters"/>: the languages and file extensions it lints, why it cannot run on
/// this machine, how it finds its project by configuration files (off the UI thread), and a run that returns diagnostics
/// plus fixes. Whoever drives it (the lint plug-in) chooses the sandbox, publishes the diagnostics
/// (<see cref="IEditorHost.PublishDiagnostics"/>) and proposes the fixes as diffs (<see cref="IEditorHost.ProposeAsync"/>).
/// </summary>
public interface ILinter : IKind
{
    /// <summary>Its name as people read it ("ESLint").</summary>
    string Title { get; }

    /// <summary>The languages it lints, by name ("javascript", "typescript").</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>The file extensions it lints (".js", ".ts"), ignoring case.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Why it cannot run on this machine at all (Android: no Node), or null.</summary>
    string? Unavailable { get; }

    /// <summary>
    /// The project <paramref name="path"/> (a file or a folder) belongs to, found by its configuration files from there up
    /// to <paramref name="workspaceRoot"/>, or null when the linter has no configuration there. Reads files: call it off
    /// the UI thread.
    /// </summary>
    Task<LinterProject?> DetectAsync(string path, string workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>Lints <paramref name="request"/>'s files (or its whole project) through its sandbox; off the UI thread.</summary>
    Task<LintResult> LintAsync(LintRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The linters (W, contract 7), by name ignoring case, in registration order. Craft's library registers none (the lint
/// plug-in registers "eslint" and "tslint"). A name that is not registered fails with "not registered".
/// </summary>
public static class Linters
{
    private static readonly KindTable<ILinter> Table = new(nameof(Linters), "Linter", "add it with Linters.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "each linter finds its own projects; none stands in for another");

    /// <summary>Registers <paramref name="linter"/> under its name.</summary>
    public static void Register(ILinter linter)
    {
        ArgumentNullException.ThrowIfNull(linter);
        Table.Register(linter, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the linter of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered linters, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<ILinter> All => Table.Kinds;

    /// <summary>The linter of that name, or null.</summary>
    public static ILinter? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The linter of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static ILinter Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The registered linters that lint the file <paramref name="path"/> by its extension, in order (nothing read).</summary>
    public static IReadOnlyList<ILinter> ForFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path.AsSpan());
        var found = new List<ILinter>();
        foreach (var linter in Table.Kinds)
        {
            foreach (var each in linter.Extensions)
            {
                if (extension.Equals(each, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(linter);
                    break;
                }
            }
        }

        return found;
    }
}

/// <summary>Whether a document formatter formats a file here, and in words what it is ("Prettier 3.6.2, the project's (.prettierrc.json)").</summary>
/// <param name="Applies">Whether it formats the file.</param>
/// <param name="Description">What it is when it applies; why not when it does not.</param>
public sealed record DocumentFormatterFit(bool Applies, string Description);

/// <summary>What a document formatter formats: the file, the text (a snapshot), the workspace, the editor's indentation, and whether the user asked.</summary>
/// <param name="FilePath">The file (a full path).</param>
/// <param name="Snapshot">The text to format.</param>
/// <param name="WorkspaceRoot">The open folder, or null.</param>
public sealed record DocumentFormatRequest(string FilePath, ITextSnapshot Snapshot, string? WorkspaceRoot)
{
    /// <summary>The columns of an indentation step (the editor's tab size).</summary>
    public int TabSize { get; init; } = 4;

    /// <summary>Whether to indent with spaces.</summary>
    public bool InsertSpaces { get; init; } = true;

    /// <summary>True when nobody asked just now (format on save), false for the user's Format Document.</summary>
    public bool Automatic { get; init; }
}

/// <summary>
/// A document formatter (W, contract 8) in <see cref="DocumentFormatters"/>: a project's own formatter (Prettier), asked
/// before the language session's own when it formats the file's language and the project's configuration says so.
/// </summary>
public interface IDocumentFormatter : IKind
{
    /// <summary>Its name as people read it ("Prettier").</summary>
    string Title { get; }

    /// <summary>The languages it formats, by name.</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>Whether it formats files like <paramref name="filePath"/> of <paramref name="languageName"/> at all (by name; nothing read, any thread).</summary>
    bool Formats(string filePath, string languageName);

    /// <summary>What it last found for <paramref name="filePath"/>'s folder, without reading anything (the status bar's tooltip), or null.</summary>
    DocumentFormatterFit? Cached(string filePath) => null;

    /// <summary>Whether it formats <paramref name="filePath"/> in this project (its install, its configuration); reads files once per folder: off the UI thread.</summary>
    Task<DocumentFormatterFit> FitAsync(string filePath, string? workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// The request's text formatted (the whole text; the editor turns it into one edit per changed run of lines, one undo
    /// step). Off the UI thread.
    /// </summary>
    /// <exception cref="FormatException">The text cannot be formatted (a syntax error), with where and why.</exception>
    Task<string> FormatAsync(DocumentFormatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The document formatters (W, contract 8), by name ignoring case, in registration order. Craft's library registers none
/// (the lint plug-in registers "prettier"). A name that is not registered fails with "not registered".
/// </summary>
public static class DocumentFormatters
{
    private static readonly KindTable<IDocumentFormatter> Table = new(nameof(DocumentFormatters), "Document formatter", "add it with DocumentFormatters.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a formatter applies where its project says so; the language's own formatter is the fallback");

    /// <summary>Registers <paramref name="formatter"/> under its name.</summary>
    public static void Register(IDocumentFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Table.Register(formatter, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the formatter of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered formatters, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IDocumentFormatter> All => Table.Kinds;

    /// <summary>The formatter of that name, or null.</summary>
    public static IDocumentFormatter? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The formatter of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IDocumentFormatter Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>Whether a registered formatter formats files like <paramref name="filePath"/> of <paramref name="languageName"/> (nothing read).</summary>
    public static bool Handles(string filePath, string languageName)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        foreach (var formatter in Table.Kinds)
        {
            if (formatter.Formats(filePath, languageName ?? ""))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The first registered formatter that formats <paramref name="filePath"/> here (asked in order, off the UI thread),
    /// with its description; else null and why none does (each one's reason), or null and "" when none formats the
    /// language. A formatter that throws is passed over, named in the reason.
    /// </summary>
    public static async Task<(IDocumentFormatter? Formatter, string Description)> ChooseAsync(string filePath, string languageName, string? workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var reasons = new List<string>();
        foreach (var formatter in Table.Kinds)
        {
            if (!formatter.Formats(filePath, languageName ?? ""))
            {
                continue;
            }

            try
            {
                var fit = await formatter.FitAsync(filePath, workspaceRoot, cancellationToken).ConfigureAwait(false);
                if (fit.Applies)
                {
                    return (formatter, fit.Description);
                }

                reasons.Add(fit.Description);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                reasons.Add($"{formatter.Title}: {e.Message}");
            }
        }

        return (null, string.Join("; ", reasons));
    }

    /// <summary>What formats <paramref name="filePath"/> as far as the formatters know without reading (the first that applies), or null.</summary>
    public static DocumentFormatterFit? Describe(string filePath, string languageName)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        foreach (var formatter in Table.Kinds)
        {
            if (formatter.Formats(filePath, languageName ?? "") && formatter.Cached(filePath) is { Applies: true } fit)
            {
                return fit;
            }
        }

        return null;
    }
}
