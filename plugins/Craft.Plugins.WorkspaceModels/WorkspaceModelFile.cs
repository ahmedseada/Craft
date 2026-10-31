// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Text.Json;
using Craft.Abstractions;

namespace Craft.Plugins.WorkspaceModels;

/// <summary>
/// A workspace's <c>.craft/model.json</c>, read: the names it defines and the model of each role.
/// <code>
/// {
///   "models": { "coder": "store:qwen2.5-coder:7b", "tiny": "models/tiny", "vision": { "model": "Qwen/Qwen2.5-VL-3B-Instruct" } },
///   "roles": { "chat": "coder", "completion": "tiny", "agent": "coder" }
/// }
/// </code>
/// A name stands for anything Idrak's model sources resolve (a folder or a .gguf file, relative to the workspace or full,
/// store:NAME, a Hugging Face id, another craft:NAME); a role names one of the file's names or any model name. Every key
/// is checked: an unknown field, a role that is not chat, completion or agent, or a value that is not text is refused with
/// the file's name and where.
/// </summary>
public sealed class WorkspaceModelFile
{
    /// <summary>The file's place in a workspace.</summary>
    public static readonly string DefaultPath = System.IO.Path.Combine(".craft", "model.json");

    private static readonly ConcurrentDictionary<string, (WorkspaceModelFile? File, DateTime Stamp, long Length)> Cache = new(StringComparer.Ordinal);

    private WorkspaceModelFile(string path, IReadOnlyDictionary<string, string> models, IReadOnlyDictionary<string, string> roles)
    {
        Path = path;
        Models = models;
        Roles = roles;
    }

    /// <summary>The file's full path.</summary>
    public string Path { get; }

    /// <summary>The names it defines ("coder") and what each stands for, in the file's order.</summary>
    public IReadOnlyDictionary<string, string> Models { get; }

    /// <summary>Each role's model as the file gives it ("coder", or a model name), by role.</summary>
    public IReadOnlyDictionary<string, string> Roles { get; }

    /// <summary>The workspace folder (the file's folder's parent).</summary>
    public string Workspace => System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(Path)) ?? "";

    /// <summary>
    /// The file of <paramref name="workspaceFolder"/> at <paramref name="relativePath"/>, or null when it has none: read once
    /// and kept while its time and length stay the same (so asking again costs a look at the file's times, not a parse).
    /// </summary>
    /// <exception cref="InvalidDataException">The file does not read (the message names it and where).</exception>
    public static WorkspaceModelFile? For(string workspaceFolder, string? relativePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceFolder);
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(workspaceFolder, relativePath is { Length: > 0 } r ? r : DefaultPath));
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            Cache.TryRemove(path, out _);
            return null;
        }

        if (Cache.TryGetValue(path, out var kept) && kept.Stamp == info.LastWriteTimeUtc && kept.Length == info.Length)
        {
            return kept.File;
        }

        var file = Parse(path, File.ReadAllBytes(path));
        Cache[path] = (file, info.LastWriteTimeUtc, info.Length);
        return file;
    }

    /// <summary>Reads a model file's bytes (its path for the messages).</summary>
    /// <exception cref="InvalidDataException">The JSON does not read or holds a field it should not.</exception>
    public static WorkspaceModelFile Parse(string path, ReadOnlySpan<byte> json)
    {
        ArgumentNullException.ThrowIfNull(path);
        var models = new Dictionary<string, string>(StringComparer.Ordinal);
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            Expect(reader.Read() && reader.TokenType == JsonTokenType.StartObject, path, "the file is not a JSON object");
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string field = reader.GetString()!;
                reader.Read();
                switch (field)
                {
                    case "models":
                        ReadModels(ref reader, path, models);
                        break;
                    case "roles":
                        ReadRoles(ref reader, path, roles);
                        break;
                    default:
                        throw new InvalidDataException($"{path}: the field '{field}' is not one a model file has (it has \"models\" and \"roles\").");
                }
            }
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"{path}: not JSON ({error.Message}).", error);
        }

        return new WorkspaceModelFile(path, models, roles);
    }

    /// <summary>What <paramref name="name"/> (a role's value, or a name of the file) stands for once the file's names are followed.</summary>
    public string Target(string name) => Models.TryGetValue(name, out var target) ? target : name;

    /// <summary>The names as the Models page lists them, each with the roles it is the default of.</summary>
    public IReadOnlyList<NamedModel> Named()
    {
        var list = new List<NamedModel>(Models.Count + Roles.Count);
        foreach (var (name, target) in Models)
        {
            list.Add(new NamedModel(WorkspaceModelSource.Prefix + name, target, [.. Roles.Where(r => r.Value == name || r.Value == WorkspaceModelSource.Prefix + name).Select(r => r.Key)]));
        }

        foreach (var (role, model) in Roles)
        {
            if (!Models.ContainsKey(model) && !(model.StartsWith(WorkspaceModelSource.Prefix, StringComparison.Ordinal) && Models.ContainsKey(model[WorkspaceModelSource.Prefix.Length..])))
            {
                list.Add(new NamedModel(model, model, [role]));
            }
        }

        return list;
    }

    private static void ReadModels(ref Utf8JsonReader reader, string path, Dictionary<string, string> models)
    {
        Expect(reader.TokenType == JsonTokenType.StartObject, path, "\"models\" is not an object of names");
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            Expect(name.Length > 0 && !name.Contains(':', StringComparison.Ordinal), path, $"the model name '{name}' is empty or holds a ':'");
            reader.Read();
            string? target = null;
            if (reader.TokenType == JsonTokenType.String)
            {
                target = reader.GetString();
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    string field = reader.GetString()!;
                    reader.Read();
                    Expect(field == "model" && reader.TokenType == JsonTokenType.String, path, $"the model '{name}' has the field '{field}' (it takes \"model\": a name)");
                    target = reader.GetString();
                }
            }

            Expect(target is { Length: > 0 }, path, $"the model '{name}' names nothing (give a folder, a .gguf file, store:NAME or a Hugging Face id)");
            Expect(models.TryAdd(name, target!.Trim()), path, $"the model '{name}' is named twice");
        }
    }

    private static void ReadRoles(ref Utf8JsonReader reader, string path, Dictionary<string, string> roles)
    {
        Expect(reader.TokenType == JsonTokenType.StartObject, path, "\"roles\" is not an object of roles");
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string role = reader.GetString()!;
            Expect(ModelRoles.All.Contains(role, StringComparer.Ordinal), path, $"'{role}' is not a role (the roles: {string.Join(", ", ModelRoles.All)})");
            reader.Read();
            Expect(reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: > 0 }, path, $"the role '{role}' names no model");
            roles[role] = reader.GetString()!.Trim();
        }
    }

    private static void Expect(bool condition, string path, string problem)
    {
        if (!condition)
        {
            throw new InvalidDataException($"{path}: {problem}.");
        }
    }
}
