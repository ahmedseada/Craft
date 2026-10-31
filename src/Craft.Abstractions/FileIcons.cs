// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// The icon a file shows in a list (quick open, the welcome page's recent files): a short badge in a colour, by extension,
/// registered in <see cref="FileIcons"/> under the extension with its dot (".cs").
/// </summary>
/// <param name="Extension">The extension it is for, with its dot, ignoring case (".cs").</param>
/// <param name="Badge">The badge's text, one to three characters ("C#", "{}").</param>
/// <param name="Argb">The badge's colour, 0xAARRGGBB.</param>
/// <param name="Language">What the files are, for a tooltip ("C# source").</param>
public sealed record FileIcon(string Extension, string Badge, uint Argb, string Language) : IKind
{
    /// <inheritdoc />
    public string Name => Extension;

    /// <inheritdoc />
    public string Summary => $"{Badge}: {Language}";
}

/// <summary>
/// The file icons by extension (ignoring case). The library's cover the files a C# developer opens (sources, projects,
/// data, scripts, documents); a language plug-in registers its own over them, and <see cref="Unregister"/> brings the
/// library's back. A file whose extension has none shows <see cref="Plain"/>.
/// </summary>
public static class FileIcons
{
    private static readonly KindTable<FileIcon> Table = BuiltIn();

    /// <summary>The icon of a file whose extension has none registered.</summary>
    public static FileIcon Plain { get; } = new("", "·", 0xFF8790A0, "File");

    /// <summary>Registers <paramref name="icon"/> under its extension (over the library's, which stays behind it).</summary>
    public static void Register(FileIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon.Extension);
        Table.Register(icon, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's icon for that extension (the library's, if any, answers again).</summary>
    public static bool Unregister(string extension) => Table.Unregister(extension);

    /// <summary>The registered extensions, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The icon registered for <paramref name="extension"/> (with its dot), or null.</summary>
    public static FileIcon? Find(ReadOnlySpan<char> extension) => Table.Find(extension);

    /// <summary>The icon registered for <paramref name="extension"/>.</summary>
    /// <exception cref="NotRegisteredException">None is registered for it.</exception>
    public static FileIcon Get(ReadOnlySpan<char> extension) => Table.Get(extension);

    /// <summary>The icon of the file <paramref name="path"/> by its extension, else <see cref="Plain"/>.</summary>
    public static FileIcon For(ReadOnlySpan<char> path)
    {
        var extension = Path.GetExtension(path);
        return extension.IsEmpty ? Plain : Table.Find(extension) ?? Plain;
    }

    private static KindTable<FileIcon> BuiltIn()
    {
        var table = new KindTable<FileIcon>(nameof(FileIcons), "File icon", "add it with FileIcons.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a file icon is data, read as it is");
        foreach (var icon in (ReadOnlySpan<FileIcon>)
        [
            new(".cs", "C#", 0xFF9B7FE0, "C# source"),
            new(".csx", "C#", 0xFF9B7FE0, "C# script"),
            new(".razor", "@", 0xFF9B7FE0, "Razor component"),
            new(".csproj", "◆", 0xFF5FC9B4, "C# project"),
            new(".props", "◆", 0xFF5FC9B4, "MSBuild properties"),
            new(".targets", "◆", 0xFF5FC9B4, "MSBuild targets"),
            new(".sln", "▣", 0xFF5FC9B4, "Solution"),
            new(".slnx", "▣", 0xFF5FC9B4, "Solution"),
            new(".json", "{}", 0xFFE3B27A, "JSON"),
            new(".xml", "<>", 0xFFE38A6A, "XML"),
            new(".xaml", "<>", 0xFFE38A6A, "XAML"),
            new(".config", "<>", 0xFFE38A6A, "Configuration"),
            new(".html", "<>", 0xFFE38A6A, "HTML"),
            new(".css", "#", 0xFF82AAFF, "CSS"),
            new(".js", "JS", 0xFFE8D58A, "JavaScript"),
            new(".ts", "TS", 0xFF82AAFF, "TypeScript"),
            new(".py", "Py", 0xFF82AAFF, "Python"),
            new(".md", "M↓", 0xFF7FB2F0, "Markdown"),
            new(".txt", "≡", 0xFF8790A0, "Text"),
            new(".yml", "Y", 0xFFE38A6A, "YAML"),
            new(".yaml", "Y", 0xFFE38A6A, "YAML"),
            new(".ps1", ">_", 0xFF7FB2F0, "PowerShell"),
            new(".sh", ">_", 0xFF5FC9B4, "Shell script"),
            new(".cmd", ">_", 0xFF8790A0, "Command script"),
        ])
        {
            table.RegisterDefault(icon);
        }

        return table;
    }
}
