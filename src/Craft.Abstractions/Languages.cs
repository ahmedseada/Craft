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

    /// <summary>
    /// The characters that open a string and close it on the same line ("\"'" for C#; empty for none): the editor's bracket
    /// pair colours (X part 9) skip the brackets inside them, as they skip comments.
    /// </summary>
    public string StringDelimiters { get; init; } = "";

    /// <summary>The character that escapes the next one inside a string ('\\'), or '\0' for none.</summary>
    public char StringEscape { get; init; } = '\\';
}

/// <summary>
/// A language the editor knows, registered in <see cref="EditorLanguages"/> under its <see cref="IKind.Name"/>: the files
/// it opens, its rules, and a session per open buffer that classifies the text.
/// </summary>
public interface ILanguage : IKind
{
    /// <summary>The file extensions it opens, with their dot, ignoring case (".cs", ".csx").</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// The file names it opens beyond its extensions (W, contract 3), ignoring case: exact names ("Dockerfile",
    /// "tsconfig.json") and globs ('*' any run, '?' one character: "tsconfig.*.json", "*.component.html"). An exact name
    /// ranks above every pattern, a longer pattern above a shorter one, and every pattern above an extension
    /// (<see cref="EditorLanguages.ForFile(string, string?)"/>). None by default.
    /// </summary>
    IReadOnlyList<string> FilePatterns => [];

    /// <summary>Its comments and brackets.</summary>
    LanguageRules Rules { get; }

    /// <summary>
    /// A session over <paramref name="buffer"/>: it follows the buffer's changes (its <see cref="ITextBuffer.Changed"/>)
    /// and classifies its snapshots. Dispose it when the document closes.
    /// </summary>
    ILanguageSession Open(ITextBuffer buffer);

    /// <summary>
    /// A session over <paramref name="buffer"/> that knows its file and workspace (<paramref name="context"/>), which a
    /// semantic layer (<see cref="ISemanticLanguageSession"/>) needs; a language with none ignores the context (the default
    /// calls <see cref="Open(ITextBuffer)"/>).
    /// </summary>
    ILanguageSession Open(ITextBuffer buffer, LanguageOpenContext context) => Open(buffer);

    /// <summary>
    /// Frees what the language holds that it can build again (a semantic layer's workspaces and their references): the
    /// host calls it when memory runs low (<see cref="MemoryPressureMonitors"/>). It may be called from any thread and must
    /// not block it; the sessions stay open and build what they need again on their next request. The default frees nothing.
    /// </summary>
    void ReleaseMemory()
    {
    }

    /// <summary>
    /// The symbols declared in the files of the workspace at <paramref name="workspaceRoot"/> whose names hold
    /// <paramref name="query"/> (as <see cref="ISemanticLanguageSession.FindSymbolsAsync"/> matches them), with no document
    /// of its own open: the folder backing of the editor tools' <c>find_symbol</c>. The files are read from disk, except
    /// that a language may share the workspace a session of the same folder holds in this process (with that session's
    /// unsaved text). Off the calling thread. Empty for a language that cannot search (the default).
    /// </summary>
    Task<IReadOnlyList<SymbolLocation>> FindSymbolsAsync(string workspaceRoot, string query, int maxResults, LanguageSettings settings, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SymbolLocation>>([]);
}

/// <summary>
/// A language's work on one open buffer. <see cref="Classify"/> is the syntactic layer: fast enough for the keystroke,
/// called on the UI thread for the lines about to be drawn, and it never waits. A session may parse off the UI thread:
/// it then classifies a snapshot newer than its last parse from that parse (the text the change touched left plain),
/// raises <see cref="Reclassified"/> when a newer parse lands, and lets the editor wait a bounded time for it
/// (<see cref="WaitUntilClassified"/>). The semantic layer (diagnostics, completion, signature help, hover, definition, formatting), for a language that has
/// one, is <see cref="ISemanticLanguageSession"/>: it runs off the UI thread.
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
    /// Adds the syntactic layer's spans of the range to <paramref name="into"/>, as <see cref="Classify"/> does, but never
    /// the semantic colours and never asking for them (the X restart's minimap classifies a window of hundreds of lines,
    /// which must not start a semantic pass). A session without semantic colours classifies as <see cref="Classify"/> (the
    /// default). It never waits.
    /// </summary>
    void ClassifySyntactic(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into) => Classify(snapshot, start, length, into);

    /// <summary>
    /// Waits up to <paramref name="timeout"/> (none when zero, without end when <see cref="Timeout.InfiniteTimeSpan"/>) until <see cref="Classify"/> of <paramref name="snapshot"/>
    /// gives its final spans (its parse landed); true when it does. The editor calls it before a frame with its budget, so
    /// ordinary typing paints classified in the same frame and a long parse never holds the UI thread longer. A session
    /// that classifies each snapshot completely when asked returns true at once (the default).
    /// </summary>
    bool WaitUntilClassified(ITextSnapshot snapshot, TimeSpan timeout) => true;

    /// <summary>
    /// The named scopes that hold <paramref name="position"/>, outermost first (a type, a nested type, a member), for
    /// the breadcrumb: from the session's last parse, never waiting for one (a parse older than
    /// <paramref name="snapshot"/> answers for its own text). It is asked when the caret moves, so a session answers
    /// from what it holds and returns the same list while the scopes are the same. None by default.
    /// </summary>
    IReadOnlyList<ScopeName> ScopeAt(ITextSnapshot snapshot, int position) => [];

    /// <summary>
    /// The symbols <paramref name="snapshot"/> declares (namespaces, types, members, enum members), outermost first and in
    /// the order of the text, each with its children: the outline that quick open's '@' (Ctrl+Shift+O), the breadcrumb's
    /// symbol parts and the chat's '@' mentions list. Off the calling thread; a session answers each version once and keeps
    /// the answer (asked again for the same version, it gives the same list). None by default (a language without symbols).
    /// </summary>
    Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(ITextSnapshot snapshot, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentSymbol>>([]);

    /// <summary>
    /// The editing rules at <paramref name="position"/> (phase W, contract 1.1): an embedded language's there (an HTML
    /// file's <c>&lt;script&gt;</c> toggles <c>//</c>, its <c>&lt;style&gt;</c> <c>/* */</c>), or null for the language's
    /// own <see cref="ILanguage.Rules"/> (the default). Asked on the UI thread when a command needs them; it never waits.
    /// </summary>
    LanguageRules? RulesAt(ITextSnapshot snapshot, int position) => null;
}

/// <summary>
/// A language session that formats its document (Format document, Shift+Alt+F, and format on save), with or without a
/// semantic layer: C#'s semantic session is one (<see cref="ISemanticLanguageSession"/>), and a language that only colours
/// and formats (XML, JSON) implements this alone. The editor applies the edits as one undo step, only when the text is
/// still the one formatted.
/// </summary>
public interface IFormattingLanguageSession : ILanguageSession
{
    /// <summary>
    /// The edits that format <paramref name="snapshot"/> (the range of <paramref name="start"/> and <paramref name="length"/>
    /// when <paramref name="length"/> is not negative, the whole document otherwise), in its positions, in order, not
    /// overlapping: applied together as one change. Off the calling thread; none when the text is formatted already.
    /// </summary>
    /// <exception cref="FormatException">
    /// The text cannot be formatted as it stands (a malformed document: an unclosed element, a missing comma); the message
    /// says where and what, and the document is left unchanged (the editor shows the message).
    /// </exception>
    Task<IReadOnlyList<TextEdit>> FormatAsync(ITextSnapshot snapshot, int start, int length, CancellationToken cancellationToken);
}

/// <summary>A named scope of a document (the breadcrumb's part): its name and what it is.</summary>
/// <param name="Name">The name ("EditorTools", "GetDiagnosticsAsync").</param>
/// <param name="Kind">What it is.</param>
public readonly record struct ScopeName(string Name, ScopeKind Kind);

/// <summary>What a named scope is.</summary>
public enum ScopeKind
{
    /// <summary>A namespace.</summary>
    Namespace,

    /// <summary>A type (class, struct, interface, enum, record, delegate).</summary>
    Type,

    /// <summary>A method, constructor, operator or local function.</summary>
    Method,

    /// <summary>A property, indexer, event or field (a scope that does not say which; <see cref="DocumentSymbol"/>s do).</summary>
    Member,

    /// <summary>A property or an indexer (contract 1.1).</summary>
    Property,

    /// <summary>A field or a constant (contract 1.1).</summary>
    Field,

    /// <summary>An event (contract 1.1).</summary>
    Event,

    /// <summary>A member of an enum (contract 1.1).</summary>
    EnumMember,
}

/// <summary>
/// A symbol a document declares (<see cref="ILanguageSession.GetDocumentSymbolsAsync"/>): its name, what it is, the text it
/// spans and its name's place, in the positions of the snapshot it was asked for. Immutable; any thread.
/// </summary>
/// <param name="Name">The name as the outline shows it ("EditorTools", "GetDiagnosticsAsync", "this[]").</param>
/// <param name="Kind">What it is.</param>
/// <param name="Start">Where its declaration starts (its attributes and modifiers included, its leading comments not).</param>
/// <param name="Length">The declaration's characters.</param>
/// <param name="NameStart">Where its name starts (what a pick selects); <paramref name="Start"/> when it has no name of its own.</param>
/// <param name="NameLength">The name's characters (0 when it has none).</param>
/// <param name="Detail">What the outline shows after the name, quieter: a method's parameters "(int line, int column)", a property's or a field's type, or null.</param>
/// <param name="Children">The symbols it declares, in the order of the text (none for a member).</param>
public sealed record DocumentSymbol(string Name, ScopeKind Kind, int Start, int Length, int NameStart, int NameLength, string? Detail, IReadOnlyList<DocumentSymbol> Children)
{
    /// <summary>The position after the declaration.</summary>
    public int End => Start + Length;

    /// <summary>Whether <paramref name="position"/> is inside the declaration (its end included: a caret just after a closing brace).</summary>
    public bool Contains(int position) => position >= Start && position <= End;
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

    /// <summary>
    /// The language that opens <paramref name="path"/> by its name alone (<see cref="ForFile(string, string?)"/> with no
    /// workspace), or null for plain text.
    /// </summary>
    public static ILanguage? ForFile(string path) => ForFile(path, null);

    /// <summary>
    /// The language that opens <paramref name="path"/> (W, contracts 3 and 4): the first registered
    /// <see cref="LanguageSelectors"/> that answers for it in the workspace at <paramref name="workspaceRoot"/> (an Angular
    /// project's templates), else by its name: a language's exact <see cref="ILanguage.FilePatterns"/> name, then its longest
    /// pattern, then its extension (<see cref="ILanguage.Extensions"/>); the first language registered wins a tie. Null
    /// for plain text. From memory, on any thread: the patterns are matched by a walk of the name's dots with alternate
    /// lookups, allocating nothing (rules 15, 17), and a selector answers from its own cache.
    /// </summary>
    public static ILanguage? ForFile(string path, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(path);
        var selectors = LanguageSelectors.All;
        for (int i = 0; i < selectors.Count; i++)
        {
            if (selectors[i].Select(path, workspaceRoot) is { } selected)
            {
                return selected;
            }
        }

        return Patterns().FindForPath(path);
    }

    // The table of every language's names, patterns and extensions, made again only when a registration changed the
    // languages (the registry's list is then a new array).
    private static FilePatternTable<ILanguage> Patterns()
    {
        var kinds = Table.Kinds;
        var made = Volatile.Read(ref _patterns);
        if (made is not null && ReferenceEquals(made.Source, kinds))
        {
            return made.Table;
        }

        var entries = new List<KeyValuePair<string, ILanguage>>();
        foreach (var language in kinds)
        {
            foreach (var pattern in language.FilePatterns)
            {
                entries.Add(KeyValuePair.Create(pattern, language));
            }
        }

        foreach (var language in kinds)
        {
            foreach (var extension in language.Extensions)
            {
                if (extension.Length > 1)
                {
                    // An extension always counts as one, even where its shape reads as a suffix (".d.ts").
                    entries.Add(KeyValuePair.Create(FilePatternTable<ILanguage>.KindOf(extension) == FilePatternKind.Extension ? extension : "*" + extension, language));
                }
            }
        }

        made = new LanguagePatterns(kinds, new FilePatternTable<ILanguage>(entries));
        Volatile.Write(ref _patterns, made);
        return made.Table;
    }

    private static LanguagePatterns? _patterns;

    private sealed record LanguagePatterns(IReadOnlyList<ILanguage> Source, FilePatternTable<ILanguage> Table);
}

/// <summary>
/// Picks a file's language from what the workspace is (W, contract 4): an Angular project's <c>.html</c> as Angular
/// templates, say, where the name alone says HTML. Registered in <see cref="LanguageSelectors"/>; asked by
/// <see cref="EditorLanguages.ForFile(string, string?)"/> before the names. <see cref="Select"/> answers from the
/// selector's own cache, with no I/O on the call (it is asked on the UI thread when a file opens);
/// <see cref="PrepareAsync"/> reads what it needs of a workspace off the UI thread.
/// </summary>
public interface ILanguageSelector : IKind
{
    /// <summary>
    /// The language of <paramref name="path"/> in the workspace at <paramref name="workspaceRoot"/> (null for a file
    /// outside one), or null to leave it to the names. From memory: no file is read here.
    /// </summary>
    ILanguage? Select(string path, string? workspaceRoot);

    /// <summary>
    /// Reads what <see cref="Select"/> needs of the workspace at <paramref name="workspaceRoot"/> (an <c>angular.json</c>),
    /// off the calling thread; the host calls it when a folder opens and re-picks the open files' languages after it. Done
    /// at once by default.
    /// </summary>
    Task PrepareAsync(string workspaceRoot, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The language selectors, by name (ignoring case). The library registers none; a language plug-in registers its own.</summary>
public static class LanguageSelectors
{
    private static readonly KindTable<ILanguageSelector> Table = new(nameof(LanguageSelectors), "Language selector", "add it with LanguageSelectors.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a selector answers from its own cache");

    /// <summary>Registers <paramref name="selector"/> under its name.</summary>
    public static void Register(ILanguageSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Table.Register(selector, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's selector of that name; false when none was registered by the app.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered selectors, in the order of <see cref="Names"/> (the order they are asked in).</summary>
    public static IReadOnlyList<ILanguageSelector> All => Table.Kinds;

    /// <summary>The selector of that name, or null.</summary>
    public static ILanguageSelector? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The selector of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ILanguageSelector Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// Prepares every selector for the workspace at <paramref name="workspaceRoot"/> (<see cref="ILanguageSelector.PrepareAsync"/>),
    /// together, off the calling thread; a selector that fails is left out and named in the result ("name: reason"), the
    /// others still prepared.
    /// </summary>
    public static async Task<IReadOnlyList<string>> PrepareAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var selectors = All;
        if (selectors.Count == 0)
        {
            return [];
        }

        var problems = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await Task.WhenAll(selectors.Select(selector => Task.Run(async () =>
        {
            try
            {
                await selector.PrepareAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                problems.Enqueue($"{selector.Name}: {e.Message}");
            }
        }, cancellationToken))).ConfigureAwait(false);
        return [.. problems];
    }
}
