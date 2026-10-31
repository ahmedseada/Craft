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
        private bool _closed;

        public bool IsClosed => _closed;

        public IReadOnlyList<IEditorDocument> Documents => host.Documents;

        public IEditorDocument? ActiveDocument => host.ActiveDocument;

        public SelectionState Selection => host.Selection;

        public string? WorkspaceFolder => host.WorkspaceFolder;

        public IEditorTerminals? Terminals => host.Terminals is { } terminals ? new ScopedTerminals(terminals, this) : null;

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

        public bool TypeText(IEditorDocument document, string text) => !_closed && host.TypeCore(document, text, name);

        public bool Select(IEditorDocument document, SelectionState selection) => !_closed && host.Select(document, selection);

        public Task<bool> ExecuteCommandAsync(string commandId, CancellationToken cancellationToken = default) =>
            _closed ? Task.FromResult(false) : host.ExecuteCommandAsync(commandId, cancellationToken);

        public IDisposable AddStatusItem(IStatusItem item) => Keep(() => host.AddStatusItem(item, name), "a status item");

        public IDisposable AddPanel(IEditorPanel panel) => Keep(() => host.AddPanel(panel, name), "a panel");

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

        public void Dispatch(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            host.Dispatch(() =>
            {
                if (!_closed)
                {
                    action();
                }
            });
        }

        /// <summary>Takes back what the plug-in left, the last added first.</summary>
        public void Close(EditorHost owner)
        {
            _closed = true;
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

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
