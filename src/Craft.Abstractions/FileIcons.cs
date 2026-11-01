// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;

namespace Craft.Abstractions;

/// <summary>
/// The icon a file or a folder shows in a list (the tree, the tabs, quick open, the breadcrumb, the welcome page,
/// Problems, search results, menus): a drawn vector <see cref="Shape"/> when it has one (W: the rich icons), else a short
/// badge in a colour. Registered in <see cref="FileIcons"/> under its key.
/// </summary>
/// <param name="Extension">
/// Its key, ignoring case: an extension with its dot (".cs"), a compound suffix (".d.ts", ".component.ts", or "*.spec.ts"),
/// a glob ("tsconfig*.json", "eslint.config.*") or an exact name ("package.json", "Dockerfile"); a folder's is its name
/// ("src", "node_modules") or a glob. <see cref="FilePatternTable{T}.KindOf"/> tells which.
/// </param>
/// <param name="Badge">The badge's text, one to three characters ("C#", "{}"): the fallback where no shape is drawn.</param>
/// <param name="Argb">The badge's colour, 0xAARRGGBB (on a dark theme; <see cref="LightArgb"/> on a light one).</param>
/// <param name="Language">What the files are, for a tooltip ("C# source").</param>
public sealed record FileIcon(string Extension, string Badge, uint Argb, string Language) : IKind
{
    /// <inheritdoc />
    public string Name => Extension;

    /// <inheritdoc />
    public string Summary => $"{Badge}: {Language}";

    /// <summary>The drawn icon (a folder's closed look), or null for the badge alone.</summary>
    public FileIconShape? Shape { get; init; }

    /// <summary>A folder's open look, or null to draw <see cref="Shape"/> open and closed alike.</summary>
    public FileIconShape? OpenShape { get; init; }

    /// <summary>The badge's colour on a light theme, 0xAARRGGBB; 0 for <see cref="Argb"/>.</summary>
    public uint LightArgb { get; init; }

    /// <summary>The badge's colour on a dark or a light theme.</summary>
    public uint BadgeArgb(bool dark) => dark || LightArgb == 0 ? Argb : LightArgb;

    /// <summary>The shape to draw: the open one for an open folder when it has one, else <see cref="Shape"/> (null for none).</summary>
    public FileIconShape? ShapeFor(bool open) => open && OpenShape is not null ? OpenShape : Shape;
}

/// <summary>One layer of a drawn icon: SVG path data on the shape's box, filled (or stroked) in its colour.</summary>
/// <param name="Path">SVG path data ("M2 2h12v12H2z"), in the units of <see cref="FileIconShape.ViewBox"/>.</param>
/// <param name="Argb">Its colour on a dark theme, 0xAARRGGBB.</param>
public sealed record FileIconLayer(string Path, uint Argb)
{
    /// <summary>Its colour on a light theme; 0 for <see cref="Argb"/>.</summary>
    public uint LightArgb { get; init; }

    /// <summary>The stroke's width in the box's units, with round caps and joins; 0 fills the path (the default).</summary>
    public float StrokeWidth { get; init; }

    /// <summary>Its colour on a dark or a light theme.</summary>
    public uint ColorFor(bool dark) => dark || LightArgb == 0 ? Argb : LightArgb;
}

/// <summary>
/// A drawn icon: its layers in order (the first at the back), on a square box of <see cref="ViewBox"/> units (16). The
/// platform parses each layer's path once and keeps it per size it is drawn at (MAUI Graphics' own parser on Windows and
/// Android: no package; docs/optimization.md rules 0, 75), so a warm frame draws without parsing or allocating (rule 63).
/// </summary>
/// <param name="Layers">The layers, back to front.</param>
public sealed record FileIconShape(IReadOnlyList<FileIconLayer> Layers)
{
    /// <summary>The side of the square box the paths are drawn on.</summary>
    public float ViewBox { get; init; } = 16;

    /// <summary>
    /// Why <paramref name="pathData"/> is not SVG path data the drawing parses (it starts with a move; every command has
    /// whole sets of numbers; an arc's flags are 0 or 1), or null when it is.
    /// </summary>
    public static string? Problem(string pathData)
    {
        ArgumentNullException.ThrowIfNull(pathData);
        return SvgPath.Walk(pathData, null, 1, 0, 0);
    }

    /// <summary>
    /// <paramref name="pathData"/> scaled by <paramref name="scale"/> and moved by (<paramref name="dx"/>, <paramref name="dy"/>)
    /// (absolute points moved, relative ones only scaled, arcs' radii scaled): how an icon set places one glyph on several
    /// bases. Made once, when the set is made.
    /// </summary>
    /// <exception cref="FormatException">It is not path data (<see cref="Problem(string)"/> says why).</exception>
    public static string Transform(string pathData, float scale, float dx, float dy)
    {
        ArgumentNullException.ThrowIfNull(pathData);
        var into = new StringBuilder(pathData.Length + 16);
        if (SvgPath.Walk(pathData, into, scale, dx, dy) is { } problem)
        {
            throw new FormatException(problem);
        }

        return into.ToString();
    }

    /// <summary>Why one of the layers is not path data, or null when every one is.</summary>
    public string? Problem()
    {
        foreach (var layer in Layers)
        {
            if (Problem(layer.Path) is { } problem)
            {
                return problem;
            }
        }

        return Layers.Count == 0 ? "a shape has no layer" : null;
    }
}

/// <summary>
/// The file and folder icons of Craft's own set ("craft" in <see cref="FileIconThemes"/>), each by its key (ignoring case):
/// files by exact name, compound suffix, glob and extension (<see cref="FilePatternTable{T}"/>), folders by name. The
/// library's cover the files of a C# and a web developer (W); a plug-in registers its own over them (<see cref="Register"/>),
/// and <see cref="Unregister"/> brings the library's back. <see cref="For"/> and <see cref="ForFolder"/> ask the icon theme
/// in use first (<see cref="FileIconThemes.Current"/>), then this set; a file with none shows <see cref="Plain"/>, a folder
/// <see cref="PlainFolder"/>. Lookups allocate nothing once the table is made (rules 15, 17).
/// </summary>
public static class FileIcons
{
    private static readonly KindTable<FileIcon> Table = MakeTable(nameof(FileIcons), "File icon", "add it with FileIcons.Register", CraftIcons.Files);
    private static readonly KindTable<FileIcon> Folders = MakeTable("FolderIcons", "Folder icon", "add it with FileIcons.RegisterFolder", CraftIcons.Folders);
    private static Matcher? _files;
    private static Matcher? _folders;

    /// <summary>The icon of a file whose name has none registered.</summary>
    public static FileIcon Plain { get; } = CraftIcons.PlainFile;

    /// <summary>The icon of a folder whose name has none registered.</summary>
    public static FileIcon PlainFolder { get; } = CraftIcons.PlainFolder;

    /// <summary>Registers <paramref name="icon"/> under its key (over the library's, which stays behind it).</summary>
    public static void Register(FileIcon icon)
    {
        Check(icon);
        Table.Register(icon, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's icon of the key <paramref name="extension"/> (any kind of key; the library's, if any, answers again).</summary>
    public static bool Unregister(string extension) => Table.Unregister(extension);

    /// <summary>The registered keys of files, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered file icons, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<FileIcon> All => Table.Kinds;

    /// <summary>The icon registered under the key <paramref name="extension"/> exactly (".cs", "package.json"), or null.</summary>
    public static FileIcon? Find(ReadOnlySpan<char> extension) => Table.Find(extension);

    /// <summary>The icon registered under the key <paramref name="extension"/>.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static FileIcon Get(ReadOnlySpan<char> extension) => Table.Get(extension);

    /// <summary>Registers a folder's icon under its key (a folder name or a glob), over the library's.</summary>
    public static void RegisterFolder(FileIcon icon)
    {
        Check(icon);
        Folders.Register(icon, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's folder icon of that key (the library's, if any, answers again).</summary>
    public static bool UnregisterFolder(string key) => Folders.Unregister(key);

    /// <summary>The registered keys of folders, in order.</summary>
    public static IReadOnlyList<string> FolderNames => Folders.Names;

    /// <summary>The registered folder icons, in the order of <see cref="FolderNames"/>.</summary>
    public static IReadOnlyList<FileIcon> AllFolders => Folders.Kinds;

    /// <summary>The icon of the file <paramref name="path"/> (or its name): the icon theme's, else Craft's set's by name, suffix, glob or extension, else <see cref="Plain"/>.</summary>
    public static FileIcon For(ReadOnlySpan<char> path)
    {
        var name = Path.GetFileName(path);
        if (FileIconThemes.Active is { } theme && theme is not CraftIconTheme && theme.ForFile(name) is { } themed)
        {
            return themed;
        }

        return ForFileInSet(name) ?? Plain;
    }

    /// <summary>The icon of the folder named <paramref name="name"/> (a name or a path; its last part counts): the theme's, else Craft's set's, else <see cref="PlainFolder"/>.</summary>
    public static FileIcon ForFolder(ReadOnlySpan<char> name)
    {
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(name));
        if (FileIconThemes.Active is { } theme && theme is not CraftIconTheme && theme.ForFolder(folder) is { } themed)
        {
            return themed;
        }

        return ForFolderInSet(folder) ?? PlainFolder;
    }

    // Craft's set (the registry): the matcher made again only when a registration changed the table (its list is a new
    // array then), so a lookup reads two fields and the pattern table.
    internal static FileIcon? ForFileInSet(ReadOnlySpan<char> name) => Current(ref _files, Table).Table.Find(name);

    internal static FileIcon? ForFolderInSet(ReadOnlySpan<char> name) => Current(ref _folders, Folders).Table.Find(name);

    private static Matcher Current(ref Matcher? field, KindTable<FileIcon> table)
    {
        var kinds = table.Kinds;
        var matcher = Volatile.Read(ref field);
        if (matcher is not null && ReferenceEquals(matcher.Source, kinds))
        {
            return matcher;
        }

        matcher = new Matcher(kinds, new FilePatternTable<FileIcon>(kinds.Select(k => KeyValuePair.Create(k.Extension, k))));
        Volatile.Write(ref field, matcher);
        return matcher;
    }

    private static void Check(FileIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon.Extension);
        if ((icon.Shape?.Problem() ?? icon.OpenShape?.Problem()) is { } problem)
        {
            throw new ArgumentException($"The icon '{icon.Extension}' has a shape that cannot be drawn: {problem}.", nameof(icon));
        }
    }

    private static KindTable<FileIcon> MakeTable(string registry, string kind, string remedy, IReadOnlyList<FileIcon> defaults)
    {
        var table = new KindTable<FileIcon>(registry, kind, remedy, comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a file icon is data, read as it is");
        foreach (var icon in defaults)
        {
            table.RegisterDefault(icon);
        }

        return table;
    }

    private sealed record Matcher(IReadOnlyList<FileIcon> Source, FilePatternTable<FileIcon> Table);
}

/// <summary>
/// An icon theme (W: <c>CRAFT_ICON_THEME</c>): the icons of files and folders by name. Craft's own is "craft"
/// (<see cref="FileIcons"/>' set); a plug-in registers another in <see cref="FileIconThemes"/>, most easily a
/// <see cref="FileIconTheme"/>. What a theme does not cover is drawn from Craft's set.
/// </summary>
public interface IFileIconTheme : IKind
{
    /// <summary>The icon of the file named <paramref name="fileName"/>, or null to leave it to Craft's set. From memory: called while drawing.</summary>
    FileIcon? ForFile(ReadOnlySpan<char> fileName);

    /// <summary>The icon of the folder named <paramref name="folderName"/>, or null to leave it to Craft's set. From memory.</summary>
    FileIcon? ForFolder(ReadOnlySpan<char> folderName);
}

/// <summary>An icon theme made of icons by key (each <see cref="FileIcon.Extension"/> a name, suffix, glob or extension), as a plug-in brings one.</summary>
public sealed class FileIconTheme : IFileIconTheme
{
    private readonly FilePatternTable<FileIcon> _files;
    private readonly FilePatternTable<FileIcon> _folders;

    /// <summary>A theme named <paramref name="name"/> of <paramref name="files"/> and <paramref name="folders"/>.</summary>
    /// <exception cref="ArgumentException">An icon's shape cannot be drawn (<see cref="FileIconShape.Problem()"/>).</exception>
    public FileIconTheme(string name, string summary, IEnumerable<FileIcon> files, IEnumerable<FileIcon>? folders = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(files);
        Name = name;
        Summary = summary ?? "";
        FileIcon[] fileList = [.. files];
        FileIcon[] folderList = [.. folders ?? []];
        foreach (var icon in fileList.Concat(folderList))
        {
            if ((icon.Shape?.Problem() ?? icon.OpenShape?.Problem()) is { } problem)
            {
                throw new ArgumentException($"The icon theme '{name}': the icon '{icon.Extension}' has a shape that cannot be drawn: {problem}.", nameof(files));
            }
        }

        Files = fileList;
        Folders = folderList;
        _files = new FilePatternTable<FileIcon>(fileList.Select(i => KeyValuePair.Create(i.Extension, i)));
        _folders = new FilePatternTable<FileIcon>(folderList.Select(i => KeyValuePair.Create(i.Extension, i)));
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary { get; }

    /// <summary>Its file icons.</summary>
    public IReadOnlyList<FileIcon> Files { get; }

    /// <summary>Its folder icons.</summary>
    public IReadOnlyList<FileIcon> Folders { get; }

    /// <inheritdoc />
    public FileIcon? ForFile(ReadOnlySpan<char> fileName) => _files.Find(fileName);

    /// <inheritdoc />
    public FileIcon? ForFolder(ReadOnlySpan<char> folderName) => _folders.Find(folderName);
}

/// <summary>
/// The icon themes, by name (ignoring case): the library's "craft" (<see cref="FileIcons"/>' set, Craft's own drawings),
/// and a plug-in's over it. <see cref="Use"/> picks the one in use (the shell from <c>CRAFT_ICON_THEME</c>); an
/// unregistered name fails with "not registered".
/// </summary>
public static class FileIconThemes
{
    /// <summary>The name of Craft's own set.</summary>
    public const string Craft = "craft";

    private static readonly KindTable<IFileIconTheme> Table = MakeTable();
    private static string _current = Craft;

    /// <summary>Raised (on the caller's thread) when <see cref="Use"/> changed the theme in use.</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="theme"/> under its name.</summary>
    public static void Register(IFileIconTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Table.Register(theme, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's theme of that name; false when none was registered by the app.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The theme of that name, or null.</summary>
    public static IFileIconTheme? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The theme of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IFileIconTheme Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The name of the theme in use (<see cref="Craft"/> until <see cref="Use"/>).</summary>
    public static string Current => _current;

    /// <summary>The theme in use: <see cref="Current"/>'s, or Craft's when it was unregistered since.</summary>
    public static IFileIconTheme Active => Table.Find(_current) ?? Table.Get(Craft);

    /// <summary>Uses the theme <paramref name="name"/> from now on.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static void Use(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var theme = Table.Get(name);
        if (string.Equals(theme.Name, _current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _current = theme.Name;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static KindTable<IFileIconTheme> MakeTable()
    {
        var table = new KindTable<IFileIconTheme>(nameof(FileIconThemes), "Icon theme", "add it with FileIconThemes.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "an icon theme answers from memory");
        table.RegisterDefault(new CraftIconTheme());
        return table;
    }
}

/// <summary>Craft's own icon theme: <see cref="FileIcons"/>' set, plug-ins' registrations included.</summary>
internal sealed class CraftIconTheme : IFileIconTheme
{
    public string Name => FileIconThemes.Craft;

    public string Summary => "Craft's own icons (files by name, suffix, glob and extension; folders by name)";

    public FileIcon? ForFile(ReadOnlySpan<char> fileName) => FileIcons.ForFileInSet(fileName);

    public FileIcon? ForFolder(ReadOnlySpan<char> folderName) => FileIcons.ForFolderInSet(folderName);
}

/// <summary>SVG path data walked command by command: checked, and written again moved and scaled.</summary>
internal static class SvgPath
{
    // The numbers each command takes per set.
    private static int Arity(char command) => char.ToUpperInvariant(command) switch
    {
        'M' or 'L' or 'T' => 2,
        'H' or 'V' => 1,
        'C' => 6,
        'S' or 'Q' => 4,
        'A' => 7,
        'Z' => 0,
        _ => -1,
    };

    // Null when the data is well formed; writes it moved and scaled into "into" when given.
    public static string? Walk(string data, StringBuilder? into, float scale, float dx, float dy)
    {
        var text = data.AsSpan();
        int at = 0;
        char command = '\0';
        Span<float> numbers = stackalloc float[7];
        bool first = true;
        while (true)
        {
            Skip(text, ref at);
            if (at >= text.Length)
            {
                break;
            }

            char c = text[at];
            if (char.IsAsciiLetter(c))
            {
                if (Arity(c) < 0)
                {
                    return $"'{c}' at {at} is not a path command";
                }

                if (first && c is not ('M' or 'm'))
                {
                    return "path data starts with a move (M)";
                }

                first = false;
                command = c;
                at++;
                into?.Append(c);
                if (Arity(c) == 0)
                {
                    continue;
                }
            }
            else if (command == '\0')
            {
                return "path data starts with a move (M)";
            }
            else if (Arity(command) == 0)
            {
                return $"a number after '{command}' at {at}";
            }

            // One set of the command's numbers (a command letter may be followed by several sets).
            int arity = Arity(command);
            for (int i = 0; i < arity; i++)
            {
                Skip(text, ref at);
                bool flag = char.ToUpperInvariant(command) == 'A' && i is 3 or 4;
                if (flag)
                {
                    if (at >= text.Length || text[at] is not ('0' or '1'))
                    {
                        return $"an arc's flag at {at} is not 0 or 1";
                    }

                    numbers[i] = text[at] - '0';
                    at++;
                    continue;
                }

                if (!Number(text, ref at, out numbers[i]))
                {
                    return $"'{command}' at {at} wants {arity} numbers a set";
                }
            }

            into?.Append(' ');
            Write(into, command, numbers[..arity], scale, dx, dy);
        }

        return first ? "path data is empty" : null;
    }

    private static void Write(StringBuilder? into, char command, ReadOnlySpan<float> n, float s, float dx, float dy)
    {
        if (into is null)
        {
            return;
        }

        bool relative = char.IsAsciiLetterLower(command);
        float ox = relative ? 0 : dx, oy = relative ? 0 : dy;
        switch (char.ToUpperInvariant(command))
        {
            case 'H':
                Append(into, (n[0] * s) + ox);
                break;
            case 'V':
                Append(into, (n[0] * s) + oy);
                break;
            case 'A':
                Append(into, n[0] * s).Append(' ');
                Append(into, n[1] * s).Append(' ');
                Append(into, n[2]).Append(' ');
                into.Append(n[3] > 0 ? '1' : '0').Append(' ').Append(n[4] > 0 ? '1' : '0').Append(' ');
                Append(into, (n[5] * s) + ox).Append(' ');
                Append(into, (n[6] * s) + oy);
                break;
            default:
                for (int i = 0; i < n.Length; i += 2)
                {
                    if (i > 0)
                    {
                        into.Append(' ');
                    }

                    Append(into, (n[i] * s) + ox).Append(' ');
                    Append(into, (n[i + 1] * s) + oy);
                }

                break;
        }
    }

    private static StringBuilder Append(StringBuilder into, float value)
    {
        float rounded = MathF.Round(value, 3);
        return into.Append((rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static void Skip(ReadOnlySpan<char> text, ref int at)
    {
        while (at < text.Length && (text[at] is ' ' or ',' or '\t' or '\r' or '\n'))
        {
            at++;
        }
    }

    // A number: sign, digits, one dot, an exponent; "1.5.5" is two numbers and "-" starts the next.
    private static bool Number(ReadOnlySpan<char> text, ref int at, out float value)
    {
        int start = at;
        if (at < text.Length && text[at] is '+' or '-')
        {
            at++;
        }

        bool digits = false, dot = false;
        while (at < text.Length)
        {
            char c = text[at];
            if (char.IsAsciiDigit(c))
            {
                digits = true;
            }
            else if (c == '.' && !dot)
            {
                dot = true;
            }
            else
            {
                break;
            }

            at++;
        }

        if (digits && at < text.Length && text[at] is 'e' or 'E')
        {
            int mark = at++;
            if (at < text.Length && text[at] is '+' or '-')
            {
                at++;
            }

            if (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                while (at < text.Length && char.IsAsciiDigit(text[at]))
                {
                    at++;
                }
            }
            else
            {
                at = mark;
            }
        }

        value = 0;
        return digits && float.TryParse(text[start..at], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
