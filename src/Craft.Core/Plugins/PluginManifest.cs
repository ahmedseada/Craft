// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;
using Craft.Abstractions;

namespace Craft.Core.Plugins;

/// <summary>
/// A plug-in folder's <c>plugin.json</c>: its name (the folder's and its assembly's), its version, one line of summary,
/// the Craft contract version it was built against (<see cref="CraftContracts"/>) and the capabilities it asks for
/// (<see cref="PluginCapabilities"/>: recorded and shown; enforced later).
/// </summary>
/// <example>
/// <code>
/// { "name": "Craft.Plugins.Sample", "version": "1.0.0", "summary": "Counts words", "contractVersion": "1.0", "capabilities": [] }
/// </code>
/// </example>
public sealed record PluginManifest
{
    /// <summary>The file's name in a plug-in folder.</summary>
    public const string FileName = "plugin.json";

    /// <summary>The plug-in's name: its folder's and its assembly's (ignoring case).</summary>
    public string Name { get; init; } = "";

    /// <summary>The plug-in's own version, as its author writes it ("1.2.0").</summary>
    public string Version { get; init; } = "";

    /// <summary>One line on what it brings.</summary>
    public string Summary { get; init; } = "";

    /// <summary>The contract version it was built against ("1.0").</summary>
    public string ContractVersion { get; init; } = "";

    /// <summary>The capabilities it asks for.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>
    /// Reads and checks a manifest from its UTF-8 bytes: the result, or the reasons it is not one (every problem found,
    /// each naming the field).
    /// </summary>
    public static PluginManifest? Read(ReadOnlySpan<byte> utf8, string folderName, out IReadOnlyList<string> problems)
    {
        var found = new List<string>();
        problems = found;
        if (utf8.StartsWith("\uFEFF"u8))
        {
            utf8 = utf8[3..];                                        // a byte-order mark, as editors on Windows often write one
        }

        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(utf8, PluginManifestJson.Default.PluginManifest);
        }
        catch (JsonException error)
        {
            found.Add($"{FileName} is not valid JSON: {error.Message}");
            return null;
        }

        if (manifest is null)
        {
            found.Add($"{FileName} holds null, not an object");
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            found.Add("\"name\" is missing");
        }
        else if (!manifest.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase))
        {
            found.Add($"\"name\" is '{manifest.Name}' but the folder is '{folderName}' (they must match, as the assembly's name does)");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            found.Add("\"version\" is missing");
        }

        if (string.IsNullOrWhiteSpace(manifest.ContractVersion))
        {
            found.Add($"\"contractVersion\" is missing (this Craft's is {CraftContracts.Version.ToString(2)})");
        }
        else if (CraftContracts.Parse(manifest.ContractVersion) is null)
        {
            found.Add($"\"contractVersion\" is '{manifest.ContractVersion}': it takes MAJOR.MINOR (this Craft's is {CraftContracts.Version.ToString(2)})");
        }

        foreach (var capability in manifest.Capabilities ?? [])
        {
            if (!PluginCapabilities.IsKnown(capability))
            {
                found.Add($"capability '{capability}' is not known (known: {string.Join(", ", PluginCapabilities.All)})");
            }
        }

        return found.Count == 0 ? manifest : null;
    }
}

/// <summary>The manifest's JSON, source-generated (no reflection, trim- and AOT-safe).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(PluginManifest))]
internal sealed partial class PluginManifestJson : JsonSerializerContext;
