// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Xml.Linq;

namespace Craft.CSharp.Workspaces;

/// <summary>
/// A small evaluation of an MSBuild project file, without MSBuild: Directory.Build.props, the project, then
/// Directory.Build.targets, each read in order with their plain <c>Import</c>s; properties set in order (<c>$(Name)</c>
/// expanded), items collected with their <c>Include</c>, <c>Remove</c> and metadata. Conditions of the shapes
/// <c>'a' == 'b'</c>, <c>'a' != 'b'</c>, <c>Exists('path')</c>, <c>!Exists(...)</c>, <c>true</c>/<c>false</c>, joined with
/// <c>and</c>, <c>or</c> and parentheses, are evaluated; one with a property function (<c>$([...])</c>) or another shape
/// is taken as false, and noted. Targets do not run.
/// </summary>
internal sealed class ProjectEvaluator
{
    private const int MaxImportDepth = 16;
    private readonly Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Type, string Include, string? Remove, XElement Element, string Folder)> _items = [];
    private readonly HashSet<string> _imported = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _global = new(StringComparer.OrdinalIgnoreCase);

    public ProjectEvaluator(string projectFile, string configuration, string? targetFramework = null)
    {
        if (targetFramework is not null)
        {
            // A global property, as an inner build of a multi-targeted project gets it: the project cannot set it.
            _properties["TargetFramework"] = targetFramework;
            _global.Add("TargetFramework");
        }

        ProjectFile = Path.GetFullPath(projectFile);
        Folder = Path.GetDirectoryName(ProjectFile)!;
        _properties["MSBuildProjectFullPath"] = ProjectFile;
        _properties["MSBuildProjectDirectory"] = Folder;
        _properties["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(ProjectFile);
        _properties["MSBuildProjectFile"] = Path.GetFileName(ProjectFile);
        _properties["MSBuildProjectExtension"] = Path.GetExtension(ProjectFile);
        _properties["Configuration"] = configuration;
        _properties["Platform"] = "AnyCPU";
        _properties["OS"] = OperatingSystem.IsWindows() ? "Windows_NT" : "Unix";

        if (FindUpward(Folder, "Directory.Build.props") is { } props)
        {
            Import(props, 0);
        }

        var project = Load(ProjectFile);
        Sdk = project?.Root?.Attribute("Sdk")?.Value;
        if (project?.Root is { } root)
        {
            _imported.Add(ProjectFile);
            Walk(root, Folder, 0);
        }

        if (FindUpward(Folder, "Directory.Build.targets") is { } targets)
        {
            Import(targets, 0);
        }
    }

    public string ProjectFile { get; }

    public string Folder { get; }

    public string? Sdk { get; }

    public List<string> Notes { get; } = [];

    public string? this[string name] => _properties.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    public bool Flag(string name) => this[name] is { } value && value.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The items of <paramref name="type"/>: each Include (split at ';', expanded) with the Remove of the same element, the element and the folder of its file.</summary>
    public IEnumerable<(string Include, string? Remove, XElement Element, string Folder)> Items(string type)
    {
        foreach (var item in _items)
        {
            if (item.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            {
                yield return (item.Include, item.Remove, item.Element, item.Folder);
            }
        }
    }

    /// <summary>A metadata value of an item: an attribute or a child element, expanded.</summary>
    public string? Metadata(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value ?? element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        return value is null ? null : Expand(value, out _);
    }

    public static string? FindUpward(string folder, string fileName)
    {
        for (var at = new DirectoryInfo(folder); at is not null; at = at.Parent)
        {
            var path = Path.Combine(at.FullName, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static XDocument? Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return XDocument.Load(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private void Import(string path, int depth)
    {
        path = Path.GetFullPath(path);
        if (depth > MaxImportDepth || !_imported.Add(path) || Load(path)?.Root is not { } root)
        {
            return;
        }

        var saved = _properties.GetValueOrDefault("MSBuildThisFileDirectory");
        Walk(root, Path.GetDirectoryName(path)!, depth);
        if (saved is not null)
        {
            _properties["MSBuildThisFileDirectory"] = saved;
        }
    }

    private void Walk(XElement root, string folder, int depth)
    {
        _properties["MSBuildThisFileDirectory"] = folder + Path.DirectorySeparatorChar;
        foreach (var element in root.Elements())
        {
            if (!Holds(element))
            {
                continue;
            }

            switch (element.Name.LocalName)
            {
                case "PropertyGroup":
                    foreach (var property in element.Elements())
                    {
                        if (Holds(property) && !_global.Contains(property.Name.LocalName))
                        {
                            var value = Expand(property.Value, out bool unknown);
                            if (unknown)
                            {
                                Notes.Add($"{property.Name.LocalName} in {Path.GetFileName(folder)} uses a property function, not evaluated");
                            }

                            _properties[property.Name.LocalName] = value.Trim();
                        }
                    }

                    break;
                case "ItemGroup":
                    foreach (var item in element.Elements())
                    {
                        if (!Holds(item))
                        {
                            continue;
                        }

                        string? remove = item.Attribute("Remove")?.Value is { } r ? Expand(r, out _) : null;
                        string include = item.Attribute("Include")?.Value is { } i ? Expand(i, out _) : "";
                        _items.Add((item.Name.LocalName, include, remove, item, folder));
                    }

                    break;
                case "Import":
                    if (element.Attribute("Project")?.Value is { } project)
                    {
                        var target = Expand(project, out bool unknown);
                        if (!unknown && target.Length > 0)
                        {
                            Import(Path.IsPathRooted(target) ? target : Path.Combine(folder, target), depth + 1);
                            _properties["MSBuildThisFileDirectory"] = folder + Path.DirectorySeparatorChar;
                        }
                    }

                    break;
            }
        }
    }

    private bool Holds(XElement element)
    {
        if (element.Attribute("Condition")?.Value is not { } condition || condition.Trim().Length == 0)
        {
            return true;
        }

        var expanded = Expand(condition, out bool unknown);
        if (unknown)
        {
            return false;
        }

        var parser = new ConditionParser(expanded, _properties.GetValueOrDefault("MSBuildThisFileDirectory") ?? Folder);
        return parser.Parse() ?? false;
    }

    /// <summary><paramref name="text"/> with every <c>$(Name)</c> replaced by its value ("" when unset); a <c>$([...])</c> sets <paramref name="unknown"/>.</summary>
    public string Expand(string text, out bool unknown)
    {
        unknown = false;
        if (!text.Contains("$(", StringComparison.Ordinal))
        {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        int at = 0;
        while (at < text.Length)
        {
            int open = text.IndexOf("$(", at, StringComparison.Ordinal);
            if (open < 0)
            {
                result.Append(text, at, text.Length - at);
                break;
            }

            result.Append(text, at, open - at);
            if (open + 2 < text.Length && text[open + 2] == '[')
            {
                int depthParen = 0;
                int i = open + 1;
                for (; i < text.Length; i++)
                {
                    if (text[i] == '(')
                    {
                        depthParen++;
                    }
                    else if (text[i] == ')' && --depthParen == 0)
                    {
                        break;
                    }
                }

                int end = Math.Min(text.Length, i + 1);
                if (Function(text[(open + 2)..Math.Max(open + 2, end - 1)]) is { } value)
                {
                    result.Append(value);
                }
                else
                {
                    unknown = true;
                }

                at = end;
                continue;
            }

            int close = text.IndexOf(')', open + 2);
            if (close < 0)
            {
                result.Append(text, open, text.Length - open);
                break;
            }

            result.Append(_properties.GetValueOrDefault(text[(open + 2)..close].Trim()) ?? "");
            at = close + 1;
        }

        return result.ToString();
    }

    /// <summary>
    /// The few property functions project files commonly use: <c>[MSBuild]::IsOSPlatform</c>,
    /// <c>GetTargetPlatformIdentifier</c>, <c>GetTargetFrameworkIdentifier</c>, <c>[System.IO.Path]::GetFullPath</c> and
    /// <c>Combine</c>; null for another (the caller notes it).
    /// </summary>
    private string? Function(string call)
    {
        int colons = call.IndexOf("]::", StringComparison.Ordinal);
        int paren = call.IndexOf('(', StringComparison.Ordinal);
        if (colons < 0 || paren < colons || !call.EndsWith(')'))
        {
            return null;
        }

        var type = call[1..colons].Trim();
        var method = call[(colons + 3)..paren].Trim();
        var arguments = new List<string>();
        var inner = call[(paren + 1)..^1];
        int depth = 0;
        int start = 0;
        bool quoted = false;
        for (int i = 0; i <= inner.Length; i++)
        {
            if (i == inner.Length || (inner[i] == ',' && depth == 0 && !quoted))
            {
                var argument = Expand(inner[start..i].Trim(), out bool unknown);
                if (unknown)
                {
                    return null;
                }

                arguments.Add(argument.Trim('\'', ' '));
                start = i + 1;
                continue;
            }

            switch (inner[i])
            {
                case '\'':
                    quoted = !quoted;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
            }
        }

        bool msbuild = type.Equals("MSBuild", StringComparison.OrdinalIgnoreCase);
        bool path = type.Equals("System.IO.Path", StringComparison.OrdinalIgnoreCase);
        return method switch
        {
            "IsOSPlatform" when msbuild && arguments.Count == 1 => (arguments[0].ToUpperInvariant() switch
            {
                "WINDOWS" => OperatingSystem.IsWindows(),
                "LINUX" => OperatingSystem.IsLinux(),
                "OSX" or "MACOS" => OperatingSystem.IsMacOS(),
                "ANDROID" => OperatingSystem.IsAndroid(),
                _ => false,
            }) ? "true" : "false",
            "GetTargetPlatformIdentifier" when msbuild && arguments.Count == 1 => PlatformOf(arguments[0]),
            "GetTargetFrameworkIdentifier" when msbuild && arguments.Count == 1 => arguments[0].StartsWith("net", StringComparison.OrdinalIgnoreCase)
                && arguments[0].Length > 3 && char.IsAsciiDigit(arguments[0][3]) && !arguments[0].Contains('.', StringComparison.Ordinal) ? ".NETFramework"
                : arguments[0].StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ? ".NETStandard" : ".NETCoreApp",
            "GetFullPath" when path && arguments.Count == 1 => Path.GetFullPath(arguments[0], Folder),
            "Combine" when path && arguments.Count >= 1 => Path.Combine([.. arguments]),
            _ => null,
        };
    }

    /// <summary>The platform of a target framework ("windows" of "net10.0-windows10.0.19041.0"), or "".</summary>
    public static string PlatformOf(string targetFramework)
    {
        int dash = targetFramework.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return "";
        }

        var platform = targetFramework[(dash + 1)..];
        int digit = 0;
        while (digit < platform.Length && !char.IsAsciiDigit(platform[digit]))
        {
            digit++;
        }

        return platform[..digit];
    }

    /// <summary>A condition's value: comparisons of quoted strings, Exists, true and false, with and, or, ! and parentheses; null for a shape it does not know.</summary>
    private ref struct ConditionParser(string text, string folder)
    {
        private int _at;

        public bool? Parse()
        {
            var value = Or();
            Skip();
            return _at == text.Length ? value : null;
        }

        private bool? Or()
        {
            var left = And();
            while (Word("or"))
            {
                var right = And();
                left = left is null || right is null ? null : left.Value || right.Value;
            }

            return left;
        }

        private bool? And()
        {
            var left = Unary();
            while (Word("and"))
            {
                var right = Unary();
                left = left is null || right is null ? null : left.Value && right.Value;
            }

            return left;
        }

        private bool? Unary()
        {
            Skip();
            if (_at < text.Length && text[_at] == '!' && (_at + 1 >= text.Length || text[_at + 1] != '='))
            {
                _at++;
                return Unary() is { } inner ? !inner : null;
            }

            if (_at < text.Length && text[_at] == '(')
            {
                _at++;
                var inner = Or();
                Skip();
                if (_at >= text.Length || text[_at] != ')')
                {
                    return null;
                }

                _at++;
                return inner;
            }

            if (Word("Exists"))
            {
                Skip();
                if (_at >= text.Length || text[_at] != '(')
                {
                    return null;
                }

                _at++;
                var path = Quoted();
                Skip();
                if (path is null || _at >= text.Length || text[_at] != ')')
                {
                    return null;
                }

                _at++;
                path = path.Trim();
                if (path.Length == 0)
                {
                    return false;
                }

                var full = Path.IsPathRooted(path) ? path : Path.Combine(folder, path);
                return File.Exists(full) || Directory.Exists(full);
            }

            if (Word("true"))
            {
                return true;
            }

            if (Word("false"))
            {
                return false;
            }

            var left = Quoted();
            if (left is null)
            {
                return null;
            }

            Skip();
            bool equal;
            if (Peek("=="))
            {
                equal = true;
            }
            else if (Peek("!="))
            {
                equal = false;
            }
            else
            {
                return left.Equals("true", StringComparison.OrdinalIgnoreCase) ? true : left.Equals("false", StringComparison.OrdinalIgnoreCase) ? false : null;
            }

            _at += 2;
            var right = Quoted();
            if (right is null)
            {
                return null;
            }

            bool same = left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);
            return equal ? same : !same;
        }

        private string? Quoted()
        {
            Skip();
            if (_at < text.Length && text[_at] != '\'')
            {
                // An unquoted value (a property that expanded to a bare word, or to nothing before an operator).
                int from = _at;
                while (_at < text.Length && !char.IsWhiteSpace(text[_at]) && text[_at] is not ('=' or '!' or '(' or ')' or '\''))
                {
                    _at++;
                }

                return text[from.._at];
            }

            if (_at >= text.Length)
            {
                return Peek("") ? "" : null;
            }

            int end = text.IndexOf('\'', _at + 1);
            if (end < 0)
            {
                return null;
            }

            var value = text[(_at + 1)..end];
            _at = end + 1;
            return value;
        }

        private bool Peek(string token) => string.CompareOrdinal(text, _at, token, 0, token.Length) == 0;

        private bool Word(string word)
        {
            Skip();
            if (_at + word.Length <= text.Length && string.Compare(text, _at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (_at + word.Length == text.Length || !char.IsLetterOrDigit(text[_at + word.Length])))
            {
                _at += word.Length;
                return true;
            }

            return false;
        }

        private void Skip()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at]))
            {
                _at++;
            }
        }
    }
}
