// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using System.Runtime.InteropServices;
using Craft.Core.Abstractions;
using Craft.Core.Diagnostics;

namespace Craft.Maui.WinUI;

/// <summary>
/// "windows-eventlog": Windows' own record of a run that died (the owner's note 21), read from the Application event log
/// through the Windows Event Log API (<c>EvtQuery</c>, <c>EvtNext</c>, <c>EvtRender</c> of wevtapi.dll, source-generated
/// P/Invoke; it only reads): Application Error 1000 (the faulting module, the exception code and offset), Application
/// Hang 1002, and .NET Runtime 1023, 1025 and 1026 (the managed stack) in the run's time window. Each event's XML is read
/// by <see cref="EventLogCrashXml"/> (Craft.Core, tested on every platform), which keeps the run's own.
/// </summary>
public sealed partial class WindowsEventLogCrashRecords : ICrashRecordSource
{
    /// <summary>The source's name.</summary>
    public const string SourceName = "windows-eventlog";

    private const int QueryChannelPath = 0x1;
    private const int RenderEventXml = 1;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private const int Batch = 16;
    private const int MostEvents = 500;

    /// <inheritdoc />
    public string Name => SourceName;

    /// <inheritdoc />
    public string Summary => "Windows' Application event log: Application Error 1000 (module, code, offset) and .NET Runtime 1026 (the managed stack)";

    /// <inheritdoc />
    public string? Unavailable => OperatingSystem.IsWindowsVersionAtLeast(6) ? null : "the Windows Event Log API needs Windows Vista or later";

    /// <inheritdoc />
    public IReadOnlyList<CrashRecord> Read(CrashRecordQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var records = new List<CrashRecord>();
        nint results = EvtQuery(0, "Application", EventLogCrashXml.XPath(query), QueryChannelPath);
        if (results == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException($"EvtQuery of the Application log failed: {Marshal.GetPInvokeErrorMessage(error)} (error {error}).");
        }

        try
        {
            var events = new nint[Batch];
            int seen = 0;
            while (seen < MostEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int returned;
                bool next;
                unsafe
                {
                    fixed (nint* handles = events)
                    {
                        next = EvtNext(results, Batch, handles, 1000, 0, out returned);
                    }
                }

                if (!next)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new InvalidOperationException($"EvtNext failed: {Marshal.GetPInvokeErrorMessage(error)} (error {error}).");
                }

                for (int i = 0; i < returned; i++)
                {
                    try
                    {
                        if (Render(events[i]) is { } xml && EventLogCrashXml.Parse(xml, query) is { } record)
                        {
                            records.Add(record);
                        }
                    }
                    finally
                    {
                        _ = EvtClose(events[i]);
                    }
                }

                seen += returned;
            }
        }
        finally
        {
            _ = EvtClose(results);
        }

        records.Sort(static (a, b) => a.Time.CompareTo(b.Time));
        return records;
    }

    // The event as XML: asked once for its size, then rendered into a buffer of that size.
    private static unsafe string? Render(nint handle)
    {
        if (EvtRender(0, handle, RenderEventXml, 0, null, out int used, out _))
        {
            return null;
        }

        if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer || used <= 0)
        {
            return null;
        }

        var buffer = new char[(used / sizeof(char)) + 1];
        fixed (char* chars = buffer)
        {
            if (!EvtRender(0, handle, RenderEventXml, buffer.Length * sizeof(char), chars, out used, out _))
            {
                return null;
            }
        }

        int length = Math.Max(0, (used / sizeof(char)) - 1);                     // without the closing NUL
        return new string(buffer, 0, Math.Min(length, buffer.Length));
    }

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtQuery", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint EvtQuery(nint session, string path, string query, int flags);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtNext", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EvtNext(nint resultSet, int eventsSize, nint* events, int timeout, int flags, out int returned);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtRender", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EvtRender(nint context, nint fragment, int flags, int bufferSize, char* buffer, out int bufferUsed, out int propertyCount);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtClose")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtClose(nint handle);
}
#endif
