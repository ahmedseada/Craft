// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;

namespace Craft.Abstractions;

/// <summary>
/// A place in a line of output a click opens (X part 9: <c>file(line,col)</c>, <c>file:line:col</c>, <c>file:line</c> in the
/// terminal and Output): where the link is in the line, where its path is, and the line and column it names. Positions
/// are characters of the line scanned; the path is read from the line only when the link is followed.
/// </summary>
/// <param name="Start">Its first character in the line (its path's first).</param>
/// <param name="Length">Its characters, the place after the path included ("Program.cs(12,5)").</param>
/// <param name="PathStart">Its path's first character.</param>
/// <param name="PathLength">Its path's characters.</param>
/// <param name="Line">The line it names, from 1.</param>
/// <param name="Column">The column it names, from 1; 0 when it names none.</param>
public readonly record struct OutputLink(int Start, int Length, int PathStart, int PathLength, int Line, int Column)
{
    /// <summary>The character after it.</summary>
    public int End => Start + Length;

    /// <summary>Whether <paramref name="offset"/> (a character of the line) is on it.</summary>
    public bool Contains(int offset) => offset >= Start && offset < End;
}

/// <summary>
/// Finds the links in a line of output (X part 9). Called for each visible line of the terminal and Output when they
/// draw underlines, and for the line under the pointer: it allocates nothing and reads nothing but the line (no disk:
/// a path is resolved only when a link is followed).
/// </summary>
public interface IOutputLinkDetector : IKind
{
    /// <summary>
    /// Writes the links of <paramref name="line"/> into <paramref name="into"/>, in order and not overlapping, at most as many
    /// as it holds; returns how many.
    /// </summary>
    int Find(ReadOnlySpan<char> line, Span<OutputLink> into);
}

/// <summary>
/// The output link detectors, by name (ignoring case). The library's default is <see cref="FileLine"/> (paths followed by
/// a place: <c>a.cs(12,5)</c>, <c>a.cs:12:5</c>, <c>a.cs:12</c> and .NET's <c>a.cs:line 12</c>, Windows drive letters
/// and UNC paths included); a plug-in adds its own formats by registering a detector of another name (Python's
/// <c>File "a.py", line 3</c>, say). <see cref="FindAll"/> asks every one.
/// </summary>
public static class OutputLinkDetectors
{
    /// <summary>The library's detector of a path and its place.</summary>
    public const string FileLine = "file-line";

    private static readonly KindTable<IOutputLinkDetector> Table = BuiltIn();

    private static KindTable<IOutputLinkDetector> BuiltIn()
    {
        var table = new KindTable<IOutputLinkDetector>(nameof(OutputLinkDetectors), "Output link detector", "add it with OutputLinkDetectors.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a detector reads one line of output and allocates nothing");
        table.RegisterDefault(new FileLineLinks());
        return table;
    }

    /// <summary>Registers <paramref name="detector"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(IOutputLinkDetector detector)
    {
        ArgumentNullException.ThrowIfNull(detector);
        Table.Register(detector, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the registration of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered detectors, in order.</summary>
    public static IReadOnlyList<IOutputLinkDetector> All => Table.Kinds;

    /// <summary>The detector of that name, or null.</summary>
    public static IOutputLinkDetector? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The detector of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IOutputLinkDetector Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// The links every registered detector finds in <paramref name="line"/>, in order of their start, an overlapping later
    /// one dropped; at most as many as <paramref name="into"/> holds. Allocates nothing.
    /// </summary>
    public static int FindAll(ReadOnlySpan<char> line, Span<OutputLink> into)
    {
        var detectors = All;
        int count = 0;
        for (int d = 0; d < detectors.Count && count < into.Length; d++)
        {
            int found = detectors[d].Find(line, into[count..]);
            count += Math.Clamp(found, 0, into.Length - count);
        }

        if (detectors.Count > 1 && count > 1)
        {
            // Few links a line: an insertion sort by start, then the overlaps dropped.
            for (int i = 1; i < count; i++)
            {
                var item = into[i];
                int j = i - 1;
                while (j >= 0 && into[j].Start > item.Start)
                {
                    into[j + 1] = into[j];
                    j--;
                }

                into[j + 1] = item;
            }

            int kept = 1;
            for (int i = 1; i < count; i++)
            {
                if (into[i].Start >= into[kept - 1].End)
                {
                    into[kept++] = into[i];
                }
            }

            count = kept;
        }

        return count;
    }

    /// <summary>The link of <paramref name="line"/> that holds <paramref name="offset"/>, or null.</summary>
    public static OutputLink? At(ReadOnlySpan<char> line, int offset)
    {
        Span<OutputLink> found = stackalloc OutputLink[16];
        int count = FindAll(line, found);
        for (int i = 0; i < count; i++)
        {
            if (found[i].Contains(offset))
            {
                return found[i];
            }
        }

        return null;
    }
}

/// <summary>
/// The library's link detector (<see cref="OutputLinkDetectors.FileLine"/>): a path followed by its place, as compilers,
/// test runners and stack traces write it: <c>src/A.cs(12,5)</c> (MSBuild, TypeScript), <c>src/a.ts:12:5</c> and
/// <c>a.py:12</c> (gcc, grep, ESLint), <c>in C:\x\A.cs:line 12</c> (.NET's stack frames); drive letters (<c>C:\</c>,
/// <c>C:/</c>), UNC paths and forward or back slashes. A path is the run of characters before the place up to a space, a
/// quote or a bracket, and must end in a file name with an extension holding a letter, so a time (<c>10:30</c>), a call
/// (<c>Main(1)</c>) or a web address (<c>http://host:80</c>) is no link. A span scan: the markers found with
/// <see cref="SearchValues{T}"/>, the path's start with <c>LastIndexOfAny</c>; nothing allocated, no regular expression.
/// </summary>
public sealed class FileLineLinks : IOutputLinkDetector
{
    private static readonly SearchValues<char> Markers = SearchValues.Create("(:");

    // What ends a path walking back from its place: white space, quotes, brackets, separators of lists and assignments.
    private static readonly SearchValues<char> Delimiters = SearchValues.Create(" \t\r\n\"'`<>|()[]{},;=*?");

    /// <inheritdoc />
    public string Name => OutputLinkDetectors.FileLine;

    /// <inheritdoc />
    public string Summary => "A path and its place: a.cs(12,5), a.cs:12:5, a.cs:12, a.cs:line 12";

    /// <inheritdoc />
    public int Find(ReadOnlySpan<char> line, Span<OutputLink> into)
    {
        int count = 0;
        int from = 0;
        while (from < line.Length && count < into.Length)
        {
            int at = line[from..].IndexOfAny(Markers);
            if (at < 0)
            {
                break;
            }

            int marker = from + at;
            if (TryAt(line, marker, out var link))
            {
                into[count++] = link;
                from = link.End;
            }
            else
            {
                from = marker + 1;
            }
        }

        return count;
    }

    // A link whose place starts at the marker `marker` ('(' or ':'), or false.
    private static bool TryAt(ReadOnlySpan<char> line, int marker, out OutputLink link)
    {
        link = default;
        if (marker == 0)
        {
            return false;
        }

        int start = line[..marker].LastIndexOfAny(Delimiters) + 1;
        start = AfterColon(line, start, marker);
        int length = marker - start;
        if (length < 3 || !IsFileName(line.Slice(start, length)))
        {
            return false;
        }

        int place = marker + 1;
        int lineNumber;
        int column = 0;
        int end;
        if (line[marker] == '(')
        {
            if (!Number(line, ref place, out lineNumber))
            {
                return false;
            }

            if (place < line.Length && line[place] == ',')
            {
                int next = place + 1;
                if (!Number(line, ref next, out var c))
                {
                    return false;
                }

                column = c;
                place = next;
            }

            // MSBuild's ranges (line,column,endLine,endColumn): the rest is read and passed over.
            while (place < line.Length && line[place] == ',')
            {
                place++;
                if (!Number(line, ref place, out _))
                {
                    return false;
                }
            }

            if (place >= line.Length || line[place] != ')')
            {
                return false;
            }

            end = place + 1;
        }
        else
        {
            if (line[place..].StartsWith("line ", StringComparison.Ordinal))
            {
                place += 5;                                   // .NET's stack frame: "in C:\x\A.cs:line 12"
            }

            if (!Number(line, ref place, out lineNumber))
            {
                return false;
            }

            end = place;
            if (place + 1 < line.Length && line[place] == ':' && char.IsAsciiDigit(line[place + 1]))
            {
                int next = place + 1;
                if (Number(line, ref next, out var c))
                {
                    column = c;
                    end = next;
                }
            }
        }

        link = new OutputLink(start, end - start, start, length, Math.Max(1, lineNumber), column);
        return true;
    }

    // The path's start once the colons inside the run are read: a drive letter's colon ("C:\", "C:/") keeps the path from
    // its letter; any other colon starts the path after it ("error:a.cs:3" gives "a.cs").
    private static int AfterColon(ReadOnlySpan<char> line, int start, int marker)
    {
        int colon = line[start..marker].LastIndexOf(':');
        if (colon < 0)
        {
            return start;
        }

        int at = start + colon;
        bool drive = at >= 1 && char.IsAsciiLetter(line[at - 1]) && at + 1 < marker && line[at + 1] is '\\' or '/'
            && (at - 1 == start || line[at - 2] == ':');
        return drive ? at - 1 : at + 1;
    }

    // A path ending in a file name whose extension holds a letter; not a web address ("//host").
    private static bool IsFileName(ReadOnlySpan<char> path)
    {
        if (path.StartsWith("//", StringComparison.Ordinal) || path[^1] is '.' or '/' or '\\')
        {
            return false;
        }

        int slash = path.LastIndexOfAny('/', '\\');
        var name = path[(slash + 1)..];
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
        {
            return false;
        }

        bool letter = false;
        foreach (char c in name[(dot + 1)..])
        {
            if (char.IsAsciiLetter(c))
            {
                letter = true;
            }
            else if (!char.IsAsciiDigit(c) && c != '_')
            {
                return false;
            }
        }

        return letter;
    }

    // Up to nine digits at `place`, moved past them; false when there is none.
    private static bool Number(ReadOnlySpan<char> line, ref int place, out int value)
    {
        value = 0;
        int digits = 0;
        while (place < line.Length && char.IsAsciiDigit(line[place]))
        {
            if (++digits > 9)
            {
                return false;
            }

            value = (value * 10) + (line[place] - '0');
            place++;
        }

        return digits > 0;
    }
}
