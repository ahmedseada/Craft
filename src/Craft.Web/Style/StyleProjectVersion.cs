// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;

namespace Craft.Web.Style;

/// <summary>
/// The CSS version a project names: the Baseline year of its browserslist query (<c>baseline 2024</c>, browserslist's own
/// Baseline query), from the nearest <c>.browserslistrc</c> or <c>package.json</c> (its <c>browserslist</c> entry: a
/// string, an array, or per environment with <c>production</c> first) at or above the file's folder, up to the workspace's
/// root. The nearest file that names a browserslist decides; a query with no Baseline year names none. Files are read as
/// UTF-8 bytes (<see cref="Utf8JsonReader"/>, rule 74), bounded in size, on the calling thread: call it off the UI thread.
/// </summary>
public static class StyleProjectVersion
{
    /// <summary>The largest file read (a larger one names nothing).</summary>
    public const int MaxFileBytes = 1 << 20;

    /// <summary>The folders walked up at most.</summary>
    public const int MaxLevels = 32;

    /// <summary>The Baseline year the project of <paramref name="folder"/> names, or null.</summary>
    /// <param name="folder">The file's folder.</param>
    /// <param name="workspaceRoot">The workspace's root (the walk stops there), or null to walk to the drive's root.</param>
    public static int? Read(string folder, string? workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        string? root = string.IsNullOrEmpty(workspaceRoot) ? null : Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? current = Path.GetFullPath(folder);
        for (int level = 0; current is not null && level < MaxLevels; level++)
        {
            if (FromFolder(current, out int? year))
            {
                return year;
            }

            if (root is not null && string.Equals(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    /// <summary>The Baseline year of a browserslist query (<c>baseline 2024</c>, any case), or null when it names none.</summary>
    public static int? FromQuery(ReadOnlySpan<char> query)
    {
        while (true)
        {
            int at = query.IndexOf("baseline", StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return null;
            }

            var rest = query[(at + "baseline".Length)..].TrimStart();
            if (rest.Length >= 4 && int.TryParse(rest[..4], NumberStyles.None, CultureInfo.InvariantCulture, out int year) && (rest.Length == 4 || !char.IsAsciiDigit(rest[4])))
            {
                return year;
            }

            query = query[(at + "baseline".Length)..];
        }
    }

    // True when the folder holds a browserslist (the year it names, or null).
    private static bool FromFolder(string folder, out int? year)
    {
        year = null;
        string rc = Path.Combine(folder, ".browserslistrc");
        if (File.Exists(rc) && Bytes(rc) is { } lines)
        {
            year = FromQuery(System.Text.Encoding.UTF8.GetString(lines));
            return true;
        }

        string package = Path.Combine(folder, "package.json");
        if (File.Exists(package) && Bytes(package) is { } json)
        {
            return FromPackage(json, out year);
        }

        return false;
    }

    private static byte[]? Bytes(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Length > MaxFileBytes ? null : File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads a package.json's top-level <c>browserslist</c>: true when it has one (and the year its queries name, or null).</summary>
    public static bool FromPackage(ReadOnlySpan<byte> json, out int? year)
    {
        year = null;
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool wanted = reader.ValueTextEquals("browserslist"u8);
                if (!reader.Read())
                {
                    return false;
                }

                if (!wanted)
                {
                    reader.TrySkip();
                    continue;
                }

                year = Queries(ref reader);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    // The year named by the value under the reader: a string, an array of strings, or an object of them (production first).
    private static int? Queries(ref Utf8JsonReader reader)
    {
        int? found = null;
        int? production = null;
        int depth = reader.CurrentDepth;
        if (reader.TokenType == JsonTokenType.String)
        {
            return FromQuery(reader.GetString() ?? "");
        }

        bool inProduction = false;
        while (reader.Read() && reader.CurrentDepth > depth)
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                inProduction = reader.ValueTextEquals("production"u8);
            }
            else if (reader.TokenType == JsonTokenType.String && FromQuery(reader.GetString() ?? "") is { } year)
            {
                found ??= year;
                if (inProduction)
                {
                    production ??= year;
                }
            }
        }

        return production ?? found;
    }
}
