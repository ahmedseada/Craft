// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Craft.Core.Abstractions;

namespace Craft.Core.Diagnostics;

/// <summary>
/// Windows' records of a crash as the event log renders them (an event's XML), read into a <see cref="CrashRecord"/>:
/// Application Error 1000 (the faulting module, the exception code and offset, the process id) and 1002 (a hang), and
/// .NET Runtime 1023, 1025 and 1026 (a fatal error, a fail-fast, an unhandled exception with its managed stack, which
/// carry no process id: matched by the application's name and the run's time). Plain XML: parsed and tested on every
/// platform; Craft.Maui's Windows source only queries the log and hands the XML here.
/// </summary>
public static class EventLogCrashXml
{
    /// <summary>The providers and event ids the Windows source asks for.</summary>
    public const string XPathEvents = "(Provider[@Name='Application Error' or @Name='Application Hang' or @Name='.NET Runtime']) and (EventID=1000 or EventID=1002 or EventID=1023 or EventID=1025 or EventID=1026)";

    // Application Error 1000's data in order, for systems that render the fields without names.
    private static readonly string[] FaultFields =
        ["AppName", "AppVersion", "AppTimeStamp", "ModuleName", "ModuleVersion", "ModuleTimeStamp", "ExceptionCode", "FaultingOffset", "ProcessId", "ProcessCreationTime", "AppPath", "ModulePath", "IntegratorReportId"];

    /// <summary>The event log's XPath query for <paramref name="query"/>'s time window (the Application log).</summary>
    public static string XPath(CrashRecordQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string from = query.From.UtcDateTime.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        string to = query.To.UtcDateTime.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        return $"*[System[{XPathEvents} and TimeCreated[@SystemTime>='{from}' and @SystemTime<='{to}']]]";
    }

    /// <summary>The record of <paramref name="xml"/> when it is one of <paramref name="query"/>'s run, else null.</summary>
    public static CrashRecord? Parse(string xml, CrashRecordQuery query)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(query);
        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (XmlException)
        {
            return null;
        }

        var ns = root.Name.Namespace;
        var system = root.Element(ns + "System");
        string provider = system?.Element(ns + "Provider")?.Attribute("Name")?.Value ?? "";
        _ = int.TryParse(system?.Element(ns + "EventID")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id);
        var time = DateTimeOffset.TryParse(system?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ? at : query.To;
        var data = root.Element(ns + "EventData")?.Elements(ns + "Data").ToList() ?? [];
        string source = string.Create(CultureInfo.InvariantCulture, $"Application log, {provider} {id}");
        if (provider is "Application Error" or "Application Hang")
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < data.Count; i++)
            {
                string name = data[i].Attribute("Name")?.Value ?? (i < FaultFields.Length ? FaultFields[i] : string.Create(CultureInfo.InvariantCulture, $"Data{i}"));
                fields[name] = data[i].Value;
            }

            if (!fields.TryGetValue("ProcessId", out var pidText) || ParseId(pidText) != query.ProcessId)
            {
                return null;
            }

            string module = fields.GetValueOrDefault("ModuleName", "?");
            string code = fields.GetValueOrDefault("ExceptionCode", "");
            string summary = provider == "Application Hang"
                ? $"Windows recorded the window as not responding ({fields.GetValueOrDefault("AppName", query.ProcessName)})"
                : $"Faulting module {module}, exception code 0x{code.TrimStart('0', 'x')}{Meaning(code)}, offset 0x{fields.GetValueOrDefault("FaultingOffset", "?").TrimStart('0', 'x')}";
            var text = new StringBuilder();
            foreach (var (name, value) in fields)
            {
                text.Append(name).Append(": ").Append(value).Append('\n');
            }

            return new CrashRecord(time, source, summary, text.ToString());
        }

        if (provider == ".NET Runtime")
        {
            string body = string.Join("\n", data.Select(d => d.Value)).ReplaceLineEndings("\n");
            string application = FirstLineAfter(body, "Application: ");
            if (!application.Equals(query.ProcessName, StringComparison.OrdinalIgnoreCase)
                && !application.Equals(query.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (time < query.From.AddMinutes(-1) || time > query.To.AddMinutes(1))
            {
                return null;
            }

            string exception = FirstLineAfter(body, "Exception Info: ");
            string description = FirstLineAfter(body, "Description: ");
            string summary = exception.Length > 0 ? exception : description.Length > 0 ? description : FirstLineAfter(body, "Message: ");
            return new CrashRecord(time, source, ".NET: " + summary, body.TrimEnd() + "\n");
        }

        return null;
    }

    /// <summary>What a Windows exception code means, for the summary ("" for one it does not know).</summary>
    public static string Meaning(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var hex = code.Trim().TrimStart('0', 'x', 'X').ToUpperInvariant();
        return hex switch
        {
            "C000027B" => " (a stowed exception: an error inside a WinUI or WinRT callback, raised by the XAML framework)",
            "C0000409" => " (a fail-fast: the process ended itself, __fastfail or a stack buffer check)",
            "E0434352" => " (a .NET exception nobody caught)",
            "C0000005" => " (an access violation)",
            "C00000FD" => " (a stack overflow)",
            "80000003" => " (a breakpoint)",
            "C0000374" => " (a corrupted heap)",
            "C0000602" => " (a fail-fast exception)",
            _ => "",
        };
    }

    private static int ParseId(string text)
    {
        var span = text.AsSpan().Trim();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(span[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex) ? hex : -1;
        }

        return int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : -1;
    }

    private static string FirstLineAfter(string body, string label)
    {
        int at = body.IndexOf(label, StringComparison.Ordinal);
        if (at < 0)
        {
            return "";
        }

        int start = at + label.Length;
        int end = body.IndexOf('\n', start);
        return (end < 0 ? body[start..] : body[start..end]).Trim();
    }
}
