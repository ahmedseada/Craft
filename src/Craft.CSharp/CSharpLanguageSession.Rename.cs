// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Rename;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Craft.CSharp;

/// <summary>
/// F2 rename (the X restart's piece 3b): the symbol at a position renamed across the workspace by Roslyn's
/// <see cref="Renamer"/>, as edits only (nothing is written here: the editor proposes them as one diff the user accepts).
/// </summary>
public sealed partial class CSharpLanguageSession
{
    /// <inheritdoc />
    public Task<RenameTarget?> PrepareRenameAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken)
    {
        Validate(snapshot, position);
        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpRename.PrepareAsync(document, position, cancellationToken).ConfigureAwait(false)
            : null, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The workspace's solution with every open document at its unsaved text (this one brought to <paramref name="snapshot"/>);
    /// each changed file's edits are in the text the solution holds for it, with that text's version when it is an open
    /// document's. A script has no workspace here yet: refused, said.
    /// </remarks>
    public Task<WorkspaceEdit> RenameAsync(ITextSnapshot snapshot, int position, string newName, CancellationToken cancellationToken)
    {
        Validate(snapshot, position);
        ArgumentNullException.ThrowIfNull(newName);
        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpRename.RenameAsync(document, position, newName, cancellationToken).ConfigureAwait(false)
            : WorkspaceEdit.Refused("A C# script (.csx) has no workspace to rename in yet."), cancellationToken);
    }
}

/// <summary>Roslyn's rename for the session: the target, the name's check, the edits per file and the conflicts.</summary>
internal static class CSharpRename
{
    private const int ConflictsShown = 10;

    /// <summary>The name at <paramref name="position"/> and whether its symbol can be renamed; null when there is no symbol there.</summary>
    public static async Task<RenameTarget?> PrepareAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var (symbol, span) = await CSharpSemantics.SymbolAtAsync(document, position, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        string written = text.ToString(span);
        return new RenameTarget(span.Start, span.Length, written, Refusal(Definition(symbol), written));
    }

    /// <summary>The edits that rename the symbol at <paramref name="position"/> to <paramref name="newName"/>, with the conflicts they make.</summary>
    public static async Task<WorkspaceEdit> RenameAsync(Document document, int position, string newName, CancellationToken cancellationToken)
    {
        newName = newName.Trim();
        if (CheckName(newName) is { } bad)
        {
            return WorkspaceEdit.Refused(bad);
        }

        var (found, span) = await CSharpSemantics.SymbolAtAsync(document, position, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return WorkspaceEdit.Refused("There is no symbol at the caret to rename.");
        }

        var symbol = Definition(found);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (Refusal(symbol, text.ToString(span)) is { } refusal)
        {
            return WorkspaceEdit.Refused(refusal);
        }

        if (string.Equals(symbol.Name, newName.TrimStart('@'), StringComparison.Ordinal))
        {
            return WorkspaceEdit.Empty;
        }

        var solution = document.Project.Solution;
        var renamed = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(), newName, cancellationToken).ConfigureAwait(false);
        var files = new List<WorkspaceFileEdit>();
        var conflicts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in renamed.GetChanges(solution).GetProjectChanges())
        {
            foreach (var id in project.GetChangedDocuments(onlyGetDocumentsWithTextChanges: true))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (solution.GetDocument(id) is not { FilePath: { Length: > 0 } path } before || renamed.GetDocument(id) is not { } after || !seen.Add(path))
                {
                    continue;                                    // a generated document, or a file linked into a second project
                }

                var changes = await after.GetTextChangesAsync(before, cancellationToken).ConfigureAwait(false);
                var edits = new List<TextEdit>();
                foreach (var change in changes.OrderBy(c => c.Span.Start))
                {
                    edits.Add(new TextEdit(change.Span.Start, change.Span.Length, change.NewText ?? ""));
                }

                if (edits.Count == 0)
                {
                    continue;
                }

                var beforeText = await before.GetTextAsync(cancellationToken).ConfigureAwait(false);
                int? version = beforeText is SnapshotSourceText open ? open.Snapshot.Version : null;
                files.Add(new WorkspaceFileEdit(path, edits, version));
                if (conflicts.Count < ConflictsShown)
                {
                    await AddConflictsAsync(before, after, path, conflicts, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        files.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return new WorkspaceEdit(files) { Conflicts = conflicts };
    }

    /// <summary>Why <paramref name="name"/> is not a C# name, or null when it is one (a keyword takes '@' first).</summary>
    internal static string? CheckName(string name)
    {
        if (name.Length == 0)
        {
            return "The new name is empty.";
        }

        bool verbatim = name[0] == '@';
        string bare = verbatim ? name[1..] : name;
        if (!SyntaxFacts.IsValidIdentifier(bare))
        {
            return $"'{name}' is not a C# name: it takes letters, digits and '_', and does not start with a digit.";
        }

        if (!verbatim && SyntaxFacts.GetKeywordKind(bare) != SyntaxKind.None)
        {
            return $"'{name}' is a C# keyword; write @{name} to use it as a name.";
        }

        return null;
    }

    // The symbol a rename changes: an extension method's declaration, a generic's definition.
    private static ISymbol Definition(ISymbol symbol) => (symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol).OriginalDefinition;

    // Why the symbol cannot be renamed, in words, or null.
    private static string? Refusal(ISymbol symbol, string written)
    {
        if (SyntaxFacts.GetKeywordKind(written) != SyntaxKind.None)
        {
            return $"'{written}' is a keyword ({symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)}); it cannot be renamed here.";
        }

        if (!symbol.Locations.Any(static l => l.IsInSource))
        {
            string where = symbol.ContainingAssembly?.Identity.Name ?? "a referenced assembly";
            return $"{symbol.Name} is defined in {where}, not in the workspace; only the workspace's own symbols can be renamed.";
        }

        if (symbol.IsImplicitlyDeclared)
        {
            return $"{symbol.Name} is declared by the compiler, not written in the code; rename what declares it.";
        }

        return null;
    }

    // The errors the renamed file has that it did not have before (a name that clashes, a reference bound elsewhere).
    private static async Task AddConflictsAsync(Document before, Document after, string path, List<string> conflicts, CancellationToken cancellationToken)
    {
        var old = await before.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var now = await after.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (old is null || now is null)
        {
            return;
        }

        var had = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var diagnostic in old.GetDiagnostics(cancellationToken: cancellationToken))
        {
            if (diagnostic.Severity == RoslynSeverity.Error)
            {
                had[diagnostic.Id] = had.GetValueOrDefault(diagnostic.Id) + 1;
            }
        }

        foreach (var diagnostic in now.GetDiagnostics(cancellationToken: cancellationToken))
        {
            if (diagnostic.Severity != RoslynSeverity.Error)
            {
                continue;
            }

            if (had.TryGetValue(diagnostic.Id, out int left) && left > 0)
            {
                had[diagnostic.Id] = left - 1;                   // an error the file had already
                continue;
            }

            int line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
            conflicts.Add(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}:{line}: {diagnostic.Id} {diagnostic.GetMessage(CultureInfo.InvariantCulture)}"));
            if (conflicts.Count >= ConflictsShown)
            {
                return;
            }
        }
    }
}
