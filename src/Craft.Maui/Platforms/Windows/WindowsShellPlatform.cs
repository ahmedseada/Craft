// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using Craft.Abstractions;
using Craft.Editor.Abstractions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
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
    /// <inheritdoc />
    public IFileDialogs FileDialogs { get; } = new WindowsFileDialogs();

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
            var key = WindowsTextInputAdapter.Map(e.Key);
            if (key == EditorKey.None)
            {
                return;
            }

            // Only chords and function keys: a plain key typed into a text box is that box's.
            var modifiers = WindowsTextInputAdapter.Modifiers();
            bool chord = (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;
            bool function = key is EditorKey.F3 || (key >= EditorKey.F1 && key <= EditorKey.F12);
            if ((chord || function) && _handler.HandleKey(key, modifiers, null))
            {
                e.Handled = true;
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
            finally
            {
                _asking = false;
            }
        }
    }
}

/// <summary>
/// The Windows file dialogs: the Windows App SDK's folder, open and save pickers, owned by the app's window
/// (<c>InitializeWithWindow</c>, which an unpackaged app needs). The pickers do not run in a process started as
/// administrator (a Windows limit), and the save picker cannot start in a given folder (it starts where the user last saved).
/// </summary>
public sealed class WindowsFileDialogs : IFileDialogs
{
    // The save picker needs at least one file type; the suggested name's own comes first.
    private static readonly string[] SaveTypes = [".cs", ".csx", ".txt", ".md", ".json", ".xml", ".csproj", ".props", ".targets"];

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowsShellPlatform.MainWindowHandle());
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    /// <inheritdoc />
    public async Task<string?> PickFileAsync(CancellationToken cancellationToken = default)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowsShellPlatform.MainWindowHandle());
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <inheritdoc />
    public async Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suggestedName);
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
        };
        var extension = Path.GetExtension(suggestedName);
        if (extension.Length > 1)
        {
            picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant() + " file", [extension]);
        }

        foreach (var type in SaveTypes)
        {
            if (!type.Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                picker.FileTypeChoices.Add(type.TrimStart('.').ToUpperInvariant() + " file", [type]);
            }
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowsShellPlatform.MainWindowHandle());
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
#endif
