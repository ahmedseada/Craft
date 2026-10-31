// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Search;

/// <summary>The code search's settings (plug-in "search", variables <c>CRAFT_PLUGIN_SEARCH_&lt;KEY&gt;</c>).</summary>
public sealed record SearchSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "search";

    /// <summary>The keyword index, by its name in Idrak.Text's KeywordIndexes (<c>keywordIndex</c>): this plug-in's "code-bm25".</summary>
    public string KeywordIndex { get; init; } = CodeKeywordIndexKind.IndexName;

    /// <summary>The embedding model (<c>embeddingModel</c>): a name Idrak's model sources resolve to an encoder; empty: BM25 alone.</summary>
    public string EmbeddingModel { get; init; } = "";

    /// <summary>The device the embedding model runs on (<c>embeddingDevice</c>): auto, cpu, cuda:0, vulkan:0.</summary>
    public string EmbeddingDevice { get; init; } = "auto";

    /// <summary>Tokens of a chunk the embedding model reads (<c>embeddingMaxTokens</c>, at most the model's own).</summary>
    public int EmbeddingMaxTokens { get; init; } = 256;

    /// <summary>Characters of a chunk given to the embedding model (<c>embeddingChars</c>).</summary>
    public int EmbeddingCharacters { get; init; } = 2000;

    /// <summary>Chunks embedded together (<c>embeddingBatch</c>).</summary>
    public int EmbeddingBatch { get; init; } = 32;

    /// <summary>The Idrak plug-in packages with the encoder families (<c>idrakPlugins</c>), registered when an embedding model loads.</summary>
    public IReadOnlyList<string> IdrakPlugins { get; init; } = ["Idrak.Text.Plugins"];

    /// <summary>The file extensions indexed (<c>extensions</c>); C# files by Roslyn's syntax, the others by windows of lines.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [".cs", ".csx", ".md", ".json", ".xml", ".csproj", ".props", ".targets", ".razor", ".xaml", ".yml", ".yaml", ".txt"];

    /// <summary>Folder names left out wherever they are (<c>ignore</c>).</summary>
    public IReadOnlyList<string> Ignore { get; init; } = ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "test-output", ".craft"];

    /// <summary>Files larger than this are left out, KiB (<c>maxFileKb</c>).</summary>
    public int MaxFileKilobytes { get; init; } = 1024;

    /// <summary>Lines in each chunk of a file that is not C# (<c>textChunkLines</c>).</summary>
    public int TextChunkLines { get; init; } = 40;

    /// <summary>Characters of a chunk whose words are indexed (<c>chunkMaxChars</c>; a longer member is cut).</summary>
    public int ChunkMaxCharacters { get; init; } = 16_000;

    /// <summary>Results the Code search view shows (<c>results</c>).</summary>
    public int Results { get; init; } = 50;

    /// <summary>Results search_code gives a model (<c>toolResults</c>).</summary>
    public int ToolResults { get; init; } = 12;

    /// <summary>Reciprocal rank fusion's constant (<c>fusionK</c>): score = Σ 1 / (k + rank).</summary>
    public int FusionK { get; init; } = 60;

    /// <summary>How many of each search's hits are fused (<c>fusionDepth</c>).</summary>
    public int FusionDepth { get; init; } = 100;

    /// <summary>The pause after a file changes before the index is brought up to date, ms (<c>reindexDelayMs</c>).</summary>
    public int ReindexDelayMilliseconds { get; init; } = 750;

    /// <summary>The pause after typing in the Code search view before it searches, ms (<c>queryDelayMs</c>).</summary>
    public int QueryDelayMilliseconds { get; init; } = 120;

    /// <summary>Whether the workspace's files are watched (<c>watch</c>); off: the index is brought up to date when a file is saved in the editor.</summary>
    public bool Watch { get; init; } = true;

    /// <summary>Where the indexes are kept (<c>indexFolder</c>); empty: the app's data folder's "search".</summary>
    public string IndexFolder { get; init; } = "";

    /// <summary>Threads that parse files (<c>threads</c>); 0: every core.</summary>
    public int Threads { get; init; }

    /// <summary>The settings it declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("keywordIndex", CodeKeywordIndexKind.IndexName, "The keyword index (Idrak.Text's KeywordIndexes): code-bm25 (identifiers split at camelCase), bm25, or one a plug-in registers."),
        new("embeddingModel", "", "The embedding model (an encoder: a folder, store:NAME or a Hugging Face id); empty: keyword search alone."),
        new("embeddingDevice", "auto", "The device the embedding model runs on: auto, cpu, cuda:0, vulkan:0."),
        new("embeddingMaxTokens", "256", "Tokens of a chunk the embedding model reads (at most the model's own)."),
        new("embeddingChars", "2000", "Characters of a chunk given to the embedding model."),
        new("embeddingBatch", "32", "Chunks embedded together."),
        new("idrakPlugins", "Idrak.Text.Plugins", "The Idrak plug-in packages with the encoder families, registered when the embedding model loads (';' between them)."),
        new("extensions", ".cs;.csx;.md;.json;.xml;.csproj;.props;.targets;.razor;.xaml;.yml;.yaml;.txt", "The file extensions indexed (C# by its syntax, others by lines)."),
        new("ignore", "bin;obj;.git;.vs;.idea;node_modules;test-output;.craft", "Folder names left out wherever they are."),
        new("maxFileKb", "1024", "Files larger than this (KiB) are left out."),
        new("textChunkLines", "40", "Lines in each chunk of a file that is not C#."),
        new("chunkMaxChars", "16000", "Characters of a chunk whose words are indexed (a longer member is cut)."),
        new("results", "50", "Results the Code search view shows."),
        new("toolResults", "12", "Results search_code gives a model."),
        new("fusionK", "60", "Reciprocal rank fusion's constant: score = sum of 1 / (k + rank) over the keyword and vector results."),
        new("fusionDepth", "100", "How many of each search's results are fused."),
        new("reindexDelayMs", "750", "The pause after a file changes before the index is brought up to date, ms."),
        new("queryDelayMs", "120", "The pause after typing in the Code search view before it searches, ms."),
        new("watch", "true", "Whether the workspace's files are watched; off: the index follows files saved in the editor."),
        new("indexFolder", "", "Where the indexes are kept; empty: the app's data folder's 'search'."),
        new("threads", "0", "Threads that parse files while indexing; 0: every core."),
    ];

    /// <summary>The settings from the environment (a host that has none of its own).</summary>
    public static SearchSettings FromEnvironment() => Read(key => Environment.GetEnvironmentVariable(PluginSetting.VariableName(PluginName, key)));

    /// <summary>The settings from <paramref name="settings"/> (the host's).</summary>
    public static SearchSettings From(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Read(key => settings.Get(PluginName, key));
    }

    /// <summary>The settings from <paramref name="value"/> (a key's text, or null for its default); a value that does not parse throws, naming its variable.</summary>
    public static SearchSettings Read(Func<string, string?> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var d = new SearchSettings();
        string? Text(string key) => value(key) is { } text && text.Trim() is { Length: > 0 } trimmed ? trimmed : null;
        int Whole(string key, int fallback, int min, int max) => Text(key) is not { } text ? fallback
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= min && number <= max ? number
            : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': it takes a whole number from {min} to {max}.", key);
        bool Flag(string key, bool fallback) => Text(key) switch
        {
            null => fallback,
            var t when t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1" => true,
            var t when t.Equals("false", StringComparison.OrdinalIgnoreCase) || t == "0" => false,
            var t => throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{t}': it takes true or false.", key),
        };
        IReadOnlyList<string> List(string key, IReadOnlyList<string> fallback) =>
            value(key) is { } text ? text.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : fallback;

        return new SearchSettings
        {
            KeywordIndex = Text("keywordIndex") ?? d.KeywordIndex,
            EmbeddingModel = Text("embeddingModel") ?? "",
            EmbeddingDevice = Text("embeddingDevice") ?? d.EmbeddingDevice,
            EmbeddingMaxTokens = Whole("embeddingMaxTokens", d.EmbeddingMaxTokens, 8, 1_000_000),
            EmbeddingCharacters = Whole("embeddingChars", d.EmbeddingCharacters, 16, 10_000_000),
            EmbeddingBatch = Whole("embeddingBatch", d.EmbeddingBatch, 1, 100_000),
            IdrakPlugins = List("idrakPlugins", d.IdrakPlugins),
            Extensions = [.. List("extensions", d.Extensions).Select(e => e.StartsWith('.') ? e : "." + e)],
            Ignore = List("ignore", d.Ignore),
            MaxFileKilobytes = Whole("maxFileKb", d.MaxFileKilobytes, 1, 10_000_000),
            TextChunkLines = Whole("textChunkLines", d.TextChunkLines, 1, 1_000_000),
            ChunkMaxCharacters = Whole("chunkMaxChars", d.ChunkMaxCharacters, 64, 100_000_000),
            Results = Whole("results", d.Results, 1, 100_000),
            ToolResults = Whole("toolResults", d.ToolResults, 1, 10_000),
            FusionK = Whole("fusionK", d.FusionK, 1, 100_000),
            FusionDepth = Whole("fusionDepth", d.FusionDepth, 1, 1_000_000),
            ReindexDelayMilliseconds = Whole("reindexDelayMs", d.ReindexDelayMilliseconds, 0, 600_000),
            QueryDelayMilliseconds = Whole("queryDelayMs", d.QueryDelayMilliseconds, 0, 60_000),
            Watch = Flag("watch", d.Watch),
            IndexFolder = Text("indexFolder") ?? "",
            Threads = Whole("threads", 0, 0, 100_000),
        };
    }

    /// <summary>What decides an index file's chunks and vectors: a file kept under other settings is built again.</summary>
    public string Signature => string.Join('|', "craft-search/1", KeywordIndex, EmbeddingModel, EmbeddingMaxTokens.ToString(CultureInfo.InvariantCulture),
        EmbeddingCharacters.ToString(CultureInfo.InvariantCulture), string.Join(';', Extensions), string.Join(';', Ignore), MaxFileKilobytes.ToString(CultureInfo.InvariantCulture),
        TextChunkLines.ToString(CultureInfo.InvariantCulture), ChunkMaxCharacters.ToString(CultureInfo.InvariantCulture));
}
