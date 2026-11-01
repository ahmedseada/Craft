// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;

namespace Craft.Abstractions;

/// <summary>
/// A code snippet (X part 9): what completion offers under its prefixes, and the body it inserts, in VS Code's snippet
/// syntax (<see cref="SnippetParser"/>): tab stops <c>$1</c>, placeholders <c>${1:name}</c>, choices <c>${1|a,b|}</c>,
/// the final caret <c>$0</c> and variables <c>$TM_FILENAME</c>. Immutable; any thread.
/// </summary>
/// <param name="Name">Its name (what the list's detail shows, and the key in a snippets file).</param>
/// <param name="Prefixes">What typing completes it from ("prop"); one at least.</param>
/// <param name="Body">The body, lines joined with '\n', in the snippet syntax.</param>
/// <param name="Description">What it does, or null.</param>
public sealed record Snippet(string Name, IReadOnlyList<string> Prefixes, string Body, string? Description = null)
{
    /// <summary>The language names it is for (<see cref="ILanguage"/>'s names, ignoring case); none: every language.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Where it came from ("C#", a file's path), for the list's tooltip, or null.</summary>
    public string? Source { get; init; }

    /// <summary>Whether it is for <paramref name="language"/> (a snippet of no language is for every one).</summary>
    public bool IsFor(string? language)
    {
        if (Languages.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrEmpty(language))
        {
            return false;
        }

        foreach (var name in Languages)
        {
            if (string.Equals(name, language, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Gives snippets (registered in <see cref="SnippetProviders"/>): a language plug-in's own set (C#'s <c>prop</c>,
/// <c>ctor</c>, <c>for</c>), the user's files, a plug-in's. Asked on the UI thread when completion opens, so it answers
/// from what it holds (a file's snippets read once and again when it changes, off the UI thread).
/// </summary>
public interface ISnippetProvider : IKind
{
    /// <summary>Its snippets for <paramref name="language"/> (a language name, or null for plain text), cheap and on the UI thread.</summary>
    IReadOnlyList<Snippet> SnippetsFor(string? language);
}

/// <summary>
/// The snippet providers, by name (ignoring case). Craft's library registers none; the C# plug-in registers C#'s, the
/// shell the user's (<c>.craft/snippets/*.json</c> and the user folder's). A name that is not registered fails with
/// "not registered", naming those that are.
/// </summary>
public static class SnippetProviders
{
    private static readonly KindTable<ISnippetProvider> Table = new(nameof(SnippetProviders), "Snippet provider",
        "add it with SnippetProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "each provider adds its own snippets; completion lists every provider's");

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(ISnippetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the provider of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<ISnippetProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static ISnippetProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static ISnippetProvider Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// Every registered provider's snippets for <paramref name="language"/>, in the providers' order (a later provider's
    /// snippet of the same prefix and name stays beside the earlier one). A provider that throws is left out
    /// (<paramref name="failed"/> hears of it).
    /// </summary>
    public static IReadOnlyList<Snippet> For(string? language, Action<string, Exception>? failed = null)
    {
        List<Snippet>? found = null;
        foreach (var provider in Table.Kinds)
        {
            try
            {
                foreach (var snippet in provider.SnippetsFor(language))
                {
                    if (snippet.IsFor(language))
                    {
                        (found ??= []).Add(snippet);
                    }
                }
            }
#pragma warning disable CA1031 // One provider's failure must not take the others' snippets away; the caller logs it.
            catch (Exception error)
#pragma warning restore CA1031
            {
                failed?.Invoke(provider.Name, error);
            }
        }

        return found is null ? [] : found;
    }
}

/// <summary>A tab stop of an expanded snippet: its number, where it is in the inserted text, and its choices.</summary>
/// <param name="Index">Its number (0 is the final caret; stops of the same number mirror each other).</param>
/// <param name="Start">Where it starts in <see cref="ExpandedSnippet.Text"/>.</param>
/// <param name="Length">Its placeholder's characters (0 for a plain stop).</param>
public sealed record SnippetStop(int Index, int Start, int Length)
{
    /// <summary>The choices of a <c>${1|a,b|}</c> stop (the first inserted), or none.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];
}

/// <summary>A snippet's body expanded: the text to insert and its tab stops, in the order of the text.</summary>
/// <param name="Text">The text to insert.</param>
/// <param name="Stops">The stops in the order of the text (a <c>$0</c> at the end is added when the body has none).</param>
public sealed record ExpandedSnippet(string Text, IReadOnlyList<SnippetStop> Stops)
{
    /// <summary>The stop numbers in the order Tab visits them: 1, 2, … then 0.</summary>
    public IReadOnlyList<int> Order
    {
        get
        {
            var numbers = new SortedSet<int>();
            foreach (var stop in Stops)
            {
                if (stop.Index > 0)
                {
                    numbers.Add(stop.Index);
                }
            }

            var order = new List<int>(numbers) { 0 };
            return order;
        }
    }
}

/// <summary>How a snippet's body is expanded where it is inserted.</summary>
public sealed record SnippetContext
{
    /// <summary>The leading white space of the line it is inserted on: every line after the first starts with it.</summary>
    public string Indent { get; init; } = "";

    /// <summary>What a tab in the body becomes (the editor's indentation unit).</summary>
    public string IndentUnit { get; init; } = "    ";

    /// <summary>The line break the body's '\n' becomes.</summary>
    public string NewLine { get; init; } = "\n";

    /// <summary>A variable's value (<c>TM_FILENAME</c>, <c>TM_SELECTED_TEXT</c>, <c>CURRENT_YEAR</c>), or null when unknown.</summary>
    public Func<string, string?>? Variables { get; init; }
}

/// <summary>
/// VS Code's snippet syntax, the subset Craft takes: <c>$1</c>, <c>${1}</c>, <c>${1:placeholder}</c> (nested
/// placeholders too), <c>${1|one,two|}</c>, <c>$0</c>, <c>$NAME</c>, <c>${NAME}</c>, <c>${NAME:default}</c>, and the
/// escapes <c>\$</c>, <c>\}</c>, <c>\\</c> (and <c>\,</c>, <c>\|</c> in a choice). A transform (<c>${1/re/fmt/}</c>) is
/// read and mirrors its stop unchanged. An unknown variable inserts its default, else its name. Any thread.
/// </summary>
public static class SnippetParser
{
    /// <summary>Expands <paramref name="body"/> as <paramref name="context"/> says (the indentation, the line break, the variables).</summary>
    public static ExpandedSnippet Expand(string body, SnippetContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        context ??= new SnippetContext();
        var text = new StringBuilder(body.Length + 16);
        var stops = new List<SnippetStop>();
        int at = 0;
        Parse(body, ref at, text, stops, context, '\0');
        if (!stops.Exists(static s => s.Index == 0))
        {
            stops.Add(new SnippetStop(0, text.Length, 0));
        }

        stops.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Length.CompareTo(b.Length));
        return Mirror(text.ToString(), stops);
    }

    // A plain stop ($1) of a number that has a placeholder elsewhere (${1:i}) holds the placeholder's text too (VS Code's
    // mirrors): the text is inserted at each such stop and every place after it moves.
    private static ExpandedSnippet Mirror(string text, List<SnippetStop> stops)
    {
        var placeholders = new Dictionary<int, string>();
        foreach (var stop in stops)
        {
            if (stop.Index > 0 && stop.Length > 0 && stop.Choices.Count == 0 && !placeholders.ContainsKey(stop.Index))
            {
                placeholders[stop.Index] = text.Substring(stop.Start, stop.Length);
            }
        }

        if (placeholders.Count == 0 || !stops.Exists(s => s.Length == 0 && placeholders.ContainsKey(s.Index)))
        {
            return new ExpandedSnippet(text, stops);
        }

        var built = new StringBuilder(text.Length + 16);
        var moved = new List<SnippetStop>(stops.Count);
        var inserts = new List<(int At, int Length)>();
        int copied = 0;
        foreach (var stop in stops)
        {
            if (stop.Length == 0 && placeholders.TryGetValue(stop.Index, out var mirrored))
            {
                built.Append(text, copied, stop.Start - copied);
                copied = stop.Start;
                inserts.Add((stop.Start, mirrored.Length));
                built.Append(mirrored);
            }
        }

        built.Append(text, copied, text.Length - copied);
        foreach (var stop in stops)
        {
            int shift = 0;
            bool self = false;
            foreach (var (at, length) in inserts)
            {
                if (at < stop.Start || (at == stop.Start && stop.Length > 0))
                {
                    shift += length;
                }
                else if (at == stop.Start && stop.Length == 0 && placeholders.ContainsKey(stop.Index) && !self)
                {
                    self = true;                               // its own insertion: it spans it
                }
            }

            moved.Add(self ? stop with { Start = stop.Start + shift, Length = placeholders[stop.Index].Length } : stop with { Start = stop.Start + shift });
        }

        return new ExpandedSnippet(built.ToString(), moved);
    }

    /// <summary>The body as plain text (stops' placeholders kept, variables as their defaults or names): the list's preview.</summary>
    public static string Preview(string body) => Expand(body, new SnippetContext { IndentUnit = "    " }).Text;

    // Parses from `at` to the end, or to an unescaped `until` ('}' inside a placeholder).
    private static void Parse(string body, ref int at, StringBuilder text, List<SnippetStop> stops, SnippetContext context, char until)
    {
        while (at < body.Length)
        {
            char c = body[at];
            if (until != '\0' && c == until)
            {
                return;
            }

            if (c == '\\' && at + 1 < body.Length && body[at + 1] is '$' or '}' or '\\')
            {
                text.Append(body[at + 1]);
                at += 2;
                continue;
            }

            if (c == '$' && at + 1 < body.Length)
            {
                if (TryDollar(body, ref at, text, stops, context))
                {
                    continue;
                }
            }

            Append(text, c, context);
            at++;
        }
    }

    private static bool TryDollar(string body, ref int at, StringBuilder text, List<SnippetStop> stops, SnippetContext context)
    {
        int start = at;
        char next = body[at + 1];
        if (char.IsAsciiDigit(next))
        {
            int end = at + 1;
            while (end < body.Length && char.IsAsciiDigit(body[end]))
            {
                end++;
            }

            stops.Add(new SnippetStop(Number(body, at + 1, end), text.Length, 0));
            at = end;
            return true;
        }

        if (IsNameStart(next))
        {
            int end = at + 1;
            while (end < body.Length && IsNamePart(body[end]))
            {
                end++;
            }

            string name = body[(at + 1)..end];
            text.Append(Variable(name, null, context));
            at = end;
            return true;
        }

        if (next != '{' || at + 2 >= body.Length)
        {
            return false;
        }

        int inner = at + 2;
        if (char.IsAsciiDigit(body[inner]))
        {
            int end = inner;
            while (end < body.Length && char.IsAsciiDigit(body[end]))
            {
                end++;
            }

            int index = Number(body, inner, end);
            if (end < body.Length && body[end] == '}')
            {
                stops.Add(new SnippetStop(index, text.Length, 0));
                at = end + 1;
                return true;
            }

            if (end < body.Length && body[end] == ':')
            {
                int from = text.Length;
                int count = stops.Count;
                at = end + 1;
                Parse(body, ref at, text, stops, context, '}');
                if (at >= body.Length)
                {
                    // Not closed: the text taken as it is (the stops found inside stay).
                    return true;
                }

                at++;
                stops.Insert(count, new SnippetStop(index, from, text.Length - from));
                return true;
            }

            if (end < body.Length && body[end] == '|' && Choices(body, end + 1, out var choices, out int after))
            {
                int from = text.Length;
                foreach (char ch in choices[0])
                {
                    Append(text, ch, context);
                }

                stops.Add(new SnippetStop(index, from, text.Length - from) { Choices = choices });
                at = after;
                return true;
            }

            if (end < body.Length && body[end] == '/' && SkipTransform(body, end + 1, out int closed))
            {
                stops.Add(new SnippetStop(index, text.Length, 0));
                at = closed;
                return true;
            }

            at = start;
            return false;
        }

        if (IsNameStart(body[inner]))
        {
            int end = inner;
            while (end < body.Length && IsNamePart(body[end]))
            {
                end++;
            }

            string name = body[inner..end];
            if (end < body.Length && body[end] == '}')
            {
                text.Append(Variable(name, null, context));
                at = end + 1;
                return true;
            }

            if (end < body.Length && body[end] == ':')
            {
                var fallback = new StringBuilder();
                var ignored = new List<SnippetStop>();
                at = end + 1;
                Parse(body, ref at, fallback, ignored, context, '}');
                if (at < body.Length)
                {
                    at++;
                }

                string? value = context.Variables?.Invoke(name);
                if (value is not null)
                {
                    text.Append(value);
                }
                else
                {
                    int offset = text.Length;
                    text.Append(fallback);
                    foreach (var stop in ignored)
                    {
                        stops.Add(stop with { Start = stop.Start + offset });
                    }
                }

                return true;
            }

            if (end < body.Length && body[end] == '/' && SkipTransform(body, end + 1, out int closed))
            {
                text.Append(Variable(name, null, context));
                at = closed;
                return true;
            }
        }

        return false;
    }

    private static string Variable(string name, string? fallback, SnippetContext context) =>
        context.Variables?.Invoke(name) ?? fallback ?? name;

    // A choice's options from `at` (after its first '|') to its "|}"; false when it does not close.
    private static bool Choices(string body, int at, out IReadOnlyList<string> choices, out int after)
    {
        var list = new List<string>();
        var current = new StringBuilder();
        for (int i = at; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '\\' && i + 1 < body.Length && body[i + 1] is ',' or '|' or '\\' or '$' or '}')
            {
                current.Append(body[++i]);
                continue;
            }

            if (c == ',')
            {
                list.Add(current.ToString());
                current.Clear();
                continue;
            }

            if (c == '|' && i + 1 < body.Length && body[i + 1] == '}')
            {
                list.Add(current.ToString());
                choices = list;
                after = i + 2;
                return list.Count > 0;
            }

            current.Append(c);
        }

        choices = [];
        after = at;
        return false;
    }

    // A transform's "regex/format/options}" from `at`; false when it does not close.
    private static bool SkipTransform(string body, int at, out int closed)
    {
        int slashes = 0;
        for (int i = at; i < body.Length; i++)
        {
            if (body[i] == '\\')
            {
                i++;
                continue;
            }

            if (body[i] == '/')
            {
                slashes++;
            }
            else if (body[i] == '}' && slashes >= 2)
            {
                closed = i + 1;
                return true;
            }
        }

        closed = at;
        return false;
    }

    private static void Append(StringBuilder text, char c, SnippetContext context)
    {
        switch (c)
        {
            case '\n':
                text.Append(context.NewLine).Append(context.Indent);
                break;
            case '\r':
                break;
            case '\t':
                text.Append(context.IndentUnit);
                break;
            default:
                text.Append(c);
                break;
        }
    }

    private static int Number(string body, int start, int end) =>
        int.TryParse(body.AsSpan(start, end - start), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0;

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool IsNamePart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
