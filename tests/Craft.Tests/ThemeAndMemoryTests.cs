// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>The themes' semantic classes and their contrast; the memory-pressure monitors and the languages' release.</summary>
[TestGroup("Inventory")]
public static class ThemeAndMemoryTests
{
    private static readonly string[] SemanticClasses =
    [
        TokenClasses.ClassName, TokenClasses.RecordClassName, TokenClasses.StructName, TokenClasses.RecordStructName, TokenClasses.InterfaceName,
        TokenClasses.EnumName, TokenClasses.DelegateName, TokenClasses.TypeParameterName, TokenClasses.MethodName, TokenClasses.ExtensionMethodName,
        TokenClasses.PropertyName, TokenClasses.FieldName, TokenClasses.EventName, TokenClasses.LocalName, TokenClasses.ParameterName,
        TokenClasses.NamespaceName, TokenClasses.ConstantName, TokenClasses.EnumMemberName, TokenClasses.StaticSymbol,
    ];

    private static double Luminance(uint argb)
    {
        static double Channel(uint value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel((argb >> 16) & 0xFF)) + (0.7152 * Channel((argb >> 8) & 0xFF)) + (0.0722 * Channel(argb & 0xFF));
    }

    private static double Contrast(uint a, uint b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Test]
    public static void EveryClassOfBothThemesIsReadable()
    {
        foreach (var name in (string[])[EditorThemes.Dark, EditorThemes.Light])
        {
            var theme = EditorThemes.Get(name);
            foreach (var tokenClass in SemanticClasses)
            {
                Check.True(theme.StyleOf(tokenClass).Argb != theme.Colors.Foreground || tokenClass == TokenClasses.NamespaceName,
                    $"{name}: '{tokenClass}' has a style of its own");
            }

            foreach (var tokenClass in SemanticClasses.Concat([TokenClasses.Keyword, TokenClasses.String, TokenClasses.Comment, TokenClasses.Number, TokenClasses.Punctuation, TokenClasses.PreprocessorKeyword]))
            {
                var style = theme.StyleOf(tokenClass);
                double ground = Contrast(style.Argb, theme.Colors.Background);
                double caretLine = Contrast(style.Argb, theme.Colors.CurrentLine);
                Check.True(ground >= 4.5 && caretLine >= 4.5,
                    $"{name}: '{tokenClass}' reads at 4.5:1 or more on the ground ({ground:F1}:1) and the caret line ({caretLine:F1}:1)");
            }

            var method = theme.StyleOf(TokenClasses.MethodName);
            var staticMethod = theme.StyleOf(TokenClasses.WithStaticSymbol(TokenClasses.MethodName));
            Check.True(staticMethod.Argb == method.Argb && staticMethod.Italic, $"{name}: a static method keeps the method's colour, in italic");
        }

        Check.True(ReferenceEquals(TokenClasses.WithStaticSymbol(TokenClasses.FieldName), TokenClasses.WithStaticSymbol(TokenClasses.FieldName)),
            "a static class name is made once and shared");
        Check.Equal("field name + static symbol", TokenClasses.WithStaticSymbol(TokenClasses.FieldName), "the joined name");
    }

    [Test]
    public static void MemoryMonitorsAreRegisteredAndTheLanguagesRelease()
    {
        Check.True(MemoryPressureMonitors.Find(MemoryPressureMonitors.Gc) is not null && MemoryPressureMonitors.Find("NONE") is not null, "the library's gc and none");
        Check.Throws<NotRegisteredException>(() => MemoryPressureMonitors.Get("no-such-monitor"), "an unknown monitor is not registered");
        using (MemoryPressureMonitors.Get(MemoryPressureMonitors.Gc).Watch(_ => { }))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        var language = new ReleasingLanguage();
        EditorLanguages.Register(language);
        try
        {
            MemoryPressureMonitors.ReleaseLanguages();
            Check.Equal(1, language.Released, "every registered language is asked to release");
        }
        finally
        {
            EditorLanguages.Unregister(language.Name);
        }
    }

    private sealed class ReleasingLanguage : ILanguage
    {
        public int Released;

        public string Name => "releasing-test-language";

        public string Summary => "a test language that counts its releases";

        public IReadOnlyList<string> Extensions => [".releasing-test"];

        public LanguageRules Rules => LanguageRules.Plain;

        public ILanguageSession Open(ITextBuffer buffer) => throw new NotSupportedException("never opened");

        public void ReleaseMemory() => Interlocked.Increment(ref Released);
    }
}
