// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Plugins.Tools;

/// <summary>
/// The folder backing of the editor's tools, for a process that is not the editor: the files under one folder, read
/// from disk; a proposed edit is written as a unified patch the user applies, never into the file.
/// </summary>
internal sealed class FolderBacking(string root, ToolSettings settings)
{
    public Task<string> ListAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var files = new StringBuilder();
        int count = 0;
        foreach (var file in ToolText.Files(root, settings.Ignore))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count == settings.SearchResults * 10)
            {
                files.Append(CultureInfo.InvariantCulture, $"(stopped at {count} files)\n");
                break;
            }

            files.Append(ToolText.Display(root, file)).Append('\n');
            count++;
        }

        return Task.FromResult(count == 0 ? $"No file under {root}." : files.ToString());
    }

    public async Task<string> ReadAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string path = EditorTools.Text(arguments, "path");
        int start = EditorTools.Whole(arguments, "start_line") ?? 1;
        int count = Math.Clamp(EditorTools.Whole(arguments, "line_count") ?? settings.ReadLines, 1, settings.ReadLines);
        if (ToolText.Inside(root, path) is not { } full || !File.Exists(full))
        {
            return NotFound(path);
        }

        var text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
        return ToolText.ReadLines(ToolText.Display(root, full), text, start, count);
    }

    public Task<string> SearchAsync(JsonObject arguments, CancellationToken cancellationToken) =>
        Task.FromResult(EditorTools.Search(arguments, settings, (query, comparison, utf8, hits, limit) =>
        {
            long maxBytes = settings.SearchFileKilobytes * 1024L;
            foreach (var file in ToolText.Files(root, settings.Ignore))
            {
                if (hits.Count >= limit)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                ToolText.SearchFile(ToolText.Display(root, file), file, query, utf8, comparison, hits, limit, maxBytes);
            }
        }));

    public async Task<string> ProposeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string path = EditorTools.Text(arguments, "path");
        string oldText = EditorTools.Text(arguments, "old_text");
        string newText = EditorTools.Text(arguments, "new_text");
        int? line = EditorTools.Whole(arguments, "line");
        if (ToolText.Inside(root, path) is not { } full || !File.Exists(full))
        {
            return NotFound(path);
        }

        var text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
        if (ToolText.Find(text, oldText, newText, line, out var problem) is not { } edit)
        {
            return $"Not proposed: {problem}";
        }

        var changed = string.Concat(text.AsSpan(0, edit.Start), edit.Text, text.AsSpan(edit.End));
        var diff = TextDiff.OfTexts(text, changed);
        if (diff.IsEmpty)
        {
            return "Not proposed: the change leaves the file as it is.";
        }

        var name = ToolText.Display(root, full);
        var patchFolder = Path.GetFullPath(Path.Combine(root, settings.PatchFolder));
        Directory.CreateDirectory(patchFolder);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var patch = Path.Combine(patchFolder, $"{Path.GetFileNameWithoutExtension(full)}-{stamp}.patch");
        await File.WriteAllTextAsync(patch, diff.ToUnified("a/" + name, "b/" + name), cancellationToken).ConfigureAwait(false);
        return string.Create(CultureInfo.InvariantCulture,
            $"Written as a patch (+{diff.Added} −{diff.Removed} lines), not applied: {ToolText.Display(root, patch)}. The user applies it with: git apply \"{patch}\"");
    }

    public async Task<string> FindSymbolAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string query = EditorTools.Text(arguments, "query");
        int limit = Math.Clamp(EditorTools.Whole(arguments, "max_results") ?? settings.SymbolResults, 1, settings.SymbolResults);
        var languages = EditorLanguages.All;
        var found = new List<SymbolLocation>();
        foreach (var language in languages)
        {
            found.AddRange(await language.FindSymbolsAsync(root, query, limit, LanguageSettings.Default, cancellationToken).ConfigureAwait(false));
        }

        return languages.Count == 0
            ? "find_symbol needs a language plug-in that knows symbols: load Craft.CSharp beside these tools (idrak ... -P Craft.CSharp.dll), as the editor does."
            : ToolText.Symbols(root, query, found, limit);
    }

    private string NotFound(string path) => $"No file '{path}' under {root}; list_files shows the files.";
}
