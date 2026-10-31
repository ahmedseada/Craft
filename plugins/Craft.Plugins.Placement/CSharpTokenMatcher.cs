// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Placement;
using Idrak.Abstraction.Plugins;
using Idrak.Text.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

[assembly: IdrakPluginPackage(typeof(PlacementPackage))]

namespace Craft.Plugins.Placement;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Placement\.</summary>
public sealed class PlacementPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.Placement";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new PlacementRegistration()];
}

/// <summary>Registers "csharp-tokens" in Idrak.Text's <see cref="TextMatchers"/> and the editor plug-in "placement", each with how to take it back.</summary>
public sealed class PlacementRegistration() : IdrakPlugin(PlacementPlugin.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => TextMatchers.Register(CSharpTokenMatcher.Instance), () => TextMatchers.Unregister(CSharpTokenMatcher.MatcherName));
        Part(() => EditorPlugins.Register(PlacementPlugin.Instance), () => EditorPlugins.Unregister(PlacementPlugin.PluginName));
    }
}

/// <summary>How a snippet was placed (<see cref="CSharpTokenMatcher.Place"/>).</summary>
/// <param name="Match">Where, or null when nowhere.</param>
/// <param name="How">"exact", "whitespace differed" (the same tokens, other spacing or comments), or "semi-global" (no run of the same tokens: the library's nearest span).</param>
public sealed record Placement(TextMatch? Match, string How)
{
    /// <summary>What the diff view shows: "placed by csharp-tokens · exact".</summary>
    public override string ToString() => $"placed by {(How == SemiGlobalHow ? TextMatchers.SemiGlobal : CSharpTokenMatcher.MatcherName)} · {How}";

    internal const string SemiGlobalHow = "semi-global";
}

/// <summary>
/// "csharp-tokens" (plan phase P4): a model's snippet is placed where the text holds the same run of C# tokens (Roslyn's
/// lexer: identifiers, keywords, literals and punctuation; whitespace and comments between them do not count), the run
/// nearest the snippet by edit distance when there are several; its distance is the snippet's edit distance (over Unicode
/// scalars) to that span. A snippet whose tokens are nowhere in the text is matched by the library's
/// <see cref="TextMatchers.SemiGlobal"/>. Thread-safe.
/// </summary>
public sealed class CSharpTokenMatcher : ITextMatcher
{
    /// <summary>Its name in <see cref="TextMatchers"/>.</summary>
    public const string MatcherName = "csharp-tokens";

    private static readonly CSharpParseOptions Options = new(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

    private CSharpTokenMatcher(string name) => Name = name;

    /// <summary>The matcher under its own name.</summary>
    public static CSharpTokenMatcher Instance { get; } = new(MatcherName);

    /// <summary>The matcher under the library's name ("semi-global"): what the setting replaceSemiGlobal registers.</summary>
    public static CSharpTokenMatcher OverSemiGlobal { get; } = new(TextMatchers.SemiGlobal);

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public TextMatch? Best(string draft, string text, int from, TextMatchOptions options) => Place(draft, text, from, options).Match;

    /// <summary>Where <paramref name="draft"/> goes in <paramref name="text"/> from <paramref name="from"/>, and how it was placed.</summary>
    public static Placement Place(string draft, string text, int from = 0, TextMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, text.Length);
        options ??= new TextMatchOptions();
        if (draft.Length == 0 || from == text.Length)
        {
            return new Placement(null, Placement.SemiGlobalHow);
        }

        var wanted = Tokens(draft, 0);
        if (wanted.Count > 0)
        {
            var have = Tokens(text, from);
            TextMatch? best = null;
            for (int i = 0; i + wanted.Count <= have.Count; i++)
            {
                if (!SameRun(have, i, wanted))
                {
                    continue;
                }

                int start = have[i].Start, end = have[i + wanted.Count - 1].End;
                int distance = Distance(draft, text.AsSpan(start, end - start));
                if (best is not { } known || distance < known.Distance)
                {
                    best = new TextMatch(start, end - start, distance);
                    if (distance == 0)
                    {
                        break;
                    }
                }
            }

            if (best is { } found)
            {
                return new Placement(found, found.Distance == 0 ? "exact" : "whitespace differed");
            }
        }

        var library = TextMatchers.Default(TextMatchers.SemiGlobal)
            ?? throw new InvalidOperationException($"The library's '{TextMatchers.SemiGlobal}' matcher is missing: {MatcherName} falls back to it.");
        return new Placement(library.Best(draft, text, from, options), Placement.SemiGlobalHow);
    }

    // The tokens of text[from..]: each token's text and its span, trivia (whitespace, comments) left out.
    private static List<(string Text, int Start, int End)> Tokens(string text, int from)
    {
        var tokens = new List<(string, int, int)>();
        foreach (var token in SyntaxFactory.ParseTokens(text, from, from, Options))
        {
            if (token.IsKind(SyntaxKind.EndOfFileToken) || token.Span.Length == 0)
            {
                continue;
            }

            tokens.Add((token.Text, token.SpanStart, token.Span.End));
        }

        return tokens;
    }

    private static bool SameRun(List<(string Text, int Start, int End)> have, int at, List<(string Text, int Start, int End)> wanted)
    {
        for (int j = 0; j < wanted.Count; j++)
        {
            if (!string.Equals(have[at + j].Text, wanted[j].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // Levenshtein distance over Unicode scalars (two rows; pooled when long).
    internal static int Distance(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var x = Scalars(a);
        var y = Scalars(b);
        var previous = new int[y.Length + 1];
        var current = new int[y.Length + 1];
        for (int j = 0; j <= y.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= x.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= y.Length; j++)
            {
                int substitute = previous[j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitute, Math.Min(previous[j], current[j - 1]) + 1);
            }

            (previous, current) = (current, previous);
        }

        return previous[y.Length];
    }

    private static int[] Scalars(ReadOnlySpan<char> text)
    {
        var scalars = new List<int>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            scalars.Add(rune.Value);
        }

        return [.. scalars];
    }
}

/// <summary>
/// The placement plug-in as the editor lists it ("placement"): its setting <c>replaceSemiGlobal</c> (false: "csharp-tokens"
/// is a matcher of its own; true: it also takes the library's "semi-global" slot, which it falls back to).
/// </summary>
public sealed class PlacementPlugin : IEditorPlugin
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "placement";

    private PlacementPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static PlacementPlugin Instance { get; } = new();

    /// <inheritdoc />
    public string Name => PluginName;

    /// <inheritdoc />
    public string Summary => "C#-aware edit placement: a model's snippet is placed by Roslyn's tokens, whitespace ignored";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings { get; } =
    [
        new("replaceSemiGlobal", "false", "Whether csharp-tokens also takes the place of the library's semi-global matcher (it falls back to it for text that is not C#)."),
    ];

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        string value = host.Settings.Get(PluginName, "replaceSemiGlobal").Trim();
        bool replace = value.Length > 0 && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" ? true
            : value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0" ? false
            : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, "replaceSemiGlobal")} is '{value}': it takes true or false."));
        if (replace)
        {
            TextMatchers.Register(CSharpTokenMatcher.OverSemiGlobal);
        }

        return new Deactivation(replace);
    }

    private sealed class Deactivation(bool replaced) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (replaced && !_done)
            {
                _done = true;
                TextMatchers.Unregister(TextMatchers.SemiGlobal);
            }
        }
    }
}
