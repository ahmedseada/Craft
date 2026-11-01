// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core.Documents;

namespace Craft.Editor.Shell;

/// <summary>Which files' own settings may have changed: one file (saved under its path), or every file (a configuration file changed).</summary>
/// <param name="path">The file, or null for every file.</param>
public sealed class FileSettingsChangedEventArgs(string? path) : EventArgs
{
    /// <summary>The file, or null for every file.</summary>
    public string? Path { get; } = path;
}

/// <summary>
/// X part 9: a file's own settings (<c>.editorconfig</c>, through <see cref="FileSettingsProviders"/>; <c>CRAFT_EDITORCONFIG</c>
/// switches the library's reader), read off the UI thread and kept per folder by the provider, applied to the file's
/// editors by the page, and the save actions they and the options ask for (trim the trailing white space, insert a final
/// newline, a new file's charset), each one undo step before the save.
/// </summary>
public sealed partial class ShellModel
{
    /// <summary>Raised on the UI thread when files' own settings may have changed (the page applies them to their editors again).</summary>
    public event EventHandler<FileSettingsChangedEventArgs>? FileSettingsChanged;

    /// <summary>The settings of <paramref name="path"/> when known without reading a file (null: ask <see cref="FileSettingsAsync"/>).</summary>
    public FileSettings? CachedFileSettings(string? path) => path is null ? FileSettings.None : FileSettingsProviders.Cached(path, UseFileSettings);

    /// <summary>The settings of <paramref name="path"/>, read off the UI thread where they are not known yet.</summary>
    public Task<FileSettings> FileSettingsAsync(string? path, CancellationToken cancellationToken = default) =>
        path is null ? Task.FromResult(FileSettings.None) : FileSettingsProviders.ForFileAsync(path, UseFileSettings, cancellationToken);

    /// <summary>A configuration file (<paramref name="path"/>) was made, saved, renamed or removed: every file's settings are read again.</summary>
    public void OnConfigurationFileChanged(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        bool any = false;
        foreach (var provider in FileSettingsProviders.All)
        {
            if (provider.IsConfigurationFile(path))
            {
                provider.Invalidate();
                any = true;
            }
        }

        if (any)
        {
            FileSettingsChanged?.Invoke(this, new FileSettingsChangedEventArgs(null));
        }
    }

    /// <summary>Whether <paramref name="path"/> is a configuration file of a provider in use.</summary>
    public static bool IsConfigurationFile(string path)
    {
        foreach (var provider in FileSettingsProviders.All)
        {
            if (provider.IsConfigurationFile(path))
            {
                return true;
            }
        }

        return false;
    }

    private bool UseFileSettings(IFileSettingsProvider provider) =>
        Options.EditorConfig || !string.Equals(provider.Name, FileSettingsProviders.EditorConfig, StringComparison.OrdinalIgnoreCase);

    // Before a save: the trailing white space, the final newline (each one undo step, through the tab's editor when it has
    // one), and a new file's charset; the file's settings win over the options.
    private async Task ApplySaveActionsAsync(DocumentTab tab, string? path, CancellationToken cancellationToken)
    {
        string? target = path ?? tab.FilePath;
        var settings = target is null ? FileSettings.None : CachedFileSettings(target) ?? await FileSettingsAsync(target, cancellationToken);
        bool trim = settings.TrimTrailingWhitespace ?? Options.TrimTrailingWhitespace;
        bool finalNewline = settings.InsertFinalNewline ?? Options.InsertFinalNewline;
        if (trim || finalNewline)
        {
            if (EditorOf?.Invoke(tab) is { IsReadOnly: false } editor)
            {
                if (trim)
                {
                    editor.TrimTrailingWhitespace();
                }

                if (finalNewline)
                {
                    editor.InsertFinalNewline();
                }
            }
            else
            {
                string newLine = settings.NewLine ?? (tab.Document as TextDocument)?.LineEnding ?? Options.NewLine ?? Environment.NewLine;
                SaveActions.Apply(tab.Document, trim, finalNewline, newLine);
            }
        }

        if (tab.FilePath is null && path is not null && settings.Charset is { } charset && tab.Document is TextDocument document && EncodingOf(charset) is var (encoding, bom))
        {
            document.SetEncoding(encoding, bom);
        }
    }

    /// <summary>
    /// The lightbulb's probe for <paramref name="tab"/>'s editors (X part 9): whether a registered code action provider offers
    /// anything for the query, on the tab's document; asked off the UI thread, a provider that fails counting as none.
    /// </summary>
    public static Func<LightbulbQuery, bool> LightbulbProbe(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var document = tab.HostDocument;
        return query => CodeActionProviders.ActionsFor(new CodeActionContext(document, query.Start, query.End, query.Diagnostics)).Count > 0;
    }

    /// <summary>The encoding and byte order mark of an <c>.editorconfig</c> charset.</summary>
    public static (Encoding Encoding, bool ByteOrderMark) EncodingOf(string charset) => charset switch
    {
        "utf-8-bom" => (new UTF8Encoding(true), true),
        "latin1" => (Encoding.Latin1, false),
        "utf-16le" => (Encoding.Unicode, true),
        "utf-16be" => (Encoding.BigEndianUnicode, true),
        _ => (new UTF8Encoding(false), false),
    };

    // The watcher's threads: an entry made, removed or renamed; a configuration file among them reads every file's settings again.
    private void OnEntryChanged(string path)
    {
        if (IsConfigurationFile(path))
        {
            Host.Dispatch(() => OnConfigurationFileChanged(path));
        }
    }
}
