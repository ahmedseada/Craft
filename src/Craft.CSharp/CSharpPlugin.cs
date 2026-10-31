// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.CSharp;
using Craft.CSharp.Workspaces;
using Idrak.Abstraction.Plugins;
using Microsoft.CodeAnalysis;

[assembly: IdrakPluginPackage(typeof(CSharpPluginPackage))]

namespace Craft.CSharp;

/// <summary>
/// C# as a plug-in: registers "csharp" (".cs") and "csharp-script" (".csx") in <see cref="EditorLanguages"/>. The app
/// ships it pre-installed in its <c>plugins/Craft.CSharp/</c> folder and loads it from there (Craft.Core's
/// <c>CraftPlugins</c>); removing the folder (or disabling it) leaves C# files as plain text.
/// </summary>
public sealed class CSharpPlugin() : IdrakPlugin("csharp")
{
    /// <summary>What it brings.</summary>
    public static string Summary => "C# on Roslyn: classification, diagnostics, completion, signature help, hover, go to definition and formatting of .cs and .csx files; the csproj and loose workspace readers";

    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorLanguages.Register(new CSharpLanguage()), () => EditorLanguages.Unregister(CSharpLanguage.RegularName));
        Part(() => EditorLanguages.Register(new CSharpLanguage(SourceCodeKind.Script)), () => EditorLanguages.Unregister(CSharpLanguage.ScriptName));
        Part(() => WorkspaceReaders.Register(new CsprojWorkspaceReader()), () => WorkspaceReaders.Unregister(CsprojWorkspaceReader.ReaderName));
        Part(() => WorkspaceReaders.Register(new LooseWorkspaceReader()), () => WorkspaceReaders.Unregister(LooseWorkspaceReader.ReaderName));
        // Dictation's C# words (phase V): what they type in a C# document, over the voice plug-in's every-language table.
        foreach (var symbol in SpokenSymbolsOfCSharp)
        {
            Part(() => SpokenSymbols.Register(symbol), () => SpokenSymbols.Unregister(symbol.Name));
        }
    }

    /// <summary>C#'s spoken symbols: "arrow" and "lambda" type " => ", "null coalescing" " ?? ", "nullable" "?", "generic" "&lt;".</summary>
    internal static IReadOnlyList<SpokenSymbol> SpokenSymbolsOfCSharp { get; } =
    [
        new("arrow", "=>", CSharpLanguage.RegularName, SpaceBefore: true, SpaceAfter: true),
        new("lambda", "=>", CSharpLanguage.RegularName, SpaceBefore: true, SpaceAfter: true),
        new("null coalescing", "??", CSharpLanguage.RegularName, SpaceBefore: true, SpaceAfter: true),
        new("nullable", "?", CSharpLanguage.RegularName, SpaceAfter: true),
        new("open generic", "<", CSharpLanguage.RegularName),
        new("close generic", ">", CSharpLanguage.RegularName, SpaceAfter: true),
    ];
}

/// <summary>Craft.CSharp's plug-in package: <see cref="CSharpPlugin"/>.</summary>
public sealed class CSharpPluginPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.CSharp";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new CSharpPlugin()];
}
