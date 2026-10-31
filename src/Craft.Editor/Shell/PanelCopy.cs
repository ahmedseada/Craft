// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// What the bottom panel's lists put on the clipboard (the owner's note 19): Output's and the telemetry's lines as they
/// show, a Problems row as a build tool writes it ("Program.cs(12,5): error CS1002: ; expected"), the chosen rows in the
/// list's order (not the order they were chosen in), a line break between rows.
/// </summary>
public static class PanelCopy
{
    /// <summary><paramref name="lines"/> joined by the system's line break.</summary>
    public static string Lines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>A problem as one line: file(line,column): severity code: message (the line and column from 1).</summary>
    public static string Problem(string? file, int line, int column, DiagnosticSeverity severity, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        string word = severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "info",
            _ => "hidden",
        };
        return string.Create(CultureInfo.InvariantCulture, $"{file ?? "(untitled)"}({line},{column}): {word} {code}: {message.ReplaceLineEndings(" ")}");
    }

    /// <summary>The <paramref name="chosen"/> rows of <paramref name="rows"/> in the list's order; rows no longer in the list are left out.</summary>
    public static IReadOnlyList<T> InListOrder<T>(IReadOnlyList<T> rows, IEnumerable<T> chosen)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(chosen);
        var set = new HashSet<T>(chosen, ReferenceEqualityComparer.Instance);
        if (set.Count == 0)
        {
            return [];
        }

        var list = new List<T>(set.Count);
        foreach (var row in rows)
        {
            if (set.Contains(row))
            {
                list.Add(row);
            }
        }

        return list;
    }
}
