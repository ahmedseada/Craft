// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;

namespace Craft.Core.Settings;

/// <summary>
/// A settings file (the user's <c>settings.json</c> in the data folder, or a workspace's <c>.craft/settings.json</c>): one
/// JSON object whose keys are the settings' variables and whose values are their text, as the environment would hold it:
/// <code>{ "CRAFT_TAB_SIZE": "2", "CRAFT_THEME": "light", "CRAFT_PLUGIN_SAMPLE_SHOWCHARACTERS": true }</code>
/// Numbers and true/false are taken as their text; comments and trailing commas are allowed and a UTF-8 byte-order mark is
/// skipped (Windows editors write one). A key that is not a <c>CRAFT_</c> variable, or a value that is an object, an array or
/// null, is reported in <see cref="Problems"/> and skipped; a file that does not parse gives no values and its reason. A
/// missing file is empty. <see cref="Save"/> writes the values sorted, as strings (comments in the file are not kept).
/// </summary>
public sealed class SettingsFile
{
    private readonly SortedDictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly List<string> _problems = [];

    private SettingsFile(string path) => FilePath = Path.GetFullPath(path);

    /// <summary>The file, as a full path.</summary>
    public string FilePath { get; }

    /// <summary>The values by variable (upper-cased), in order.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>What was wrong with the file when it was read, each naming the key or the reason; empty when nothing was.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The value of <paramref name="variable"/> in this file, or null.</summary>
    public string? Get(string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        // A name already in its stored form (what the options and the plug-ins' settings read) is looked up as it is.
        return _values.GetValueOrDefault(IsVariable(variable) ? variable : Normalize(variable));
    }

    /// <summary>The file at <paramref name="path"/> read (empty when there is none).</summary>
    public static SettingsFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new SettingsFile(path);
        byte[] bytes;
        try
        {
            if (!File.Exists(file.FilePath))
            {
                return file;
            }

            bytes = File.ReadAllBytes(file.FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            file._problems.Add($"{file.FilePath} could not be read: {e.Message}");
            return file;
        }

        file.Read(bytes);
        return file;
    }

    /// <summary>A file at <paramref name="path"/> read from <paramref name="utf8"/> (tests; the file itself is not touched).</summary>
    public static SettingsFile Parse(string path, ReadOnlySpan<byte> utf8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new SettingsFile(path);
        file.Read(utf8);
        return file;
    }

    /// <summary>Sets <paramref name="variable"/> to <paramref name="value"/> (null or empty removes it); true when the file's values changed. <see cref="Save"/> writes it.</summary>
    /// <exception cref="ArgumentException">The variable is not a <c>CRAFT_</c> name.</exception>
    public bool Set(string variable, string? value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        var name = Normalize(variable);
        if (!IsVariable(name))
        {
            throw new ArgumentException($"'{variable}' is not a Craft setting's variable (they start with CRAFT_).", nameof(variable));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return _values.Remove(name);
        }

        var text = value.Trim();
        if (_values.TryGetValue(name, out var old) && old == text)
        {
            return false;
        }

        _values[name] = text;
        return true;
    }

    /// <summary>Writes the values to <see cref="FilePath"/> (its folder made when missing), through a temporary file moved over it.</summary>
    public void Save()
    {
        var folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(folder);
        var temporary = FilePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in _values)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        File.Move(temporary, FilePath, overwrite: true);
    }

    /// <summary>Whether <paramref name="name"/> (upper-cased) is a Craft variable's name: "CRAFT_" and letters, digits or '_'.</summary>
    public static bool IsVariable(ReadOnlySpan<char> name)
    {
        if (name.Length <= 6 || !name.StartsWith("CRAFT_", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string variable) => variable.Trim().ToUpperInvariant();

    private void Read(ReadOnlySpan<byte> utf8)
    {
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            utf8 = utf8[3..];
        }

        if (utf8.Trim((ReadOnlySpan<byte>)[(byte)' ', (byte)'\t', (byte)'\r', (byte)'\n']).IsEmpty)
        {
            return;
        }

        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                _problems.Add($"{FilePath} is not a JSON object of settings ({{ \"CRAFT_TAB_SIZE\": \"2\" }}).");
                return;
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString() ?? "";
                var name = Normalize(key);
                reader.Read();
                string? value = reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString(),
                    JsonTokenType.Number => reader.HasValueSequence ? null : System.Text.Encoding.UTF8.GetString(reader.ValueSpan),
                    JsonTokenType.True => "true",
                    JsonTokenType.False => "false",
                    _ => null,
                };
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }

                if (!IsVariable(name))
                {
                    problems.Add($"{FilePath}: '{key}' is not a Craft setting's variable (they start with CRAFT_); skipped.");
                }
                else if (value is null)
                {
                    problems.Add($"{FilePath}: {name} is not text, a number or true/false; skipped.");
                }
                else if (value.Trim().Length > 0)
                {
                    values[name] = value.Trim();
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                throw new JsonException("text follows the settings' object");
            }
        }
        catch (JsonException e)
        {
            _problems.Add(string.Create(CultureInfo.InvariantCulture, $"{FilePath} does not parse ({e.Message}); none of its settings are used."));
            return;
        }

        foreach (var (name, value) in values)
        {
            _values[name] = value;
        }

        _problems.AddRange(problems);
    }
}
