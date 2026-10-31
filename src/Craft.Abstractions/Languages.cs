// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>A pair of brackets a language matches (the editor highlights the partner of the one at the caret).</summary>
/// <param name="Open">The opening character.</param>
/// <param name="Close">The closing character.</param>
public readonly record struct BracketPair(char Open, char Close);

/// <summary>A language's editing rules: its comments and its brackets.</summary>
/// <param name="LineComment">What starts a line comment ("//"), or null.</param>
/// <param name="BlockCommentOpen">What opens a block comment ("/*"), or null.</param>
/// <param name="BlockCommentClose">What closes it ("*/"), or null.</param>
/// <param name="Brackets">The brackets it matches, in order.</param>
public sealed record LanguageRules(string? LineComment, string? BlockCommentOpen, string? BlockCommentClose, IReadOnlyList<BracketPair> Brackets)
{
    /// <summary>No comments; (), [] and {} as brackets: the rules of a file of no language.</summary>
    public static LanguageRules Plain { get; } = new(null, null, null, [new('(', ')'), new('[', ']'), new('{', '}')]);
}

/// <summary>
/// A language the editor knows, registered in <see cref="EditorLanguages"/> under its <see cref="IKind.Name"/>: the files
/// it opens, its rules, and a session per open buffer that classifies the text.
/// </summary>
public interface ILanguage : IKind
{
    /// <summary>The file extensions it opens, with their dot, ignoring case (".cs", ".csx").</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Its comments and brackets.</summary>
    LanguageRules Rules { get; }

    /// <summary>
    /// A session over <paramref name="buffer"/>: it follows the buffer's changes (its <see cref="ITextBuffer.Changed"/>)
    /// and classifies its snapshots. Dispose it when the document closes.
    /// </summary>
    ILanguageSession Open(ITextBuffer buffer);
}

/// <summary>
/// A language's work on one open buffer. <see cref="Classify"/> is the syntactic layer: fast enough for the keystroke,
/// called on the UI thread for the lines about to be drawn, and it never waits. A session may parse off the UI thread:
/// it then classifies a snapshot newer than its last parse from that parse (the text the change touched left plain),
/// raises <see cref="Reclassified"/> when a newer parse lands, and lets the editor wait a bounded time for it
/// (<see cref="WaitUntilClassified"/>). The semantic layer (diagnostics, completion) comes later and runs off the UI thread.
/// </summary>
public interface ILanguageSession : IDisposable
{
    /// <summary>
    /// Raised when the spans <see cref="Classify"/> gives may have changed without the buffer changing: a parse made off
    /// the UI thread landed (the first one after the session opened included). It is raised on the session's own thread,
    /// never the UI thread's: the subscriber marshals to its UI thread before it redraws. A session that classifies each
    /// snapshot completely when asked never raises it (the default).
    /// </summary>
    event EventHandler? Reclassified
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Adds the classified spans of <paramref name="start"/> to <paramref name="start"/> + <paramref name="length"/> of
    /// <paramref name="snapshot"/> to <paramref name="into"/>, in order of position. A character no span covers is plain
    /// text. A span may start before the range or end after it (a block comment that began above); the caller clips.
    /// It never waits for a parse: before the parse of <paramref name="snapshot"/> lands, the text its changes touched
    /// gets no span (and <see cref="Reclassified"/> follows).
    /// </summary>
    void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into);

    /// <summary>
    /// Waits up to <paramref name="timeout"/> (none when zero, without end when <see cref="Timeout.InfiniteTimeSpan"/>) until <see cref="Classify"/> of <paramref name="snapshot"/>
    /// gives its final spans (its parse landed); true when it does. The editor calls it before a frame with its budget, so
    /// ordinary typing paints classified in the same frame and a long parse never holds the UI thread longer. A session
    /// that classifies each snapshot completely when asked returns true at once (the default).
    /// </summary>
    bool WaitUntilClassified(ITextSnapshot snapshot, TimeSpan timeout) => true;
}

/// <summary>
/// The languages, by name (ignoring case). Craft's library registers none; <c>Craft.CSharp</c> registers "csharp" when
/// an app calls its <c>Register</c>. A file whose extension no language claims opens as plain text, and the editor says so.
/// </summary>
public static class EditorLanguages
{
    private static readonly KindTable<ILanguage> Table = new(nameof(EditorLanguages), "Language", "add it with EditorLanguages.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a language session follows its buffer across calls");

    /// <summary>Registers <paramref name="language"/> under its name.</summary>
    public static void Register(ILanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        Table.Register(language, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's language of that name; false when none was registered by the app.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered languages, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<ILanguage> All => Table.Kinds;

    /// <summary>The language of that name, or null.</summary>
    public static ILanguage? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The language of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static ILanguage Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The language that opens <paramref name="path"/> by its extension, or null for plain text.</summary>
    public static ILanguage? ForFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path.AsSpan());
        if (extension.IsEmpty)
        {
            return null;
        }

        foreach (var language in Table.Kinds)
        {
            foreach (var claimed in language.Extensions)
            {
                if (extension.Equals(claimed, StringComparison.OrdinalIgnoreCase))
                {
                    return language;
                }
            }
        }

        return null;
    }
}
