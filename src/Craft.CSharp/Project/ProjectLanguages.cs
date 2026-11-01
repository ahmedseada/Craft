// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.CSharp.Project;

namespace Craft.CSharp;

/// <summary>
/// The languages of a C# project's other files, which the C# plug-in brings beside C# and takes away with it: MSBuild
/// (.csproj, .props, .targets), XML (.xml, .config, .resx, .slnx), XAML, Razor (.razor, .cshtml: markup with its C#
/// coloured as C#), .editorconfig and the solution file (.sln). Each colours the lines on screen from a state kept per
/// line (no parse, nothing allocated per token) and matches its brackets; MSBuild, XML and XAML also format (Format
/// document, format on save). Built on the public line-state kit (<see cref="LineStateLanguage"/>) and registered through
/// <see cref="EditorLanguages"/> only, as any plug-in's language is. JSON moved to its own plug-in, Craft.Plugins.Json
/// (phase W), so a web project's JSON keeps its colours without C#.
/// </summary>
public static partial class ProjectLanguages
{
    /// <summary>MSBuild's project files: .csproj, .props, .targets.</summary>
    public const string MsBuild = "msbuild";

    /// <summary>XML: .xml, .config, .resx, .slnx.</summary>
    public const string Xml = "xml";

    /// <summary>XAML: .xaml.</summary>
    public const string Xaml = "xaml";

    /// <summary>Razor: .razor, .cshtml.</summary>
    public const string Razor = "razor";

    /// <summary>EditorConfig: .editorconfig.</summary>
    public const string EditorConfig = "editorconfig";

    /// <summary>A Visual Studio solution: .sln.</summary>
    public const string Solution = "sln";

    private static readonly LanguageRules MarkupRules = new(null, "<!--", "-->", [new('<', '>'), new('(', ')'), new('[', ']'), new('{', '}')]) { StringDelimiters = "\"", StringEscape = '\0' };
    private static readonly LanguageRules RazorRules = new("//", "@*", "*@", [new('<', '>'), new('(', ')'), new('[', ']'), new('{', '}')]) { StringDelimiters = "\"" };
    private static readonly LanguageRules EditorConfigRules = new("#", null, null, [new('[', ']'), new('{', '}')]);
    private static readonly LanguageRules SolutionRules = new("#", null, null, [new('(', ')'), new('{', '}')]) { StringDelimiters = "\"", StringEscape = '\0' };

    /// <summary>The names, in the order they are registered.</summary>
    public static IReadOnlyList<string> Names { get; } = [MsBuild, Xml, Xaml, Razor, EditorConfig, Solution];

    /// <summary>Each language, made once (a language holds nothing per document: its sessions do).</summary>
    internal static IReadOnlyList<ILanguage> All { get; } = Make();

    /// <summary>Registers every one in <see cref="EditorLanguages"/> (the plug-in registers them one part each).</summary>
    public static void Register()
    {
        foreach (var language in All)
        {
            EditorLanguages.Register(language);
        }

        FoldingRangeProviders.Register(new MarkupFolding());
        FoldingRangeProviders.Register(new RazorFolding());
        FoldingRangeProviders.Register(new SolutionFolding());
        FoldingRangeProviders.Register(new EditorConfigFolding());
    }

    /// <summary>Removes every one.</summary>
    public static void Unregister()
    {
        foreach (var name in Names)
        {
            EditorLanguages.Unregister(name);
        }

        FoldingRangeProviders.Unregister(MarkupFolding.ProviderName);
        FoldingRangeProviders.Unregister(RazorFolding.ProviderName);
        FoldingRangeProviders.Unregister(SolutionFolding.ProviderName);
        FoldingRangeProviders.Unregister(EditorConfigFolding.ProviderName);
    }

    private static ILanguage[] Make()
    {
        var xml = new XmlFormatter();
        return
        [
            new LineStateLanguage(MsBuild, "MSBuild project files: colours (with $(Property), @(Item) and %(Metadata)) and format", [".csproj", ".props", ".targets"],
                MarkupRules, new MarkupTokenizer(MarkupDialect.MsBuild), xml),
            new LineStateLanguage(Xml, "XML: colours and format", [".xml", ".config", ".resx", ".slnx"], MarkupRules, new MarkupTokenizer(MarkupDialect.Xml), xml),
            new LineStateLanguage(Xaml, "XAML: colours (namespace prefixes too) and format", [".xaml"], MarkupRules, new MarkupTokenizer(MarkupDialect.Xml), xml),
            new LineStateLanguage(Razor, "Razor: the markup's colours and its C# (@code, @{ }, @expr, @( ), @if, directives) coloured as C#", [".razor", ".cshtml"],
                RazorRules, new MarkupTokenizer(MarkupDialect.Razor), null),
            new LineStateLanguage(EditorConfig, "EditorConfig: sections, keys, values and severities", [".editorconfig"], EditorConfigRules, new EditorConfigTokenizer(), null),
            new LineStateLanguage(Solution, "Visual Studio solutions (.sln): colours", [".sln"], SolutionRules, new SolutionTokenizer(), null),
        ];
    }
}
