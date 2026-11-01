// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Collections.Frozen;
using Craft.Abstractions;

namespace Craft.CSharp;

/// <summary>
/// The folding ranges of MSBuild, XML and XAML files (phase X): each element whose start and end tags are on different
/// lines (from its start tag's line to its end tag's), a start tag over several lines (many attributes), and comments
/// over several lines. One pass over the lines, a state carried from line to line (no parse, no copy of the text).
/// </summary>
public sealed class MarkupFolding : IFoldingRangeProvider
{
    /// <summary>Its name in <see cref="FoldingRangeProviders"/>.</summary>
    public const string ProviderName = "markup";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "MSBuild, XML and XAML: elements over several lines, long start tags and comments";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = [ProjectLanguages.MsBuild, ProjectLanguages.Xml, ProjectLanguages.Xaml];

    /// <inheritdoc />
    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult<IReadOnlyList<FoldingRange>>(Ranges(request.Snapshot, cancellationToken));
    }

    /// <summary>The ranges of <paramref name="snapshot"/>.</summary>
    public static List<FoldingRange> Ranges(ITextSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ranges = new List<FoldingRange>();
        var open = new Stack<int>();
        var state = State.Text;
        int markStart = 0;
        char quote = '\0';
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var text = LineText.Read(snapshot, index, ref buffer);
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (state)
                    {
                        case State.Text when c == '<':
                            var rest = text[(i + 1)..];
                            markStart = index;
                            if (rest.StartsWith("!--"))
                            {
                                state = State.Comment;
                                i += 3;
                            }
                            else if (rest.StartsWith("![CDATA["))
                            {
                                state = State.CData;
                                i += 8;
                            }
                            else if (rest.StartsWith("?"))
                            {
                                state = State.Instruction;
                            }
                            else if (rest.StartsWith("!"))
                            {
                                state = State.Declaration;
                            }
                            else if (rest.StartsWith("/"))
                            {
                                state = State.EndTag;
                                i++;
                            }
                            else
                            {
                                state = State.StartTag;
                            }

                            break;
                        case State.Comment when c == '-' && text[i..].StartsWith("-->"):
                            if (index > markStart)
                            {
                                ranges.Add(new FoldingRange(markStart, index, FoldingRangeKind.Comment));
                            }

                            i += 2;
                            state = State.Text;
                            break;
                        case State.CData when c == ']' && text[i..].StartsWith("]]>"):
                            i += 2;
                            state = State.Text;
                            break;
                        case State.Instruction when c == '?' && text[i..].StartsWith("?>"):
                            i++;
                            state = State.Text;
                            break;
                        case State.Declaration when c == '>':
                            state = State.Text;
                            break;
                        case State.StartTag when quote != '\0':
                            if (c == quote)
                            {
                                quote = '\0';
                            }

                            break;
                        case State.StartTag when c is '"' or '\'':
                            quote = c;
                            break;
                        case State.StartTag when c == '/' && i + 1 < text.Length && text[i + 1] == '>':
                            if (index > markStart)
                            {
                                ranges.Add(new FoldingRange(markStart, index));
                            }

                            i++;
                            state = State.Text;
                            break;
                        case State.StartTag when c == '>':
                            open.Push(markStart);
                            state = State.Text;
                            break;
                        case State.EndTag when c == '>':
                            if (open.Count > 0)
                            {
                                int first = open.Pop();
                                if (index > first)
                                {
                                    ranges.Add(new FoldingRange(first, index));
                                }
                            }

                            state = State.Text;
                            break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return ranges;
    }

    private enum State
    {
        Text,
        Comment,
        CData,
        Instruction,
        Declaration,
        StartTag,
        EndTag,
    }
}

/// <summary>A line of a snapshot read into a pooled buffer (grown as needed), without its line break.</summary>
internal static class LineText
{
    public static ReadOnlySpan<char> Read(ITextSnapshot snapshot, int index, ref char[] buffer)
    {
        var line = snapshot.GetLine(index);
        if (buffer.Length < line.Length)
        {
            ArrayPool<char>.Shared.Return(buffer);
            buffer = ArrayPool<char>.Shared.Rent(line.Length);
        }

        var span = buffer.AsSpan(0, line.Length);
        snapshot.CopyTo(line.Start, span);
        return span;
    }
}

/// <summary>
/// Razor's folding ranges (.razor, .cshtml; X part 5): each element over several lines (matched by its name, HTML's void
/// elements never waiting for an end tag), a start tag over several lines, the C# blocks (<c>@code { }</c>,
/// <c>@{ }</c>, <c>@if (…) { }</c> and every brace inside them, script and style bodies' braces too), and comments over
/// several lines (<c>@* *@</c>, <c>&lt;!-- --&gt;</c>, C#'s <c>/* */</c>). One pass over the lines with the state carried
/// (no parse, no copy of the text, no string per tag: names compared by a hash of their span).
/// </summary>
public sealed class RazorFolding : IFoldingRangeProvider
{
    /// <summary>Its name in <see cref="FoldingRangeProviders"/>.</summary>
    public const string ProviderName = "razor";

    private static readonly FrozenSet<string> VoidElements = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr", "param", "keygen");

    // The Razor keywords whose next brace opens C# (a @section's body is markup, so it is not one).
    private static readonly FrozenSet<string> CodeKeywords = FrozenSet.Create(StringComparer.Ordinal,
        "code", "functions", "if", "else", "for", "foreach", "while", "do", "switch", "try", "catch", "finally", "lock", "using");

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> VoidLookup = VoidElements.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> CodeLookup = CodeKeywords.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "Razor: elements, C# blocks and comments over several lines";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = [ProjectLanguages.Razor];

    /// <inheritdoc />
    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult<IReadOnlyList<FoldingRange>>(Ranges(request.Snapshot, cancellationToken));
    }

    /// <summary>The ranges of <paramref name="snapshot"/>.</summary>
    public static List<FoldingRange> Ranges(ITextSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ranges = new List<FoldingRange>();
        var elements = new List<(int Line, int Name)>();
        var braces = new List<(int Line, bool Code)>();
        int codeDepth = 0;                 // braces open that hold C#
        bool pendingCode = false;          // a Razor keyword (or @{) was seen: the next brace opens C#
        var state = State.Text;
        int markStart = 0, tagName = 0, rawName = 0;
        bool tagVoid = false;
        char quote = '\0';
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var text = LineText.Read(snapshot, index, ref buffer);
                if (state is State.CodeString or State.CodeChar)
                {
                    state = State.Text;    // a C# string or char ends with its line (verbatim strings over lines are rare in Razor)
                }

                char before = '\0';        // the last character on this line that is not white space, before the one read
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (state)
                    {
                        case State.Comment:
                            if (c == '-' && text[i..].StartsWith("-->"))
                            {
                                AddIf(ranges, markStart, index, FoldingRangeKind.Comment);
                                i += 2;
                                state = State.Text;
                            }

                            continue;
                        case State.RazorComment:
                            if (c == '*' && i + 1 < text.Length && text[i + 1] == '@')
                            {
                                AddIf(ranges, markStart, index, FoldingRangeKind.Comment);
                                i++;
                                state = State.Text;
                            }

                            continue;
                        case State.CodeComment:
                            if (c == '*' && i + 1 < text.Length && text[i + 1] == '/')
                            {
                                AddIf(ranges, markStart, index, FoldingRangeKind.Comment);
                                i++;
                                state = State.Text;
                            }

                            continue;
                        case State.CodeString or State.CodeChar:
                            if (c == '\\')
                            {
                                i++;
                            }
                            else if (c == (state == State.CodeString ? '"' : '\''))
                            {
                                state = State.Text;
                            }

                            continue;
                        case State.Tag:
                            if (quote != '\0')
                            {
                                if (c == quote)
                                {
                                    quote = '\0';
                                }
                            }
                            else if (c is '"' or '\'')
                            {
                                quote = c;
                            }
                            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '>')
                            {
                                AddIf(ranges, markStart, index, FoldingRangeKind.Region);
                                i++;
                                state = State.Text;
                            }
                            else if (c == '>')
                            {
                                if (tagVoid)
                                {
                                    AddIf(ranges, markStart, index, FoldingRangeKind.Region);
                                }
                                else
                                {
                                    elements.Add((markStart, tagName));
                                }

                                state = rawName != 0 && !tagVoid ? State.Raw : State.Text;
                                if (state == State.Text)
                                {
                                    rawName = 0;
                                }
                            }

                            continue;
                        case State.EndTag:
                            if (c == '>')
                            {
                                Close(ranges, elements, tagName, index);
                                state = State.Text;
                            }

                            continue;
                    }

                    // Text, C# or a script's or style's body (Raw): braces, comments, strings and the next tag.
                    if (state == State.Raw)
                    {
                        if (c == '<' && i + 1 < text.Length && text[i + 1] == '/' && NameAt(text, i + 2, out int rawEnd) is var closing && closing == rawName)
                        {
                            tagName = closing;
                            rawName = 0;
                            state = State.EndTag;
                            i = rawEnd - 1;
                        }
                        else
                        {
                            Brace(ranges, braces, ref codeDepth, ref pendingCode, c, index);
                        }

                        continue;
                    }

                    bool inCode = codeDepth > 0;
                    if (inCode && c == '"')
                    {
                        state = State.CodeString;
                    }
                    else if (inCode && c == '\'')
                    {
                        state = State.CodeChar;
                    }
                    else if (inCode && c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                    {
                        break;    // a C# line comment: the rest of the line
                    }
                    else if (inCode && c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                    {
                        markStart = index;
                        state = State.CodeComment;
                        i++;
                    }
                    else if (c == '@' && i + 1 < text.Length)
                    {
                        char next = text[i + 1];
                        if (next == '*')
                        {
                            markStart = index;
                            state = State.RazorComment;
                            i++;
                        }
                        else if (next == '{')
                        {
                            pendingCode = true;
                        }
                        else if (next == '@')
                        {
                            i++;
                        }
                        else if (char.IsLetter(next))
                        {
                            int end = i + 1;
                            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                            {
                                end++;
                            }

                            if (CodeLookup.Contains(text[(i + 1)..end]))
                            {
                                pendingCode = true;
                            }

                            i = end - 1;
                        }
                    }
                    else if (c == '<' && i + 1 < text.Length && (!inCode || !(char.IsLetterOrDigit(before) || before is '_' or ')' or ']')))
                    {
                        char next = text[i + 1];
                        if (text[(i + 1)..].StartsWith("!--"))
                        {
                            markStart = index;
                            state = State.Comment;
                            i += 3;
                        }
                        else if (next == '/' && NameAt(text, i + 2, out int endEnd) is var endName && endName != 0)
                        {
                            tagName = endName;
                            state = State.EndTag;
                            i = endEnd - 1;
                        }
                        else if (next == '!' || char.IsLetter(next))
                        {
                            int nameEnd = i + 1;
                            int name = next == '!' ? 0 : NameAt(text, i + 1, out nameEnd);
                            var nameSpan = text[(i + 1)..nameEnd];
                            markStart = index;
                            tagName = name;
                            tagVoid = next == '!' || VoidLookup.Contains(nameSpan);
                            rawName = nameSpan.Equals("script", StringComparison.OrdinalIgnoreCase) || nameSpan.Equals("style", StringComparison.OrdinalIgnoreCase) ? name : 0;
                            quote = '\0';
                            state = State.Tag;
                            i = Math.Max(i, nameEnd - 1);
                        }
                    }
                    else
                    {
                        Brace(ranges, braces, ref codeDepth, ref pendingCode, c, index);
                    }

                    if (!char.IsWhiteSpace(c))
                    {
                        before = c;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return ranges;
    }

    // A brace: '{' opens a block (C# when a Razor keyword or @{ came before it, or inside C#), '}' closes the last one.
    private static void Brace(List<FoldingRange> ranges, List<(int Line, bool Code)> braces, ref int codeDepth, ref bool pendingCode, char c, int index)
    {
        if (c == '{')
        {
            bool code = pendingCode || codeDepth > 0;
            braces.Add((index, code));
            codeDepth += code ? 1 : 0;
            pendingCode = false;
        }
        else if (c == '}' && braces.Count > 0)
        {
            var (line, code) = braces[^1];
            braces.RemoveAt(braces.Count - 1);
            codeDepth -= code ? 1 : 0;
            AddIf(ranges, line, index, FoldingRangeKind.Region);
        }
    }

    // An end tag: the innermost open element of its name closes (those opened inside it and never closed go with it); an
    // end tag no open element has is skipped.
    private static void Close(List<FoldingRange> ranges, List<(int Line, int Name)> elements, int name, int index)
    {
        for (int k = elements.Count - 1; k >= 0; k--)
        {
            if (elements[k].Name == name)
            {
                AddIf(ranges, elements[k].Line, index, FoldingRangeKind.Region);
                elements.RemoveRange(k, elements.Count - k);
                return;
            }
        }
    }

    // The hash of the tag name starting at `start` (letters, digits, '-', ':', '.', '_'), ignoring case; 0 when none; its end.
    private static int NameAt(ReadOnlySpan<char> text, int start, out int end)
    {
        end = start;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '-' or ':' or '.' or '_'))
        {
            end++;
        }

        if (end == start)
        {
            return 0;
        }

        int hash = string.GetHashCode(text[start..end], StringComparison.OrdinalIgnoreCase);
        return hash == 0 ? 1 : hash;
    }

    private static void AddIf(List<FoldingRange> ranges, int start, int end, FoldingRangeKind kind)
    {
        if (end > start)
        {
            ranges.Add(new FoldingRange(start, end, kind));
        }
    }

    private enum State
    {
        Text,
        Tag,
        EndTag,
        Raw,
        Comment,
        RazorComment,
        CodeComment,
        CodeString,
        CodeChar,
    }
}

/// <summary>
/// A Visual Studio solution's folding ranges (.sln; X part 5): each <c>Project(…)</c> to its <c>EndProject</c>,
/// <c>ProjectSection</c>, <c>Global</c> and <c>GlobalSection</c> to their ends, and runs of comment lines. One pass over
/// the lines, the open blocks on a stack (an unmatched end closes the innermost of its kind).
/// </summary>
public sealed class SolutionFolding : IFoldingRangeProvider
{
    /// <summary>Its name in <see cref="FoldingRangeProviders"/>.</summary>
    public const string ProviderName = "sln";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "Visual Studio solutions: projects, sections, Global and comment runs";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = [ProjectLanguages.Solution];

    /// <inheritdoc />
    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult<IReadOnlyList<FoldingRange>>(Ranges(request.Snapshot, cancellationToken));
    }

    /// <summary>The ranges of <paramref name="snapshot"/>.</summary>
    public static List<FoldingRange> Ranges(ITextSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ranges = new List<FoldingRange>();
        var open = new List<(int Line, Block Kind)>();
        int comments = -1;
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var line = LineText.Read(snapshot, index, ref buffer).Trim();
                if (line.StartsWith('#'))
                {
                    comments = comments < 0 ? index : comments;
                    continue;
                }

                if (comments >= 0 && index - 1 > comments)
                {
                    ranges.Add(new FoldingRange(comments, index - 1, FoldingRangeKind.Comment));
                }

                comments = -1;
                if (line.StartsWith("ProjectSection(", StringComparison.Ordinal))
                {
                    open.Add((index, Block.ProjectSection));
                }
                else if (line.StartsWith("Project(", StringComparison.Ordinal))
                {
                    open.Add((index, Block.Project));
                }
                else if (line.StartsWith("GlobalSection(", StringComparison.Ordinal))
                {
                    open.Add((index, Block.GlobalSection));
                }
                else if (line.SequenceEqual("Global"))
                {
                    open.Add((index, Block.Global));
                }
                else if (line.SequenceEqual("EndProjectSection"))
                {
                    Close(ranges, open, Block.ProjectSection, index);
                }
                else if (line.SequenceEqual("EndProject"))
                {
                    Close(ranges, open, Block.Project, index);
                }
                else if (line.SequenceEqual("EndGlobalSection"))
                {
                    Close(ranges, open, Block.GlobalSection, index);
                }
                else if (line.SequenceEqual("EndGlobal"))
                {
                    Close(ranges, open, Block.Global, index);
                }
            }

            if (comments >= 0 && snapshot.LineCount - 1 > comments)
            {
                ranges.Add(new FoldingRange(comments, snapshot.LineCount - 1, FoldingRangeKind.Comment));
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return ranges;
    }

    private static void Close(List<FoldingRange> ranges, List<(int Line, Block Kind)> open, Block kind, int index)
    {
        for (int k = open.Count - 1; k >= 0; k--)
        {
            if (open[k].Kind == kind)
            {
                if (index > open[k].Line)
                {
                    ranges.Add(new FoldingRange(open[k].Line, index));
                }

                open.RemoveRange(k, open.Count - k);
                return;
            }
        }
    }

    private enum Block
    {
        Project,
        ProjectSection,
        Global,
        GlobalSection,
    }
}

/// <summary>
/// .editorconfig's folding ranges (X part 5): each <c>[section]</c> from its header to its last line before the next
/// header (comments right above the next header are that one's, so they stay outside), and runs of comment lines
/// (<c>#</c> or <c>;</c>). One pass over the lines with the state carried.
/// </summary>
public sealed class EditorConfigFolding : IFoldingRangeProvider
{
    /// <summary>Its name in <see cref="FoldingRangeProviders"/>.</summary>
    public const string ProviderName = "editorconfig";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "EditorConfig: sections and comment runs";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = [ProjectLanguages.EditorConfig];

    /// <inheritdoc />
    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult<IReadOnlyList<FoldingRange>>(Ranges(request.Snapshot, cancellationToken));
    }

    /// <summary>The ranges of <paramref name="snapshot"/>.</summary>
    public static List<FoldingRange> Ranges(ITextSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ranges = new List<FoldingRange>();
        int section = -1;          // the open section's header line
        int lastContent = -1;      // the last line that is not blank
        int run = -1;              // the first line of the comment run that ends on the line before, or -1
        int beforeRun = -1;        // the last line that was not blank before that run
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var line = LineText.Read(snapshot, index, ref buffer).Trim();
                if (line.Length > 0 && line[0] is '#' or ';')
                {
                    if (run < 0)
                    {
                        run = index;
                        beforeRun = lastContent;
                    }

                    lastContent = index;
                    continue;
                }

                bool header = line.Length > 0 && line[0] == '[';
                if (header && section >= 0)
                {
                    // Comments right above a header belong to it: the section before ends where the run starts.
                    int end = run >= 0 ? beforeRun : lastContent;
                    if (end > section)
                    {
                        ranges.Add(new FoldingRange(section, end));
                    }
                }

                EndRun(ranges, ref run, index - 1);
                if (line.Length > 0)
                {
                    lastContent = index;
                }

                if (header)
                {
                    section = index;
                }
            }

            EndRun(ranges, ref run, snapshot.LineCount - 1);
            if (section >= 0 && lastContent > section)
            {
                ranges.Add(new FoldingRange(section, lastContent));
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return ranges;
    }

    private static void EndRun(List<FoldingRange> ranges, ref int run, int last)
    {
        if (run >= 0 && last > run)
        {
            ranges.Add(new FoldingRange(run, last, FoldingRangeKind.Comment));
        }

        run = -1;
    }
}
