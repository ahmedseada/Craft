// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Globalization;
using System.Text;
using Craft.Abstractions;

namespace Craft.Plugins.Tools;

/// <summary>What both backings of the editor's tools share: paths under a folder, its files, reading lines and searching.</summary>
internal static class ToolText
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Symbols as find_symbol answers: "path:line:column: kind name — description (in container)" one a line, the
    /// closest first (an exact name, then a name that starts with the query, then the rest), at most <paramref name="limit"/>.
    /// </summary>
    public static string Symbols(string? root, string query, List<SymbolLocation> found, int limit)
    {
        if (found.Count == 0)
        {
            return $"No symbol's name matches \"{query}\".";
        }

        int Rank(SymbolLocation s) => s.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 : s.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        var lines = new List<string>(Math.Min(limit, found.Count) + 1);
        foreach (var symbol in found.Select((s, i) => (s, i)).OrderBy(p => Rank(p.s)).ThenBy(p => p.i).Select(p => p.s).Take(limit))
        {
            string where = symbol.FilePath is { } file ? Display(root, file) : "(no file)";
            lines.Add(string.Create(Culture, $"{where}:{symbol.Line}:{symbol.Column}: {symbol.Kind} {symbol.Name} — {symbol.Description}{(symbol.Container is { } c ? $" (in {c})" : "")}"));
        }

        if (found.Count > limit)
        {
            lines.Add(string.Create(Culture, $"(stopped at {limit} symbols)"));
        }

        return string.Join('\n', lines);
    }

    /// <summary>The full path of <paramref name="path"/> (relative to <paramref name="root"/>) when it lies inside it, else null.</summary>
    public static string? Inside(string root, string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        // GetRelativePath compares as the platform's file system does (ignoring case where it does).
        var relative = Path.GetRelativePath(root, full);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative)) ? full : null;
    }

    /// <summary><paramref name="full"/> as the tools show it: relative to <paramref name="root"/> with '/' when inside it, else as it is.</summary>
    public static string Display(string? root, string full)
    {
        if (root is null)
        {
            return full;
        }

        var relative = Path.GetRelativePath(root, full);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? full : relative.Replace('\\', '/');
    }

    /// <summary>The files under <paramref name="root"/>, skipping the ignored folder names and hidden folders, in a stable order.</summary>
    public static IEnumerable<string> Files(string root, IReadOnlyList<string> ignore)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            string[] files, folders;
            try
            {
                files = Directory.GetFiles(folder);
                folders = Directory.GetDirectories(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                yield return file;
            }

            Array.Sort(folders, StringComparer.Ordinal);
            for (int i = folders.Length - 1; i >= 0; i--)
            {
                var name = Path.GetFileName(folders[i]);
                if (!Ignored(name, ignore))
                {
                    pending.Push(folders[i]);
                }
            }
        }
    }

    private static bool Ignored(string name, IReadOnlyList<string> ignore)
    {
        foreach (var skipped in ignore)
        {
            if (string.Equals(name, skipped, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lines <paramref name="startLine"/> (from 1) on of <paramref name="text"/>, at most <paramref name="count"/>, under a
    /// header naming the file and the lines shown of how many.
    /// </summary>
    public static string ReadLines(string name, string text, int startLine, int count)
    {
        var lines = new List<(int Start, int Length)>();
        var span = text.AsSpan();
        int at = 0;
        while (true)
        {
            int index = span[at..].IndexOf('\n');
            if (index < 0)
            {
                lines.Add((at, span.Length - at));
                break;
            }

            int length = index > 0 && span[at + index - 1] == '\r' ? index - 1 : index;
            lines.Add((at, length));
            at += index + 1;
        }

        return Range(name, lines.Count, startLine, count, i => text.AsSpan(lines[i].Start, lines[i].Length));
    }

    /// <summary>The same for an open document's snapshot: only the lines shown are read.</summary>
    public static string ReadLines(string name, ITextSnapshot snapshot, int startLine, int count) =>
        RangeOfSnapshot(name, snapshot, startLine, count);

    private static string RangeOfSnapshot(string name, ITextSnapshot snapshot, int startLine, int count)
    {
        int total = snapshot.LineCount;
        int first = Math.Clamp(startLine, 1, Math.Max(1, total));
        int last = Math.Min(total, first + count - 1);
        var text = new StringBuilder();
        text.Append(Culture, $"{name}: lines {first}-{last} of {total}\n");
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            for (int i = first - 1; i < last; i++)
            {
                var line = snapshot.GetLine(i);
                if (buffer.Length < line.Length)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                    buffer = ArrayPool<char>.Shared.Rent(line.Length);
                }

                snapshot.CopyTo(line.Start, buffer.AsSpan(0, line.Length));
                text.Append(buffer.AsSpan(0, line.Length)).Append('\n');
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return text.ToString();
    }

    private static string Range(string name, int total, int startLine, int count, Func<int, ReadOnlySpan<char>> line)
    {
        int first = Math.Clamp(startLine, 1, Math.Max(1, total));
        int last = Math.Min(total, first + count - 1);
        var text = new StringBuilder();
        text.Append(Culture, $"{name}: lines {first}-{last} of {total}\n");
        for (int i = first - 1; i < last; i++)
        {
            text.Append(line(i)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Adds to <paramref name="hits"/> each line of <paramref name="snapshot"/> holding <paramref name="query"/>
    /// ("name:line: text"), until it holds <paramref name="limit"/>; the lines are read one at a time into a pooled buffer.
    /// </summary>
    public static void Search(string name, ITextSnapshot snapshot, string query, StringComparison comparison, List<string> hits, int limit)
    {
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            for (int i = 0; i < snapshot.LineCount && hits.Count < limit; i++)
            {
                var line = snapshot.GetLine(i);
                if (line.Length < query.Length)
                {
                    continue;
                }

                if (buffer.Length < line.Length)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                    buffer = ArrayPool<char>.Shared.Rent(line.Length);
                }

                var text = buffer.AsSpan(0, line.Length);
                snapshot.CopyTo(line.Start, text);
                if (text.IndexOf(query, comparison) >= 0)
                {
                    hits.Add(Hit(name, i + 1, text));
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// The same for a file on disk: its bytes are read once, and a case-sensitive query that is not in the UTF-8 bytes
    /// skips the file without decoding it. Files over <paramref name="maxBytes"/> or with a NUL byte (binary) are skipped.
    /// </summary>
    public static void SearchFile(string name, string path, string query, byte[] queryUtf8, StringComparison comparison, List<string> hits, int limit, long maxBytes)
    {
        byte[] bytes;
        try
        {
            if (new FileInfo(path).Length > maxBytes)
            {
                return;
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var span = bytes.AsSpan();
        if (span.IndexOf((byte)0) >= 0 || (comparison == StringComparison.Ordinal && span.IndexOf(queryUtf8) < 0))
        {
            return;
        }

        var text = Encoding.UTF8.GetString(span.StartsWith(Encoding.UTF8.Preamble) ? span[3..] : span).AsSpan();
        int lineNumber = 0;
        while (text.Length > 0 && hits.Count < limit)
        {
            lineNumber++;
            int end = text.IndexOf('\n');
            var line = end < 0 ? text : text[..end];
            line = line.TrimEnd('\r');
            if (line.IndexOf(query, comparison) >= 0)
            {
                hits.Add(Hit(name, lineNumber, line));
            }

            text = end < 0 ? [] : text[(end + 1)..];
        }
    }

    private static string Hit(string name, int line, ReadOnlySpan<char> text)
    {
        const int Shown = 240;
        var trimmed = text.Trim();
        bool cut = trimmed.Length > Shown;
        return string.Create(Culture, $"{name}:{line}: {trimmed[..Math.Min(Shown, trimmed.Length)]}{(cut ? "…" : "")}");
    }

    /// <summary>
    /// The edit that replaces the one occurrence of <paramref name="oldText"/> in <paramref name="text"/> with
    /// <paramref name="newText"/>; an empty <paramref name="oldText"/> inserts at the start of <paramref name="line"/>
    /// (from 1; the end of the text when null). Null with the reason when it does not occur once.
    /// </summary>
    public static TextEdit? Find(string text, string oldText, string newText, int? line, out string? problem)
    {
        problem = null;
        if (oldText.Length == 0)
        {
            int at = text.Length;
            if (line is { } wanted)
            {
                at = 0;
                for (int current = 1; current < wanted; current++)
                {
                    int next = text.IndexOf('\n', at);
                    if (next < 0)
                    {
                        at = text.Length;
                        break;
                    }

                    at = next + 1;
                }
            }

            return TextEdit.Insert(at, newText);
        }

        int first = text.IndexOf(oldText, StringComparison.Ordinal);
        if (first < 0)
        {
            problem = "old_text does not occur in the document; read it again (read_document) and give the exact text to replace.";
            return null;
        }

        if (text.IndexOf(oldText, first + 1, StringComparison.Ordinal) >= 0)
        {
            problem = "old_text occurs more than once; give more of the text around it so it occurs once.";
            return null;
        }

        return new TextEdit(first, oldText.Length, newText);
    }
}
