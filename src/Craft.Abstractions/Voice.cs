// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;

namespace Craft.Abstractions;

/// <summary>
/// A spoken symbol for dictation (plan 1, phase V): the words said ("open paren") and the text they type ("("), for one
/// language (its name, "csharp") or every language (""), with how it sits between words. A language plug-in registers
/// its own (C#'s "arrow" types "=>"), which win over the every-language ones for its documents.
/// </summary>
/// <param name="Words">What is said, in lower case, words separated by one space ("open paren", "new line").</param>
/// <param name="Text">What it types ("(", "\n").</param>
/// <param name="Language">The language it is for (an <see cref="ILanguage"/>'s name), or "" for every language.</param>
/// <param name="SpaceBefore">A space goes before it when text precedes it ("x = 1": "equals").</param>
/// <param name="SpaceAfter">A space goes after it when a word follows it ("a, b": "comma").</param>
public sealed record SpokenSymbol(string Words, string Text, string Language = "", bool SpaceBefore = false, bool SpaceAfter = false) : IKind
{
    /// <summary>Its name in <see cref="SpokenSymbols"/>: the words, after the language and a colon when it has one ("csharp:arrow").</summary>
    public string Name { get; } = string.IsNullOrEmpty(Language) ? Words : Language + ":" + Words;

    /// <summary>What it does.</summary>
    public string Summary => $"\"{Words}\" types {(Text == "\n" ? "a line break" : Text == "\t" ? "a tab" : Text)}{(Language.Length == 0 ? "" : " in " + Language)}";
}

/// <summary>
/// The spoken symbols, by name (ignoring case): Craft's library registers none; the voice plug-in registers the
/// every-language table, a language plug-in its own. <see cref="Version"/> changes with every registration, so a
/// dictation that built its table from them knows when to build it again.
/// </summary>
public static class SpokenSymbols
{
    private static readonly KindTable<SpokenSymbol> Table = new(nameof(SpokenSymbols), "Spoken symbol", "add it with SpokenSymbols.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a symbol only maps words to text; the last registration of the same words wins");

    private static int s_version;

    /// <summary>Changes with every <see cref="Register"/> and <see cref="Unregister"/>.</summary>
    public static int Version => Volatile.Read(ref s_version);

    /// <summary>Registers <paramref name="symbol"/> under its name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]   // the caller is the registering assembly
    public static void Register(SpokenSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol.Words);
        ArgumentException.ThrowIfNullOrEmpty(symbol.Text);
        Table.Register(symbol, System.Reflection.Assembly.GetCallingAssembly());
        Interlocked.Increment(ref s_version);
    }

    /// <summary>Removes the symbol of that name ("open paren", "csharp:arrow"); false when none was registered.</summary>
    public static bool Unregister(string name)
    {
        bool removed = Table.Unregister(name);
        Interlocked.Increment(ref s_version);
        return removed;
    }

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered symbols, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<SpokenSymbol> All => Table.Kinds;

    /// <summary>The symbol of that name, or null.</summary>
    public static SpokenSymbol? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The symbol of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static SpokenSymbol Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// The symbols a document of <paramref name="language"/> dictates with: its language's own, then every language's
    /// whose words its own do not say.
    /// </summary>
    public static IReadOnlyList<SpokenSymbol> For(string? language)
    {
        var all = Table.Kinds;
        var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SpokenSymbol>(all.Count);
        if (!string.IsNullOrEmpty(language))
        {
            foreach (var symbol in all)
            {
                if (symbol.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(symbol);
                    own.Add(symbol.Words);
                }
            }
        }

        foreach (var symbol in all)
        {
            if (symbol.Language.Length == 0 && !own.Contains(symbol.Words))
            {
                result.Add(symbol);
            }
        }

        return result;
    }
}

/// <summary>
/// A spoken command (plan 1, phase V): a phrase that runs a registered command ("next error" runs
/// <c>voice.nextProblem</c>), or a phrase with a number in it that runs an action with that number ("go to line #":
/// "go to line forty" goes to line 40). The voice plug-in also matches every registered command's title, so these are
/// the aliases a title does not say and the commands that take a number. Any plug-in registers its own.
/// </summary>
public sealed class SpokenCommand : IKind
{
    /// <summary>The word of <see cref="Phrase"/> where a number is said.</summary>
    public const string NumberWord = "#";

    private readonly Func<IEditorHost, int, CancellationToken, Task>? _run;

    /// <summary>The phrase <paramref name="phrase"/> runs the command <paramref name="commandId"/>.</summary>
    public SpokenCommand(string phrase, string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        Phrase = Normalize(phrase);
        CommandId = commandId;
        Summary = $"\"{Phrase}\" runs {commandId}";
    }

    /// <summary>The phrase <paramref name="phrase"/>, which holds the word "#" once (where the number is said), runs <paramref name="run"/> with that number.</summary>
    public SpokenCommand(string phrase, Func<IEditorHost, int, CancellationToken, Task> run, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Phrase = Normalize(phrase);
        if (Array.IndexOf(Phrase.Split(' '), NumberWord) < 0)
        {
            throw new ArgumentException($"The phrase '{phrase}' says no number: put the word '{NumberWord}' where it is said.", nameof(phrase));
        }

        _run = run;
        Summary = summary;
    }

    /// <summary>Its name in <see cref="SpokenCommands"/>: the phrase.</summary>
    public string Name => Phrase;

    /// <summary>What it does.</summary>
    public string Summary { get; }

    /// <summary>The phrase, in lower case, words separated by one space, "#" where a number is said.</summary>
    public string Phrase { get; }

    /// <summary>The command it runs, or null when it runs an action with a number.</summary>
    public string? CommandId { get; }

    /// <summary>Whether the phrase says a number.</summary>
    public bool TakesNumber => _run is not null;

    /// <summary>Runs it: the command through <paramref name="host"/>, or the action with <paramref name="number"/>; false when the command did not run.</summary>
    public async Task<bool> RunAsync(IEditorHost host, int number, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_run is { } run)
        {
            await run(host, number, cancellationToken).ConfigureAwait(true);
            return true;
        }

        return await host.ExecuteCommandAsync(CommandId!, cancellationToken).ConfigureAwait(true);
    }

    private static string Normalize(string phrase) =>
        string.Join(' ', phrase.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>The spoken commands, by phrase (ignoring case). Craft's library registers none; the voice plug-in registers its aliases.</summary>
public static class SpokenCommands
{
    private static readonly KindTable<SpokenCommand> Table = new(nameof(SpokenCommands), "Spoken command", "add it with SpokenCommands.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a phrase only names what runs; the last registration of a phrase wins");

    /// <summary>Registers <paramref name="command"/> under its phrase.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]   // the caller is the registering assembly
    public static void Register(SpokenCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Table.Register(command, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the spoken command of that phrase; false when none was registered.</summary>
    public static bool Unregister(string phrase) => Table.Unregister(phrase);

    /// <summary>The registered phrases, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered spoken commands, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<SpokenCommand> All => Table.Kinds;

    /// <summary>The spoken command of that phrase, or null.</summary>
    public static SpokenCommand? Find(ReadOnlySpan<char> phrase) => Table.Find(phrase);

    /// <summary>The spoken command of that phrase.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static SpokenCommand Get(ReadOnlySpan<char> phrase) => Table.Get(phrase);
}
