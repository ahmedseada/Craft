// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;

namespace Craft.Editor.Terminal;

/// <summary>
/// A key as the bytes a VT terminal (xterm) sends for it: the arrows, Home and End (their application form after DECCKM),
/// Insert, Delete, Page Up and Page Down, F1-F12, each with xterm's modifier parameter (";2" Shift, ";3" Alt, ";5" Ctrl
/// and their sums); Enter, Tab, Shift+Tab, Backspace and Escape; Ctrl with a letter as its control character (Ctrl+Space
/// NUL); Alt as an ESC before the key. Ctrl with Alt and a letter is AltGr on many keyboards: it is left to arrive as
/// text. A plain letter, digit or space is text too, and so is anything typed (<see cref="EncodeText"/>).
/// </summary>
public static class TerminalKeys
{
    /// <summary>The longest sequence a key writes.</summary>
    public const int MaxLength = 16;

    /// <summary>
    /// Writes <paramref name="key"/> with <paramref name="modifiers"/> into <paramref name="destination"/> (at least
    /// <see cref="MaxLength"/> long): the bytes written, 0 when the key is not the terminal's (it arrives as text, or the
    /// shell has no sequence for it).
    /// </summary>
    public static int Encode(EditorKey key, KeyModifiers modifiers, bool applicationCursorKeys, Span<byte> destination)
    {
        bool ctrl = (modifiers & KeyModifiers.Control) != 0;
        bool alt = (modifiers & KeyModifiers.Alt) != 0;
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        int parameter = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
        switch (key)
        {
            case EditorKey.Up:
                return Cursor('A', parameter, applicationCursorKeys, destination);
            case EditorKey.Down:
                return Cursor('B', parameter, applicationCursorKeys, destination);
            case EditorKey.Right:
                return Cursor('C', parameter, applicationCursorKeys, destination);
            case EditorKey.Left:
                return Cursor('D', parameter, applicationCursorKeys, destination);
            case EditorKey.Home:
                return Cursor('H', parameter, applicationCursorKeys, destination);
            case EditorKey.End:
                return Cursor('F', parameter, applicationCursorKeys, destination);
            case EditorKey.Insert:
                return Tilde(2, parameter, destination);
            case EditorKey.Delete:
                return Tilde(3, parameter, destination);
            case EditorKey.PageUp:
                return Tilde(5, parameter, destination);
            case EditorKey.PageDown:
                return Tilde(6, parameter, destination);
            case EditorKey.F1:
                return Function('P', parameter, destination);
            case EditorKey.F2:
                return Function('Q', parameter, destination);
            case EditorKey.F3:
                return Function('R', parameter, destination);
            case EditorKey.F4:
                return Function('S', parameter, destination);
            case EditorKey.F5:
                return Tilde(15, parameter, destination);
            case EditorKey.F6:
                return Tilde(17, parameter, destination);
            case EditorKey.F7:
                return Tilde(18, parameter, destination);
            case EditorKey.F8:
                return Tilde(19, parameter, destination);
            case EditorKey.F9:
                return Tilde(20, parameter, destination);
            case EditorKey.F10:
                return Tilde(21, parameter, destination);
            case EditorKey.F11:
                return Tilde(23, parameter, destination);
            case EditorKey.F12:
                return Tilde(24, parameter, destination);
            case EditorKey.Enter:
                return Prefixed(alt, (byte)'\r', destination);
            case EditorKey.Tab when shift:
                "\e[Z"u8.CopyTo(destination);
                return 3;
            case EditorKey.Tab:
                return Prefixed(alt, (byte)'\t', destination);
            case EditorKey.Backspace:
                return Prefixed(alt, ctrl ? (byte)0x08 : (byte)0x7F, destination);
            case EditorKey.Escape:
                return Prefixed(alt, 0x1B, destination);
            case EditorKey.Space when ctrl && !alt:
                destination[0] = 0;
                return 1;
            case EditorKey.Space when alt && !ctrl:
                return Prefixed(true, (byte)' ', destination);
        }

        if (LetterOf(key) is var letter && letter != 0)
        {
            if (ctrl && alt)
            {
                return 0; // AltGr on many layouts: the character arrives as text.
            }

            if (ctrl)
            {
                destination[0] = (byte)(letter - 'a' + 1);
                return 1;
            }

            if (alt)
            {
                return Prefixed(true, shift ? (byte)char.ToUpperInvariant((char)letter) : letter, destination);
            }

            return 0;
        }

        if (key is >= EditorKey.D0 and <= EditorKey.D9 && alt && !ctrl && !shift)
        {
            return Prefixed(true, (byte)('0' + (key - EditorKey.D0)), destination);
        }

        return 0;
    }

    /// <summary>The bytes of typed text (UTF-8).</summary>
    public static byte[] EncodeText(string text) => Encoding.UTF8.GetBytes(text ?? "");

    /// <summary>
    /// Pasted text as the shell takes it: line breaks as "\r" (Enter), inside ESC[200~ and ESC[201~ when the shell asked
    /// for bracketed paste (an ESC[201~ inside the text is taken out, so a paste cannot end its own brackets).
    /// </summary>
    public static byte[] EncodePaste(string text, bool bracketed)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        if (!bracketed)
        {
            return Encoding.UTF8.GetBytes(normalized);
        }

        normalized = normalized.Replace("\e[201~", "", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes("\e[200~" + normalized + "\e[201~");
    }

    /// <summary>The lowercase letter of a letter key, or 0.</summary>
    private static byte LetterOf(EditorKey key) => key switch
    {
        EditorKey.A => (byte)'a',
        EditorKey.B => (byte)'b',
        EditorKey.C => (byte)'c',
        EditorKey.D => (byte)'d',
        EditorKey.E => (byte)'e',
        EditorKey.F => (byte)'f',
        EditorKey.G => (byte)'g',
        EditorKey.H => (byte)'h',
        EditorKey.I => (byte)'i',
        EditorKey.J => (byte)'j',
        EditorKey.K => (byte)'k',
        EditorKey.L => (byte)'l',
        EditorKey.M => (byte)'m',
        EditorKey.N => (byte)'n',
        EditorKey.O => (byte)'o',
        EditorKey.P => (byte)'p',
        EditorKey.Q => (byte)'q',
        EditorKey.R => (byte)'r',
        EditorKey.S => (byte)'s',
        EditorKey.T => (byte)'t',
        EditorKey.U => (byte)'u',
        EditorKey.V => (byte)'v',
        EditorKey.W => (byte)'w',
        EditorKey.X => (byte)'x',
        EditorKey.Y => (byte)'y',
        EditorKey.Z => (byte)'z',
        _ => 0,
    };

    private static int Prefixed(bool alt, byte value, Span<byte> destination)
    {
        if (alt)
        {
            destination[0] = 0x1B;
            destination[1] = value;
            return 2;
        }

        destination[0] = value;
        return 1;
    }

    // "ESC [ A" (or "ESC O A" in application mode); with modifiers "ESC [ 1 ; m A".
    private static int Cursor(char final, int parameter, bool application, Span<byte> destination)
    {
        destination[0] = 0x1B;
        if (parameter == 1)
        {
            destination[1] = application ? (byte)'O' : (byte)'[';
            destination[2] = (byte)final;
            return 3;
        }

        destination[1] = (byte)'[';
        destination[2] = (byte)'1';
        destination[3] = (byte)';';
        destination[4] = (byte)('0' + parameter);
        destination[5] = (byte)final;
        return 6;
    }

    // F1-F4: "ESC O P"; with modifiers "ESC [ 1 ; m P".
    private static int Function(char final, int parameter, Span<byte> destination) => Cursor(final, parameter, application: true, destination);

    // "ESC [ n ~"; with modifiers "ESC [ n ; m ~".
    private static int Tilde(int number, int parameter, Span<byte> destination)
    {
        int at = 0;
        destination[at++] = 0x1B;
        destination[at++] = (byte)'[';
        if (number >= 10)
        {
            destination[at++] = (byte)('0' + (number / 10));
        }

        destination[at++] = (byte)('0' + (number % 10));
        if (parameter != 1)
        {
            destination[at++] = (byte)';';
            destination[at++] = (byte)('0' + parameter);
        }

        destination[at++] = (byte)'~';
        return at;
    }
}
