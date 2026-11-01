// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// Craft's own icon set, "craft" (W: rich file icons): original drawings in the design's style (docs/design: the logo's
/// rounded tile with an ink mark), no icon set copied and no vendor logo taken. A file is a base (a rounded tile for
/// sources, a page with a folded corner for documents and data, a hexagon for Angular's parts) in its type's colour with a
/// glyph in ink (a monogram of stroked letters or a small drawing); a folder is a folder in its colour, open or closed,
/// with an emblem. Each glyph is drawn once on a 10-unit box and placed on its base by <see cref="FileIconShape.Transform"/>
/// when the set is made (once per process, rule 75). Light variants: the base moved until it reads at 3:1 on the light
/// theme's grounds (<see cref="ColorContrast"/>) with a white ink; on dark, the design's hues with a near-black ink.
/// </summary>
internal static class CraftIcons
{
    // The design's hues (Themes.cs' dark classes), and a few more for the web set.
    private const uint Purple = 0xFF9B7FE0, Teal = 0xFF5FC9B4, Amber = 0xFFE3B27A, Orange = 0xFFE38A6A, Blue = 0xFF82AAFF, Sky = 0xFF7FB2F0,
        Yellow = 0xFFE8D58A, Grey = 0xFF8790A0, Red = 0xFFF07178, Pink = 0xFFE58FB8, Cyan = 0xFF6FD3E8, Green = 0xFF9BCB6B, Indigo = 0xFF8F8CF0,
        Rose = 0xFFE0607E, Slate = 0xFF9AA2B1;

    private const uint InkDark = 0xFF14161B, InkLight = 0xFFFFFFFF;

    // The light theme's ground, surfaces and selected row (Themes.cs' MakeLight).
    private static readonly uint[] LightGrounds = [0xFFF3F4F6, 0xFFFFFFFF, 0xFFF6F8FA, 0xFFEEF1F4, 0xFFDDE4EC];

    // The bases on the 16-unit box.
    private const string TilePath = "M4.5 1.5H11.5A3 3 0 0 1 14.5 4.5V11.5A3 3 0 0 1 11.5 14.5H4.5A3 3 0 0 1 1.5 11.5V4.5A3 3 0 0 1 4.5 1.5Z";
    private const string PagePath = "M4 1H9.6L13.5 4.9V13.5A1.5 1.5 0 0 1 12 15H4A1.5 1.5 0 0 1 2.5 13.5V2.5A1.5 1.5 0 0 1 4 1Z";
    private const string FoldPath = "M9.6 1V3.6A1.3 1.3 0 0 0 10.9 4.9H13.5Z";
    private const string HexPath = "M8 0.9L14.2 4.45V11.55L8 15.1L1.8 11.55V4.45Z";
    private const string FolderPath = "M1.5 4A1.5 1.5 0 0 1 3 2.5H6.2L7.8 4.1H13A1.5 1.5 0 0 1 14.5 5.6V12.5A1.5 1.5 0 0 1 13 14H3A1.5 1.5 0 0 1 1.5 12.5Z";
    private const string FolderBackPath = "M1.5 4A1.5 1.5 0 0 1 3 2.5H6.2L7.8 4.1H12A1.5 1.5 0 0 1 13.5 5.6V6.5H4.4L1.5 13Z";
    private const string FolderFrontPath = "M4.2 6.5H14.6A0.7 0.7 0 0 1 15.3 7.4L13.4 13.2A1.2 1.2 0 0 1 12.3 14H2A0.6 0.6 0 0 1 1.5 13.2Z";

    // The glyphs on the 10-unit box.
    private const string Cube = "M5 0.6L9 2.8V7.2L5 9.4L1 7.2V2.8ZM1 2.8L5 5L9 2.8M5 5V9.4";
    private const string Sliders = "M1 2.5H9M1 5H9M1 7.5H9M3.2 1.2V3.8M6.8 3.7V6.3M4.4 6.2V8.8";
    private const string Target = "M1 5A4 4 0 1 0 9 5A4 4 0 1 0 1 5ZM3.6 5A1.4 1.4 0 1 0 6.4 5A1.4 1.4 0 1 0 3.6 5Z";
    private const string Solution = "M1 3.6H6.4V9H1ZM3.6 3.6V1H9V6.4H6.4";
    private const string Gear = "M3.3 5A1.7 1.7 0 1 0 6.7 5A1.7 1.7 0 1 0 3.3 5ZM5 0.6V2.2M5 7.8V9.4M0.6 5H2.2M7.8 5H9.4M1.9 1.9L3 3M7 7L8.1 8.1M1.9 8.1L3 7M7 3L8.1 1.9";
    private const string Globe = "M1 5A4 4 0 1 0 9 5A4 4 0 1 0 1 5ZM1.2 5H8.8M5 1C3.4 2.3 3.4 7.7 5 9M5 1C6.6 2.3 6.6 7.7 5 9";
    private const string Play = "M2.8 1.2L8.6 5L2.8 8.8Z";
    private const string Lines = "M1 1.8H9M1 4.2H9M1 6.6H9M1 9H6";
    private const string Picture = "M0.8 8.6L3.6 4.9L5.6 7.2L7 5.8L9.2 8.6ZM6.4 2.4A1.1 1.1 0 1 0 8.6 2.4A1.1 1.1 0 1 0 6.4 2.4Z";
    private const string Pen = "M1.2 8.8C1.6 4.4 5.6 5.6 8.8 1.2M0.4 8H2V9.6H0.4ZM8 0.4H9.6V2H8Z";
    private const string Branch = "M2.6 1.2V7.6M2.6 6.4C2.6 4.2 7.4 5.2 7.4 3.2M1.4 8.8A1.2 1.2 0 1 0 3.8 8.8A1.2 1.2 0 1 0 1.4 8.8ZM6.2 2A1.2 1.2 0 1 0 8.6 2A1.2 1.2 0 1 0 6.2 2Z";
    private const string Crates = "M1 5.6H5V9.2H1ZM5 5.6H9V9.2H5ZM3 2H7V5.6H3Z";
    private const string Indented = "M1 1.8H6M3 4.2H9M3 6.6H8M1 9H6";
    private const string Tick = "M1.2 5.3L3.9 8L8.8 2.2";
    private const string Lint = "M1 5A4 4 0 1 0 9 5A4 4 0 1 0 1 5ZM3.1 5.1L4.5 6.5L7 3.6";
    private const string Format = "M1 1.8H9M3 4.2H9M3 6.6H7.5M1 9H8";
    private const string Lock = "M2.2 4.6H7.8V9.2H2.2ZM3.4 4.6V3A1.6 1.6 0 0 1 6.6 3V4.6M5 6.4V7.4";
    private const string Puzzle = "M1 3H3.6A1.4 1.4 0 1 1 6.4 3H9V9H1Z";
    private const string Hexagon = "M5 0.6L8.8 2.8V7.2L5 9.4L1.2 7.2V2.8Z";
    private const string Binary = "M0.8 5A1.6 2.8 0 1 0 4 5A1.6 2.8 0 1 0 0.8 5ZM6 3.2L7.6 2.2V7.8M6.1 7.8H9.1";
    private const string Export = "M5 7.6V1.4M2.4 4L5 1.4L7.6 4M1 9H9";
    private const string Pencil = "M1.4 8.6L1.9 6.3L6.9 1.3L8.7 3.1L3.7 8.1ZM5.8 2.4L7.6 4.2";

    // Stroked capitals and marks on a 4 by 6 cell.
    private static readonly Dictionary<char, string> Letters = new()
    {
        ['C'] = "M4 1.1C3.5 0.4 2.8 0 2 0C0.9 0 0 1.3 0 3S0.9 6 2 6C2.8 6 3.5 5.6 4 4.9",
        ['#'] = "M1.5 0.4L1 5.6M3.2 0.4L2.7 5.6M0.2 2.1H4M0 3.9H3.8",
        ['S'] = "M3.8 0.9C3.4 0.3 2.8 0 2 0C1 0 0.2 0.6 0.2 1.6C0.2 3.4 3.8 2.6 3.8 4.4C3.8 5.4 3 6 2 6C1.2 6 0.5 5.6 0.1 5",
        ['J'] = "M3.4 0V4.2C3.4 5.3 2.7 6 1.7 6C1 6 0.4 5.6 0 5",
        ['T'] = "M0 0H4M2 0V6",
        ['X'] = "M0.2 0L3.8 6M3.8 0L0.2 6",
        ['M'] = "M0 6V0L2 3.4L4 0V6",
        ['Y'] = "M0 0L2 3V6M4 0L2 3",
        ['L'] = "M0.4 0V6H4",
        ['N'] = "M0.2 6V0L3.8 6V0",
        ['D'] = "M0.3 0V6H1.8C3.1 6 4 4.7 4 3S3.1 0 1.8 0Z",
        ['P'] = "M0.3 6V0H2.3C3.3 0 4 0.7 4 1.7S3.3 3.4 2.3 3.4H0.3",
        ['G'] = "M4 1.1C3.5 0.4 2.8 0 2 0C0.9 0 0 1.3 0 3S0.9 6 2 6C3.1 6 4 5.2 4 3.8V3.3H2.2",
        ['R'] = "M0.3 6V0H2.3C3.3 0 4 0.7 4 1.7S3.3 3.4 2.3 3.4H0.3M2.3 3.4L4 6",
        ['I'] = "M2 0V6M0.8 0H3.2M0.8 6H3.2",
        ['↓'] = "M2 0V5.6M0.3 3.9L2 5.7L3.7 3.9",
        ['>'] = "M0.4 0.9L3.6 3L0.4 5.1",
        ['_'] = "M0 6H4",
        ['<'] = "M3.6 0.9L0.4 3L3.6 5.1",
        ['/'] = "M3 0L1 6",
        ['{'] = "M3.4 0C2.4 0 1.9 0.4 1.9 1.4V2.2C1.9 2.7 1.5 3 0.8 3C1.5 3 1.9 3.3 1.9 3.8V4.6C1.9 5.6 2.4 6 3.4 6",
        ['}'] = "M0.6 0C1.6 0 2.1 0.4 2.1 1.4V2.2C2.1 2.7 2.5 3 3.2 3C2.5 3 2.1 3.3 2.1 3.8V4.6C2.1 5.6 1.6 6 0.6 6",
        ['@'] = "M2.9 3C2.9 3.6 2.5 4.1 2 4.1S1.1 3.6 1.1 3S1.5 1.9 2 1.9S2.9 2.4 2.9 3V3.8C2.9 4.3 3.2 4.6 3.6 4.6C4.2 4.6 4.4 3.8 4.4 3C4.4 1.3 3.4 0 2.1 0C0.9 0 -0.1 1.3 -0.1 3S0.9 6 2.1 6C2.7 6 3.2 5.8 3.6 5.4",
    };

    private enum Base
    {
        Tile,
        Page,
        Hex,
    }

    public static FileIcon PlainFile { get; } = File("", "·", Grey, "File", Base.Page, Lines);

    public static FileIcon PlainFolder { get; } = Folder("", Slate, null, "Folder");

    public static IReadOnlyList<FileIcon> Files { get; } = MakeFiles();

    public static IReadOnlyList<FileIcon> Folders { get; } = MakeFolders();

    private static FileIcon[] MakeFiles()
    {
        var images = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff" };
        var list = new List<FileIcon>
        {
            // The C# set, redrawn.
            File(".cs", "C#", Purple, "C# source", Base.Tile, Mono("C#")),
            File(".csx", "C#", Purple, "C# script", Base.Page, Mono("C#")),
            File(".csproj", "◆", Teal, "C# project", Base.Tile, Cube),
            File(".props", "◆", Teal, "MSBuild properties", Base.Page, Sliders),
            File(".targets", "◆", Teal, "MSBuild targets", Base.Page, Target),
            File(".sln", "▣", Teal, "Solution", Base.Tile, Solution),
            File(".slnx", "▣", Teal, "Solution", Base.Tile, Solution),
            File(".razor", "@", Purple, "Razor component", Base.Tile, Mono("@")),
            File(".cshtml", "@", Purple, "Razor page", Base.Page, Mono("@")),
            File(".xaml", "<>", Orange, "XAML", Base.Tile, Mono("<>")),
            File(".xml", "<>", Orange, "XML", Base.Page, Mono("<>")),
            File(".config", "<>", Orange, "Configuration", Base.Page, Gear),
            File(".resx", "<>", Orange, "Resources", Base.Page, Globe),
            File(".json", "{}", Amber, "JSON", Base.Page, Mono("{}")),
            File("appsettings.json", "{}", Amber, "App settings", Base.Tile, Gear),
            File("appsettings*.json", "{}", Amber, "App settings", Base.Tile, Gear),
            File("launchSettings.json", "{}", Amber, "Launch settings", Base.Tile, Play),
            File("global.json", "{}", Amber, "SDK settings", Base.Tile, Globe),
            File(".editorconfig", "≡", Grey, "Editor settings", Base.Page, Pencil),
            File(".md", "M↓", Sky, "Markdown", Base.Page, Mono("M↓")),
            File(".txt", "≡", Grey, "Text", Base.Page, Lines),
            File(".log", "≡", Grey, "Log", Base.Page, Lines),
            File(".svg", "◇", Amber, "SVG image", Base.Tile, Pen),
            File(".ps1", ">_", Sky, "PowerShell", Base.Tile, Mono(">_")),
            File(".psm1", ">_", Sky, "PowerShell module", Base.Tile, Mono(">_")),
            File(".psd1", ">_", Sky, "PowerShell data", Base.Page, Mono(">_")),
            File(".cmd", ">_", Grey, "Command script", Base.Tile, Mono(">_")),
            File(".bat", ">_", Grey, "Command script", Base.Tile, Mono(">_")),
            File(".sh", ">_", Teal, "Shell script", Base.Tile, Mono(">_")),
            File(".bash", ">_", Teal, "Shell script", Base.Tile, Mono(">_")),
            File(".gitignore", "⑂", Red, "Git ignore", Base.Page, Branch),
            File(".gitattributes", "⑂", Red, "Git attributes", Base.Page, Branch),
            File(".gitmodules", "⑂", Red, "Git modules", Base.Page, Branch),
            File(".gitkeep", "⑂", Red, "Git keep", Base.Page, Branch),
            File("Dockerfile", "▤", Blue, "Dockerfile", Base.Tile, Crates),
            File("Dockerfile.*", "▤", Blue, "Dockerfile", Base.Tile, Crates),
            File("*.Dockerfile", "▤", Blue, "Dockerfile", Base.Tile, Crates),
            File(".dockerignore", "▤", Blue, "Docker ignore", Base.Page, Crates),
            File(".yml", "Y", Indigo, "YAML", Base.Page, Indented),
            File(".yaml", "Y", Indigo, "YAML", Base.Page, Indented),
            File(".py", "Py", Blue, "Python", Base.Tile, Mono("PY")),

            // The web set (W).
            File(".html", "<>", Orange, "HTML", Base.Tile, Mono("</>")),
            File(".htm", "<>", Orange, "HTML", Base.Tile, Mono("</>")),
            File(".css", "#", Blue, "CSS", Base.Tile, Mono("#")),
            File(".scss", "S", Pink, "SCSS", Base.Tile, Mono("S")),
            File(".sass", "S", Pink, "Sass", Base.Tile, Mono("S")),
            File(".less", "L", Indigo, "LESS", Base.Tile, Mono("L")),
            File(".js", "JS", Yellow, "JavaScript", Base.Tile, Mono("JS")),
            File(".mjs", "JS", Yellow, "JavaScript module", Base.Tile, Mono("JS")),
            File(".cjs", "JS", Yellow, "CommonJS module", Base.Tile, Mono("JS")),
            File(".jsx", "JSX", Cyan, "JavaScript with JSX", Base.Tile, Mono("JSX")),
            File("jsconfig.json", "{}", Yellow, "JavaScript project", Base.Tile, Gear),
            File(".ts", "TS", Blue, "TypeScript", Base.Tile, Mono("TS")),
            File(".mts", "TS", Blue, "TypeScript module", Base.Tile, Mono("TS")),
            File(".cts", "TS", Blue, "CommonJS TypeScript", Base.Tile, Mono("TS")),
            File(".tsx", "TSX", Cyan, "TypeScript with JSX", Base.Tile, Mono("TSX")),
            File(".d.ts", "TS", Blue, "TypeScript declarations", Base.Page, Mono("TS")),
            File(".d.mts", "TS", Blue, "TypeScript declarations", Base.Page, Mono("TS")),
            File(".d.cts", "TS", Blue, "TypeScript declarations", Base.Page, Mono("TS")),
            File("tsconfig*.json", "{}", Blue, "TypeScript project", Base.Tile, Gear),
            File("package.json", "▣", Green, "npm package", Base.Tile, Cube),
            File("package-lock.json", "▣", Green, "npm lock file", Base.Page, Lock),
            File(".npmrc", "⚙", Red, "npm settings", Base.Page, Gear),
            File(".nvmrc", "N", Green, "Node version", Base.Page, Mono("N")),
            File(".node-version", "N", Green, "Node version", Base.Page, Mono("N")),
            File("eslint.config.*", "✓", Indigo, "ESLint settings", Base.Tile, Lint),
            File(".eslintrc*", "✓", Indigo, "ESLint settings", Base.Page, Lint),
            File(".eslintignore", "✓", Indigo, "ESLint ignore", Base.Page, Lint),
            File("tslint.json", "✓", Blue, "TSLint settings", Base.Page, Lint),
            File(".prettierrc*", "≡", Pink, "Prettier settings", Base.Page, Format),
            File("prettier.config.*", "≡", Pink, "Prettier settings", Base.Tile, Format),
            File(".prettierignore", "≡", Pink, "Prettier ignore", Base.Page, Format),

            // Angular's parts (a hexagon of Craft's own, not Angular's mark).
            File("angular.json", "{}", Rose, "Angular workspace", Base.Hex, Mono("{}")),
            File(".component.ts", "C", Rose, "Angular component", Base.Hex, Mono("C")),
            File(".component.html", "<>", Rose, "Angular template", Base.Hex, Mono("<>")),
            File(".component.css", "#", Rose, "Angular component styles", Base.Hex, Mono("#")),
            File(".component.scss", "#", Rose, "Angular component styles", Base.Hex, Mono("#")),
            File(".component.sass", "#", Rose, "Angular component styles", Base.Hex, Mono("#")),
            File(".component.less", "#", Rose, "Angular component styles", Base.Hex, Mono("#")),
            File(".service.ts", "S", Rose, "Angular service", Base.Hex, Mono("S")),
            File(".module.ts", "M", Rose, "Angular module", Base.Hex, Mono("M")),
            File(".directive.ts", "D", Rose, "Angular directive", Base.Hex, Mono("D")),
            File(".pipe.ts", "P", Rose, "Angular pipe", Base.Hex, Mono("P")),
            File(".guard.ts", "G", Rose, "Angular guard", Base.Hex, Mono("G")),
            File(".routes.ts", "R", Rose, "Angular routes", Base.Hex, Mono("R")),
            File(".resolver.ts", "R", Rose, "Angular resolver", Base.Hex, Mono("R")),
            File(".interceptor.ts", "I", Rose, "Angular interceptor", Base.Hex, Mono("I")),
        };

        // Tests by their suffix (a tick), over the language's own icon.
        foreach (var language in new[] { "ts", "tsx", "js", "jsx", "mts", "mjs", "cts", "cjs" })
        {
            list.Add(File(".spec." + language, "✓", Green, "Test", Base.Tile, Tick));
            list.Add(File(".test." + language, "✓", Green, "Test", Base.Tile, Tick));
        }

        foreach (var image in images)
        {
            list.Add(File(image, "▨", Green, "Image", Base.Tile, Picture));
        }

        return [.. list];
    }

    private static FileIcon[] MakeFolders() =>
    [
        Folder("src", Blue, Mono("<>"), "Sources"),
        Folder("source", Blue, Mono("<>"), "Sources"),
        Folder("test", Green, Tick, "Tests"),
        Folder("tests", Green, Tick, "Tests"),
        Folder("__tests__", Green, Tick, "Tests"),
        Folder("spec", Green, Tick, "Tests"),
        Folder("docs", Sky, Lines, "Documents"),
        Folder("doc", Sky, Lines, "Documents"),
        Folder(".git", Red, Branch, "Git's own"),
        Folder(".github", Indigo, Branch, "GitHub workflows"),
        Folder(".vscode", Blue, Gear, "Editor settings"),
        Folder(".vs", Purple, Gear, "Editor settings"),
        Folder(".craft", Amber, Mono("C"), "Craft's settings"),
        Folder("node_modules", Green, Cube, "Installed packages"),
        Folder("dist", Amber, Export, "Build output"),
        Folder("out", Amber, Export, "Build output"),
        Folder("build", Amber, Export, "Build output"),
        Folder("bin", Grey, Binary, "Binaries"),
        Folder("obj", Grey, Binary, "Intermediate files"),
        Folder("assets", Pink, Picture, "Assets"),
        Folder("images", Pink, Picture, "Images"),
        Folder("img", Pink, Picture, "Images"),
        Folder("public", Cyan, Globe, "Public files"),
        Folder("wwwroot", Cyan, Globe, "Web root"),
        Folder("static", Cyan, Globe, "Static files"),
        Folder("components", Rose, Puzzle, "Components"),
        Folder("services", Purple, Gear, "Services"),
        Folder("scripts", Teal, Mono(">_"), "Scripts"),
        Folder(".angular", Rose, Hexagon, "Angular's cache"),
        Folder("Properties", Teal, Sliders, "Project properties"),
        Folder("test-output", Grey, Lines, "Test output"),
    ];

    private static FileIcon File(string key, string badge, uint color, string language, Base kind, string glyph)
    {
        uint light = Fit(color);
        var layers = new List<FileIconLayer>(3);
        float scale, x, y, stroke;
        switch (kind)
        {
            case Base.Page:
                layers.Add(new FileIconLayer(PagePath, color) { LightArgb = light });
                layers.Add(new FileIconLayer(FoldPath, ColorContrast.Mix(color, 0xFFFFFFFF, 0.45)) { LightArgb = ColorContrast.Mix(light, 0xFFFFFFFF, 0.45) });
                (scale, x, y, stroke) = (0.74f, 4.3f, 5.6f, 1.15f);
                break;
            case Base.Hex:
                layers.Add(new FileIconLayer(HexPath, color) { LightArgb = light });
                (scale, x, y, stroke) = (0.72f, 4.4f, 4.4f, 1.15f);
                break;
            default:
                layers.Add(new FileIconLayer(TilePath, color) { LightArgb = light });
                (scale, x, y, stroke) = (0.86f, 3.7f, 3.7f, 1.3f);
                break;
        }

        layers.Add(Ink(glyph, scale, x, y, stroke));
        return new FileIcon(key, badge, color, language)
        {
            Shape = new FileIconShape(layers),
            LightArgb = ColorContrast.Ensure(color, ColorContrast.Text, LightGrounds),
        };
    }

    private static FileIcon Folder(string name, uint color, string? emblem, string what)
    {
        uint light = Fit(color);
        uint back = ColorContrast.Mix(color, 0xFF000000, 0.28), backLight = ColorContrast.Mix(light, 0xFF000000, 0.22);
        var closed = new List<FileIconLayer> { new(FolderPath, color) { LightArgb = light } };
        var open = new List<FileIconLayer> { new(FolderBackPath, back) { LightArgb = backLight }, new(FolderFrontPath, color) { LightArgb = light } };
        if (emblem is not null)
        {
            closed.Add(Ink(emblem, 0.56f, 6.6f, 6.4f, 1.05f));
            open.Add(Ink(emblem, 0.52f, 5.9f, 7.6f, 1.05f));
        }

        return new FileIcon(name, "▸", color, what)
        {
            Shape = new FileIconShape(closed),
            OpenShape = new FileIconShape(open),
            LightArgb = ColorContrast.Ensure(color, ColorContrast.Text, LightGrounds),
        };
    }

    private static FileIconLayer Ink(string glyph, float scale, float x, float y, float stroke) =>
        new(FileIconShape.Transform(glyph, scale, x, y), InkDark) { LightArgb = InkLight, StrokeWidth = stroke };

    private static uint Fit(uint color) => ColorContrast.Ensure(color, ColorContrast.Line, LightGrounds);

    // A monogram of stroked letters, centred on the glyphs' 10-unit box.
    private static string Mono(string text)
    {
        const float cell = 4, gap = 1.2f, height = 6;
        float width = (text.Length * cell) + ((text.Length - 1) * gap);
        float scale = MathF.Min(MathF.Min(10 / width, 10 / height), 1.25f);
        float left = (10 - (width * scale)) / 2, top = (10 - (height * scale)) / 2;
        var path = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            path.Append(FileIconShape.Transform(Letters[text[i]], scale, left + (i * (cell + gap) * scale), top));
        }

        return path.ToString();
    }
}
