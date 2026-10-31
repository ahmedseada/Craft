// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Craft.Benches;

/// <summary>What the benches share: the generated C# file, the results file, the clock.</summary>
internal static class BenchSupport
{
    /// <summary>The repository's root (the folder holding Craft.slnx), above the program's folder.</summary>
    public static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Craft.slnx")))
            {
                return folder.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>The benches' output folder, test-output/benches, created.</summary>
    public static string OutputFolder()
    {
        var folder = Path.Combine(RepositoryRoot(), "test-output", "benches");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A realistic C# file of <paramref name="lines"/> lines (CRLF), made the same way every run.</summary>
    public static string GenerateCSharp(int lines)
    {
        var text = new StringBuilder(lines * 40);
        text.Append("// A generated file for Craft's benches.\r\nusing System;\r\nusing System.Collections.Generic;\r\n\r\nnamespace Bench.Generated;\r\n\r\n");
        int written = 6, type = 0;
        while (written < lines - 2)
        {
            text.Append(CultureInfo.InvariantCulture, $"/// <summary>Type {type}: a class of members.</summary>\r\npublic sealed class Type{type}<T> where T : notnull\r\n{{\r\n");
            text.Append(CultureInfo.InvariantCulture, $"    private readonly List<T> _items = [];\r\n    private int _count = {type};\r\n\r\n");
            written += 6;
            for (int member = 0; member < 8 && written < lines - 12; member++)
            {
                text.Append(CultureInfo.InvariantCulture, $"    /// <summary>Member {member} of type {type}.</summary>\r\n");
                text.Append(CultureInfo.InvariantCulture, $"    public string Method{member}(int value, T item)\r\n    {{\r\n");
                text.Append(CultureInfo.InvariantCulture, $"        if (value > {member} && _count++ < 0x{type & 0xFFFF:X4})\r\n        {{\r\n");
                text.Append(CultureInfo.InvariantCulture, $"            _items.Add(item); // keep it\r\n            return $\"{{value}} of {{nameof(Method{member})}}: \\t{member}\";\r\n        }}\r\n\r\n");
                text.Append(CultureInfo.InvariantCulture, $"        var raw = \"\"\"\r\n            raw {member}\r\n            \"\"\";\r\n        return raw + @\"verbatim\\path\" + 'c' + {member}.5m.ToString();\r\n    }}\r\n\r\n");
                written += 15;
            }

            text.Append("}\r\n\r\n");
            written += 2;
            type++;
        }

        while (written < lines)
        {
            text.Append("// end\r\n");
            written++;
        }

        text.Length -= 2;
        return text.ToString();
    }

    /// <summary>Microseconds since <paramref name="start"/>.</summary>
    public static double Microseconds(long start) => Stopwatch.GetElapsedTime(start).TotalMicroseconds;

    /// <summary>Formats a number for the results, invariantly.</summary>
    public static string F(double value, int decimals = 2) => value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>The median of <paramref name="values"/> (sorted in place).</summary>
    public static double Median(double[] values)
    {
        Array.Sort(values);
        return values.Length == 0 ? 0 : values[values.Length / 2];
    }

    /// <summary>The 99th percentile of <paramref name="values"/> (sorted in place).</summary>
    public static double P99(double[] values)
    {
        Array.Sort(values);
        return values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)(values.Length * 0.99))];
    }

    /// <summary>Writes the results to the console and to <paramref name="file"/> in the output folder.</summary>
    public static void Report(string file, StringBuilder results)
    {
        var path = Path.Combine(OutputFolder(), file);
        File.WriteAllText(path, results.ToString());
        Console.Write(results);
        Console.WriteLine($"(written to {path})");
    }
}
