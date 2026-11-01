// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;

namespace Craft.Web.Script;

/// <summary>What a script lexer reads (W-script): plain JavaScript, JavaScript with JSX, TypeScript, TypeScript with JSX, or an Angular template's expressions (W-markup's).</summary>
public enum ScriptDialect
{
    /// <summary>JavaScript (ES2025 and later): .js, .mjs, .cjs; never JSX.</summary>
    JavaScript,

    /// <summary>JavaScript with JSX: .jsx, and .js in a React or Preact project (or by the <c>jsx</c> setting).</summary>
    Jsx,

    /// <summary>TypeScript: .ts, .mts, .cts, .d.ts; never JSX (a <c>&lt;</c> is a type assertion or a generic).</summary>
    TypeScript,

    /// <summary>TypeScript with JSX: .tsx (<c>&lt;T,&gt;</c> and <c>&lt;T extends U&gt;</c> are generics).</summary>
    Tsx,

    /// <summary>
    /// An Angular template's expressions (the stub W-markup builds on): JavaScript's words with no regular expression and no
    /// JSX, a pipe (<c>| name</c>) named as a method.
    /// </summary>
    AngularExpression,
}

/// <summary>
/// The language version a script lexer reads, as two levels: <see cref="EcmaScript"/> (the year of the edition: 2015 for
/// ES6, 2009 for ES5, <see cref="Latest"/> for ESNext) and <see cref="TypeScript"/> (major × 100 + minor: 509 for 5.9,
/// <see cref="Latest"/> for the newest). An older version reads a newer word as a plain name (<c>satisfies</c> before
/// TypeScript 4.9, <c>using</c> before 5.2 or ES2026); the other syntax lexes the same in every version.
/// </summary>
/// <param name="EcmaScript">The ECMAScript edition's year, or <see cref="Latest"/>.</param>
/// <param name="TypeScript">TypeScript's major × 100 + minor, or <see cref="Latest"/>.</param>
public readonly record struct ScriptVersion(int EcmaScript, int TypeScript)
{
    /// <summary>The level of the newest version (ESNext, the newest TypeScript): every word read.</summary>
    public const int Latest = 9999;

    /// <summary>The newest of both.</summary>
    public static ScriptVersion Newest { get; } = new(Latest, Latest);

    /// <summary>
    /// The ECMAScript level of a <c>target</c> or a setting ("ES2022", "es2015", "ES6", "ES5", "ES3", "ESNext", "latest",
    /// "2022"), ignoring case; null when it is not one.
    /// </summary>
    public static int? ParseEcmaScript(ReadOnlySpan<char> text)
    {
        text = text.Trim();
        if (text.Equals("latest", StringComparison.OrdinalIgnoreCase) || text.Equals("esnext", StringComparison.OrdinalIgnoreCase))
        {
            return Latest;
        }

        if (text.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            return null;
        }

        return number switch
        {
            3 => 1999,
            5 => 2009,
            >= 6 and <= 16 => 2009 + number,   // ES6 is ES2015, ES7 ES2016, ...
            >= 2015 and < Latest => number,
            _ => null,
        };
    }

    /// <summary>
    /// The TypeScript level of a version or a range ("5.4.2", "^5.4.2", "~5.4", "&gt;=5.0", "5", "latest", "next"), the
    /// first number pair read; null when there is none.
    /// </summary>
    public static int? ParseTypeScript(ReadOnlySpan<char> text)
    {
        text = text.Trim();
        if (text.Equals("latest", StringComparison.OrdinalIgnoreCase) || text.Equals("next", StringComparison.OrdinalIgnoreCase)
            || text.Equals("beta", StringComparison.OrdinalIgnoreCase) || text.Equals("rc", StringComparison.OrdinalIgnoreCase)
            || text.Equals("*", StringComparison.Ordinal))
        {
            return Latest;
        }

        int at = 0;
        while (at < text.Length && !char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        if (at == text.Length)
        {
            return null;
        }

        text = text[at..];
        int end = 0;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        if (!int.TryParse(text[..end], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
        {
            return null;
        }

        int minor = 0;
        if (end + 1 < text.Length && text[end] == '.' && char.IsAsciiDigit(text[end + 1]))
        {
            int from = end + 1;
            int to = from;
            while (to < text.Length && char.IsAsciiDigit(text[to]))
            {
                to++;
            }

            _ = int.TryParse(text[from..to], NumberStyles.None, CultureInfo.InvariantCulture, out minor);
        }

        return major >= 99 ? Latest : (major * 100) + Math.Min(minor, 99);
    }

    /// <summary>"ESNext, TypeScript latest"; "ES2022, TypeScript 5.4": for a message.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{(EcmaScript >= Latest ? "ESNext" : "ES" + EcmaScript.ToString(CultureInfo.InvariantCulture))}, TypeScript {(TypeScript >= Latest ? "latest" : (TypeScript / 100).ToString(CultureInfo.InvariantCulture) + "." + (TypeScript % 100).ToString(CultureInfo.InvariantCulture))}");
}
