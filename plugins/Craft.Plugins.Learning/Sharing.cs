// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.IO.Enumeration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Craft.Plugins.Learning.Abstractions;

namespace Craft.Plugins.Learning.Abstractions
{
    /// <summary>One item a share sends, exactly as the preview showed it.</summary>
    /// <param name="Name">Its name at the destination ("episodes/a12.json", "adapter/adapter_model.safetensors", "scores.json").</param>
    /// <param name="Kind">What it is (<see cref="ShareKinds"/>).</param>
    /// <param name="Content">The bytes sent.</param>
    /// <param name="Preview">A preview of it, a few lines.</param>
    /// <param name="Paths">The workspace's files and paths it touches (an episode's file), or none.</param>
    public sealed record ShareItem(string Name, string Kind, ReadOnlyMemory<byte> Content, string Preview, IReadOnlyList<string> Paths)
    {
        /// <summary>Its size in bytes.</summary>
        public long Size => Content.Length;
    }

    /// <summary>
    /// Where a share goes (the learning plug-in's own contract): a kind registered in <see cref="ShareDestinations"/> under
    /// its name ("folder", "https"); the setting <c>shareTo</c> names it as "kind:target" (an https address is its own kind).
    /// </summary>
    public interface IShareDestination : IKind
    {
        /// <summary>Sends <paramref name="items"/> to <paramref name="target"/>, every one and nothing else.</summary>
        Task SendAsync(string target, IReadOnlyList<ShareItem> items, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The share destinations, by name (ignoring case): "folder" (each item written under the folder, in a sub-folder per
    /// share) and "https" (each item POSTed to the address) are the plug-in's; another plug-in adds its own. A name nobody
    /// registered fails "not registered". There is no default destination: nothing is shared until the user names one.
    /// </summary>
    public static class ShareDestinations
    {
        /// <summary>The folder destination.</summary>
        public const string Folder = "folder";

        /// <summary>The HTTPS destination.</summary>
        public const string Https = "https";

        private static readonly KindTable<IShareDestination> Table = new(nameof(ShareDestinations), "Share destination", "add it with ShareDestinations.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a destination is where the user said data may go; none stands in for another");

        /// <summary>Registers <paramref name="destination"/> under its name.</summary>
        public static void Register(IShareDestination destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            Table.Register(destination, System.Reflection.Assembly.GetCallingAssembly());
        }

        /// <summary>Removes the destination of that name; false when none was registered.</summary>
        public static bool Unregister(string name) => Table.Unregister(name);

        /// <summary>The registered names, in order.</summary>
        public static IReadOnlyList<string> Names => Table.Names;

        /// <summary>The destination of that name, or null.</summary>
        public static IShareDestination? Find(ReadOnlySpan<char> name) => Table.Find(name);

        /// <summary>The destination of that name.</summary>
        /// <exception cref="NotRegisteredException">None is registered under it.</exception>
        public static IShareDestination Get(ReadOnlySpan<char> name) => Table.Get(name);

        /// <summary>The kind and the target of the setting <paramref name="shareTo"/>: "kind:target", or an https address (kind "https").</summary>
        /// <exception cref="ArgumentException">No kind.</exception>
        public static (string Kind, string Target) Parse(string shareTo)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(shareTo);
            string text = shareTo.Trim();
            if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return (Https, text);
            }

            int colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 1)                                           // "D:\x" is a drive, not a kind
            {
                throw new ArgumentException($"The destination '{text}' names no kind: write kind:target (folder:PATH, or an https:// address).", nameof(shareTo));
            }

            return (text[..colon], text[(colon + 1)..]);
        }
    }
}

namespace Craft.Plugins.Learning
{
    /// <summary>What a share may send (the setting <c>shareWhat</c>).</summary>
    public static class ShareKinds
    {
        /// <summary>The recorded episodes.</summary>
        public const string Episodes = "episodes";

        /// <summary>The current adapter's files.</summary>
        public const string Adapter = "adapter";

        /// <summary>Only the scores (counts, rewards, held-out margins).</summary>
        public const string Scores = "scores";

        /// <summary>Every kind, in order.</summary>
        public static IReadOnlyList<string> All { get; } = [Episodes, Adapter, Scores];
    }

    /// <summary>A share before it is sent: exactly the items to go, and what was left out and why.</summary>
    /// <param name="Destination">The destination as the setting names it.</param>
    /// <param name="Items">What goes.</param>
    /// <param name="LeftOut">What was left out, one line each ("episode a7 (src/Secrets.cs): matched .gitignore").</param>
    public sealed record SharePlan(string Destination, IReadOnlyList<ShareItem> Items, IReadOnlyList<string> LeftOut)
    {
        /// <summary>The bytes that go.</summary>
        public long Size => Items.Sum(i => i.Size);

        /// <summary>The preview's lines: a summary, each item (name, size, the paths it touches, a preview), and what was left out.</summary>
        public IReadOnlyList<string> PreviewLines()
        {
            var lines = new List<string>
            {
                string.Create(CultureInfo.InvariantCulture, $"{Items.Count} item(s), {Bytes(Size)}, to {Destination}"),
            };
            foreach (var item in Items)
            {
                string paths = item.Paths.Count == 0 ? "" : " · touches " + string.Join(", ", item.Paths);
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{item.Name} ({Bytes(item.Size)}){paths}: {item.Preview}"));
            }

            lines.Add(LeftOut.Count == 0 ? "Left out: nothing matched .gitignore, .craft/learn-ignore or the secrets scanner." : $"Left out ({LeftOut.Count}):");
            lines.AddRange(LeftOut);
            return lines;
        }

        /// <summary>A size in bytes as people read it.</summary>
        public static string Bytes(long bytes) => bytes < 1024 ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
            : bytes < 1024 * 1024 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F1} KB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):F1} MB");
    }

    /// <summary>
    /// What a share leaves out: files matched by the workspace's <c>.gitignore</c> and <c>.craft/learn-ignore</c> (git's
    /// pattern rules: <c>#</c> comments, <c>!</c> negation, a leading <c>/</c> anchors, a trailing <c>/</c> is a folder,
    /// <c>*</c>, <c>?</c>, <c>**</c>), and text the secrets scanner finds (private keys, cloud and service tokens, passwords
    /// and keys assigned in code, plus the setting <c>secretPatterns</c>).
    /// </summary>
    public sealed class ShareFilter
    {
        private static readonly Regex[] BuiltInSecrets =
        [
            new(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant),
            new(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.CultureInvariant),
            new(@"\bgh[pousr]_[A-Za-z0-9]{30,}\b", RegexOptions.CultureInvariant),
            new(@"\bxox[abpr]-[A-Za-z0-9-]{10,}\b", RegexOptions.CultureInvariant),
            new(@"\bsk-[A-Za-z0-9_-]{20,}\b", RegexOptions.CultureInvariant),
            new(@"\bAIza[0-9A-Za-z_-]{35}\b", RegexOptions.CultureInvariant),
            new(@"(?i)\b(password|passwd|pwd|secret|api[_-]?key|access[_-]?token|client[_-]?secret|connectionstring)\b\s*[:=]\s*[""'][^""'\s]{6,}[""']", RegexOptions.CultureInvariant),
        ];

        private readonly List<(string Pattern, bool Negated, bool Folder, bool Anchored, string Source)> _rules = [];
        private readonly Regex[] _secrets;

        /// <summary>The filter of <paramref name="workspace"/> (its ignore files, when it has a folder) with <paramref name="extraSecrets"/>.</summary>
        public ShareFilter(string? workspace, IReadOnlyList<string> extraSecrets)
        {
            ArgumentNullException.ThrowIfNull(extraSecrets);
            _secrets = [.. BuiltInSecrets, .. extraSecrets.Select(p => new Regex(p, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))];
            if (workspace is not null)
            {
                Read(Path.Combine(workspace, ".gitignore"), ".gitignore");
                Read(Path.Combine(workspace, ".craft", "learn-ignore"), ".craft/learn-ignore");
            }
        }

        /// <summary>The ignore file whose rule leaves <paramref name="relative"/> out ('/' separated, relative to the workspace), or null when none does.</summary>
        public string? IgnoredBy(string relative)
        {
            ArgumentNullException.ThrowIfNull(relative);
            string path = relative.Replace('\\', '/').TrimStart('/');
            string? by = null;
            foreach (var rule in _rules)
            {
                if (Matches(rule.Pattern, rule.Folder, rule.Anchored, path))
                {
                    by = rule.Negated ? null : rule.Source;          // the last matching rule decides, as in git
                }
            }

            return by;
        }

        /// <summary>What the secrets scanner found in <paramref name="text"/> (the pattern's kind), or null.</summary>
        public string? Secret(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            foreach (var pattern in _secrets)
            {
                if (pattern.IsMatch(text))
                {
                    return "the secrets scanner found something that looks like a key or a password";
                }
            }

            return null;
        }

        private void Read(string file, string source)
        {
            if (!File.Exists(file))
            {
                return;
            }

            foreach (var raw in File.ReadAllLines(file))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                bool negated = line[0] == '!';
                if (negated)
                {
                    line = line[1..];
                }

                bool folder = line.EndsWith('/');
                line = line.TrimEnd('/');
                bool anchored = line.StartsWith('/') || line.Contains('/', StringComparison.Ordinal);
                line = line.TrimStart('/');
                if (line.Length > 0)
                {
                    _rules.Add((line, negated, folder, anchored, source));
                }
            }
        }

        // A path matches when the pattern matches it or one of its parent folders (a folder's files are ignored with it).
        private static bool Matches(string pattern, bool folderOnly, bool anchored, string path)
        {
            var parts = path.Split('/');
            for (int end = parts.Length; end >= 1; end--)
            {
                bool isFolder = end < parts.Length;
                if (folderOnly && !isFolder)
                {
                    continue;
                }

                string prefix = string.Join('/', parts, 0, end);
                if (anchored ? Glob(pattern, prefix) : Glob(pattern, parts[end - 1]))
                {
                    return true;
                }
            }

            return false;
        }

        // '**' matches any folders; '*' and '?' within one name.
        private static bool Glob(string pattern, string path)
        {
            var patternParts = pattern.Split('/');
            var pathParts = path.Split('/');
            return GlobParts(patternParts, 0, pathParts, 0);
        }

        private static bool GlobParts(string[] pattern, int p, string[] path, int q)
        {
            if (p == pattern.Length)
            {
                return q == path.Length;
            }

            if (pattern[p] == "**")
            {
                for (int skip = q; skip <= path.Length; skip++)
                {
                    if (GlobParts(pattern, p + 1, path, skip))
                    {
                        return true;
                    }
                }

                return false;
            }

            return q < path.Length && FileSystemName.MatchesSimpleExpression(pattern[p], path[q], ignoreCase: false) && GlobParts(pattern, p + 1, path, q + 1);
        }
    }

    /// <summary>The "folder" destination: each item written under the folder, in a sub-folder named by the share's time.</summary>
    internal sealed class FolderDestination : IShareDestination
    {
        public string Name => ShareDestinations.Folder;

        public string Summary => "a folder: each share's items written under it, in a sub-folder named by the share's time";

        public async Task SendAsync(string target, IReadOnlyList<ShareItem> items, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(target);
            ArgumentNullException.ThrowIfNull(items);
            string folder = Path.Combine(Path.GetFullPath(target), DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            foreach (var item in items)
            {
                string path = Path.GetFullPath(Path.Combine(folder, item.Name));
                if (!path.StartsWith(folder, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"The item '{item.Name}' would be written outside {folder}.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, item.Content, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>The "https" destination: each item POSTed to the address (its name in the header X-Craft-Item).</summary>
    internal sealed class HttpsDestination : IShareDestination
    {
        public string Name => ShareDestinations.Https;

        public string Summary => "an https:// address: each item POSTed to it, its name in the header X-Craft-Item";

        public async Task SendAsync(string target, IReadOnlyList<ShareItem> items, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(target);
            ArgumentNullException.ThrowIfNull(items);
            var address = new Uri(target, UriKind.Absolute);
            if (address.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException($"A share goes over https only, not {address.Scheme}.", nameof(target));
            }

            using var client = new HttpClient();
            foreach (var item in items)
            {
                using var content = new ReadOnlyMemoryContent(item.Content);
                content.Headers.ContentType = new MediaTypeHeaderValue(item.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json" : "application/octet-stream");
                using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = content };
                request.Headers.Add("X-Craft-Item", item.Name);
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
        }
    }

    /// <summary>Makes a share's plan: the items of the kinds the settings name, the ignored and secret ones left out and listed.</summary>
    internal static class SharePlans
    {
        public static SharePlan Make(LearningSettings settings, string? workspace, IReadOnlyList<EditorEpisode> episodes, string? adapterFolder, string scores)
        {
            var filter = new ShareFilter(workspace, settings.SecretPatterns);
            var items = new List<ShareItem>();
            var leftOut = new List<string>();
            foreach (string kind in settings.ShareWhat)
            {
                switch (kind)
                {
                    case ShareKinds.Episodes:
                        foreach (var episode in episodes)
                        {
                            string where = episode.File is { } file ? $" ({file})" : "";
                            if (episode.File is { } relative && filter.IgnoredBy(relative) is { } by)
                            {
                                leftOut.Add($"episode {episode.Id}{where}: matched {by}");
                                continue;
                            }

                            if ((filter.Secret(episode.Prompt) ?? filter.Secret(episode.Answer) ?? filter.Secret(episode.Kept)) is { } secret)
                            {
                                leftOut.Add($"episode {episode.Id}{where}: {secret}");
                                continue;
                            }

                            string preview = OneLine(episode.Answer, 80);
                            items.Add(new ShareItem($"episodes/{episode.Id}.json", ShareKinds.Episodes, episode.ToJson(),
                                $"{episode.Kind}, {episode.Verdict}: {preview}", episode.File is { } path ? [path] : []));
                        }

                        break;
                    case ShareKinds.Adapter when adapterFolder is not null && Directory.Exists(adapterFolder):
                        foreach (var file in Directory.GetFiles(adapterFolder).Order(StringComparer.Ordinal))
                        {
                            string name = Path.GetFileName(file);
                            items.Add(new ShareItem($"adapter/{name}", ShareKinds.Adapter, File.ReadAllBytes(file),
                                name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? OneLine(File.ReadAllText(file), 80) : "the adapter's weights (numbers, no text)", []));
                        }

                        break;
                    case ShareKinds.Adapter:
                        leftOut.Add("the adapter: none is promoted yet");
                        break;
                    default:
                        items.Add(new ShareItem("scores.json", ShareKinds.Scores, Encoding.UTF8.GetBytes(scores), OneLine(scores, 120), []));
                        break;
                }
            }

            return new SharePlan(settings.ShareTo, items, leftOut);
        }

        /// <summary>The scores a "scores" share sends: counts by verdict and the adapters' held-out margins, no text.</summary>
        public static string Scores(IReadOnlyList<EditorEpisode> episodes, IReadOnlyList<AdapterVersion> versions)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("episodes", episodes.Count);
                writer.WriteStartObject("verdicts");
                foreach (var group in episodes.GroupBy(e => e.Verdict, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    writer.WriteNumber(group.Key, group.Count());
                }

                writer.WriteEndObject();
                writer.WriteStartArray("adapters");
                foreach (var version in versions)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("version", version.Number);
                    writer.WriteNumber("before", version.ScoreBefore);
                    writer.WriteNumber("after", version.ScoreAfter);
                    writer.WriteBoolean("promoted", version.Promoted);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string OneLine(string text, int most)
        {
            string line = text.ReplaceLineEndings(" ⏎ ");
            return line.Length <= most ? line : line[..most] + "…";
        }
    }
}
