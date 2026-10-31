// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;

namespace Craft.Abstractions;

/// <summary>
/// The version of Craft's plug-in contracts (this assembly's public API as a plug-in sees it). A plug-in states the
/// version it was built against in its <c>plugin.json</c> (<c>"contractVersion": "1.0"</c>): one built against a newer
/// version than the running Craft's (a newer major, or a newer minor of the same major) is refused with the reason,
/// never loaded half-way; one built against an older major is loaded and marked outdated; an older minor of the same
/// major is current (a minor only adds).
/// </summary>
public static class CraftContracts
{
    /// <summary>The running Craft's contract version.</summary>
    public static Version Version { get; } = new(1, 0);

    /// <summary>
    /// Parses a contract version ("1.0", "1", "2.3"): a major and an optional minor, whole numbers from 0. Null when the
    /// text is not one.
    /// </summary>
    public static Version? Parse(ReadOnlySpan<char> text)
    {
        text = text.Trim();
        int dot = text.IndexOf('.');
        var majorText = dot < 0 ? text : text[..dot];
        var minorText = dot < 0 ? "0" : text[(dot + 1)..];
        return int.TryParse(majorText, NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            && int.TryParse(minorText, NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            ? new Version(major, minor)
            : null;
    }

    /// <summary>
    /// How a plug-in built against <paramref name="builtAgainst"/> fits the running version: <see cref="ContractFit.Current"/>,
    /// <see cref="ContractFit.Outdated"/> (an older major: loaded and marked) or <see cref="ContractFit.Newer"/> (refused).
    /// </summary>
    public static ContractFit Fit(Version builtAgainst)
    {
        ArgumentNullException.ThrowIfNull(builtAgainst);
        var running = Version;
        if (builtAgainst.Major > running.Major || (builtAgainst.Major == running.Major && builtAgainst.Minor > running.Minor))
        {
            return ContractFit.Newer;
        }

        return builtAgainst.Major < running.Major ? ContractFit.Outdated : ContractFit.Current;
    }
}

/// <summary>How a plug-in's contract version fits the running Craft's (<see cref="CraftContracts.Fit"/>).</summary>
public enum ContractFit
{
    /// <summary>The same major, an equal or older minor: loaded.</summary>
    Current,

    /// <summary>An older major: loaded and marked outdated (it may miss what changed since).</summary>
    Outdated,

    /// <summary>Newer than the running Craft's: refused with the reason.</summary>
    Newer,
}

/// <summary>
/// The capabilities a plug-in asks for in its <c>plugin.json</c> (<c>"capabilities": ["files", "network"]</c>): recorded
/// and shown on the Plug-ins page; their enforcement comes later (a plug-in is code, trusted as a whole for now).
/// </summary>
public static class PluginCapabilities
{
    /// <summary>Reads or writes files beyond the open documents.</summary>
    public const string Files = "files";

    /// <summary>Reaches the network.</summary>
    public const string Network = "network";

    /// <summary>Runs commands through a registered sandbox (Idrak's <c>CommandSandboxes</c>).</summary>
    public const string Sandbox = "sandbox";

    /// <summary>Uses a model (chat or text) through Craft's model providers.</summary>
    public const string Model = "model";

    /// <summary>Types the user's own input into documents (<see cref="IEditorHost.TypeText"/>: dictation).</summary>
    public const string Input = "input";

    /// <summary>Records from the microphone (the status bar says while it is open) and plays sound.</summary>
    public const string Microphone = "microphone";

    /// <summary>Every capability a <c>plugin.json</c> may name, in order.</summary>
    public static IReadOnlyList<string> All { get; } = [Files, Network, Sandbox, Model, Input, Microphone];

    /// <summary>Whether <paramref name="name"/> is one of <see cref="All"/> (ignoring case).</summary>
    public static bool IsKnown(ReadOnlySpan<char> name)
    {
        foreach (var known in All)
        {
            if (name.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// An editor plug-in, registered in <see cref="EditorPlugins"/> under its <see cref="IKind.Name"/> (usually by its
/// assembly's Idrak plug-in package, in <c>Register</c>, as Craft.CSharp registers its language). Each editor host
/// activates every registered plug-in once (<see cref="Activate"/>) and disposes what it returned to deactivate it.
/// </summary>
public interface IEditorPlugin : IKind
{
    /// <summary>The settings it declares (read with <see cref="IEditorHost.Settings"/>); none by default.</summary>
    IReadOnlyList<PluginSetting> Settings => [];

    /// <summary>
    /// Starts the plug-in in <paramref name="host"/>, on the host's UI thread: it subscribes to the host's events and adds
    /// its status items and panels. The result deactivates it when disposed (the host then also takes back whatever the
    /// plug-in added through it and left behind). A plug-in that throws here is reported and skipped.
    /// </summary>
    IDisposable Activate(IEditorHost host);
}

/// <summary>
/// The editor plug-ins, by name (ignoring case). Craft's library registers none; a plug-in package registers its own
/// (and takes them back when it is unregistered), so a removed or disabled plug-in leaves nothing behind.
/// </summary>
public static class EditorPlugins
{
    private static readonly KindTable<IEditorPlugin> Table = new(nameof(EditorPlugins), "Editor plug-in", "add it with EditorPlugins.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a plug-in is activated once per host and holds its own state");

    /// <summary>Registers <paramref name="plugin"/> under its name.</summary>
    public static void Register(IEditorPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        Table.Register(plugin, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the plug-in of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered plug-ins, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IEditorPlugin> All => Table.Kinds;

    /// <summary>The plug-in of that name, or null.</summary>
    public static IEditorPlugin? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The plug-in of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IEditorPlugin Get(ReadOnlySpan<char> name) => Table.Get(name);
}

/// <summary>
/// A setting a plug-in declares: its key, its default (as text) and what it sets. Its value comes from the environment
/// variable <see cref="VariableName"/> (<c>CRAFT_PLUGIN_&lt;PLUGIN&gt;_&lt;KEY&gt;</c>), else the default; the settings
/// page shows it with the others.
/// </summary>
/// <param name="Key">The key, unique within the plug-in ("showCharacters").</param>
/// <param name="Default">The value when none is set.</param>
/// <param name="Description">What it sets, one line.</param>
public sealed record PluginSetting(string Key, string Default, string Description)
{
    /// <summary>
    /// The environment variable of <paramref name="key"/> of the plug-in <paramref name="plugin"/>:
    /// <c>CRAFT_PLUGIN_</c>, the plug-in's name, <c>_</c>, the key, upper-cased, each character not a letter or a digit
    /// made <c>_</c> ("sample", "showCharacters" → <c>CRAFT_PLUGIN_SAMPLE_SHOWCHARACTERS</c>).
    /// </summary>
    public static string VariableName(string plugin, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plugin);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        const string Prefix = "CRAFT_PLUGIN_";
        return string.Create(Prefix.Length + plugin.Length + 1 + key.Length, (plugin, key), static (span, parts) =>
        {
            Prefix.CopyTo(span);
            int at = Prefix.Length;
            at = Upper(parts.plugin, span, at);
            span[at++] = '_';
            Upper(parts.key, span, at);
        });

        static int Upper(string text, Span<char> into, int at)
        {
            foreach (char c in text)
            {
                into[at++] = char.IsAsciiLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_';
            }

            return at;
        }
    }
}

/// <summary>One declared setting and its value now.</summary>
/// <param name="Plugin">The plug-in that declares it.</param>
/// <param name="Setting">The declaration.</param>
/// <param name="Variable">Its environment variable.</param>
/// <param name="Value">Its value: the variable's when set, else the default.</param>
public sealed record PluginSettingValue(string Plugin, PluginSetting Setting, string Variable, string Value);

/// <summary>The plug-ins' settings, as the host reads them.</summary>
public interface IPluginSettings
{
    /// <summary>The value of <paramref name="key"/> of the plug-in <paramref name="plugin"/>: its variable's when set, else its default.</summary>
    /// <exception cref="NotRegisteredException">No plug-in of that name is registered.</exception>
    /// <exception cref="ArgumentException">The plug-in declares no setting of that key (the message lists those it declares).</exception>
    string Get(string plugin, string key);

    /// <summary>Every registered plug-in's declared settings with their values, in order (for the settings page).</summary>
    IReadOnlyList<PluginSettingValue> All { get; }
}
