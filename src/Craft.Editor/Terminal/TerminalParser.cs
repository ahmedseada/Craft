// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace Craft.Editor.Terminal;

/// <summary>
/// The parser of <see cref="TerminalScreen"/>: a DEC/ECMA-48 state machine over the shell's bytes (after Paul Williams'),
/// UTF-8 decoded on the way (a sequence split across reads is kept; a bad one is drawn as U+FFFD), runs of printable ASCII
/// written a row stretch at a time. It knows C0 controls; ESC sequences (DECSC/DECRC, IND, NEL, RI, HTS, RIS, the
/// keypad modes, G0/G1 with DEC line drawing, DECALN); CSI: ICH, CUU, CUD, CUF, CUB, CNL, CPL, CHA, CUP, CHT, ED, EL, IL,
/// DL, DCH, SU, SD, ECH, CBT, HPA, HPR, REP, DA, VPA, VPR, HVP, TBC, SM/RM (insert) and DECSET/DECRST (1 cursor keys,
/// 6 origin, 7 wrap, 25 cursor, 47/1047/1049 alternate screen, 1048 cursor, 1000-1003 mouse kept, 2004 bracketed paste),
/// SGR (16, 256 and true colours, ';' and ':' forms, bold, faint, italic, underline, blink, inverse, hidden, strike),
/// DSR (5, 6), DECSTBM, SCOSC/SCORC, DECSCUSR, DECSTR; OSC 0 and 2 (title), 7 and 9;9 (working folder), the rest
/// ignored; DCS, SOS, PM and APC strings skipped to their terminator. Strings are capped, so no input grows memory.
/// </summary>
public sealed partial class TerminalScreen
{
    private const int MaxParameters = 32;
    private const int MaxParameter = 65_535;
    private const int MaxOsc = 4096;
    private const int MaxTitle = 256;

    // Every byte that is not printable ASCII: the ground state's fast path stops at the first of them.
    private static readonly SearchValues<byte> NotPrintable = SearchValues.Create(MakeNotPrintable());

    private readonly int[] _params = new int[MaxParameters];
    private readonly byte[] _osc = new byte[MaxOsc];
    private State _state;
    private int _paramCount;
    private int _current;
    private ulong _subParameters;
    private bool _nextIsSub;
    private byte _private;
    private byte _intermediate;
    private int _oscLength;
    private int _utf8Need;
    private int _utf8Length;
    private int _codePoint;
    private bool _insertMode;
    private bool _originMode;

    private enum State : byte
    {
        Ground,
        Escape,
        EscapeIntermediate,
        Csi,
        CsiIgnore,
        Osc,
        OscEscape,
        StringIgnore,
        StringIgnoreEscape,
    }

    /// <summary>Parses <paramref name="data"/> (the shell's output, any split) into the screen.</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i < data.Length)
        {
            if (_state == State.Ground && _utf8Need == 0)
            {
                var rest = data[i..];
                int run = rest.IndexOfAny(NotPrintable);
                if (run < 0)
                {
                    run = rest.Length;
                }

                if (run > 0)
                {
                    PrintAscii(rest[..run]);
                    i += run;
                    continue;
                }
            }

            Step(data[i]);
            i++;
        }

        Changes++;
    }

    private static byte[] MakeNotPrintable()
    {
        var bytes = new List<byte>(161);
        for (int b = 0; b < 256; b++)
        {
            if (b is < 0x20 or > 0x7E)
            {
                bytes.Add((byte)b);
            }
        }

        return [.. bytes];
    }

    private void Step(byte b)
    {
        switch (_state)
        {
            case State.Ground:
                Ground(b);
                break;
            case State.Escape:
                Escape(b);
                break;
            case State.EscapeIntermediate:
                if (b < 0x20)
                {
                    Control(b);
                }
                else if (b < 0x30)
                {
                    _intermediate = b;
                }
                else
                {
                    _state = State.Ground;
                    if (b < 0x7F)
                    {
                        EscapeDispatch(b);
                    }
                }

                break;
            case State.Csi:
                Csi(b);
                break;
            case State.CsiIgnore:
                if (b < 0x20)
                {
                    Control(b);
                }
                else if (b is >= 0x40 and <= 0x7E)
                {
                    _state = State.Ground;
                }

                break;
            case State.Osc:
                if (b == 0x07)
                {
                    OscDispatch();
                    _state = State.Ground;
                }
                else if (b == 0x1B)
                {
                    _state = State.OscEscape;
                }
                else if (b is 0x18 or 0x1A)
                {
                    _state = State.Ground;
                }
                else if (b >= 0x20 && _oscLength < MaxOsc)
                {
                    _osc[_oscLength++] = b;
                }

                break;
            case State.OscEscape:
                OscDispatch();
                _state = State.Ground;
                if (b != (byte)'\\')
                {
                    BeginEscape();
                    Escape(b);
                }

                break;
            case State.StringIgnore:
                if (b == 0x1B)
                {
                    _state = State.StringIgnoreEscape;
                }
                else if (b is 0x18 or 0x1A)
                {
                    _state = State.Ground;
                }

                break;
            case State.StringIgnoreEscape:
                _state = State.Ground;
                if (b != (byte)'\\')
                {
                    BeginEscape();
                    Escape(b);
                }

                break;
        }
    }

    private void Ground(byte b)
    {
        if (_utf8Need > 0)
        {
            if ((b & 0xC0) == 0x80)
            {
                _codePoint = (_codePoint << 6) | (b & 0x3F);
                if (--_utf8Need == 0)
                {
                    int code = _codePoint;
                    bool valid = _utf8Length switch
                    {
                        2 => code >= 0x80,
                        3 => code >= 0x800 && code is < 0xD800 or > 0xDFFF,
                        _ => code is >= 0x10000 and <= 0x10FFFF,
                    };
                    Print(valid ? code : 0xFFFD);
                }

                return;
            }

            // A sequence cut short: its replacement, then the byte that cut it.
            _utf8Need = 0;
            Print(0xFFFD);
        }

        if (b < 0x20)
        {
            Control(b);
        }
        else if (b < 0x7F)
        {
            Print(b);
        }
        else if (b == 0x7F)
        {
            // DEL draws nothing.
        }
        else if (b is >= 0xC2 and <= 0xDF)
        {
            Begin(b & 0x1F, 2);
        }
        else if (b is >= 0xE0 and <= 0xEF)
        {
            Begin(b & 0x0F, 3);
        }
        else if (b is >= 0xF0 and <= 0xF4)
        {
            Begin(b & 0x07, 4);
        }
        else
        {
            Print(0xFFFD);
        }
    }

    private void Begin(int bits, int length)
    {
        _codePoint = bits;
        _utf8Length = length;
        _utf8Need = length - 1;
    }

    private void Control(byte b)
    {
        switch (b)
        {
            case 0x07:
                Bells++;
                break;
            case 0x08:
                Backspace();
                break;
            case 0x09:
                Tab(1);
                break;
            case 0x0A or 0x0B or 0x0C:
                LineFeed();
                break;
            case 0x0D:
                CarriageReturn();
                break;
            case 0x0E:
                _shifted = true;
                break;
            case 0x0F:
                _shifted = false;
                break;
            case 0x18 or 0x1A:
                _state = State.Ground;
                break;
            case 0x1B:
                BeginEscape();
                break;
        }
    }

    private void BeginEscape()
    {
        _state = State.Escape;
        _intermediate = 0;
    }

    private void Escape(byte b)
    {
        if (b < 0x20)
        {
            Control(b);
            return;
        }

        if (b < 0x30)
        {
            _intermediate = b;
            _state = State.EscapeIntermediate;
            return;
        }

        switch (b)
        {
            case (byte)'[':
                _state = State.Csi;
                _paramCount = 0;
                _current = 0;
                _subParameters = 0;
                _nextIsSub = false;
                _private = 0;
                _intermediate = 0;
                return;
            case (byte)']':
                _state = State.Osc;
                _oscLength = 0;
                return;
            case (byte)'P' or (byte)'X' or (byte)'^' or (byte)'_':
                _state = State.StringIgnore;
                return;
        }

        _state = State.Ground;
        if (b < 0x7F)
        {
            EscapeDispatch(b);
        }
    }

    private void EscapeDispatch(byte final)
    {
        switch (_intermediate)
        {
            case 0:
                switch (final)
                {
                    case (byte)'7':
                        SaveCursor();
                        break;
                    case (byte)'8':
                        RestoreCursor();
                        break;
                    case (byte)'D':
                        LineFeed();
                        break;
                    case (byte)'E':
                        CarriageReturn();
                        LineFeed();
                        break;
                    case (byte)'M':
                        ReverseIndex();
                        break;
                    case (byte)'H':
                        _tabs[_col] = true;
                        break;
                    case (byte)'c':
                        Reset();
                        break;
                    case (byte)'=':
                        ApplicationKeypad = true;
                        break;
                    case (byte)'>':
                        ApplicationKeypad = false;
                        break;
                }

                break;
            case (byte)'(':
                _g0Graphics = final == (byte)'0';
                break;
            case (byte)')':
                _g1Graphics = final == (byte)'0';
                break;
            case (byte)'#' when final == (byte)'8':
                FillWithE();
                break;
        }
    }

    private void Csi(byte b)
    {
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            _current = Math.Min((_current * 10) + (b - '0'), MaxParameter);
        }
        else if (b is (byte)';' or (byte)':')
        {
            PushParameter();
            _nextIsSub = b == (byte)':';
        }
        else if (b < 0x20)
        {
            Control(b);
        }
        else if (b is >= 0x3C and <= 0x3F)
        {
            if (_paramCount == 0 && _current == 0 && _private == 0)
            {
                _private = b;
            }
            else
            {
                _state = State.CsiIgnore;
            }
        }
        else if (b < 0x30)
        {
            _intermediate = b;
        }
        else if (b is >= 0x40 and <= 0x7E)
        {
            PushParameter();
            _state = State.Ground;
            CsiDispatch(b);
        }
        else if (b != 0x7F)
        {
            _state = State.CsiIgnore;
        }
    }

    private void PushParameter()
    {
        if (_paramCount < MaxParameters)
        {
            if (_nextIsSub)
            {
                _subParameters |= 1UL << _paramCount;
            }

            _params[_paramCount++] = _current;
        }

        _current = 0;
        _nextIsSub = false;
    }

    /// <summary>Parameter <paramref name="index"/>, or <paramref name="fallback"/> when it is missing or 0.</summary>
    private int P(int index, int fallback = 1) => index < _paramCount && _params[index] != 0 ? _params[index] : fallback;

    /// <summary>Parameter <paramref name="index"/> as given (0 when missing).</summary>
    private int Raw(int index) => index < _paramCount ? _params[index] : 0;

    private bool IsSub(int index) => index < _paramCount && (_subParameters & (1UL << index)) != 0;

    private void CsiDispatch(byte final)
    {
        if (_private == (byte)'?')
        {
            switch (final)
            {
                case (byte)'h':
                    SetPrivateModes(true);
                    break;
                case (byte)'l':
                    SetPrivateModes(false);
                    break;
                case (byte)'J':
                    EraseInDisplay(Raw(0));
                    break;
                case (byte)'K':
                    EraseInLine(Raw(0));
                    break;
            }

            return;
        }

        if (_private == (byte)'>')
        {
            if (final == (byte)'c')
            {
                Reply("\e[>0;10;1c"u8);
            }

            return;
        }

        if (_private != 0)
        {
            return;
        }

        if (_intermediate == (byte)' ')
        {
            if (final == (byte)'q')
            {
                CursorStyle = Raw(0) switch
                {
                    3 or 4 => TerminalCursorStyle.Underline,
                    5 or 6 => TerminalCursorStyle.Bar,
                    _ => TerminalCursorStyle.Block,
                };
            }

            return;
        }

        if (_intermediate == (byte)'!')
        {
            if (final == (byte)'p')
            {
                SoftReset();
            }

            return;
        }

        if (_intermediate != 0)
        {
            return;
        }

        switch (final)
        {
            case (byte)'@':
                InsertBlanks(ScreenRow(_row), _col, P(0));
                _pendingWrap = false;
                break;
            case (byte)'A':
                MoveUp(P(0));
                break;
            case (byte)'B' or (byte)'e':
                MoveDown(P(0));
                break;
            case (byte)'C' or (byte)'a':
                _col = Math.Min(Columns - 1, _col + P(0));
                _pendingWrap = false;
                break;
            case (byte)'D':
                _col = Math.Max(0, _col - P(0));
                _pendingWrap = false;
                break;
            case (byte)'E':
                MoveDown(P(0));
                _col = 0;
                break;
            case (byte)'F':
                MoveUp(P(0));
                _col = 0;
                break;
            case (byte)'G' or (byte)'`':
                _col = Math.Clamp(P(0) - 1, 0, Columns - 1);
                _pendingWrap = false;
                break;
            case (byte)'H' or (byte)'f':
                MoveTo(P(0) - 1, P(1) - 1);
                break;
            case (byte)'I':
                Tab(P(0));
                break;
            case (byte)'J':
                EraseInDisplay(Raw(0));
                break;
            case (byte)'K':
                EraseInLine(Raw(0));
                break;
            case (byte)'L':
                InsertLines(P(0));
                break;
            case (byte)'M':
                DeleteLines(P(0));
                break;
            case (byte)'P':
                DeleteCells(P(0));
                break;
            case (byte)'S':
                ScrollUp(P(0));
                break;
            case (byte)'T':
                ScrollDown(P(0));
                break;
            case (byte)'X':
                EraseCells(ScreenRow(_row), _col, _col + P(0));
                _pendingWrap = false;
                break;
            case (byte)'Z':
                BackTab(P(0));
                break;
            case (byte)'b':
                for (int i = Math.Min(P(0), Columns * Rows); i > 0; i--)
                {
                    Print(_lastPrinted);
                }

                break;
            case (byte)'c':
                Reply("\e[?1;2c"u8);
                break;
            case (byte)'d':
                _row = Math.Clamp((_originMode ? _top : 0) + P(0) - 1, 0, Rows - 1);
                _pendingWrap = false;
                break;
            case (byte)'g':
                if (Raw(0) == 3)
                {
                    Array.Clear(_tabs);
                }
                else if (Raw(0) == 0)
                {
                    _tabs[_col] = false;
                }

                break;
            case (byte)'h' or (byte)'l':
                for (int i = 0; i < _paramCount; i++)
                {
                    if (_params[i] == 4)
                    {
                        _insertMode = final == (byte)'h';
                    }
                }

                break;
            case (byte)'m':
                SelectGraphicRendition();
                break;
            case (byte)'n':
                ReportStatus(Raw(0));
                break;
            case (byte)'r':
                SetRegion(Raw(0), Raw(1));
                break;
            case (byte)'s':
                SaveCursor();
                break;
            case (byte)'u':
                RestoreCursor();
                break;
        }
    }

    private void SetPrivateModes(bool on)
    {
        for (int i = 0; i < _paramCount; i++)
        {
            switch (_params[i])
            {
                case 1:
                    ApplicationCursorKeys = on;
                    break;
                case 6:
                    _originMode = on;
                    MoveTo(0, 0);
                    break;
                case 7:
                    AutoWrap = on;
                    if (!on)
                    {
                        _pendingWrap = false;
                    }

                    break;
                case 25:
                    CursorVisible = on;
                    break;
                case 47:
                    if (on)
                    {
                        EnterAlternate(saveCursor: false, clear: false);
                    }
                    else
                    {
                        LeaveAlternate(restoreCursor: false);
                    }

                    break;
                case 1047:
                    if (on)
                    {
                        EnterAlternate(saveCursor: false, clear: true);
                    }
                    else
                    {
                        LeaveAlternate(restoreCursor: false);
                    }

                    break;
                case 1048:
                    if (on)
                    {
                        SaveCursor();
                    }
                    else
                    {
                        RestoreCursor();
                    }

                    break;
                case 1049:
                    if (on)
                    {
                        EnterAlternate(saveCursor: true, clear: true);
                    }
                    else
                    {
                        LeaveAlternate(restoreCursor: true);
                    }

                    break;
                case 1000 or 1002 or 1003:
                    MouseTracking = on ? _params[i] : 0;
                    break;
                case 2004:
                    BracketedPaste = on;
                    break;
            }
        }
    }

    private void SelectGraphicRendition()
    {
        for (int i = 0; i < _paramCount; i++)
        {
            int p = _params[i];
            switch (p)
            {
                case 0:
                    _attr = TerminalAttributes.None;
                    _fg = _bg = TerminalColor.Default;
                    break;
                case 1:
                    _attr |= TerminalAttributes.Bold;
                    break;
                case 2:
                    _attr |= TerminalAttributes.Dim;
                    break;
                case 3:
                    _attr |= TerminalAttributes.Italic;
                    break;
                case 4:
                    // 4:0 is "no underline" (the colon form's styles); every other form underlines.
                    if (IsSub(i + 1))
                    {
                        _attr = _params[i + 1] == 0 ? _attr & ~TerminalAttributes.Underline : _attr | TerminalAttributes.Underline;
                        i++;
                    }
                    else
                    {
                        _attr |= TerminalAttributes.Underline;
                    }

                    break;
                case 5 or 6:
                    _attr |= TerminalAttributes.Blink;
                    break;
                case 7:
                    _attr |= TerminalAttributes.Inverse;
                    break;
                case 8:
                    _attr |= TerminalAttributes.Hidden;
                    break;
                case 9:
                    _attr |= TerminalAttributes.Strike;
                    break;
                case 21:
                    _attr |= TerminalAttributes.Underline;
                    break;
                case 22:
                    _attr &= ~(TerminalAttributes.Bold | TerminalAttributes.Dim);
                    break;
                case 23:
                    _attr &= ~TerminalAttributes.Italic;
                    break;
                case 24:
                    _attr &= ~TerminalAttributes.Underline;
                    break;
                case 25:
                    _attr &= ~TerminalAttributes.Blink;
                    break;
                case 27:
                    _attr &= ~TerminalAttributes.Inverse;
                    break;
                case 28:
                    _attr &= ~TerminalAttributes.Hidden;
                    break;
                case 29:
                    _attr &= ~TerminalAttributes.Strike;
                    break;
                case >= 30 and <= 37:
                    _fg = TerminalColor.Indexed(p - 30);
                    break;
                case 38:
                    _fg = ExtendedColor(ref i, _fg);
                    break;
                case 39:
                    _fg = TerminalColor.Default;
                    break;
                case >= 40 and <= 47:
                    _bg = TerminalColor.Indexed(p - 40);
                    break;
                case 48:
                    _bg = ExtendedColor(ref i, _bg);
                    break;
                case 49:
                    _bg = TerminalColor.Default;
                    break;
                case 58:
                    // The underline's colour: read and dropped (the underline takes the text's).
                    _ = ExtendedColor(ref i, 0);
                    break;
                case >= 90 and <= 97:
                    _fg = TerminalColor.Indexed(p - 90 + 8);
                    break;
                case >= 100 and <= 107:
                    _bg = TerminalColor.Indexed(p - 100 + 8);
                    break;
            }
        }
    }

    /// <summary>38/48's colour at parameter <paramref name="i"/>: "5;n", "2;r;g;b", or their ':' forms ("2::r:g:b" too); moves past it.</summary>
    private uint ExtendedColor(ref int i, uint current)
    {
        if (IsSub(i + 1))
        {
            int subs = 0;
            while (IsSub(i + 1 + subs))
            {
                subs++;
            }

            int kind = _params[i + 1];
            uint color = current;
            if (kind == 5 && subs >= 2)
            {
                color = TerminalColor.Indexed(_params[i + 2]);
            }
            else if (kind == 2 && subs >= 4)
            {
                // "2:r:g:b", or "2:colourspace:r:g:b".
                int at = subs >= 5 ? i + 3 : i + 2;
                color = TerminalColor.Rgb(_params[at], _params[at + 1], _params[at + 2]);
            }

            i += subs;
            return color;
        }

        int mode = Raw(i + 1);
        if (mode == 5 && i + 2 < _paramCount)
        {
            i += 2;
            return TerminalColor.Indexed(_params[i]);
        }

        if (mode == 2 && i + 4 < _paramCount)
        {
            var color = TerminalColor.Rgb(_params[i + 2], _params[i + 3], _params[i + 4]);
            i += 4;
            return color;
        }

        i = _paramCount;
        return current;
    }

    private void ReportStatus(int what)
    {
        if (what == 5)
        {
            Reply("\e[0n"u8);
        }
        else if (what == 6)
        {
            Span<byte> reply = stackalloc byte[32];
            int row = (_originMode ? _row - _top : _row) + 1;
            int written = 0;
            "\e["u8.CopyTo(reply);
            written += 2;
            row.TryFormat(reply[written..], out int n, default, CultureInfo.InvariantCulture);
            written += n;
            reply[written++] = (byte)';';
            (_col + 1).TryFormat(reply[written..], out n, default, CultureInfo.InvariantCulture);
            written += n;
            reply[written++] = (byte)'R';
            Reply(reply[..written]);
        }
    }

    private void Reply(ReadOnlySpan<byte> reply) => Respond?.Invoke(reply);

    private void OscDispatch()
    {
        var text = _osc.AsSpan(0, _oscLength);
        int semicolon = text.IndexOf((byte)';');
        if (semicolon <= 0 || !int.TryParse(text[..semicolon], NumberStyles.None, CultureInfo.InvariantCulture, out int command))
        {
            return;
        }

        var value = text[(semicolon + 1)..];
        switch (command)
        {
            case 0 or 2:
                var title = Encoding.UTF8.GetString(value);
                if (title.Length > MaxTitle)
                {
                    title = title[..MaxTitle];
                }

                if (title != Title)
                {
                    Title = title.Length == 0 ? null : title;
                    TitleChanged?.Invoke(this, EventArgs.Empty);
                }

                break;
            case 7:
                SetWorkingDirectory(FolderOfFileUri(Encoding.UTF8.GetString(value)));
                break;
            case 9 when value.StartsWith("9;"u8):
                SetWorkingDirectory(Encoding.UTF8.GetString(value[2..]).Trim('"'));
                break;
        }
    }

    private void SetWorkingDirectory(string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder) && folder != WorkingDirectory)
        {
            WorkingDirectory = folder;
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The folder of "file://host/path" (OSC 7): the path unescaped, "/C:/x" read as "C:/x".</summary>
    internal static string? FolderOfFileUri(string uri)
    {
        if (!uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int slash = uri.IndexOf('/', 7);
        if (slash < 0)
        {
            return null;
        }

        var path = Uri.UnescapeDataString(uri[slash..]);
        return path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':' ? path[1..] : path;
    }
}
