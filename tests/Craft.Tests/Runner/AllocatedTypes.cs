// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Globalization;

namespace Craft.Tests.Runner;

/// <summary>
/// What a call that should allocate nothing allocates (Idrak's rule 90, step 1): the types the runtime's allocation ticks
/// name (one tick about every 100 KB allocated, so the call is repeated until enough was allocated), for an allocation
/// test's failure message to name the cause on whatever machine it fails. A boxed char reads <c>System.Char</c>.
/// </summary>
public static class AllocatedTypes
{
    /// <summary>
    /// Calls <paramref name="call"/> until it allocated <paramref name="bytes"/> on this thread (or <paramref name="maxCalls"/>
    /// calls) and says the bytes a call and the types the ticks named.
    /// </summary>
    public static string Of(Action call, long bytes = 4 << 20, int maxCalls = 20_000)
    {
        ArgumentNullException.ThrowIfNull(call);
        using var ticks = new AllocationTicks();
        long start = GC.GetAllocatedBytesForCurrentThread();
        int calls = 0;
        while (calls < maxCalls && GC.GetAllocatedBytesForCurrentThread() - start < bytes)
        {
            call();
            calls++;
        }

        double perCall = (GC.GetAllocatedBytesForCurrentThread() - start) / (double)Math.Max(1, calls);
        for (int wait = 0; wait < 40 && ticks.Types.IsEmpty; wait++)
        {
            Thread.Sleep(50);                                            // the events arrive on their own thread
        }

        Thread.Sleep(200);
        string types = ticks.Types.IsEmpty ? "no allocation ticks arrived (too little allocated for a tick)"
            : string.Join(", ", ticks.Types.OrderByDescending(t => t.Value).Select(t => $"{t.Key} ({t.Value} ticks)"));
        return string.Create(CultureInfo.InvariantCulture, $"{perCall:F1} bytes a call over {calls} calls; allocated: {types}");
    }

    private sealed class AllocationTicks : EventListener
    {
        public ConcurrentDictionary<string, int> Types { get; } = new();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
            {
                EnableEvents(eventSource, EventLevel.Verbose, (EventKeywords)0x1);   // the GC keyword: GCAllocationTick
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true || eventData.PayloadNames is null)
            {
                return;
            }

            int at = eventData.PayloadNames.IndexOf("TypeName");
            if (at >= 0 && eventData.Payload?[at] is string type)
            {
                Types.AddOrUpdate(type, 1, (_, n) => n + 1);
            }
        }
    }
}
