// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.CSharp;

/// <summary>
/// C#'s snippets (X part 9), registered by the plug-in as the "csharp" snippet provider: Visual Studio's everyday set
/// (prop, propfull, ctor, class, interface, record, struct, enum, for, forr, foreach, if, else, while, do, switch, try,
/// tryf, using, lock, cw, test for xUnit, nunit and mstest's, …) in VS Code's snippet syntax; a stop that appears twice
/// is filled at both places, and <c>${TM_FILENAME_BASE}</c> names a type after its file.
/// </summary>
public sealed class CSharpSnippets : ISnippetProvider
{
    /// <summary>Its name in <see cref="SnippetProviders"/>.</summary>
    public const string ProviderName = "csharp";

    private static readonly IReadOnlyList<Snippet> Set = Make();

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "C#'s snippets: prop, ctor, for, foreach, if, try, cw, test, …";

    /// <summary>Every snippet of the set.</summary>
    public static IReadOnlyList<Snippet> All => Set;

    /// <inheritdoc />
    public IReadOnlyList<Snippet> SnippetsFor(string? language) =>
        string.Equals(language, CSharpLanguage.RegularName, StringComparison.OrdinalIgnoreCase) || string.Equals(language, CSharpLanguage.ScriptName, StringComparison.OrdinalIgnoreCase) ? Set : [];

    private static IReadOnlyList<Snippet> Make()
    {
        IReadOnlyList<string> languages = [CSharpLanguage.RegularName, CSharpLanguage.ScriptName];
        Snippet S(string prefix, string description, string body) => new(prefix, [prefix], body, description) { Languages = languages, Source = "C#" };
        return
        [
            S("prop", "An automatic property", "public ${1:int} ${2:MyProperty} { get; set; }$0"),
            S("propg", "A property with a private setter", "public ${1:int} ${2:MyProperty} { get; private set; }$0"),
            S("propfull", "A property with a backing field", "private ${1:int} ${2:myVar};\n\npublic $1 ${3:MyProperty}\n{\n\tget { return $2; }\n\tset { $2 = value; }\n}$0"),
            S("ctor", "A constructor", "public ${1:${TM_FILENAME_BASE}}($2)\n{\n\t$0\n}"),
            S("class", "A class", "public class ${1:${TM_FILENAME_BASE}}\n{\n\t$0\n}"),
            S("interface", "An interface", "public interface ${1:IMyInterface}\n{\n\t$0\n}"),
            S("record", "A record", "public record ${1:MyRecord}($2);$0"),
            S("struct", "A struct", "public struct ${1:MyStruct}\n{\n\t$0\n}"),
            S("enum", "An enum", "public enum ${1:MyEnum}\n{\n\t$0\n}"),
            S("for", "A for loop", "for (int ${1:i} = 0; $1 < ${2:length}; $1++)\n{\n\t$0\n}"),
            S("forr", "A for loop backwards", "for (int ${1:i} = ${2:length} - 1; $1 >= 0; $1--)\n{\n\t$0\n}"),
            S("foreach", "A foreach loop", "foreach (${1:var} ${2:item} in ${3:collection})\n{\n\t$0\n}"),
            S("if", "An if statement", "if (${1:true})\n{\n\t$0\n}"),
            S("else", "An else block", "else\n{\n\t$0\n}"),
            S("while", "A while loop", "while (${1:true})\n{\n\t$0\n}"),
            S("do", "A do-while loop", "do\n{\n\t$0\n} while (${1:true});"),
            S("switch", "A switch statement", "switch (${1:value})\n{\n\tcase ${2:0}:\n\t\t$0\n\t\tbreak;\n\tdefault:\n\t\tbreak;\n}"),
            S("try", "A try-catch block", "try\n{\n\t$1\n}\ncatch (${2:Exception} ${3:ex})\n{\n\t$0\n}"),
            S("tryf", "A try-finally block", "try\n{\n\t$1\n}\nfinally\n{\n\t$0\n}"),
            S("using", "A using block", "using (${1:resource})\n{\n\t$0\n}"),
            S("lock", "A lock block", "lock (${1:this})\n{\n\t$0\n}"),
            S("cw", "Console.WriteLine", "Console.WriteLine($0);"),
            S("cwi", "Console.WriteLine of an interpolated string", "Console.WriteLine($\"$0\");"),
            S("namespace", "A file-scoped namespace", "namespace ${1:Name};\n\n$0"),
            S("main", "A Main method", "static void Main(string[] args)\n{\n\t$0\n}"),
            S("sim", "A static int Main", "static int Main(string[] args)\n{\n\t$0\n\treturn 0;\n}"),
            S("svm", "A static void Main", "static void Main(string[] args)\n{\n\t$0\n}"),
            S("mbox", "A method", "${1|public,private,protected,internal|} ${2:void} ${3:MyMethod}($4)\n{\n\t$0\n}"),
            S("async", "An async method", "${1|public,private,protected,internal|} async Task ${2:MyMethodAsync}($3)\n{\n\t$0\n}"),
            S("exception", "An exception class", "public class ${1:My}Exception : ${2:Exception}\n{\n\tpublic $1Exception() { }\n\tpublic $1Exception(string message) : base(message) { }\n\tpublic $1Exception(string message, Exception inner) : base(message, inner) { }\n}$0"),
            S("indexer", "An indexer", "public ${1:object} this[${2:int} index]\n{\n\tget { $3 }\n\tset { $0 }\n}"),
            S("iterator", "An iterator", "public IEnumerable<${1:int}> ${2:Items}()\n{\n\tyield return ${3:default};$0\n}"),
            S("test", "An xUnit test", "[Fact]\npublic void ${1:Test}()\n{\n\t$0\n}"),
            S("testa", "An async xUnit test", "[Fact]\npublic async Task ${1:Test}Async()\n{\n\t$0\n}"),
            S("theory", "An xUnit theory", "[Theory]\n[InlineData(${2:0})]\npublic void ${1:Test}(${3:int} ${4:value})\n{\n\t$0\n}"),
            S("nunit", "An NUnit test", "[Test]\npublic void ${1:Test}()\n{\n\t$0\n}"),
            S("mstest", "An MSTest test", "[TestMethod]\npublic void ${1:Test}()\n{\n\t$0\n}"),
            S("region", "A region", "#region ${1:Name}\n$TM_SELECTED_TEXT$0\n#endregion"),
            S("summary", "A documentation summary", "/// <summary>\n/// $0\n/// </summary>"),
        ];
    }
}
