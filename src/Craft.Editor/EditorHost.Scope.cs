// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

public sealed partial class EditorHost
{
    /// <summary>
    /// The host as one plug-in sees it: every call goes to the host under the plug-in's name, and everything the plug-in
    /// adds (handlers, status items, panels, progress) is remembered, so its deactivation takes back what it left behind.
    /// After that, what it adds is refused (logged) and its calls do nothing.
    /// </summary>
    private sealed class PluginScope(EditorHost host, string name) : IEditorHost
    {
        private readonly List<Action> _undo = [];
        private readonly HashSet<string> _published = new(StringComparer.Ordinal);
        private ScopedActivities? _activities;
        private bool _closed;

        public bool IsClosed => _closed;

        public IReadOnlyList<IEditorDocument> Documents => host.Documents;

        public IEditorDocument? ActiveDocument => host.ActiveDocument;

        public SelectionState Selection => host.Selection;

        public string? WorkspaceFolder => host.WorkspaceFolder;

        public IEditorTerminals? Terminals => host.Terminals is { } terminals ? new ScopedTerminals(terminals, this) : null;

        public string? DataFolder => host.DataFolder;

        public Task<bool> OpenExternalAsync(string path) => _closed ? Task.FromResult(false) : host.OpenExternalAsync(path);

        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) =>
            _closed ? Task.FromResult<string?>(null) : host.PickFolderAsync(startFolder, cancellationToken);

        public bool ShowUndo(string text, Action undo, Action? kept = null) => !_closed && host.ShowUndo(text, undo, kept);

        public IReadOnlyList<IEditorCommand> Commands => host.Commands;

        public IPluginSettings Settings => host.Settings;

        public IReadOnlyList<IEditorPanel> Panels => host.Panels;

        public event EventHandler<EditorKeyEventArgs>? KeyPressed
        {
            add => Track(host._keyPressed, value);
            remove => host._keyPressed.Remove(value);
        }

        public event EventHandler<EditorDocumentEventArgs>? DocumentOpened
        {
            add => Track(host._opened, value);
            remove => host._opened.Remove(value);
        }

        public event EventHandler<EditorDocumentChangedEventArgs>? DocumentChanged
        {
            add => Track(host._changed, value);
            remove => host._changed.Remove(value);
        }

        public event EventHandler<EditorDocumentEventArgs>? DocumentSaved
        {
            add => Track(host._saved, value);
            remove => host._saved.Remove(value);
        }

        public event EventHandler<EditorDocumentEventArgs>? DocumentClosed
        {
            add => Track(host._closed, value);
            remove => host._closed.Remove(value);
        }

        public event EventHandler? ActiveDocumentChanged
        {
            add => Track(host._activeChanged, value);
            remove => host._activeChanged.Remove(value);
        }

        public event EventHandler<EditorDocumentEventArgs>? SelectionChanged
        {
            add => Track(host._selectionChanged, value);
            remove => host._selectionChanged.Remove(value);
        }

        public Task<bool> ProposeEditsAsync(IEditorDocument document, IReadOnlyList<TextEdit> edits, string title, CancellationToken cancellationToken = default) =>
            _closed ? Task.FromResult(false) : host.ProposeCoreAsync(document, new ProposedEdits(edits, $"{name}: {title}"), name, cancellationToken);

        public Task<bool> ProposeAsync(IEditorDocument document, ProposedEdits proposal, CancellationToken cancellationToken = default) =>
            _closed ? Task.FromResult(false)
                : host.ProposeCoreAsync(document, (proposal ?? throw new ArgumentNullException(nameof(proposal))) with { Title = $"{name}: {proposal.Title}" }, name, cancellationToken);

        public void ShowPanel(string panelId)
        {
            if (!_closed)
            {
                host.ShowPanel(panelId);
            }
        }

        public Task<IEditorDocument?> OpenDocumentAsync(string path, CancellationToken cancellationToken = default) =>
            _closed ? Task.FromResult<IEditorDocument?>(null) : host.OpenDocumentAsync(path, cancellationToken);

        public Task SaveAsync(IEditorDocument document, string? path = null, CancellationToken cancellationToken = default) =>
            _closed ? Task.CompletedTask : host.SaveAsync(document, path, cancellationToken);

        public bool TypeText(IEditorDocument document, string text) => !_closed && host.TypeCore(document, text, name);

        public bool Select(IEditorDocument document, SelectionState selection) => !_closed && host.Select(document, selection);

        public Task<bool> ExecuteCommandAsync(string commandId, CancellationToken cancellationToken = default) =>
            _closed ? Task.FromResult(false) : host.ExecuteCommandAsync(commandId, cancellationToken);

        public IDisposable AddStatusItem(IStatusItem item) => Keep(() => host.AddStatusItem(item, name), "a status item");

        public IDisposable AddPanel(IEditorPanel panel) => Keep(() => host.AddPanel(panel, name), "a panel");

        public event EventHandler? WorkspaceFolderChanged
        {
            add => Track(host._workspaceChanged, value);
            remove => host._workspaceChanged.Remove(value);
        }

        public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged
        {
            add => Track(host._workspaceChangedWith, value);
            remove => host._workspaceChangedWith.Remove(value);
        }

        public event EventHandler<LineMarkEventArgs>? LineMarkClicked
        {
            add => Track(host._lineMarkClicked, value);
            remove => host._lineMarkClicked.Remove(value);
        }

        public bool SetLineMarks(IEditorDocument document, string source, IReadOnlyList<LineMark> marks)
        {
            if (_closed || !host.SetLineMarks(document, source, marks))
            {
                return false;
            }

            // Taken back with the plug-in: its marks leave the document when it is deactivated.
            _undo.Add(() =>
            {
                if (document is OpenDocument { IsClosed: false } open)
                {
                    DocumentLineMarks.Of(open.Document).Set(source, []);
                }
            });
            return true;
        }

        public bool ShowInlineCard(IEditorDocument document, InlineCard card) => !_closed && host.ShowInlineCard(document, card);

        public bool PublishDiagnostics(string path, string source, ITextSnapshot? snapshot, IReadOnlyList<FileDiagnostic> items)
        {
            if (_closed || !host.PublishDiagnostics(path, source, snapshot, items))
            {
                return false;
            }

            // Taken back with the plug-in: every diagnostic of its sources leaves the files when it is deactivated.
            if (_published.Add(source))
            {
                _undo.Add(() => host.ClearDiagnostics(source));
            }

            return true;
        }

        public void ClearDiagnostics(string source)
        {
            if (!_closed)
            {
                host.ClearDiagnostics(source);
            }
        }

        public IDisposable AddRepository(ISourceControlRepository repository) => Keep(() => host.AddRepository(repository, name), "a repository");

        public Task<bool> ShowDiffAsync(string title, string path, string before, string after, CancellationToken cancellationToken = default) =>
            _closed ? Task.FromResult(false) : host.ShowDiffAsync(title, path, before, after, cancellationToken);

        public Task<bool> ProposeWorkspaceAsync(WorkspaceEdit edit, string title, CancellationToken cancellationToken = default) =>
            _closed ? Task.FromResult(false) : host.ProposeWorkspaceAsync(edit, $"{name}: {title}", cancellationToken);

        public IEditorProgress StartProgress(string title)
        {
            var item = host.StartProgress(title, name);
            if (_closed)
            {
                item.Dispose();
            }
            else
            {
                _undo.Add(item.Dispose);
            }

            return item;
        }

        public Task ShowMessageAsync(string title, string message) => _closed ? Task.CompletedTask : host.ShowMessageAsync(title, message);

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken = default) =>
            _closed ? Task.FromResult(-1) : host.AskAsync(question, name, cancellationToken);

        public void Log(EditorLogLevel level, string message, Exception? error = null) => host.Log(name, level, message, error);

        public void Record(string metric, double value, string unit) => host.Record(name, metric, value, unit);

        public void Dispatch(Action action) => Dispatch("plug-in " + name, action);

        public void Dispatch(string label, Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            host.Dispatch(string.IsNullOrWhiteSpace(label) ? "plug-in " + name : name + ": " + label, () =>
            {
                if (!_closed)
                {
                    action();
                }
            });
        }

        public IEditorActivities Activities => _activities ??= new ScopedActivities(host.ActivityCenter, name, this);

        public IEditorNotification Notify(string title, string text, NotificationSeverity severity = NotificationSeverity.Information, IReadOnlyList<NotificationAction>? actions = null) =>
            _closed ? NoNotification.Instance : host.Notify(name, title, text, severity, actions);

        public IEditorNotification ShowStatus(string title, string text, NotificationSeverity severity = NotificationSeverity.Information, IReadOnlyList<NotificationAction>? actions = null) =>
            _closed ? NoNotification.Instance : host.Notifications.ShowStatus(name, title, text, severity, actions);

        /// <summary>Remembers how to take back something the plug-in added.</summary>
        public void Remember(Action undo)
        {
            if (!_closed)
            {
                _undo.Add(undo);
            }
        }

        /// <summary>Takes back what the plug-in left, the last added first.</summary>
        public void Close(EditorHost owner)
        {
            _closed = true;
            owner.ActivityCenter.EndAll(name, "the plug-in was deactivated");
            owner.Notifications.RemoveStatuses(name);
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                try
                {
                    _undo[i]();
                }
#pragma warning disable CA1031 // Taking back one contribution never stops taking back the others.
                catch (Exception error)
#pragma warning restore CA1031
                {
                    owner.Log(HostSource, EditorLogLevel.Error, $"Taking back what plug-in '{name}' added failed: {error.Message}", error);
                }
            }

            _undo.Clear();
        }

        private void Track<THandler>(HandlerList<THandler> handlers, THandler? handler) where THandler : Delegate
        {
            if (_closed || handler is null)
            {
                return;
            }

            handlers.Add(handler);
            _undo.Add(() => handlers.Remove(handler));
        }

        private IDisposable Keep(Func<IDisposable> add, string what)
        {
            if (_closed)
            {
                host.Log(name, EditorLogLevel.Warning, $"Plug-in '{name}' is not active: {what} it added was not shown.", null);
                return Nothing.Instance;
            }

            var handle = add();
            _undo.Add(handle.Dispose);
            return handle;
        }
    }

    /// <summary>The terminals as one plug-in reaches them: what it writes stops once it is deactivated (the terminal stays the user's).</summary>
    private sealed class ScopedTerminals(IEditorTerminals terminals, PluginScope scope) : IEditorTerminals
    {
        public IReadOnlyList<IEditorTerminal> All => [.. terminals.All.Select(t => (IEditorTerminal)new ScopedTerminal(t, scope))];

        public IEditorTerminal? Active => terminals.Active is { } active ? new ScopedTerminal(active, scope) : null;

        public IEditorTerminal Open(EditorTerminalRequest request) =>
            scope.IsClosed ? throw new InvalidOperationException("The plug-in is not active: it cannot open a terminal.") : new ScopedTerminal(terminals.Open(request), scope);

        public IEditorTerminalOutput? OpenOutput(string name) =>
            scope.IsClosed ? null : terminals.OpenOutput(name) is { } output ? new ScopedOutput(output, scope) : null;

        public IEditorTerminalProgram? OpenProgram(EditorProgramRequest request) =>
            scope.IsClosed ? null : terminals.OpenProgram(request) is { } program ? new ScopedProgram(program, scope) : null;
    }

    /// <summary>A plug-in's program tab: its notes and its Show stop once the plug-in is deactivated; its end always lands (the program is the plug-in's to end).</summary>
    private sealed class ScopedProgram(IEditorTerminalProgram program, PluginScope scope) : IEditorTerminalProgram
    {
        public string Title => program.Title;

        public bool IsOpen => !scope.IsClosed && program.IsOpen;

        public Task OutputEnded => program.OutputEnded;

        public bool Append(string text) => !scope.IsClosed && program.Append(text);

        public void End(string? text = null, int exitCode = 0) => program.End(scope.IsClosed ? null : text, exitCode);

        public void Show()
        {
            if (!scope.IsClosed)
            {
                program.Show();
            }
        }
    }

    private sealed class ScopedOutput(IEditorTerminalOutput output, PluginScope scope) : IEditorTerminalOutput
    {
        public string Title => output.Title;

        public bool IsOpen => !scope.IsClosed && output.IsOpen;

        public bool Append(string text) => !scope.IsClosed && output.Append(text);

        public void Show()
        {
            if (!scope.IsClosed)
            {
                output.Show();
            }
        }
    }

    private sealed class ScopedTerminal(IEditorTerminal terminal, PluginScope scope) : IEditorTerminal
    {
        public string Title => terminal.Title;

        public string WorkingDirectory => terminal.WorkingDirectory;

        public Task<int> Exited => terminal.Exited;

        public bool Write(string text) => !scope.IsClosed && terminal.Write(text);

        public void Show()
        {
            if (!scope.IsClosed)
            {
                terminal.Show();
            }
        }
    }

    /// <summary>The activities as one plug-in starts them: under its name, and refused (shown nowhere) once it is deactivated.</summary>
    private sealed class ScopedActivities(Activities.ActivityCenter center, string name, PluginScope scope) : IEditorActivities
    {
        public IReadOnlyList<ActivitySnapshot> Running => center.Running;

        public IReadOnlyList<ActivitySnapshot> Recent => center.Recent;

        public event EventHandler? Changed
        {
            add
            {
                center.Changed += value;
                scope.Remember(() => center.Changed -= value);
            }

            remove => center.Changed -= value;
        }

        public IActivity Start(string title, string kind = ActivityKinds.General, bool cancellable = false, CancellationToken cancellationToken = default, bool keep = true) =>
            scope.IsClosed ? NoActivities.Instance.Start(title, kind, cancellable, cancellationToken, keep) : center.Start(title, kind, cancellable, cancellationToken, keep, name);

        public bool Cancel(long id) => center.Cancel(id);

        public bool Retry(long id) => center.Retry(id);
    }

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
