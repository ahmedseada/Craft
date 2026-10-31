// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using Craft.Abstractions;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using VirtualKey = Windows.System.VirtualKey;

namespace Craft.Maui.WinUI;

/// <summary>
/// The Windows keyboard for the editor (WinUI 3): the GraphicsView's native view is made focusable and focused when
/// pressed; its key presses (PreviewKeyDown, so Tab never moves the focus when the editor takes it) become
/// <see cref="IEditorInputSink.OnKey"/> and its characters (CharacterReceived, surrogate pairs joined, control characters
/// dropped, AltGr's Ctrl+Alt characters kept) become <see cref="IEditorInputSink.OnText"/>.
/// </summary>
public sealed class WindowsTextInputAdapter : ITextInputAdapter
{
    /// <inheritdoc />
    public IDisposable Attach(object platformView, IEditorInputSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (platformView is not UIElement element)
        {
            throw new ArgumentException($"The Windows input adapter attaches to a WinUI UIElement, not {platformView?.GetType().FullName ?? "null"}.", nameof(platformView));
        }

        return new Connection(element, sink);
    }

    /// <summary>The modifier keys held now on this thread.</summary>
    public static KeyModifiers Modifiers()
    {
        var modifiers = KeyModifiers.None;
        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (IsDown(VirtualKey.Control))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= KeyModifiers.Alt;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    private static EditorKey Map(VirtualKey key) => key switch
    {
        VirtualKey.Left => EditorKey.Left,
        VirtualKey.Right => EditorKey.Right,
        VirtualKey.Up => EditorKey.Up,
        VirtualKey.Down => EditorKey.Down,
        VirtualKey.Home => EditorKey.Home,
        VirtualKey.End => EditorKey.End,
        VirtualKey.PageUp => EditorKey.PageUp,
        VirtualKey.PageDown => EditorKey.PageDown,
        VirtualKey.Back => EditorKey.Backspace,
        VirtualKey.Delete => EditorKey.Delete,
        VirtualKey.Enter => EditorKey.Enter,
        VirtualKey.Tab => EditorKey.Tab,
        VirtualKey.Escape => EditorKey.Escape,
        VirtualKey.Insert => EditorKey.Insert,
        VirtualKey.F3 => EditorKey.F3,
        VirtualKey.A => EditorKey.A,
        VirtualKey.C => EditorKey.C,
        VirtualKey.F => EditorKey.F,
        VirtualKey.H => EditorKey.H,
        VirtualKey.S => EditorKey.S,
        VirtualKey.V => EditorKey.V,
        VirtualKey.X => EditorKey.X,
        VirtualKey.Y => EditorKey.Y,
        VirtualKey.Z => EditorKey.Z,
        // Phase K: the keys left free for key bindings (EditorKeyBindings).
        VirtualKey.B => EditorKey.B,
        VirtualKey.D => EditorKey.D,
        VirtualKey.E => EditorKey.E,
        VirtualKey.G => EditorKey.G,
        VirtualKey.I => EditorKey.I,
        VirtualKey.J => EditorKey.J,
        VirtualKey.K => EditorKey.K,
        VirtualKey.L => EditorKey.L,
        VirtualKey.M => EditorKey.M,
        VirtualKey.N => EditorKey.N,
        VirtualKey.O => EditorKey.O,
        VirtualKey.P => EditorKey.P,
        VirtualKey.Q => EditorKey.Q,
        VirtualKey.R => EditorKey.R,
        VirtualKey.T => EditorKey.T,
        VirtualKey.U => EditorKey.U,
        VirtualKey.W => EditorKey.W,
        VirtualKey.Number0 => EditorKey.D0,
        VirtualKey.Number1 => EditorKey.D1,
        VirtualKey.Number2 => EditorKey.D2,
        VirtualKey.Number3 => EditorKey.D3,
        VirtualKey.Number4 => EditorKey.D4,
        VirtualKey.Number5 => EditorKey.D5,
        VirtualKey.Number6 => EditorKey.D6,
        VirtualKey.Number7 => EditorKey.D7,
        VirtualKey.Number8 => EditorKey.D8,
        VirtualKey.Number9 => EditorKey.D9,
        VirtualKey.F1 => EditorKey.F1,
        VirtualKey.F2 => EditorKey.F2,
        VirtualKey.F4 => EditorKey.F4,
        VirtualKey.F5 => EditorKey.F5,
        VirtualKey.F6 => EditorKey.F6,
        VirtualKey.F7 => EditorKey.F7,
        VirtualKey.F8 => EditorKey.F8,
        VirtualKey.F9 => EditorKey.F9,
        VirtualKey.F10 => EditorKey.F10,
        VirtualKey.F11 => EditorKey.F11,
        VirtualKey.F12 => EditorKey.F12,
        _ => EditorKey.None,
    };

    private sealed class Connection : IDisposable, IKeyModifierSource
    {
        private readonly UIElement _element;
        private readonly IEditorInputSink _sink;
        private readonly PointerEventHandler _pointerPressed;
        private char _highSurrogate;
        private bool _attached;

        public Connection(UIElement element, IEditorInputSink sink)
        {
            _element = element;
            _sink = sink;
            _pointerPressed = OnPointerPressed;
            if (element is Control control)
            {
                control.IsTabStop = true;
            }

            element.PreviewKeyDown += OnPreviewKeyDown;
            element.CharacterReceived += OnCharacterReceived;
            element.AddHandler(UIElement.PointerPressedEvent, _pointerPressed, true);
            _attached = true;
        }

        public KeyModifiers CurrentModifiers => Modifiers();

        public void Dispose()
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _element.PreviewKeyDown -= OnPreviewKeyDown;
            _element.CharacterReceived -= OnCharacterReceived;
            _element.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressed);
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_element is Control control)
            {
                control.Focus(FocusState.Pointer);
            }
        }

        private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var key = Map(e.Key);
            if (key != EditorKey.None && _sink.OnKey(key, Modifiers()))
            {
                e.Handled = true;
            }
        }

        private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
        {
            char c = e.Character;
            if (char.IsControl(c))
            {
                return;
            }

            var modifiers = Modifiers();
            if ((modifiers & KeyModifiers.Control) != 0 && (modifiers & KeyModifiers.Alt) == 0)
            {
                return;
            }

            e.Handled = true;
            if (char.IsHighSurrogate(c))
            {
                _highSurrogate = c;
                return;
            }

            if (char.IsLowSurrogate(c))
            {
                if (_highSurrogate != '\0')
                {
                    _sink.OnText(string.Create(2, (_highSurrogate, c), static (span, pair) =>
                    {
                        span[0] = pair.Item1;
                        span[1] = pair.Item2;
                    }));
                }

                _highSurrogate = '\0';
                return;
            }

            _highSurrogate = '\0';
            _sink.OnText(c.ToString());
        }
    }
}
#endif
