// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Benches;

// The benches of plan 1, each writing its results under test-output/benches/ at the repository root.
//   dotnet run -c Release --project benches/Craft.Benches -- buffer     (E1: the piece table and the document)
//   dotnet run -c Release --project benches/Craft.Benches -- csharp     (E2: the incremental parse and classification)
//   dotnet run -c Release --project benches/Craft.Benches -- editor     (E3: keystroke to render, blink and scroll frames)
// No argument runs them all. CRAFT_BENCH_LINES sets the generated file's lines (100,000 by default).
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
    case "all":
        await BufferBench.RunAsync(lines).ConfigureAwait(false);
        CSharpBench.Run(lines);
        EditorBench.Run(lines);
        break;
    default:
        await Console.Error.WriteLineAsync($"Unknown bench '{which}'. Benches: buffer, csharp, editor (none for all).");
        return 2;
}

return 0;
