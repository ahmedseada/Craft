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

    // The approved design's tokens (docs/design, phase D), for the app's own screens (the diff view first).

    /// <summary>The window's ground, behind the surfaces.</summary>
    public uint Ground { get; init; } = 0xFF101217;

    /// <summary>A panel's or a page's surface.</summary>
    public uint Surface { get; init; } = 0xFF16181D;

    /// <summary>A surface's border and dividers.</summary>
    public uint Border { get; init; } = 0xFF2A2E37;

    /// <summary>Secondary text (labels, line numbers, details).</summary>
    public uint SecondaryText { get; init; } = 0xFF9AA2B1;

    /// <summary>The accent (the primary action, the active tab's line).</summary>
    public uint Accent { get; init; } = 0xFFE5A54B;

    /// <summary>Success (an added line's marker, a check that passed).</summary>
    public uint Success { get; init; } = 0xFF5FC9B4;

    /// <summary>An added line's background in a diff.</summary>
    public uint DiffAdded { get; init; } = 0xFF15302C;

    /// <summary>A removed line's background in a diff.</summary>
    public uint DiffRemoved { get; init; } = 0xFF3A1F26;
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
        // A static name's class ("method name + static symbol"): its own class's style with the static symbol's bold and
        // italic over it, made here once, so StyleOf stays one lookup.
        var all = new Dictionary<string, TextStyle>(styles, StringComparer.Ordinal);
        if (styles.TryGetValue(TokenClasses.StaticSymbol, out var @static))
        {
            foreach (var (tokenClass, style) in styles)
            {
                if (tokenClass != TokenClasses.StaticSymbol && !tokenClass.EndsWith(TokenClasses.StaticSymbolSuffix, StringComparison.Ordinal))
                {
                    all.TryAdd(TokenClasses.WithStaticSymbol(tokenClass), style with { Bold = style.Bold || @static.Bold, Italic = style.Italic || @static.Italic });
                }
            }
        }

        _styles = all.ToFrozenDictionary(StringComparer.Ordinal);
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
            Error = 0xFFF07178, Warning = 0xFFE3B27A, Info = 0xFF6CB6FF,
            PopupBackground = 0xFF22262E, PopupBorder = 0xFF4A5260, PopupSelection = 0xFF2C3A55, PopupText = 0xFFD5D9E0,
            Ground = 0xFF101217, Surface = 0xFF16181D, Border = 0xFF2A2E37, SecondaryText = 0xFF9AA2B1, Accent = 0xFFE5A54B, Success = 0xFF5FC9B4,
            DiffAdded = 0xFF15302C, DiffRemoved = 0xFF3A1F26,
        },
        Styles(keyword: 0xFF82AAFF, type: 0xFF5FC9B4, @string: 0xFFE3B27A, comment: 0xFF8790A0, number: 0xFFD9A0E8, method: 0xFFE8D58A,
            local: 0xFFB8C7DC, preprocessor: 0xFFB09EE0, excluded: 0xFF6B7382, punctuation: 0xFFAEB6C2,
            new Semantic(Value: 0xFF8FD19E, Interface: 0xFFB8D7A3, TypeParameter: 0xFF7FD3E6, Member: 0xFF9CCBF0, Parameter: 0xFFC9B8E8,
                Namespace: 0xFFC8CDD6, Constant: 0xFFE0A7C2)));

    private static TableTheme MakeLight() => new TableTheme(Light, "Light: a paper ground, deep inks", isDark: false,
        new EditorColors(Background: 0xFFFFFFFF, Foreground: 0xFF1F2328, Selection: 0xFFC8DAF8, CurrentLine: 0xFFF3F5F8, Caret: 0xFFB2650F,
            Gutter: 0xFFFFFFFF, GutterText: 0xFF6E7781, GutterCurrentText: 0xFF1F2328, BracketMatch: 0xFFDDE6F0, FindMatch: 0xFFFFE3A3)
        {
            Error = 0xFFCF222E, Warning = 0xFF9A6700, Info = 0xFF0969DA,
            PopupBackground = 0xFFF6F8FA, PopupBorder = 0xFF8C959F, PopupSelection = 0xFFC8DAF8, PopupText = 0xFF1F2328,
            Ground = 0xFFF3F4F6, Surface = 0xFFFFFFFF, Border = 0xFFD0D7DE, SecondaryText = 0xFF57606A, Accent = 0xFFB2650F, Success = 0xFF0E7A6B,
            DiffAdded = 0xFFDDF4EC, DiffRemoved = 0xFFFCE4E6,
        },
        Styles(keyword: 0xFF0B4FC6, type: 0xFF0E7A6B, @string: 0xFF9A4A06, comment: 0xFF5F6B78, number: 0xFF8A2BB8, method: 0xFF795E00,
            local: 0xFF2A4A75, preprocessor: 0xFF6F42C1, excluded: 0xFF8C959F, punctuation: 0xFF404852,
            new Semantic(Value: 0xFF2E7D32, Interface: 0xFF4D6B16, TypeParameter: 0xFF16697F, Member: 0xFF0B5C8E, Parameter: 0xFF6A3D9A,
                Namespace: 0xFF404852, Constant: 0xFF9C2F6B)));

    /// <summary>
    /// The hues of the semantic classes beside the syntactic ones: value types (structs, enums), interfaces, type
    /// parameters, members (properties, fields, events), parameters, namespaces and constants (enum members too).
    /// </summary>
    private sealed record Semantic(uint Value, uint Interface, uint TypeParameter, uint Member, uint Parameter, uint Namespace, uint Constant);

    private static Dictionary<string, TextStyle> Styles(uint keyword, uint type, uint @string, uint comment, uint number, uint method,
        uint local, uint preprocessor, uint excluded, uint punctuation, Semantic semantic) => new(StringComparer.Ordinal)
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
        [TokenClasses.StructName] = new(semantic.Value),
        [TokenClasses.RecordStructName] = new(semantic.Value),
        [TokenClasses.InterfaceName] = new(semantic.Interface),
        [TokenClasses.EnumName] = new(semantic.Value),
        [TokenClasses.DelegateName] = new(type),
        [TokenClasses.TypeParameterName] = new(semantic.TypeParameter),
        [TokenClasses.MethodName] = new(method),
        [TokenClasses.ExtensionMethodName] = new(method),
        [TokenClasses.PropertyName] = new(semantic.Member),
        [TokenClasses.FieldName] = new(semantic.Member),
        [TokenClasses.EventName] = new(semantic.Member),
        [TokenClasses.LocalName] = new(local),
        [TokenClasses.ParameterName] = new(semantic.Parameter),
        [TokenClasses.NamespaceName] = new(semantic.Namespace),
        [TokenClasses.ConstantName] = new(semantic.Constant),
        [TokenClasses.EnumMemberName] = new(semantic.Constant),
        [TokenClasses.LabelName] = new(local),
        // Static members and types in their own colour, italic (Roslyn's additive "static symbol").
        [TokenClasses.StaticSymbol] = new(type, Italic: true),
    };
}
