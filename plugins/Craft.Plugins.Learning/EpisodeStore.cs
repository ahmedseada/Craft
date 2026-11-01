// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Craft.Plugins.Learning;

/// <summary>
/// One recorded episode of the editor: an AI answer, the context it was made from, and what the user did with it (the
/// verdict and, when the user wrote or kept other text, that text: Idrak's <c>PreferenceInfoKeys</c> verdict and kept).
/// </summary>
/// <param name="Id">The answer's id.</param>
/// <param name="Time">When the model answered.</param>
/// <param name="Kind">What answered (Craft's <c>AiAnswerKinds</c>: completion, chat, code-action, agent).</param>
/// <param name="Prompt">The context sent to the model.</param>
/// <param name="Answer">The model's answer.</param>
/// <param name="Verdict">What the user did (accepted, edited, rejected, undone).</param>
public sealed record EditorEpisode(string Id, DateTimeOffset Time, string Kind, string Prompt, string Answer, string Verdict)
{
    /// <summary>The plug-in that answered.</summary>
    public string Plugin { get; init; } = "";

    /// <summary>The model, as the plug-in named it, or null.</summary>
    public string? Model { get; init; }

    /// <summary>The file, relative to the workspace ('/' separated), or null.</summary>
    public string? File { get; init; }

    /// <summary>The document's language, or "".</summary>
    public string Language { get; init; } = "";

    /// <summary>The text the user kept in the answer's place (what was written, edited or left instead), or null.</summary>
    public string? Kept { get; init; }

    /// <summary>How far an accepted answer was edited: 0 (kept as it was) to 1 (nothing of it left).</summary>
    public double Edited { get; init; }

    /// <summary>The prompt's ids as the model saw them, or null.</summary>
    public IReadOnlyList<int>? PromptIds { get; init; }

    /// <summary>The answer's ids as the model sampled them, or null.</summary>
    public IReadOnlyList<int>? AnswerIds { get; init; }

    /// <summary>The episode as one JSON object (UTF-8, no line break): a line of the store.</summary>
    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", Id);
            writer.WriteString("time", Time.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("kind", Kind);
            writer.WriteString("plugin", Plugin);
            if (Model is not null)
            {
                writer.WriteString("model", Model);
            }

            if (File is not null)
            {
                writer.WriteString("file", File);
            }

            writer.WriteString("language", Language);
            writer.WriteString("prompt", Prompt);
            writer.WriteString("answer", Answer);
            writer.WriteString("verdict", Verdict);
            if (Kept is not null)
            {
                writer.WriteString("kept", Kept);
            }

            writer.WriteNumber("edited", Edited);
            WriteIds(writer, "promptIds", PromptIds);
            WriteIds(writer, "answerIds", AnswerIds);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The episode of one store line, or null when it does not read.</summary>
    public static EditorEpisode? FromJson(ReadOnlySpan<byte> line)
    {
        try
        {
            var reader = new Utf8JsonReader(line);
            using var json = JsonDocument.ParseValue(ref reader);
            var root = json.RootElement;
            string? id = Text(root, "id"), time = Text(root, "time"), kind = Text(root, "kind"), prompt = Text(root, "prompt"), answer = Text(root, "answer"), verdict = Text(root, "verdict");
            if (id is null || time is null || kind is null || prompt is null || answer is null || verdict is null
                || !DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
            {
                return null;
            }

            return new EditorEpisode(id, when, kind, prompt, answer, verdict)
            {
                Plugin = Text(root, "plugin") ?? "",
                Model = Text(root, "model"),
                File = Text(root, "file"),
                Language = Text(root, "language") ?? "",
                Kept = Text(root, "kept"),
                Edited = root.TryGetProperty("edited", out var edited) && edited.ValueKind == JsonValueKind.Number ? edited.GetDouble() : 0,
                PromptIds = Ids(root, "promptIds"),
                AnswerIds = Ids(root, "answerIds"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteIds(Utf8JsonWriter writer, string name, IReadOnlyList<int>? ids)
    {
        if (ids is null)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (int id in ids)
        {
            writer.WriteNumberValue(id);
        }

        writer.WriteEndArray();
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int[]? Ids(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var ids = new int[value.GetArrayLength()];
        int i = 0;
        foreach (var item in value.EnumerateArray())
        {
            ids[i++] = item.GetInt32();
        }

        return ids;
    }
}

/// <summary>
/// One workspace's episodes on disk: <c>episodes.jsonl</c> (one JSON object a line, appended once per settled answer) in
/// the workspace's folder under the learning folder, at most <see cref="LearningSettings.CapMegabytes"/> (the oldest go
/// first when an append passes it) and <see cref="LearningSettings.KeepDays"/> days. Listed, exported and deleted from
/// the Learning panel. Used on one thread at a time.
/// </summary>
public sealed class EpisodeStore
{
    private readonly long _capBytes;
    private readonly TimeSpan _keep;

    /// <summary>The store of <paramref name="workspace"/> (null: files opened without a folder) under <paramref name="root"/>.</summary>
    public EpisodeStore(string root, string? workspace, LearningSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(settings);
        Workspace = workspace;
        Folder = WorkspaceFolder(root, workspace);
        FilePath = Path.Combine(Folder, "episodes.jsonl");
        _capBytes = (long)settings.CapMegabytes * 1024 * 1024;
        _keep = TimeSpan.FromDays(settings.KeepDays);
    }

    /// <summary>The workspace, or null.</summary>
    public string? Workspace { get; }

    /// <summary>The workspace's folder under the learning folder (its episodes, tasks and adapters).</summary>
    public string Folder { get; }

    /// <summary>The episodes' file.</summary>
    public string FilePath { get; }

    /// <summary>Its size on disk in bytes (0 when there is none).</summary>
    public long Bytes => System.IO.File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;

    /// <summary>The folder of <paramref name="workspace"/>'s data under <paramref name="root"/>: "workspaces/" and a short hash of its full path ("no-folder" for none).</summary>
    public static string WorkspaceFolder(string root, string? workspace)
    {
        if (workspace is null)
        {
            return Path.Combine(root, "workspaces", "no-folder");
        }

        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        if (OperatingSystem.IsWindows())
        {
            full = full.ToUpperInvariant();                          // one folder whatever the path's case on a case-blind file system
        }

        string name = Path.GetFileName(full);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0, 6);
        return Path.Combine(root, "workspaces", Safe(name) + "-" + hash);
    }

    /// <summary>Appends <paramref name="episode"/>; past the cap, the oldest episodes go (and any past the days kept).</summary>
    public void Append(EditorEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        Directory.CreateDirectory(Folder);
        if (Workspace is not null && !System.IO.File.Exists(Path.Combine(Folder, "workspace.txt")))
        {
            System.IO.File.WriteAllText(Path.Combine(Folder, "workspace.txt"), Workspace);
        }

        byte[] line = episode.ToJson();
        using (var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            stream.Write(line);
            stream.WriteByte((byte)'\n');
        }

        if (Bytes > _capBytes)
        {
            Trim(_capBytes - _capBytes / 10);
        }
    }

    /// <summary>The episodes kept (oldest first), those past the days kept left out.</summary>
    public List<EditorEpisode> All()
    {
        var episodes = new List<EditorEpisode>();
        if (!System.IO.File.Exists(FilePath))
        {
            return episodes;
        }

        var cutoff = DateTimeOffset.UtcNow - _keep;
        byte[] bytes = System.IO.File.ReadAllBytes(FilePath);
        var rest = bytes.AsSpan();
        while (!rest.IsEmpty)
        {
            int end = rest.IndexOf((byte)'\n');
            var line = end < 0 ? rest : rest[..end];
            rest = end < 0 ? default : rest[(end + 1)..];
            if (line.IsEmpty)
            {
                continue;
            }

            if (EditorEpisode.FromJson(line) is { } episode && episode.Time >= cutoff)
            {
                episodes.Add(episode);
            }
        }

        return episodes;
    }

    /// <summary>Deletes the episode <paramref name="id"/>; the bytes freed (0 when there was none).</summary>
    public long Delete(string id)
    {
        long before = Bytes;
        var kept = All().Where(e => e.Id != id).ToList();
        Write(kept);
        return before - Bytes;
    }

    /// <summary>Deletes every episode; the bytes freed.</summary>
    public long DeleteAll()
    {
        long before = Bytes;
        if (System.IO.File.Exists(FilePath))
        {
            System.IO.File.Delete(FilePath);
        }

        return before;
    }

    /// <summary>Where <see cref="Hold"/> keeps the episodes while their delete can still be undone.</summary>
    public string HeldPath => FilePath + ".held";

    /// <summary>
    /// Deletes every episode so that it can be undone (B11: Delete with Undo): the file moves aside to <see cref="HeldPath"/>
    /// (one held at a time: an older held file goes for good first); the episodes and bytes taken.
    /// </summary>
    public (int Episodes, long Bytes) Hold()
    {
        DropHeld();
        if (!System.IO.File.Exists(FilePath))
        {
            return (0, 0);
        }

        int count = All().Count;
        long bytes = Bytes;
        System.IO.File.Move(FilePath, HeldPath);
        return (count, bytes);
    }

    /// <summary>Takes a <see cref="Hold"/> back: the held episodes return, before any recorded since; true when there were some.</summary>
    public bool Restore()
    {
        if (!System.IO.File.Exists(HeldPath))
        {
            return false;
        }

        if (System.IO.File.Exists(FilePath))
        {
            byte[] since = System.IO.File.ReadAllBytes(FilePath);
            using (var stream = new FileStream(HeldPath, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                stream.Write(since);
            }

            System.IO.File.Delete(FilePath);
        }

        System.IO.File.Move(HeldPath, FilePath);
        return true;
    }

    /// <summary>Makes a <see cref="Hold"/> final: the held episodes are deleted; the bytes freed.</summary>
    public long DropHeld()
    {
        if (!System.IO.File.Exists(HeldPath))
        {
            return 0;
        }

        long bytes = new FileInfo(HeldPath).Length;
        System.IO.File.Delete(HeldPath);
        return bytes;
    }

    /// <summary>Copies the episodes to <paramref name="path"/> (JSON lines); the path.</summary>
    public string Export(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        Write(All(), path);
        return Path.GetFullPath(path);
    }

    // Keeps the newest episodes that fit in `bytes`.
    private void Trim(long bytes)
    {
        var all = All();
        long total = 0;
        int first = all.Count;
        while (first > 0)
        {
            long size = all[first - 1].ToJson().Length + 1;
            if (total + size > bytes)
            {
                break;
            }

            total += size;
            first--;
        }

        Write(all.GetRange(first, all.Count - first));
    }

    private void Write(List<EditorEpisode> episodes, string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
        {
            foreach (var episode in episodes)
            {
                stream.Write(episode.ToJson());
                stream.WriteByte((byte)'\n');
            }
        }

        System.IO.File.Move(temporary, path, overwrite: true);
    }

    private static string Safe(string name)
    {
        var text = new StringBuilder(Math.Min(name.Length, 40));
        foreach (char c in name)
        {
            if (text.Length == 40)
            {
                break;
            }

            text.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return text.Length == 0 ? "folder" : text.ToString();
    }
}
