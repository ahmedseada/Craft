// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Plugins;
using Craft.Tests.Runner;
using Idrak.Abstraction.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Craft.Tests;

/// <summary>
/// B3 (the owner's third Windows run): the sample plug-in, built against contract 1.0, failed with a
/// TypeInitializationException: KeyBinding had gained two parameters and EditorKey's letters had moved. A plug-in
/// compiled here against the 1.0 shapes (a small Craft.Abstractions of the 1.0 signatures, as the old DLL saw them) loads
/// on this Craft and binds the keys it meant; one calling a member this Craft lacks is reported by its cause.
/// </summary>
[TestGroup("PluginKit")]
public static class ContractCompatibilityTests
{
    // Craft.Abstractions as contract 1.0 had the parts the sample used (its KeyBinding, EditorKey.W = 41), plus a member
    // this Craft does not have (for the failure's report).
    private const string Contract10 = """
        namespace Craft.Abstractions
        {
            [System.Flags]
            public enum KeyModifiers { None = 0, Shift = 1, Control = 2, Alt = 4 }

            public enum EditorKey { None = 0, A = 16, W = 41 }

            public readonly record struct KeyChord(EditorKey Key, KeyModifiers Modifiers);

            public sealed record KeyBinding(KeyChord Chord, string CommandId, bool OverridesEditor = false)
            {
                public static KeyBinding Legacy(string commandId) => new(default, commandId);
            }

            public static class EditorKeyBindings
            {
                public static void Register(KeyBinding binding) { }

                public static bool Unregister(KeyChord chord) => false;
            }
        }
        """;

    private static string Plugin(string name, string binding) => $$"""
        using System.Collections.Generic;
        using Craft.Abstractions;
        using Idrak.Abstraction.Plugins;

        [assembly: IdrakPluginPackage(typeof(Old.OldPackage))]

        namespace Old
        {
            public sealed class OldPackage : IIdrakPluginPackage
            {
                public string Name => "{{name}}";

                public IReadOnlyList<IIdrakPlugin> All { get; } = [new OldRegistration()];
            }

            public sealed class OldRegistration() : IdrakPlugin("{{name}}")
            {
                public static KeyBinding Binding { get; } = {{binding}};

                protected override void RegisterParts() =>
                    Part(() => EditorKeyBindings.Register(Binding), () => EditorKeyBindings.Unregister(Binding.Chord));
            }
        }
        """;

    [Test]
    public static void APluginBuiltAgainstContract10LoadsAndBindsTheKeysItMeant()
    {
        using var temp = new TempFolder();
        string folder = Compile(temp, "Craft.Plugins.Old10",
            "new(new KeyChord(EditorKey.W, KeyModifiers.Control | KeyModifiers.Shift), \"old10.count\")");
        var result = CraftPlugins.LoadFolder(folder);
        try
        {
            Check.True(result.Loaded, $"the 1.0 plug-in loads ({result.Error})");
            var chord = new KeyChord(EditorKey.W, KeyModifiers.Control | KeyModifiers.Shift);
            Check.True(EditorKeyBindings.Find(chord) is { CommandId: "old10.count", OverridesEditor: false, Then: null, When: KeyContext.Anywhere },
                $"its binding is Ctrl+Shift+W, as it meant (1.0's EditorKey.W is 41 again): {string.Join(", ", EditorKeyBindings.All.Where(b => b.CommandId == "old10.count").Select(b => b.Name))}");
            Check.Equal(41, (int)EditorKey.W, "EditorKey.W keeps its 1.0 value");
            Check.Equal(16, (int)EditorKey.A, "EditorKey.A keeps its 1.0 value");
            Check.Equal(62, (int)EditorKey.F12, "EditorKey.F12 keeps its 1.0 value");
        }
        finally
        {
            if (result.Loaded)
            {
                IdrakPlugins.UnregisterAll(IdrakPlugins.Find("Craft.Plugins.Old10") is { } plugin ? IdrakPlugins.PackageOf(plugin.GetType().Assembly)! : throw new InvalidOperationException("not registered"));
            }
        }
    }

    [Test]
    public static void ALoadFailureNamesItsCauseNotTheTypeInitializer()
    {
        using var temp = new TempFolder();
        string folder = Compile(temp, "Craft.Plugins.Old10Missing", "KeyBinding.Legacy(\"old10.legacy\")");
        var result = CraftPlugins.LoadFolder(folder);
        Check.True(!result.Loaded && result.Error is { } error
                   && error.StartsWith("MissingMethodException:", StringComparison.Ordinal)
                   && error.Contains("Legacy", StringComparison.Ordinal)
                   && error.Contains("type initializer of Old.OldRegistration", StringComparison.Ordinal)
                   && error.Contains("rebuild it against", StringComparison.Ordinal),
            $"the cause, where it was thrown and what to do ({result.Error})");
        var wrapped = new TypeInitializationException("Old.X", new System.Reflection.TargetInvocationException(new MissingFieldException("gone")));
        Check.True(CraftPlugins.Describe(wrapped).StartsWith("MissingFieldException: gone (in the type initializer of Old.X)", StringComparison.Ordinal),
            $"every wrapper unwrapped ({CraftPlugins.Describe(wrapped)})");
    }

    // The plug-in's folder: its assembly compiled against the 1.0 shapes (the shapes' own assembly left out, as Craft's
    // pre-installed plug-ins leave the shared contracts out).
    private static string Compile(TempFolder temp, string name, string binding)
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var contract = CSharpCompilation.Create("Craft.Abstractions", [CSharpSyntaxTree.ParseText(Contract10, parse)], platform, options);
        using var contractImage = new MemoryStream();
        Emit(contract, contractImage);
        var references = new List<MetadataReference>(platform)
        {
            MetadataReference.CreateFromImage(contractImage.ToArray()),
            MetadataReference.CreateFromFile(typeof(IIdrakPlugin).Assembly.Location),
        };
        var plugin = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(Plugin(name, binding), parse)], references, options);
        string folder = temp.File(name);
        Directory.CreateDirectory(folder);
        using (var file = File.Create(Path.Combine(folder, name + ".dll")))
        {
            Emit(plugin, file);
        }

        return folder;
    }

    private static void Emit(CSharpCompilation compilation, Stream to)
    {
        var emitted = compilation.Emit(to);
        if (!emitted.Success)
        {
            throw new InvalidOperationException($"{compilation.AssemblyName} did not compile: {string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))}");
        }
    }
}
