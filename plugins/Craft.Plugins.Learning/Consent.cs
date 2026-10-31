// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Craft.Abstractions;

namespace Craft.Plugins.Learning;

/// <summary>The two consents (plan 1, phase R, "Consent"): learning on this machine, and sharing what was learned.</summary>
public static class ConsentKinds
{
    /// <summary>Learn on this machine.</summary>
    public const string Learn = "learn";

    /// <summary>Share what was learned.</summary>
    public const string Share = "share";
}

/// <summary>What the user answered for one consent and one scope.</summary>
/// <param name="Kind">The consent (<see cref="ConsentKinds"/>).</param>
/// <param name="Scope">"all" (every workspace) or a workspace's full path.</param>
/// <param name="Granted">Yes or no.</param>
/// <param name="Time">When.</param>
/// <param name="Version">The version of the text the user answered (<see cref="ConsentText.Version"/>): a changed text asks again.</param>
public sealed record ConsentRecord(string Kind, string Scope, bool Granted, DateTimeOffset Time, string Version);

/// <summary>Where a consent stands for a workspace.</summary>
public enum ConsentState
{
    /// <summary>Never answered: ask.</summary>
    Undecided,

    /// <summary>Yes, to the text as it reads now.</summary>
    Granted,

    /// <summary>No (or withdrawn).</summary>
    Refused,

    /// <summary>Yes to an older text (what is recorded, shared or where changed): ask again; until then, no.</summary>
    Changed,
}

/// <summary>A consent's words as the user reads them, and their version (a hash of the words: any change asks again).</summary>
/// <param name="Kind">The consent (<see cref="ConsentKinds"/>).</param>
/// <param name="Title">The question's title.</param>
/// <param name="Message">What it is, in plain words.</param>
/// <param name="Details">The facts, one a line.</param>
public sealed record ConsentText(string Kind, string Title, string Message, IReadOnlyList<string> Details)
{
    /// <summary>The text's version: the kind, the text's form (1) and a hash of every word shown.</summary>
    public string Version
    {
        get
        {
            var words = Encoding.UTF8.GetBytes(Title + "\n" + Message + "\n" + string.Join("\n", Details));
            return string.Create(CultureInfo.InvariantCulture, $"{Kind}-1-{Convert.ToHexStringLower(SHA256.HashData(words), 0, 6)}");
        }
    }

    /// <summary>
    /// The first consent's words: what is recorded, where (the folder, openable), how much, how long, what training runs and
    /// when, that a learned model only proposes, and how to withdraw.
    /// </summary>
    public static ConsentText Learn(LearningSettings settings, string folder, string? model)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string when = settings.Schedule == "idle"
            ? string.Create(CultureInfo.InvariantCulture, $"only while you are away (after {settings.IdleMinutes} minutes without typing, once {settings.MinEpisodes} new episodes are kept) or when you run it")
            : "only when you run it (Learning: train now)";
        string limits = string.Create(CultureInfo.InvariantCulture,
            $"{(settings.Threads > 0 ? $"{settings.Threads} CPU threads" : "all CPU threads")}, {(settings.CpuMemoryMegabytes > 0 ? $"{settings.CpuMemoryMegabytes} MiB of memory" : "no memory cap")}, {(settings.GpuMemoryMegabytes > 0 ? $"{settings.GpuMemoryMegabytes} MiB of device memory" : "no device memory cap")}");
        return new ConsentText(ConsentKinds.Learn, "Learn on this machine?",
            "Craft can learn from how you use the AI: your local model is trained, as an adapter beside its weights, on what you accept and what you reject. "
            + "Nothing is recorded unless you say yes here, and nothing leaves this machine.",
            [
                $"What is recorded: for each AI answer ({string.Join(", ", settings.Record)}), the context sent to the model, its answer, and what you did with it (accepted, edited and how far, rejected, undone).",
                $"Where: {folder} (the folder is shown when you pick Open the folder).",
                string.Create(CultureInfo.InvariantCulture, $"How much: at most {settings.CapMegabytes} MiB per workspace; the oldest episodes go first."),
                string.Create(CultureInfo.InvariantCulture, $"How long: {settings.KeepDays} days, or until you delete them on the Learning panel."),
                $"What training runs, and when: an adapter for {(model is { Length: > 0 } ? model : "your local model")}, {when}; limits: {limits}; it shows its progress and stops when you say.",
                "A learned model still only proposes: every change it makes is a diff you accept.",
                "Withdraw at any time with one switch (Learning: stop learning, or the setting consent=off): recording stops at once, and you choose whether the stored data is deleted.",
            ]);
    }

    /// <summary>
    /// The second consent's words: exactly what would leave the machine, to where, the preview before each share, what is
    /// always left out, and how to withdraw.
    /// </summary>
    public static ConsentText Share(LearningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var what = settings.ShareWhat.Select(kind => kind switch
        {
            ShareKinds.Episodes => "the recorded episodes (the context sent to the model, its answer, what you did with it)",
            ShareKinds.Adapter => "the trained adapter's files (the learned weights, no code)",
            _ => "only the scores (episode counts, rewards and held-out margins; no code, no text)",
        });
        return new ConsentText(ConsentKinds.Share, "Share what was learned?",
            $"A separate choice from learning: this sends data off this machine, to {settings.ShareTo}. Saying yes to learning did not decide it.",
            [
                $"What would leave: {string.Join("; ", what)}.",
                $"To where: {settings.ShareTo} (the setting shareTo; nothing is sent anywhere else).",
                "Before each share you see every item about to go (the count, the size, a preview of each, the files and paths it touches) and confirm it.",
                "Always left out: files matched by .gitignore and .craft/learn-ignore, and anything the secrets scanner finds; the preview lists what was left out.",
                "Withdraw at any time (Learning: stop sharing, or the setting sharing=off): nothing more is shared.",
            ]);
    }
}

/// <summary>
/// The user's consents, stored with their time, their scope and the version of the text answered, in
/// <c>consent.json</c> under the learning folder. A setting can only turn a consent off (<see cref="LearningSettings.ConsentOff"/>);
/// a yes is only ever the user's answer, recorded here. Used on the UI thread.
/// </summary>
public sealed class ConsentStore
{
    /// <summary>The scope of every workspace.</summary>
    public const string AllWorkspaces = "all";

    private readonly string _path;
    private readonly List<ConsentRecord> _records = [];

    /// <summary>The consents kept in <paramref name="folder"/>'s consent.json (none when there is no file yet).</summary>
    public ConsentStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _path = Path.Combine(folder, "consent.json");
        Load();
    }

    /// <summary>The file.</summary>
    public string FilePath => _path;

    /// <summary>Every record, as stored.</summary>
    public IReadOnlyList<ConsentRecord> Records => _records;

    /// <summary>The record that decides <paramref name="kind"/> for <paramref name="workspace"/>: the workspace's own, else every workspace's; null for none.</summary>
    public ConsentRecord? Find(string kind, string? workspace)
    {
        ConsentRecord? all = null;
        foreach (var record in _records)
        {
            if (record.Kind != kind)
            {
                continue;
            }

            if (workspace is not null && SameScope(record.Scope, workspace))
            {
                return record;
            }

            if (record.Scope == AllWorkspaces)
            {
                all = record;
            }
        }

        return all;
    }

    /// <summary>Where <paramref name="kind"/> stands for <paramref name="workspace"/> against the text <paramref name="text"/> as it reads now.</summary>
    public ConsentState State(string kind, string? workspace, ConsentText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Find(kind, workspace) switch
        {
            null => ConsentState.Undecided,
            { Granted: false } => ConsentState.Refused,
            { Version: var version } when version == text.Version => ConsentState.Granted,
            _ => ConsentState.Changed,
        };
    }

    /// <summary>Records the user's answer (replacing the one of the same consent and scope) and saves.</summary>
    public ConsentRecord Set(string kind, string scope, bool granted, ConsentText text, DateTimeOffset? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(text);
        _records.RemoveAll(r => r.Kind == kind && SameScope(r.Scope, scope));
        var record = new ConsentRecord(kind, scope, granted, time ?? DateTimeOffset.UtcNow, text.Version);
        _records.Add(record);
        Save();
        return record;
    }

    /// <summary>Withdraws <paramref name="kind"/> everywhere (every scope's answer becomes no) and saves.</summary>
    public void Withdraw(string kind, ConsentText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _records.RemoveAll(r => r.Kind == kind);
        _records.Add(new ConsentRecord(kind, AllWorkspaces, false, DateTimeOffset.UtcNow, text.Version));
        Save();
    }

    private static bool SameScope(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(_path));
            if (!json.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in records.EnumerateArray())
            {
                string? kind = Text(item, "kind"), scope = Text(item, "scope"), version = Text(item, "version"), time = Text(item, "time");
                if (kind is null || scope is null || version is null || time is null || !item.TryGetProperty("granted", out var granted)
                    || granted.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || !DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                {
                    continue;                                          // a record that does not read is no consent
                }

                _records.Add(new ConsentRecord(kind, scope, granted.GetBoolean(), when, version));
            }
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable file holds no consent: the question is asked again.
        }
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("records");
            foreach (var record in _records)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", record.Kind);
                writer.WriteString("scope", record.Scope);
                writer.WriteBoolean("granted", record.Granted);
                writer.WriteString("time", record.Time.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteString("version", record.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        string temporary = _path + ".tmp";
        File.WriteAllBytes(temporary, stream.ToArray());
        File.Move(temporary, _path, overwrite: true);
    }
}
