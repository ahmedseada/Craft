// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>What a key does while the peek overlay has the keyboard.</summary>
public enum PeekAction
{
    /// <summary>Nothing of the peek's own: the read-only editor takes it (moving, selecting, copying).</summary>
    None,

    /// <summary>Escape: the overlay closes.</summary>
    Close,

    /// <summary>Enter: the definition opens in the focused group.</summary>
    Open,
}

/// <summary>
/// A peek of a definition (the X restart's Alt+F12): the document it is in, read-only, with its language session, and
/// where. An open document is shared (the same text the tabs show, and the session its views share); a closed file is read
/// off the UI thread and coloured by its language's syntactic layer only, no workspace built. Disposing it lets go of
/// what it took (the read file, the session's hold).
/// </summary>
public sealed class PeekSession : IDisposable
{
    private readonly LanguageSessionLease? _lease;
    private readonly ILanguageSession? _ownSession;
    private readonly bool _ownsDocument;
    private bool _disposed;

    internal PeekSession(DefinitionLocation location, ITextDocument document, ILanguage? language, string? filePath, bool ownsDocument,
        LanguageSessionLease? lease, ILanguageSession? ownSession, ILanguageSession? borrowed)
    {
        Location = location;
        Document = document;
        Language = language;
        FilePath = filePath;
        _ownsDocument = ownsDocument;
        _lease = lease;
        _ownSession = ownSession;
        Session = lease?.Session ?? ownSession ?? borrowed;
        var snapshot = document.Buffer.Current;
        Start = Math.Clamp(location.Start, 0, snapshot.Length);
        Length = Math.Clamp(location.Length, 0, snapshot.Length - Start);
        Line = snapshot.GetLineIndex(Start);
        string name = filePath is { } path ? Path.GetFileName(path) : document.FilePath is { } own ? Path.GetFileName(own) : "this file";
        Title = string.Create(CultureInfo.InvariantCulture, $"{name} · line {Line + 1}");
    }

    /// <summary>Where the definition is (its file, its name's span, its description).</summary>
    public DefinitionLocation Location { get; }

    /// <summary>The document shown (an open tab's own when the file is open, else the file read for the peek).</summary>
    public ITextDocument Document { get; }

    /// <summary>The document's language, or null for plain text.</summary>
    public ILanguage? Language { get; }

    /// <summary>The session the peek's editor colours with (not the editor's to dispose), or null for plain text.</summary>
    public ILanguageSession? Session { get; }

    /// <summary>The definition's file, or null when it is the source document with no file.</summary>
    public string? FilePath { get; }

    /// <summary>Whether the document is one a tab shows (shared), rather than a closed file read for the peek.</summary>
    public bool IsOpenDocument => !_ownsDocument;

    /// <summary>Whether the session has the syntactic layer only (a closed file: no workspace built).</summary>
    public bool IsSyntacticOnly => Session is SyntacticLanguageSession;

    /// <summary>The definition's name's first character, in the document.</summary>
    public int Start { get; }

    /// <summary>The name's characters.</summary>
    public int Length { get; }

    /// <summary>The name's line, from 0.</summary>
    public int Line { get; }

    /// <summary>The overlay's title: the file's name and the line ("Program.cs · line 12").</summary>
    public string Title { get; }

    /// <summary>What <paramref name="key"/> does in the overlay: Escape closes, Enter opens, the rest goes to its editor.</summary>
    public static PeekAction ActionFor(EditorKey key, KeyModifiers modifiers) => (key, modifiers) switch
    {
        (EditorKey.Escape, KeyModifiers.None) => PeekAction.Close,
        (EditorKey.Enter, KeyModifiers.None) => PeekAction.Open,
        _ => PeekAction.None,
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lease?.Dispose();
        _ownSession?.Dispose();
        if (_ownsDocument)
        {
            Document.Dispose();
        }
    }
}

public sealed partial class ShellModel
{
    /// <summary>
    /// The peek of <paramref name="location"/>, asked from <paramref name="source"/> (the editor whose caret it was): the
    /// source's own document for a definition in it; an open tab's document when the file is open in any group (shared,
    /// with the session its views share); else the file read off the UI thread (up to <c>CRAFT_PEEK_MAX_MB</c>) with its
    /// language's syntactic colours only. Null, said in the status bar, for a metadata symbol or a file that cannot be read.
    /// </summary>
    public async Task<PeekSession?> PeekAsync(EditorModel source, DefinitionLocation location, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(location);
        if (location.IsSameDocument || (location.FilePath is { } own && source.Document.FilePath is { } sourcePath && PathKeys.Comparer.Equals(Path.GetFullPath(own), Path.GetFullPath(sourcePath))))
        {
            var lease = Sessions.AcquireOpen(source.Document);
            return new PeekSession(location, source.Document, source.Language, source.Document.FilePath, ownsDocument: false, lease, null, lease is null ? source.LanguageSession : null);
        }

        if (location.FilePath is not { } path)
        {
            Host.ShowStatus("Peek definition", location.Description);
            return null;
        }

        var full = Path.GetFullPath(path);
        if (Groups.FindOpen(full) is { } tab)
        {
            var lease = Sessions.AcquireOpen(tab.Document);
            ILanguageSession? colours = lease is null && tab.Language is { } tabLanguage
                ? SyntacticLanguageSession.Open(tabLanguage, tab.Document.Buffer, full, Options.ToLanguageSettings())
                : null;
            return new PeekSession(location, tab.Document, tab.Language, full, ownsDocument: false, lease, colours, null);
        }

        // A closed file: its size checked, read and coloured off the UI thread; nothing semantic, no workspace.
        long limit = (long)Options.PeekMaxMegabytes * 1024 * 1024;
        var options = Options;
        var language = EditorLanguages.ForFile(full);
        try
        {
            var (document, session, problem) = await Task.Run<(ITextDocument? Document, ILanguageSession? Session, string? Problem)>(async () =>
            {
                var info = new FileInfo(full);
                if (!info.Exists)
                {
                    return (null, null, $"{Path.GetFileName(full)} is no longer there.");
                }

                if (info.Length > limit)
                {
                    return (null, null, string.Create(CultureInfo.InvariantCulture,
                        $"{Path.GetFileName(full)} is {info.Length / (1024 * 1024)} MB, more than CRAFT_PEEK_MAX_MB ({options.PeekMaxMegabytes}): open it with F12."));
                }

                var read = await _openDocument(full, options, cancellationToken).ConfigureAwait(false);
                ILanguageSession? syntactic = language is null ? null : SyntacticLanguageSession.Open(language, read.Buffer, full, options.ToLanguageSettings());
                return (read, syntactic, null);
            }, cancellationToken);
            if (document is null)
            {
                Host.ShowStatus("Peek definition", problem ?? "The file could not be read.", NotificationSeverity.Warning);
                return null;
            }

            return new PeekSession(location, document, language, full, ownsDocument: true, null, session, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or DecoderFallbackException)
        {
            Host.ShowStatus("Peek definition", $"{Path.GetFileName(full)} could not be read: {e.Message}", NotificationSeverity.Warning);
            return null;
        }
    }

    /// <summary>
    /// Enter or a double click in the peek: the definition opens in the focused group (its tab, or the file opened there)
    /// with its name selected; for a definition in the source document, the caret moves there. The tab, or null.
    /// </summary>
    public async Task<DocumentTab?> OpenPeekAsync(PeekSession peek, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peek);
        DocumentTab? tab;
        if (peek.FilePath is { } path)
        {
            tab = await OpenInGroupAsync(Groups.Focused, path, activate: true, cancellationToken);
        }
        else
        {
            tab = Tabs.Showing(peek.Document);
            if (tab is not null)
            {
                Tabs.Activate(tab);
            }
        }

        if (tab is not null && EditorOf?.Invoke(tab) is { } editor)
        {
            int length = editor.Snapshot.Length;
            int start = Math.Clamp(peek.Start, 0, length);
            editor.Select(Math.Min(length, start + peek.Length), start);
            editor.EnsureCaretVisible();
        }

        return tab;
    }
}
