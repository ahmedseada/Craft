// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Benches;

// The benches of plan 1, each writing its results under test-output/benches/ at the repository root.
//   dotnet run -c Release --project benches/Craft.Benches -- buffer     (E1: the piece table and the document)
//   dotnet run -c Release --project benches/Craft.Benches -- csharp     (E2: the incremental parse and classification)
//   dotnet run -c Release --project benches/Craft.Benches -- editor     (E3: keystroke to render, blink and scroll frames)
//   dotnet run -c Release --project benches/Craft.Benches -- semantic   (E5: the workspace, generators, diagnostics, completion, colours, memory)
//   dotnet run -c Release --project benches/Craft.Benches -- sampler    (P3: the bracket-balanced sampler's tokens per second against the library's)
//   dotnet run -c Release --project benches/Craft.Benches -- terminal   (T: the VT parser's bytes/s over a million lines, memory, frames)
//   dotnet run -c Release --project benches/Craft.Benches -- agent      (the agent end to end: Claude Code or a local model, MCP, auto-apply or ask; CRAFT_BENCH_AGENT_*)
// No argument runs them all (the agent bench only on request: it runs real models). CRAFT_BENCH_LINES sets the generated file's lines (100,000 by default).
var which = args.Length > 0 ? args[0] : "all";
int lines = int.TryParse(Environment.GetEnvironmentVariable("CRAFT_BENCH_LINES"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 100_000;
switch (which)
{
    case "buffer":
        await BufferBench.RunAsync(lines).ConfigureAwait(false);
        break;
    case "csharp":
        CSharpBench.Run(lines);
        break;
    case "editor":
        EditorBench.Run(lines);
        break;
    case "semantic":
        await SemanticBench.RunAsync().ConfigureAwait(false);
        break;
    case "sampler":
        SamplerBench.Run();
        break;
    case "terminal":
        TerminalBench.Run();
        break;
    case "agent":
        return await AgentBench.RunAsync().ConfigureAwait(false);
    case "chat":
        return await AgentBench.ChatAsync().ConfigureAwait(false);
    case "complete":
        return await AgentBench.CompletionAsync().ConfigureAwait(false);
    case "all":
        await BufferBench.RunAsync(lines).ConfigureAwait(false);
        CSharpBench.Run(lines);
        EditorBench.Run(lines);
        await SemanticBench.RunAsync().ConfigureAwait(false);
        SamplerBench.Run();
        TerminalBench.Run();
        break;
    default:
        await Console.Error.WriteLineAsync($"Unknown bench '{which}'. Benches: buffer, csharp, editor, semantic, sampler, terminal (none for all).");
        return 2;
}

return 0;
