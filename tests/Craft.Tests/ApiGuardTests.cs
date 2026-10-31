// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Reflection;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The public API guard: each library's public surface, dumped from its assembly, must equal its checked-in dump
/// (api/&lt;assembly&gt;.txt). CRAFT_UPDATE=1 rewrites the dumps after a deliberate change.
/// </summary>
[TestGroup("ApiGuard")]
public static class ApiGuardTests
{
    /// <summary>The libraries whose API is guarded (the UI projects follow with their own dumps once they hold public API).</summary>
    internal static readonly Assembly[] Libraries =
    [
        typeof(ITextBuffer).Assembly,
        typeof(CraftOptions).Assembly,
        Assembly.Load(new AssemblyName("Craft.Editor")),
        Assembly.Load(new AssemblyName("Craft.CSharp")),
    ];

    [Test]
    public static void PublicApiMatchesTheDumps()
    {
        var problems = new List<string>();
        bool update = Environment.GetEnvironmentVariable("CRAFT_UPDATE") == "1";
        foreach (var assembly in Libraries)
        {
            var name = assembly.GetName().Name!;
            var path = TestRunner.RepositoryPath("api", name + ".txt");
            var actual = PublicApi.Dump(assembly);
            if (update)
            {
                File.WriteAllText(path, actual);
                continue;
            }

            if (!File.Exists(path))
            {
                problems.Add($"{name}: no dump at api/{name}.txt (run with CRAFT_UPDATE=1 to write it)");
                continue;
            }

            var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
            if (expected != actual)
            {
                problems.Add($"{name}: {FirstDifference(expected, actual)} (a deliberate change: run with CRAFT_UPDATE=1)");
            }
        }

        Check.True(problems.Count == 0, "the public API differs from its dump: " + string.Join("; ", problems));
    }

    private static string FirstDifference(string expected, string actual)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : "(end)";
            var y = i < b.Length ? b[i] : "(end)";
            if (x != y)
            {
                return $"line {i + 1}: dump '{x}', assembly '{y}'";
            }
        }

        return "same lines, different text";
    }
}

/// <summary>A readable, ordered dump of an assembly's public and protected surface.</summary>
internal static class PublicApi
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static string Dump(Assembly assembly)
    {
        var text = new StringBuilder();
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            text.Append(TypeLine(type)).Append('\n');
            foreach (var line in Members(type).Order(StringComparer.Ordinal))
            {
                text.Append("  ").Append(line).Append('\n');
            }
        }

        return text.ToString();
    }

    private static string TypeLine(Type type)
    {
        string kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct"
            : typeof(Delegate).IsAssignableFrom(type) ? "delegate" : type.IsAbstract && type.IsSealed ? "static class"
            : type.IsAbstract ? "abstract class" : type.IsSealed ? "sealed class" : "class";
        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum) && !typeof(Delegate).IsAssignableFrom(type))
        {
            bases.Add(Name(baseType));
        }

        bases.AddRange(type.GetInterfaces().Where(i => i.IsPublic || i.IsNestedPublic).Select(Name).Order(StringComparer.Ordinal));
        return $"{kind} {Name(type)}" + (bases.Count > 0 ? " : " + string.Join(", ", bases) : "");
    }

    private static IEnumerable<string> Members(Type type)
    {
        if (type.IsEnum)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                yield return $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture)}";
            }

            yield break;
        }

        foreach (var member in type.GetMembers(Declared))
        {
            switch (member)
            {
                case ConstructorInfo c when Visible(c):
                    yield return $"{Access(c)}ctor({Parameters(c)})";
                    break;
                case MethodInfo m when Visible(m) && !m.IsSpecialName:
                    yield return $"{Access(m)}{(m.IsStatic ? "static " : "")}{Name(m.ReturnType)} {m.Name}{Generic(m)}({Parameters(m)})";
                    break;
                case PropertyInfo p when (p.GetMethod is { } g && Visible(g)) || (p.SetMethod is { } s && Visible(s)):
                    yield return $"{(p.GetMethod?.IsStatic ?? p.SetMethod!.IsStatic ? "static " : "")}{Name(p.PropertyType)} {p.Name}" +
                                 (p.GetIndexParameters() is { Length: > 0 } index ? $"[{string.Join(", ", index.Select(i => Name(i.ParameterType)))}]" : "") +
                                 " {" + (p.GetMethod is { } get && Visible(get) ? " get;" : "") + (p.SetMethod is { } set && Visible(set) ? (IsInit(set) ? " init;" : " set;") : "") + " }";
                    break;
                case FieldInfo f when f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly:
                    yield return $"{(f.IsLiteral ? "const " : f.IsStatic ? "static " : "")}{(f.IsInitOnly ? "readonly " : "")}{Name(f.FieldType)} {f.Name}" +
                                 (f.IsLiteral ? $" = {f.GetRawConstantValue()}" : "");
                    break;
                case EventInfo e when e.AddMethod is { } add && Visible(add):
                    yield return $"event {Name(e.EventHandlerType!)} {e.Name}";
                    break;
            }
        }
    }

    private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static string Access(MethodBase method) => method.IsPublic ? "" : "protected ";

    private static bool IsInit(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string Generic(MethodInfo method) =>
        method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">" : "";

    private static string Parameters(MethodBase method) => string.Join(", ", method.GetParameters().Select(p =>
        (p.IsOut ? "out " : p.ParameterType.IsByRef ? (p.IsIn ? "in " : "ref ") : "") +
        Name(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType) + " " + p.Name +
        (p.HasDefaultValue ? " = " + (p.DefaultValue switch { null => "null", string s => $"\"{s}\"", bool b => b ? "true" : "false", var v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) }) : "")));

    private static string Name(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsArray)
        {
            return Name(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsByRef)
        {
            return Name(type.GetElementType()!) + "&";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Name(underlying) + "?";
        }

        var name = type.IsNested ? Name(type.DeclaringType!) + "." + type.Name : (type.Namespace is { } ns ? ns + "." : "") + type.Name;
        if (type.IsGenericType)
        {
            name = name[..name.IndexOf('`', StringComparison.Ordinal)] + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
        }

        return name;
    }
}
