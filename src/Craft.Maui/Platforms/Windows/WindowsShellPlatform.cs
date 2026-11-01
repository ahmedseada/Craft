// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using Craft.Abstractions;
using Craft.Core.Diagnostics;
using Craft.Editor.Abstractions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiWindow = Microsoft.Maui.Controls.Window;
using NativeWindow = Microsoft.UI.Xaml.Window;

namespace Craft.Maui.WinUI;

/// <summary>
/// The shell's Windows pieces (WinUI 3 and the Windows App SDK): the file dialogs (<see cref="WindowsFileDialogs"/>), the
/// keys no control handled sent to the key bindings (a KeyDown handler on the window's content, so a key the editor or a
/// text box took never comes twice), and the unsaved-changes question on closing (<see cref="AppWindow.Closing"/>).
/// </summary>
public sealed class WindowsShellPlatform : IShellPlatform
{
    /// <summary>The Windows pieces (X part 10: the conversation is Craft's own read-only text surface, no selectable labels).</summary>
    public WindowsShellPlatform()
    {
        WindowsControlStyles.Configure();                            // the owner's notes 10 and 12, before any control is made
        ComposerKeys.Configure();                                    // Enter sends, Shift+Enter a line, Escape leaves the composer
        EntryKeys.Configure();                                       // phase X: the palette's and find's keys before their text boxes
    }

    /// <inheritdoc />
    public IFileDialogs FileDialogs { get; } = new WindowsFileDialogs();

    /// <inheritdoc />
    public ITerminalHost? TerminalHost { get; } = new ConPtyTerminalHost();

    /// <inheritdoc />
    public IDrawnFonts? DrawnFonts { get; } = new WindowsDrawnFonts();

    /// <inheritdoc />
    public IDrawnTextLayouts? TextLayouts { get; } = new Win2DTextLayouts();

    /// <inheritdoc />
    public bool IsReady(MauiWindow window) => window?.Handler?.PlatformView is NativeWindow { Content: UIElement };

    /// <inheritdoc />
    public Craft.Core.Abstractions.ICrashRecordSource? CrashRecords { get; } = new WindowsEventLogCrashRecords();

    /// <inheritdoc />
    public IShellFiles? ShellFiles { get; } = new WindowsShellFiles();

    /// <inheritdoc />
    public IPointerCursor? PointerCursor { get; } = new WindowsPointerCursor();

    /// <inheritdoc />
    public ITerminalProfileSource? TerminalProfiles { get; } = WindowsTerminalProfiles.Create();

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> DroppedPathsAsync(Microsoft.Maui.Controls.DropEventArgs drop)
    {
        ArgumentNullException.ThrowIfNull(drop);
        if (drop.PlatformArgs?.DragEventArgs is not { } args || !args.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            return [];
        }

        var deferral = args.GetDeferral();                       // the data stays readable across the await
        try
        {
            var items = await args.DataView.GetStorageItemsAsync();
            return [.. items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p))];
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <inheritdoc />
    public bool DropCopies(Microsoft.Maui.Controls.DropEventArgs drop) =>
        drop?.PlatformArgs?.DragEventArgs is { } args && (args.Modifiers & global::Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control) != 0;

    /// <inheritdoc />
    public IDisposable RouteKeys(MauiWindow window, IEditorKeyHandler handler)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(handler);
        if (window.Handler?.PlatformView is not NativeWindow { Content: UIElement root })
        {
            throw new InvalidOperationException("The window has no WinUI content yet: route the keys once the page has appeared.");
        }

        return new KeyRoute(root, handler);
    }

    /// <inheritdoc />
    public IDisposable GuardClose(MauiWindow window, Func<bool> hasUnsaved, Func<Task<bool>> confirm)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(hasUnsaved);
        ArgumentNullException.ThrowIfNull(confirm);
        if (window.Handler?.PlatformView is not NativeWindow native)
        {
            throw new InvalidOperationException("The window has no WinUI window yet: guard it once the page has appeared.");
        }

        return new CloseGuard(window, native.AppWindow, hasUnsaved, confirm);
    }

    /// <summary>The native window of the app's first window (the dialogs' owner).</summary>
    internal static nint MainWindowHandle()
    {
        var windows = MauiApplication.Current?.Windows;
        if (windows is not { Count: > 0 } || windows[0].Handler?.PlatformView is not NativeWindow native)
        {
            throw new InvalidOperationException("The app has no window to own the dialog.");
        }

        return WinRT.Interop.WindowNative.GetWindowHandle(native);
    }

    private sealed class KeyRoute : IDisposable
    {
        private readonly UIElement _root;
        private readonly IEditorKeyHandler _handler;
        private readonly KeyEventHandler _onKeyDown;
        private bool _attached;

        public KeyRoute(UIElement root, IEditorKeyHandler handler)
        {
            _root = root;
            _handler = handler;
            _onKeyDown = OnKeyDown;
            root.AddHandler(UIElement.KeyDownEvent, _onKeyDown, false);
            _attached = true;
        }

        public void Dispose()
        {
            if (_attached)
            {
                _attached = false;
                _root.RemoveHandler(UIElement.KeyDownEvent, _onKeyDown);
            }
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                var key = WindowsTextInputAdapter.Map(e.Key);
                if (key == EditorKey.None)
                {
                    return;
                }

                // Every key no control took goes to the handler, which decides (the shell's DialogQueue.Before: an open Craft
                // dialog takes Escape, Enter, Tab and every other key; with none open only chords and function keys reach the
                // key bindings, so a plain key typed into a text box stays that box's).
                var modifiers = WindowsTextInputAdapter.Modifiers();
                if (_handler.HandleKey(key, modifiers, null))
                {
                    e.Handled = true;
                }
            }
#pragma warning disable CA1031 // Nothing may leave a WinUI callback (the owner's note 20).
            catch (Exception error)
#pragma warning restore CA1031
            {
                e.Handled = true;
                CallbackGuard.Report("the window: a key no control took", error);
            }
        }
    }

    private sealed class CloseGuard : IDisposable
    {
        private readonly MauiWindow _window;
        private readonly AppWindow _appWindow;
        private readonly Func<bool> _hasUnsaved;
        private readonly Func<Task<bool>> _confirm;
        private bool _confirmed;
        private bool _asking;
        private bool _attached;

        public CloseGuard(MauiWindow window, AppWindow appWindow, Func<bool> hasUnsaved, Func<Task<bool>> confirm)
        {
            _window = window;
            _appWindow = appWindow;
            _hasUnsaved = hasUnsaved;
            _confirm = confirm;
            appWindow.Closing += OnClosing;
            _attached = true;
        }

        public void Dispose()
        {
            if (_attached)
            {
                _attached = false;
                _appWindow.Closing -= OnClosing;
            }
        }

        private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
        {
            if (_confirmed || !_hasUnsaved())
            {
                return;
            }

            e.Cancel = true;
            if (_asking)
            {
                return;
            }

            _asking = true;
            try
            {
                if (await _confirm())
                {
                    _confirmed = true;
                    MauiApplication.Current?.CloseWindow(_window);
                }
            }
#pragma warning disable CA1031 // An async void WinUI callback: nothing may leave it (the owner's note 20).
            catch (Exception error)
#pragma warning restore CA1031
            {
                CallbackGuard.Report("the window: closing", error);
            }
            finally
            {
                _asking = false;
            }
        }
    }
}

/// <summary>
/// The Windows file dialogs: the Shell's own folder, open and save dialogs (<see cref="ShellFileDialog"/>), modal to the
/// app's window, each opening on a real folder (the one asked for, else the user's Documents folder), so "Select folder"
/// works at once and a new file's save starts in its own folder. Run on the UI thread: the dialog's modal loop keeps the
/// window painting while it is open.
/// </summary>
public sealed class WindowsFileDialogs : IFileDialogs
{
    /// <inheritdoc />
    public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShellFileDialog.PickFolder(WindowsShellPlatform.MainWindowHandle(), Start(startFolder)));

    /// <inheritdoc />
    public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShellFileDialog.PickFile(WindowsShellPlatform.MainWindowHandle(), Start(startFolder)));

    /// <inheritdoc />
    public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suggestedName);
        return Task.FromResult(ShellFileDialog.PickSave(WindowsShellPlatform.MainWindowHandle(), Start(folder), suggestedName));
    }

    // The folder asked for when it exists, else the user's Documents folder, else their profile: always a real folder.
    private static string Start(string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            return Path.GetFullPath(folder);
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
/// <summary>
/// File Explorer and the Recycle Bin for the shell's Reveal and Delete (phase X), each off the UI thread: Reveal starts
/// explorer.exe with the item selected; Delete moves the item to the Recycle Bin (Microsoft.VisualBasic's FileSystem, the
/// shell's own recycle, no dialog but an error's).
/// </summary>
public sealed class WindowsShellFiles : IShellFiles
{
    /// <inheritdoc />
    public string RevealTitle => "Reveal in File Explorer";

    /// <inheritdoc />
    public string? TrashName => "the Recycle Bin";

    /// <inheritdoc />
    public Task RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = System.IO.Path.GetFullPath(path);
        return Task.Run(() =>
        {
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + full + "\"") { UseShellExecute = false };
            using var process = System.Diagnostics.Process.Start(start);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> TrashAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = System.IO.Path.GetFullPath(path);
        return Task.Run(() =>
        {
            if (System.IO.Directory.Exists(full))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(full, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            return true;
        }, cancellationToken);
    }
}
#endif
