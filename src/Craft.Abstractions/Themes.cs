// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;

namespace Craft.Abstractions;

/// <summary>How a run of text is drawn: its colour (0xAARRGGBB) and whether it is bold or italic.</summary>
/// <param name="Argb">The colour, alpha first.</param>
/// <param name="Bold">Drawn bold.</param>
/// <param name="Italic">Drawn italic.</param>
public readonly record struct TextStyle(uint Argb, bool Bold = false, bool Italic = false);

/// <summary>The colours of the editor's surface (0xAARRGGBB each).</summary>
/// <param name="Background">The text area.</param>
/// <param name="Foreground">Plain text.</param>
/// <param name="Selection">The selection's fill.</param>
/// <param name="CurrentLine">The caret line's fill.</param>
/// <param name="Caret">The caret.</param>
/// <param name="Gutter">The line numbers' column.</param>
/// <param name="GutterText">A line number.</param>
/// <param name="GutterCurrentText">The caret line's number.</param>
/// <param name="BracketMatch">The fill behind a matched bracket pair.</param>
/// <param name="FindMatch">The fill behind a find match.</param>
public sealed record EditorColors(uint Background, uint Foreground, uint Selection, uint CurrentLine, uint Caret, uint Gutter,
    uint GutterText, uint GutterCurrentText, uint BracketMatch, uint FindMatch)
{
    /// <summary>An error's squiggle (and its mark in the problems list).</summary>
    public uint Error { get; init; } = 0xFFE5534B;

    /// <summary>A warning's squiggle.</summary>
    public uint Warning { get; init; } = 0xFFC69026;

    /// <summary>An informational diagnostic's dotted underline.</summary>
    public uint Info { get; init; } = 0xFF4A90D9;

    /// <summary>The fill of the editor's popups (the completion list, signature help, hover).</summary>
    public uint PopupBackground { get; init; } = 0xFF2A2E36;

    /// <summary>The popups' border.</summary>
    public uint PopupBorder { get; init; } = 0xFF5A6270;

    /// <summary>The selected row of the completion list.</summary>
    public uint PopupSelection { get; init; } = 0xFF2C3A55;

    /// <summary>The popups' text.</summary>
    public uint PopupText { get; init; } = 0xFFD5D9E0;
}

/// <summary>A theme, registered in <see cref="EditorThemes"/>: the surface's colours and a style per token class.</summary>
public interface ITheme : IKind
{
    /// <summary>Whether it is a dark theme (the app's own chrome follows it).</summary>
    bool IsDark { get; }

    /// <summary>The surface's colours.</summary>
    EditorColors Colors { get; }

    /// <summary>The style of a token class (see <see cref="TokenClasses"/>); plain text for a class it does not know.</summary>
    TextStyle StyleOf(string tokenClass);
}

/// <summary>A theme from a table of styles by class name; a class not in it is drawn as plain text.</summary>
public sealed class TableTheme : ITheme
{
    private readonly FrozenDictionary<string, TextStyle> _styles;
    private readonly TextStyle _plain;

    /// <summary>A theme named <paramref name="name"/> with <paramref name="colors"/> and <paramref name="styles"/> by class name.</summary>
    public TableTheme(string name, string summary, bool isDark, EditorColors colors, IReadOnlyDictionary<string, TextStyle> styles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(styles);
        Name = name;
        Summary = summary;
        IsDark = isDark;
        Colors = colors;
        _plain = new TextStyle(colors.Foreground);
        _styles = styles.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary { get; }

    /// <inheritdoc />
    public bool IsDark { get; }

    /// <inheritdoc />
    public EditorColors Colors { get; }

    /// <inheritdoc />
    public TextStyle StyleOf(string tokenClass) => _styles.TryGetValue(tokenClass, out var style) ? style : _plain;
}

/// <summary>
/// The themes, by name (ignoring case). The library's: <see cref="Dark"/> and <see cref="Light"/>. Register another with
/// <see cref="Register"/>; one under a library name shadows it, which <see cref="Unregister"/> brings back.
/// </summary>
public static class EditorThemes
{
    /// <summary>The library's dark theme.</summary>
    public const string Dark = "dark";

    /// <summary>The library's light theme.</summary>
    public const string Light = "light";

    private static readonly KindTable<ITheme> Table = BuiltIn();

    private static KindTable<ITheme> BuiltIn()
    {
        var table = new KindTable<ITheme>(nameof(EditorThemes), "Theme", "add it with EditorThemes.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a theme is data, read as it is");
        table.RegisterDefault(MakeDark());
        table.RegisterDefault(MakeLight());
        return table;
    }

    /// <summary>Registers <paramref name="theme"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Table.Register(theme, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's theme of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The theme of that name, or null.</summary>
    public static ITheme? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The theme of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ITheme Get(ReadOnlySpan<char> name) => Table.Get(name);

    private static TableTheme MakeDark() => new TableTheme(Dark, "Dark: a deep ground, amber accent", isDark: true,
        new EditorColors(Background: 0xFF16181D, Foreground: 0xFFD5D9E0, Selection: 0xFF2C3A55, CurrentLine: 0xFF1E222B, Caret: 0xFFE5A54B,
            Gutter: 0xFF16181D, GutterText: 0xFF7D8696, GutterCurrentText: 0xFFD5D9E0, BracketMatch: 0xFF34404F, FindMatch: 0xFF5A4520)
        {
            Error = 0xFFF47067, Warning = 0xFFD9A93A, Info = 0xFF6CB6FF,
            PopupBackground = 0xFF22262E, PopupBorder = 0xFF4A5260, PopupSelection = 0xFF2C3A55, PopupText = 0xFFD5D9E0,
        },
        Styles(keyword: 0xFF82AAFF, type: 0xFF5FC9B4, @string: 0xFFE3B27A, comment: 0xFF8790A0, number: 0xFFD9A0E8, method: 0xFFE8D58A,
            local: 0xFFB8C7DC, preprocessor: 0xFFB09EE0, excluded: 0xFF6B7382, punctuation: 0xFFAEB6C2));

    private static TableTheme MakeLight() => new TableTheme(Light, "Light: a paper ground, deep inks", isDark: false,
        new EditorColors(Background: 0xFFFFFFFF, Foreground: 0xFF1F2328, Selection: 0xFFC8DAF8, CurrentLine: 0xFFF3F5F8, Caret: 0xFFB2650F,
            Gutter: 0xFFFFFFFF, GutterText: 0xFF6E7781, GutterCurrentText: 0xFF1F2328, BracketMatch: 0xFFDDE6F0, FindMatch: 0xFFFFE3A3)
        {
            Error = 0xFFCF222E, Warning = 0xFF9A6700, Info = 0xFF0969DA,
            PopupBackground = 0xFFF6F8FA, PopupBorder = 0xFF8C959F, PopupSelection = 0xFFC8DAF8, PopupText = 0xFF1F2328,
        },
        Styles(keyword: 0xFF0B4FC6, type: 0xFF0E7A6B, @string: 0xFF9A4A06, comment: 0xFF5F6B78, number: 0xFF8A2BB8, method: 0xFF795E00,
            local: 0xFF2A4A75, preprocessor: 0xFF6F42C1, excluded: 0xFF8C959F, punctuation: 0xFF404852));

    private static Dictionary<string, TextStyle> Styles(uint keyword, uint type, uint @string, uint comment, uint number, uint method,
        uint local, uint preprocessor, uint excluded, uint punctuation) => new(StringComparer.Ordinal)
    {
        [TokenClasses.Keyword] = new(keyword),
        [TokenClasses.ControlKeyword] = new(keyword),
        [TokenClasses.String] = new(@string),
        [TokenClasses.VerbatimString] = new(@string),
        [TokenClasses.StringEscape] = new(number),
        [TokenClasses.Number] = new(number),
        [TokenClasses.Comment] = new(comment, Italic: true),
        [TokenClasses.DocComment] = new(comment, Italic: true),
        [TokenClasses.DocCommentTag] = new(comment),
        [TokenClasses.Operator] = new(punctuation),
        [TokenClasses.Punctuation] = new(punctuation),
        [TokenClasses.PreprocessorKeyword] = new(preprocessor),
        [TokenClasses.PreprocessorText] = new(preprocessor),
        [TokenClasses.ExcludedCode] = new(excluded),
        [TokenClasses.ClassName] = new(type),
        [TokenClasses.RecordClassName] = new(type),
        [TokenClasses.StructName] = new(type),
        [TokenClasses.RecordStructName] = new(type),
        [TokenClasses.InterfaceName] = new(type),
        [TokenClasses.EnumName] = new(type),
        [TokenClasses.DelegateName] = new(type),
        [TokenClasses.TypeParameterName] = new(type),
        [TokenClasses.MethodName] = new(method),
        [TokenClasses.LocalName] = new(local),
        [TokenClasses.ParameterName] = new(local),
    };
}
