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
        Assembly.Load(new AssemblyName("Craft.Ai")),
        Assembly.Load(new AssemblyName("Craft.Plugins.Tools")),
        Assembly.Load(new AssemblyName("Craft.Plugins.StarCoder2")),
        Assembly.Load(new AssemblyName("Craft.Plugins.Sampler")),
        Assembly.Load(new AssemblyName("Craft.Plugins.Sandbox")),
        Assembly.Load(new AssemblyName("Craft.Plugins.Placement")),
        Assembly.Load(new AssemblyName("Craft.Voice")),
        Assembly.Load(new AssemblyName("Craft.Plugins.Learning")),
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

    /// <summary>
    /// B3: Craft.Abstractions is the plug-ins' contract. Every public member of the contract's version (its list, kept in
    /// api/Craft.Abstractions.contract.txt under "contract MAJOR.MINOR") must still be there: a plug-in built against it
    /// calls it by its exact signature (the sample's KeyBinding constructor) and holds an enum's numbers (EditorKey).
    /// A change or a removal fails here unless CraftContracts.Version's major has stepped; an addition is merged into the
    /// list with CRAFT_UPDATE=1, as the dumps are.
    /// </summary>
    [Test]
    public static void RemovedOrChangedMembersOfTheContractNeedAContractStep()
    {
        var path = TestRunner.RepositoryPath("api", "Craft.Abstractions.contract.txt");
        var current = PublicApi.ContractEntries(PublicApi.Dump(typeof(ITextBuffer).Assembly));
        bool update = Environment.GetEnvironmentVariable("CRAFT_UPDATE") == "1";
        var running = CraftContracts.Version;
        if (!File.Exists(path))
        {
            if (update)
            {
                PublicApi.WriteContract(path, running, current);
                return;
            }

            Check.True(false, "no contract list at api/Craft.Abstractions.contract.txt (run with CRAFT_UPDATE=1 to write it)");
        }

        var (version, listed) = PublicApi.ReadContract(path);
        var problems = PublicApi.ContractProblems(listed, current, version, running);
        if (update && problems.Count == 0)
        {
            // Additions join the list (a minor step names the running version); a major step starts the list again from the surface as it is.
            PublicApi.WriteContract(path, running > version ? running : version, running.Major > version.Major ? current : [.. listed.Union(current)]);
            return;
        }

        Check.True(problems.Count == 0, string.Join("\n", problems));
        var added = current.Except(listed).ToList();
        Check.True(added.Count == 0, $"{added.Count} new public members of Craft.Abstractions are not in the contract's list yet (an addition keeps the version): run with CRAFT_UPDATE=1. First: {string.Join("; ", added.Take(5))}");
    }

    /// <summary>The check itself: the 1.0 KeyBinding constructor and EditorKey.W's value, taken away, are named; a major step allows it.</summary>
    [Test]
    public static void TheContractCheckNamesTheMembersARemovalBreaks()
    {
        string[] listed =
        [
            "Craft.Abstractions.EditorKey",
            "Craft.Abstractions.EditorKey  W = 41",
            "Craft.Abstractions.KeyBinding",
            "Craft.Abstractions.KeyBinding  ctor(Craft.Abstractions.KeyChord Chord, System.String CommandId, System.Boolean OverridesEditor)",
        ];
        string[] now = ["Craft.Abstractions.EditorKey", "Craft.Abstractions.EditorKey  W = 42", "Craft.Abstractions.KeyBinding"];
        var problems = PublicApi.ContractProblems(listed, now, new Version(1, 0), new Version(1, 0));
        Check.True(problems.Count == 1 && problems[0].Contains("EditorKey  W = 41", StringComparison.Ordinal) && problems[0].Contains("KeyBinding  ctor(", StringComparison.Ordinal)
                   && problems[0].Contains("CraftContracts.Version", StringComparison.Ordinal), $"both named, with what to do: {string.Join(" | ", problems)}");
        Check.True(PublicApi.ContractProblems(listed, now, new Version(1, 0), new Version(2, 0)).Count == 0, "a major step allows a removal");
        Check.True(PublicApi.ContractProblems(listed, [.. listed, "Craft.Abstractions.New"], new Version(1, 0), new Version(1, 0)).Count == 0, "an addition breaks nothing");
        var entries = PublicApi.ContractEntries("sealed class Craft.Abstractions.A : System.IEquatable<Craft.Abstractions.A>, Idrak.Abstraction.IKind\n  ctor(System.Int32 x)\nenum Craft.Abstractions.E : System.IComparable\n  One = 1\n");
        Check.True(entries.SetEquals([
            "Craft.Abstractions.A", "Craft.Abstractions.A : System.IEquatable<Craft.Abstractions.A>", "Craft.Abstractions.A : Idrak.Abstraction.IKind",
            "Craft.Abstractions.A  ctor(System.Int32 x)", "Craft.Abstractions.E", "Craft.Abstractions.E : System.IComparable", "Craft.Abstractions.E  One = 1",
        ]), $"entries: a type, each base, each member under its type ({string.Join(" | ", entries)})");
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

    /// <summary>
    /// The contract's entries of a dump: each type by its name, each of its bases ("NAME : BASE"), each member under its
    /// type's name ("NAME  member"); a type's kind is not one (unsealing a class breaks no plug-in).
    /// </summary>
    public static HashSet<string> ContractEntries(string dump)
    {
        var entries = new HashSet<string>(StringComparer.Ordinal);
        string type = "";
        foreach (var line in dump.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("  ", StringComparison.Ordinal))
            {
                entries.Add(type + "  " + line[2..]);
                continue;
            }

            int colon = line.IndexOf(" : ", StringComparison.Ordinal);
            string head = colon < 0 ? line : line[..colon];
            type = head;
            foreach (var kind in (string[])["static class ", "abstract class ", "sealed class ", "class ", "enum ", "interface ", "struct ", "delegate "])
            {
                if (head.StartsWith(kind, StringComparison.Ordinal))
                {
                    type = head[kind.Length..];
                    break;
                }
            }

            entries.Add(type);
            if (colon >= 0)
            {
                foreach (var baseType in TopLevel(line[(colon + 3)..]))
                {
                    entries.Add(type + " : " + baseType);
                }
            }
        }

        return entries;
    }

    /// <summary>The problems of <paramref name="current"/> against the contract's <paramref name="listed"/> entries: none when nothing was removed or the major stepped.</summary>
    public static List<string> ContractProblems(IEnumerable<string> listed, IEnumerable<string> current, Version listedVersion, Version running)
    {
        var now = current.ToHashSet(StringComparer.Ordinal);
        var gone = listed.Where(e => !now.Contains(e)).Order(StringComparer.Ordinal).ToList();
        if (gone.Count == 0 || running.Major > listedVersion.Major)
        {
            return [];
        }

        return [$"{gone.Count} public members of Craft.Abstractions as contract {listedVersion.ToString(2)} has them were changed or removed, so a plug-in built against it fails to load "
                + $"(a missing method, another enum value): {string.Join("; ", gone.Take(20))}{(gone.Count > 20 ? "; …" : "")}. Keep the old member beside the new one (a constructor, a Deconstruct, an "
                + $"enum value: a new key goes at the end), or step CraftContracts.Version's major (now {running.ToString(2)}) and run with CRAFT_UPDATE=1."];
    }

    /// <summary>The contract's version and entries from its file.</summary>
    public static (Version Version, HashSet<string> Entries) ReadContract(string path)
    {
        var lines = File.ReadAllLines(path);
        var version = lines.Length > 0 && lines[0].StartsWith("contract ", StringComparison.Ordinal) && CraftContracts.Parse(lines[0].AsSpan("contract ".Length)) is { } parsed
            ? parsed
            : throw new InvalidDataException($"{path}: its first line is not 'contract MAJOR.MINOR'.");
        return (version, lines.Skip(1).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>Writes the contract's version and entries, in order.</summary>
    public static void WriteContract(string path, Version version, IEnumerable<string> entries) =>
        File.WriteAllText(path, $"contract {version.ToString(2)}\n" + string.Concat(entries.Order(StringComparer.Ordinal).Select(e => e + "\n")));

    // The comma-separated parts of a base list, commas inside angle brackets kept.
    private static IEnumerable<string> TopLevel(string list)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < list.Length; i++)
        {
            switch (list[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return list[start..i].Trim();
                    start = i + 1;
                    break;
            }
        }

        if (start < list.Length)
        {
            yield return list[start..].Trim();
        }
    }

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
