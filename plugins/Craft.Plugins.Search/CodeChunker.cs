// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Craft.Plugins.Search;

/// <summary>
/// One searchable piece of a file: a type (its declaration and the names of its members), a member (its whole text with its
/// doc comment), the file's top-level statements, or a window of lines of a file that is not C#. It keeps only what a hit
/// shows and its words (<see cref="Terms"/>, <see cref="CodeTerms"/>), never the source text: the file holds that.
/// </summary>
/// <param name="Kind">"class", "record", "struct", "interface", "enum", "delegate", "method", "constructor", "property", "field", "event", "indexer", "operator", "statements", "text".</param>
/// <param name="Name">Its name ("ParseAsync", "Shapes.Circle" for a nested type's own name "Circle").</param>
/// <param name="Container">Where it is declared ("Craft.Core.Documents.TextDocument"), or "".</param>
/// <param name="Line">Its declaration's line, 1-based (what a hit opens).</param>
/// <param name="EndLine">Its last line, 1-based.</param>
/// <param name="Terms">Its words: the file's path, its kind, container and name (twice: a name weighs more), then its text.</param>
public sealed record CodeChunk(string Kind, string Name, string Container, int Line, int EndLine, string Terms)
{
    /// <summary>"Container.Name", or the name.</summary>
    public string FullName => Container.Length > 0 ? Container + "." + Name : Name;
}

/// <summary>A chunk and the text an embedding model reads for it (made only when vectors are on, given away once embedded).</summary>
internal readonly record struct ChunkText(CodeChunk Chunk, string? Text);

/// <summary>
/// Cuts a file into <see cref="CodeChunk"/>s: C# by Roslyn's syntax (a parse of the file alone: no project, no semantic
/// model), each type and member its own chunk; any other text in windows of lines. The text an embedding model reads is
/// made only when asked for, cut to the chunk's first characters.
/// </summary>
internal static class CodeChunker
{
    private static readonly CSharpParseOptions Options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview).WithDocumentationMode(DocumentationMode.None);

    /// <summary>The chunks of <paramref name="text"/>, the file <paramref name="relativePath"/> ('/'-separated).</summary>
    public static List<ChunkText> Chunk(string relativePath, string text, SearchSettings settings, bool withText)
    {
        string extension = Path.GetExtension(relativePath);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) || extension.Equals(".csx", StringComparison.OrdinalIgnoreCase)
            ? CSharp(relativePath, text, settings, withText)
            : Lines(relativePath, text, settings, withText);
    }

    private static List<ChunkText> CSharp(string path, string text, SearchSettings settings, bool withText)
    {
        var source = SourceText.From(text);
        var options = path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase) ? Options.WithKind(SourceCodeKind.Script) : Options;
        var root = CSharpSyntaxTree.ParseText(source, options, path).GetCompilationUnitRoot();
        var chunks = new List<ChunkText>();
        var words = new StringBuilder();
        var statements = root.Members.OfType<GlobalStatementSyntax>().ToList();
        if (statements.Count > 0)
        {
            Add(chunks, words, path, source, "statements", "top-level statements", "", TextSpan.FromBounds(statements[0].SpanStart, statements[^1].Span.End), statements[0].SpanStart, settings, withText);
        }

        Walk(chunks, words, path, source, root.Members, "", settings, withText);
        if (chunks.Count == 0 && text.Length > 0)
        {
            // Usings, attributes or comments only: the file as one chunk, so its words still find it.
            Add(chunks, words, path, source, "file", Path.GetFileName(path), "", new TextSpan(0, text.Length), 0, settings, withText);
        }

        return chunks;
    }

    private static void Walk(List<ChunkText> chunks, StringBuilder words, string path, SourceText source, SyntaxList<MemberDeclarationSyntax> members, string container,
        SearchSettings settings, bool withText)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    Walk(chunks, words, path, source, ns.Members, Join(container, ns.Name.ToString()), settings, withText);
                    break;
                case EnumDeclarationSyntax e:
                    Add(chunks, words, path, source, "enum", e.Identifier.Text, container, Span(e), e.Identifier.SpanStart, settings, withText);
                    break;
                case TypeDeclarationSyntax type:
                {
                    string name = type.Identifier.Text + (type.TypeParameterList is { } parameters ? parameters.ToString() : "");
                    // The declaration to its open brace (attributes, doc comment, base list, a record's parameters), then its
                    // members' names: the type is found by what it holds, its members by their own chunks.
                    int headerEnd = type.OpenBraceToken.IsKind(SyntaxKind.None) || type.OpenBraceToken.IsMissing ? type.Span.End : type.OpenBraceToken.Span.End;
                    var header = TextSpan.FromBounds(Start(type), headerEnd);
                    string memberNames = string.Join(' ', type.Members.Select(NameOf).Where(n => n.Length > 0));
                    Add(chunks, words, path, source, Keyword(type), name, container, header, type.Identifier.SpanStart, settings, withText, memberNames, type.Span.End);
                    Walk(chunks, words, path, source, type.Members, Join(container, name), settings, withText);
                    break;
                }

                case DelegateDeclarationSyntax d:
                    Add(chunks, words, path, source, "delegate", d.Identifier.Text, container, Span(d), d.Identifier.SpanStart, settings, withText);
                    break;
                case GlobalStatementSyntax:
                    break;                                                             // the statements' own chunk
                default:
                    if (NameOf(member) is { Length: > 0 } memberName)
                    {
                        Add(chunks, words, path, source, MemberKind(member), memberName, container, Span(member), DeclarationStart(member), settings, withText);
                    }

                    break;
            }
        }
    }

    private static void Add(List<ChunkText> chunks, StringBuilder words, string path, SourceText source, string kind, string name, string container, TextSpan span,
        int declaration, SearchSettings settings, bool withText, string? extra = null, int end = -1)
    {
        int line = source.Lines.GetLineFromPosition(Math.Clamp(declaration, 0, source.Length)).LineNumber + 1;
        int endLine = source.Lines.GetLineFromPosition(Math.Clamp(end >= 0 ? end : span.End, 0, source.Length)).LineNumber + 1;
        var cut = new TextSpan(span.Start, Math.Min(span.Length, settings.ChunkMaxCharacters));
        string body = source.ToString(cut);
        words.Clear();
        CodeTerms.Append(words, Path.ChangeExtension(path, null));
        CodeTerms.Append(words, kind);
        CodeTerms.Append(words, container);
        CodeTerms.Append(words, name);
        CodeTerms.Append(words, name);
        CodeTerms.Append(words, body);
        if (extra is not null)
        {
            CodeTerms.Append(words, extra);
        }

        var chunk = new CodeChunk(kind, name, container, line, endLine, words.ToString());
        chunks.Add(new ChunkText(chunk, withText ? EmbeddingText(kind, chunk.FullName, path, body, extra, settings) : null));
    }

    // What the embedding model reads: where it is, what it is, then its text (cut to the setting).
    private static string EmbeddingText(string kind, string fullName, string path, string body, string? extra, SearchSettings settings)
    {
        var text = new StringBuilder(Math.Min(body.Length, settings.EmbeddingCharacters) + 128);
        text.Append(kind).Append(' ').Append(fullName).Append(" (").Append(path).Append(")\n");
        text.Append(body.AsSpan(0, Math.Min(body.Length, settings.EmbeddingCharacters)));
        if (extra is { Length: > 0 } && text.Length < settings.EmbeddingCharacters)
        {
            text.Append("\nmembers: ").Append(extra.AsSpan(0, Math.Min(extra.Length, settings.EmbeddingCharacters - text.Length)));
        }

        return text.ToString();
    }

    private static List<ChunkText> Lines(string path, string text, SearchSettings settings, bool withText)
    {
        var chunks = new List<ChunkText>();
        var words = new StringBuilder();
        int lineStart = 0, line = 1;
        while (lineStart < text.Length)
        {
            int windowStart = lineStart, firstLine = line;
            string? title = null;
            for (int n = 0; n < settings.TextChunkLines && lineStart < text.Length; n++)
            {
                int breakAt = text.IndexOf('\n', lineStart);
                int lineEnd = breakAt < 0 ? text.Length : breakAt;
                if (title is null && text.AsSpan(lineStart, lineEnd - lineStart).Trim() is { Length: > 0 } first)
                {
                    title = first.Length > 80 ? first[..80].ToString() + "…" : first.ToString();
                }

                lineStart = breakAt < 0 ? text.Length : breakAt + 1;
                line++;
            }

            var span = TextSpan.FromBounds(windowStart, lineStart);
            string body = text.Substring(span.Start, Math.Min(span.Length, settings.ChunkMaxCharacters));
            words.Clear();
            CodeTerms.Append(words, Path.ChangeExtension(path, null));
            CodeTerms.Append(words, body);
            var chunk = new CodeChunk("text", title ?? Path.GetFileName(path), "", firstLine, Math.Max(firstLine, line - 1), words.ToString());
            chunks.Add(new ChunkText(chunk, withText ? EmbeddingText("text", Path.GetFileName(path), path, body, null, settings) : null));
        }

        return chunks;
    }

    // A member's text with its doc comment or comments above it, not the blank lines before them.
    private static TextSpan Span(SyntaxNode node) => TextSpan.FromBounds(Start(node), node.Span.End);

    private static int Start(SyntaxNode node)
    {
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                return trivia.SpanStart;
            }
        }

        return node.SpanStart;
    }

    // Where a member's name is (what a hit opens).
    private static int DeclarationStart(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.SpanStart,
        ConstructorDeclarationSyntax c => c.Identifier.SpanStart,
        DestructorDeclarationSyntax d => d.Identifier.SpanStart,
        PropertyDeclarationSyntax p => p.Identifier.SpanStart,
        EventDeclarationSyntax e => e.Identifier.SpanStart,
        IndexerDeclarationSyntax i => i.ThisKeyword.SpanStart,
        OperatorDeclarationSyntax o => o.OperatorToken.SpanStart,
        BaseFieldDeclarationSyntax f when f.Declaration.Variables.Count > 0 => f.Declaration.Variables[0].Identifier.SpanStart,
        _ => member.SpanStart,
    };

    private static string NameOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        DestructorDeclarationSyntax d => "~" + d.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        OperatorDeclarationSyntax o => "operator " + o.OperatorToken.Text,
        ConversionOperatorDeclarationSyntax c => (c.ImplicitOrExplicitKeyword.Text) + " operator " + c.Type,
        BaseFieldDeclarationSyntax f => string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.Text)),
        BaseTypeDeclarationSyntax t => t.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        _ => "",
    };

    private static string MemberKind(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax => "method",
        ConstructorDeclarationSyntax => "constructor",
        DestructorDeclarationSyntax => "destructor",
        PropertyDeclarationSyntax => "property",
        EventDeclarationSyntax or EventFieldDeclarationSyntax => "event",
        IndexerDeclarationSyntax => "indexer",
        OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => "operator",
        FieldDeclarationSyntax f when f.Modifiers.Any(SyntaxKind.ConstKeyword) => "constant",
        FieldDeclarationSyntax => "field",
        _ => "member",
    };

    private static string Keyword(TypeDeclarationSyntax type) => type switch
    {
        RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
        ClassDeclarationSyntax => "class",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        _ => type.Keyword.Text,
    };

    private static string Join(string container, string name) => container.Length == 0 ? name : container + "." + name;
}
