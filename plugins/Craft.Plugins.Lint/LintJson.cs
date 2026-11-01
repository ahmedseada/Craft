// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using Craft.Abstractions;

namespace Craft.Plugins.Lint;

/// <summary>
/// The linters' JSON reports read as UTF-8 bytes with <see cref="Utf8JsonReader"/> (rule 74: no document model, no
/// reflection, strings made only for what is kept): ESLint's <c>--format json</c> (an array of files, each with its
/// messages, fixes and suggestions; <c>fix.range</c> in UTF-16 units, as .NET counts) and TSLint's (an array of failures
/// with 0-based positions). Everything else in a report (ESLint's <c>source</c>, its deprecated rules) is skipped unread.
/// </summary>
public static class LintJson
{
    private static readonly JsonReaderOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 64 };

    /// <summary>ESLint's report; files in its order, each file's diagnostics in its order, the fixes and suggestions after.</summary>
    /// <exception cref="FormatException">The bytes are not ESLint's JSON report (with where it broke).</exception>
    public static IReadOnlyList<LintFileResult> ReadEslint(ReadOnlySpan<byte> json)
    {
        json = SkipBom(json);
        var reader = new Utf8JsonReader(json, Options);
        var files = new List<LintFileResult>();
        try
        {
            Expect(ref reader, JsonTokenType.StartArray, "an array of files");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    reader.Skip();
                    continue;
                }

                files.Add(ReadEslintFile(ref reader));
            }
        }
        catch (JsonException e)
        {
            throw new FormatException($"ESLint's report is not valid JSON at byte {reader.BytesConsumed}: {e.Message}", e);
        }

        return files;
    }

    /// <summary>TSLint's report, grouped by file in the order each file first appears.</summary>
    /// <exception cref="FormatException">The bytes are not TSLint's JSON report.</exception>
    public static IReadOnlyList<LintFileResult> ReadTslint(ReadOnlySpan<byte> json)
    {
        json = SkipBom(json);
        if (json.Trim(" \t\r\n"u8).IsEmpty)
        {
            return [];                                   // TSLint writes nothing when nothing failed
        }

        var reader = new Utf8JsonReader(json, Options);
        var order = new List<string>();
        var diagnostics = new Dictionary<string, List<FileDiagnostic>>(StringComparer.Ordinal);
        var fixes = new Dictionary<string, List<LintFix>>(StringComparer.Ordinal);
        try
        {
            Expect(ref reader, JsonTokenType.StartArray, "an array of failures");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    reader.Skip();
                    continue;
                }

                string file = "", rule = "", failure = "", severity = "";
                (int Line, int Character) start = default, end = default;
                var replacements = new List<(int Start, int Length, string Text)>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.ValueTextEquals("name"u8))
                    {
                        file = String(ref reader);
                    }
                    else if (reader.ValueTextEquals("ruleName"u8))
                    {
                        rule = String(ref reader);
                    }
                    else if (reader.ValueTextEquals("failure"u8))
                    {
                        failure = String(ref reader);
                    }
                    else if (reader.ValueTextEquals("ruleSeverity"u8))
                    {
                        severity = String(ref reader);
                    }
                    else if (reader.ValueTextEquals("startPosition"u8))
                    {
                        start = Position(ref reader);
                    }
                    else if (reader.ValueTextEquals("endPosition"u8))
                    {
                        end = Position(ref reader);
                    }
                    else if (reader.ValueTextEquals("fix"u8))
                    {
                        TslintFix(ref reader, replacements);
                    }
                    else
                    {
                        reader.Read();
                        reader.Skip();
                    }
                }

                if (file.Length == 0)
                {
                    continue;
                }

                if (!diagnostics.TryGetValue(file, out var list))
                {
                    order.Add(file);
                    list = [];
                    diagnostics[file] = list;
                    fixes[file] = [];
                }

                var level = severity.Equals("error", StringComparison.OrdinalIgnoreCase) ? DiagnosticSeverity.Error
                    : severity.Equals("warning", StringComparison.OrdinalIgnoreCase) ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Info;
                list.Add(new FileDiagnostic(start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1, level, rule, failure));
                foreach (var (at, length, text) in replacements)
                {
                    fixes[file].Add(new LintFix(at, length, text, $"Fix: {failure} ({rule})", list.Count - 1));
                }
            }
        }
        catch (JsonException e)
        {
            throw new FormatException($"TSLint's report is not valid JSON at byte {reader.BytesConsumed}: {e.Message}", e);
        }

        return [.. order.Select(file => new LintFileResult(file, diagnostics[file], fixes[file]))];
    }

    /// <summary>The top-level string <paramref name="name"/> of a JSON object (a package.json's "version"), or null; comments and a BOM taken.</summary>
    public static string? TopLevelString(ReadOnlySpan<byte> json, ReadOnlySpan<byte> name)
    {
        try
        {
            var reader = new Utf8JsonReader(SkipBom(json), Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool match = reader.ValueTextEquals(name);
                reader.Read();
                if (match)
                {
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>Whether a JSON object has the top-level property <paramref name="name"/> (a package.json's "prettier" or "eslintConfig").</summary>
    public static bool HasTopLevel(ReadOnlySpan<byte> json, ReadOnlySpan<byte> name)
    {
        try
        {
            var reader = new Utf8JsonReader(SkipBom(json), Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals(name))
                {
                    return true;
                }

                reader.Read();
                reader.Skip();
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static LintFileResult ReadEslintFile(ref Utf8JsonReader reader)
    {
        string path = "";
        var diagnostics = new List<FileDiagnostic>();
        var fixes = new List<LintFix>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("filePath"u8))
            {
                path = String(ref reader);
            }
            else if (reader.ValueTextEquals("messages"u8))
            {
                reader.Read();
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    reader.Skip();
                    continue;
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        ReadEslintMessage(ref reader, diagnostics, fixes);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
            else
            {
                reader.Read();
                reader.Skip();                           // "source", "usedDeprecatedRules", the counts: never read
            }
        }

        return new LintFileResult(path, diagnostics, fixes);
    }

    private static void ReadEslintMessage(ref Utf8JsonReader reader, List<FileDiagnostic> diagnostics, List<LintFix> fixes)
    {
        string rule = "", message = "";
        int severity = 0, line = 1, column = 1, endLine = -1, endColumn = -1;
        bool fatal = false;
        (int Start, int End, string Text)? fix = null;
        var suggestions = new List<(string Title, int Start, int End, string Text)>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("ruleId"u8))
            {
                rule = String(ref reader);
            }
            else if (reader.ValueTextEquals("message"u8))
            {
                message = String(ref reader);
            }
            else if (reader.ValueTextEquals("severity"u8))
            {
                severity = Int(ref reader, 0);
            }
            else if (reader.ValueTextEquals("line"u8))
            {
                line = Int(ref reader, 1);
            }
            else if (reader.ValueTextEquals("column"u8))
            {
                column = Int(ref reader, 1);
            }
            else if (reader.ValueTextEquals("endLine"u8))
            {
                endLine = Int(ref reader, -1);
            }
            else if (reader.ValueTextEquals("endColumn"u8))
            {
                endColumn = Int(ref reader, -1);
            }
            else if (reader.ValueTextEquals("fatal"u8))
            {
                reader.Read();
                fatal = reader.TokenType == JsonTokenType.True;
            }
            else if (reader.ValueTextEquals("fix"u8))
            {
                reader.Read();
                fix = EslintFix(ref reader);
            }
            else if (reader.ValueTextEquals("suggestions"u8))
            {
                reader.Read();
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    reader.Skip();
                    continue;
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    string desc = "";
                    (int Start, int End, string Text)? suggested = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.ValueTextEquals("desc"u8))
                        {
                            desc = String(ref reader);
                        }
                        else if (reader.ValueTextEquals("fix"u8))
                        {
                            reader.Read();
                            suggested = EslintFix(ref reader);
                        }
                        else
                        {
                            reader.Read();
                            reader.Skip();
                        }
                    }

                    if (suggested is { } s)
                    {
                        suggestions.Add((desc.Length > 0 ? desc : "Apply the suggestion", s.Start, s.End, s.Text));
                    }
                }
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        var level = fatal || severity >= 2 ? DiagnosticSeverity.Error : severity == 1 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info;
        diagnostics.Add(new FileDiagnostic(line, column, endLine < 0 ? line : endLine, endColumn < 0 ? column : endColumn, level, rule, message));
        int index = diagnostics.Count - 1;
        if (fix is { } f)
        {
            fixes.Add(new LintFix(f.Start, f.End - f.Start, f.Text, rule.Length > 0 ? $"Fix: {message} ({rule})" : $"Fix: {message}", index));
        }

        foreach (var (title, start, end, text) in suggestions)
        {
            fixes.Add(new LintFix(start, end - start, text, title, index) { Suggestion = true });
        }
    }

    // {"range":[start,end],"text":"…"}: a fix in the linted text's UTF-16 positions; null when it is not one.
    private static (int Start, int End, string Text)? EslintFix(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        int start = -1, end = -1;
        string? text = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("range"u8))
            {
                reader.Read();
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    reader.Skip();
                    continue;
                }

                int at = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
                    {
                        if (at == 0)
                        {
                            start = value;
                        }
                        else if (at == 1)
                        {
                            end = value;
                        }
                    }

                    at++;
                }
            }
            else if (reader.ValueTextEquals("text"u8))
            {
                text = String(ref reader);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        return start >= 0 && end >= start && text is not null ? (start, end, text) : null;
    }

    // TSLint's fix: one replacement {"innerStart","innerLength","innerText"} or an array of them.
    private static void TslintFix(ref Utf8JsonReader reader, List<(int Start, int Length, string Text)> replacements)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                Replacement(ref reader, replacements);
            }
        }
        else
        {
            Replacement(ref reader, replacements);
        }
    }

    private static void Replacement(ref Utf8JsonReader reader, List<(int Start, int Length, string Text)> replacements)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        int start = -1, length = -1;
        string text = "";
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("innerStart"u8))
            {
                start = Int(ref reader, -1);
            }
            else if (reader.ValueTextEquals("innerLength"u8))
            {
                length = Int(ref reader, -1);
            }
            else if (reader.ValueTextEquals("innerText"u8))
            {
                text = String(ref reader);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        if (start >= 0 && length >= 0)
        {
            replacements.Add((start, length, text));
        }
    }

    private static (int Line, int Character) Position(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return default;
        }

        int line = 0, character = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("line"u8))
            {
                line = Int(ref reader, 0);
            }
            else if (reader.ValueTextEquals("character"u8))
            {
                character = Int(ref reader, 0);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        return (line, character);
    }

    // The next value as a string ("" for null or another kind, skipped).
    private static string String(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? "";
        }

        reader.Skip();
        return "";
    }

    private static int Int(ref Utf8JsonReader reader, int fallback)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
        {
            return value;
        }

        reader.Skip();
        return fallback;
    }

    private static void Expect(ref Utf8JsonReader reader, JsonTokenType token, string what)
    {
        if (!reader.Read() || reader.TokenType != token)
        {
            throw new FormatException($"The report is not {what}.");
        }
    }

    private static ReadOnlySpan<byte> SkipBom(ReadOnlySpan<byte> json) => json.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? json[3..] : json;
}
