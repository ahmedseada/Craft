// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Plugins.Tools;

/// <summary>The editor's tools' names (the same in both backings).</summary>
public static class EditorToolNames
{
    /// <summary>The open documents (live only).</summary>
    public const string OpenDocuments = "open_documents";

    /// <summary>The files under the folder (folder backing).</summary>
    public const string ListFiles = "list_files";

    /// <summary>Lines of a document or file.</summary>
    public const string ReadDocument = "read_document";

    /// <summary>The active document's selection (live only).</summary>
    public const string GetSelection = "get_selection";

    /// <summary>A document's diagnostics (live only: the language's semantic layer).</summary>
    public const string GetDiagnostics = "get_diagnostics";

    /// <summary>Lines that hold a text, across the open documents and the folder's files.</summary>
    public const string SearchWorkspace = "search_workspace";

    /// <summary>Where symbols are declared, found by name (the language's semantic layer: Roslyn for C#).</summary>
    public const string FindSymbol = "find_symbol";

    /// <summary>An edit the user accepts in the diff view (live), or a patch file the user applies (folder).</summary>
    public const string ProposeEdit = "propose_edit";
}

/// <summary>
/// The editor's tools as Idrak <see cref="ITool"/>s, with two backings behind the same definitions (plan phases A1 and P1):
/// <see cref="Live"/> over an <see cref="IEditorHost"/> inside the app (the open documents with their unsaved text, the
/// selection, the diagnostics, <c>propose_edit</c> into the diff view the user accepts), and <see cref="Folder"/> over a
/// folder for any other process (<c>idrak tools test</c>, <c>idrak mcp serve</c>, <c>idrak chat --tools</c>), whose
/// <c>propose_edit</c> writes a patch file and never changes a file. Neither edits without the user. The schemas are
/// explicit (no reflection: trimming and AOT safe).
/// </summary>
public static class EditorTools
{
    private static readonly JsonNode NoArguments = Schema("""{"type":"object","properties":{}}""");

    private static readonly JsonNode ReadArguments = Schema("""
        {"type":"object","properties":{
          "path":{"type":"string","description":"The document: its path (relative to the folder, or full), or 'untitled-N' for an unsaved new one."},
          "start_line":{"type":"integer","description":"The first line to return, from 1.","default":1},
          "line_count":{"type":"integer","description":"How many lines to return (at most the editor's limit)."}},
         "required":["path"]}
        """);

    private static readonly JsonNode SearchArguments = Schema("""
        {"type":"object","properties":{
          "query":{"type":"string","description":"The text to find (not a pattern)."},
          "ignore_case":{"type":"boolean","description":"Whether case is ignored.","default":false},
          "max_results":{"type":"integer","description":"The most matching lines to return (at most the editor's limit)."}},
         "required":["query"]}
        """);

    private static readonly JsonNode ProposeArguments = Schema("""
        {"type":"object","properties":{
          "path":{"type":"string","description":"The document to change: its path, or 'untitled-N'."},
          "old_text":{"type":"string","description":"The exact text to replace; it must occur once. Empty inserts new_text at the start of 'line' (or at the end)."},
          "new_text":{"type":"string","description":"The text that takes its place."},
          "line":{"type":"integer","description":"For an insertion (empty old_text): the line it goes before, from 1."},
          "title":{"type":"string","description":"What the change does, in a few words (the undo step's name)."}},
         "required":["path","old_text","new_text"]}
        """);

    private static readonly JsonNode SymbolArguments = Schema("""
        {"type":"object","properties":{
          "query":{"type":"string","description":"The symbol's name or part of it (ignoring case; its capitals also match: 'FB' finds FolderBacking)."},
          "max_results":{"type":"integer","description":"The most symbols to return (at most the editor's limit)."}},
         "required":["query"]}
        """);

    private static readonly JsonNode DiagnosticsArguments = Schema("""
        {"type":"object","properties":{
          "path":{"type":"string","description":"The document; the active one when not given."}}}
        """);

    /// <summary>
    /// The live tools over <paramref name="host"/>: <c>open_documents</c>, <c>read_document</c>, <c>get_selection</c>,
    /// <c>get_diagnostics</c>, <c>search_workspace</c>, <c>find_symbol</c>, <c>propose_edit</c>. Each reads the host on its UI thread
    /// (<see cref="IEditorHost.Dispatch(Action)"/>) and does its work off it, so a call from an MCP client's thread is safe.
    /// </summary>
    public static IReadOnlyList<ITool> Live(IEditorHost host, ToolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        var live = new LiveBacking(host, settings);
        return
        [
            Tool.Create(EditorToolNames.OpenDocuments, "The documents open in the editor: path, language, lines, whether unsaved, which is active.", NoArguments, live.OpenDocumentsAsync),
            Tool.Create(EditorToolNames.ReadDocument, "Lines of an open document (its text as the editor has it, unsaved changes included) or of a file in the open folder.", ReadArguments, live.ReadAsync),
            Tool.Create(EditorToolNames.GetSelection, "The active document's selection: its path, start and end (line and column, from 1) and the selected text.", NoArguments, live.SelectionAsync),
            Tool.Create(EditorToolNames.GetDiagnostics, "A document's errors and warnings from its language (line, column, severity, id, message).", DiagnosticsArguments, live.DiagnosticsAsync),
            Tool.Create(EditorToolNames.SearchWorkspace, "Lines holding a text, in the open documents and the open folder's files: path:line: text.", SearchArguments, live.SearchAsync),
            Tool.Create(EditorToolNames.FindSymbol, "Where types and members are declared, by name: path:line:column: kind name — signature. The open documents' unsaved text included.",
                SymbolArguments, live.FindSymbolAsync),
            Tool.Create(EditorToolNames.ProposeEdit, "Proposes a change to an open document. The user sees it as a diff and accepts or rejects it; nothing changes without them. Answers whether it was applied.",
                ProposeArguments, live.ProposeAsync),
        ];
    }

    /// <summary>
    /// The folder tools over <paramref name="folder"/>: <c>list_files</c>, <c>read_document</c>, <c>search_workspace</c>,
    /// <c>find_symbol</c> (through the registered languages: Craft.CSharp when it is loaded beside them), <c>propose_edit</c> (a unified patch under <see cref="ToolSettings.PatchFolder"/>, never a change to the file).
    /// </summary>
    public static IReadOnlyList<ITool> Folder(string folder, ToolSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(settings);
        var files = new FolderBacking(Path.GetFullPath(folder), settings);
        return
        [
            Tool.Create(EditorToolNames.ListFiles, "The files under the folder (relative paths), skipping build output and hidden folders.", NoArguments, files.ListAsync),
            Tool.Create(EditorToolNames.ReadDocument, "Lines of a file under the folder.", ReadArguments, files.ReadAsync),
            Tool.Create(EditorToolNames.SearchWorkspace, "Lines holding a text in the folder's files: path:line: text.", SearchArguments, files.SearchAsync),
            Tool.Create(EditorToolNames.FindSymbol, "Where types and members are declared in the folder's code, by name: path:line:column: kind name — signature.", SymbolArguments, files.FindSymbolAsync),
            Tool.Create(EditorToolNames.ProposeEdit, "Proposes a change to a file as a patch the user applies (git apply); the file itself is not changed. Answers where the patch is.",
                ProposeArguments, files.ProposeAsync),
        ];
    }

    private static JsonNode Schema(string json) => JsonNode.Parse(json)!;

    internal static string Text(JsonObject arguments, string name) =>
        arguments[name] is JsonValue value && value.TryGetValue(out string? text) ? text : throw new ArgumentException($"'{name}' must be a string.", name);

    internal static string? OptionalText(JsonObject arguments, string name) =>
        arguments[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    internal static int? Whole(JsonObject arguments, string name)
    {
        if (arguments[name] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue(out int number))
        {
            return number;
        }

        return value.TryGetValue(out double real) && real == Math.Floor(real) && real is >= int.MinValue and <= int.MaxValue
            ? (int)real
            : throw new ArgumentException($"'{name}' must be a whole number.", name);
    }

    internal static bool Flag(JsonObject arguments, string name) => arguments[name] is JsonValue value && value.TryGetValue(out bool flag) && flag;

    internal static string Search(JsonObject arguments, ToolSettings settings, Action<string, StringComparison, byte[], List<string>, int> search)
    {
        string query = Text(arguments, "query");
        if (query.Length == 0)
        {
            throw new ArgumentException("'query' must not be empty.", "query");
        }

        int limit = Math.Clamp(Whole(arguments, "max_results") ?? settings.SearchResults, 1, settings.SearchResults);
        var comparison = Flag(arguments, "ignore_case") ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var hits = new List<string>();
        search(query, comparison, Encoding.UTF8.GetBytes(query), hits, limit);
        return hits.Count == 0
            ? $"No line holds \"{query}\"."
            : string.Join('\n', hits) + (hits.Count >= limit ? string.Create(CultureInfo.InvariantCulture, $"\n(stopped at {limit} matches)") : "");
    }
}
