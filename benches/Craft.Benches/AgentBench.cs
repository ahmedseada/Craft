// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Plugins.ClaudeCode;
using Idrak.Abstraction.Plugins;

namespace Craft.Benches;

/// <summary>
/// The agent end to end, headless, as the app runs it (the owner, 2026-11-01): the AI and Claude Code plug-ins activated on
/// an editor host, Craft's MCP server started, the agent given a task on a copy of the owner's test project, its changes
/// applied automatically (agentAutoApply) or each accepted as a diff ("ask"). Measured: the time to the first step and to
/// the end, the turns and tool calls, whether the run ended by itself, whether the model repeated itself after it was
/// done, the changes, and whether the project builds afterwards. Settings: CRAFT_BENCH_AGENT_MODEL (the agent's model:
/// "Claude Code (default)", a Hugging Face id or a folder), CRAFT_BENCH_AGENT_APPLY (auto or ask), CRAFT_BENCH_AGENT_TASK
/// (the task; a calculator by default), CRAFT_BENCH_AGENT_RUNS (1), CRAFT_BENCH_AGENT_SOURCE (the project copied, the
/// owner's test project by default), CRAFT_BENCH_AGENT_MINUTES (the limit of one run, 15). Every other CRAFT_* variable
/// reaches the plug-ins as the app's do.
/// </summary>
internal static class AgentBench
{
    private const string DefaultTask =
        "Add a static class Calculator in a new file Calculator.cs with Add, Subtract, Multiply and Divide methods on doubles " +
        "(Divide throws DivideByZeroException when the divisor is 0), and change Program.cs to print the result of " +
        "Calculator.Multiply(6, 7). Make sure the project builds with dotnet build, then stop.";

    public static async Task<int> RunAsync()
    {
        string model = Variable("CRAFT_BENCH_AGENT_MODEL") ?? "Claude Code (default)";
        bool auto = !string.Equals(Variable("CRAFT_BENCH_AGENT_APPLY"), "ask", StringComparison.OrdinalIgnoreCase);
        string task = Variable("CRAFT_BENCH_AGENT_TASK") ?? DefaultTask;
        int runs = int.TryParse(Variable("CRAFT_BENCH_AGENT_RUNS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) && r > 0 ? r : 1;
        int minutes = int.TryParse(Variable("CRAFT_BENCH_AGENT_MINUTES"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && m > 0 ? m : 15;
        string source = Variable("CRAFT_BENCH_AGENT_SOURCE") ?? Path.Combine(BenchSupport.RepositoryRoot(), "test-output", "CarftTestPOC");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string folder = Path.Combine(BenchSupport.OutputFolder(), "agent", stamp);
        Directory.CreateDirectory(folder);

        // The plug-ins register as the app's loader registers them: their Idrak packages.
        IdrakPlugins.Load(typeof(AiPackage).Assembly);
        IdrakPlugins.Load(typeof(ClaudeCodePackage).Assembly);
        IdrakPlugins.Load(typeof(Craft.Plugins.Tools.ToolsPackage).Assembly);       // the editor's tools (MCP, the local agent)
        IdrakPlugins.Load(typeof(Craft.Plugins.Sandbox.SandboxPackage).Assembly);   // approve-each: the local agent's commands

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Agent bench {stamp}: model {model}, apply {(auto ? "auto" : "ask (each diff accepted)")}, {runs} run(s), limit {minutes} min");
        report.AppendLine(CultureInfo.InvariantCulture, $"Task: {task}");
        int failures = 0;
        for (int run = 1; run <= runs; run++)
        {
            var result = await OneAsync(model, auto, task, source, Path.Combine(folder, "run" + run.ToString(CultureInfo.InvariantCulture)), minutes).ConfigureAwait(false);
            report.AppendLine(CultureInfo.InvariantCulture, $"--- run {run}").Append(result.Text);
            failures += result.Passed ? 0 : 1;
            Console.Write(result.Text);
        }

        string file = Path.Combine(folder, "report.txt");
        await File.WriteAllTextAsync(file, report.ToString()).ConfigureAwait(false);
        Console.WriteLine($"Report: {file}");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<(bool Passed, string Text)> OneAsync(string model, bool auto, string task, string source, string folder, int minutes)
    {
        string workspace = Path.Combine(folder, "workspace");
        Copy(source, workspace);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CRAFT_PLUGIN_AI_AGENTMODEL"] = model,
            ["CRAFT_PLUGIN_AI_AGENTAUTOAPPLY"] = auto ? "true" : "false",
            ["CRAFT_PLUGIN_AI_AGENTFOLDER"] = Path.Combine(folder, "copies"),
            ["CRAFT_PLUGIN_AI_AGENTMINUTES"] = minutes.ToString(CultureInfo.InvariantCulture),
        };
        var text = new StringBuilder();
        var presenter = new AcceptingPresenter();
        var gate = new Lock();
        using var host = new EditorHost(dispatch: action => { lock (gate) { action(); } }, presenter: presenter,
            readVariable: name => variables.TryGetValue(name, out var value) ? value : Environment.GetEnvironmentVariable(name))
        {
            WorkspaceFolder = workspace,
        };
        host.OpenFile = async (path, token) => File.Exists(path)
            ? host.Open(await TextDocument.OpenAsync(path, cancellationToken: token).ConfigureAwait(false), null)
            : host.Open(TextDocument.Create(""), null);
        var log = new List<string>();
        host.Logged += (_, entry) =>
        {
            lock (log)
            {
                log.Add($"{entry.Time:HH:mm:ss.fff} {entry.Level} {entry.Source}: {entry.Message}{(entry.Error is { } e ? " (" + e.GetType().Name + ": " + e.Message + ")" : "")}");
            }
        };

        var activated = host.RefreshPlugins();
        foreach (var failed in activated.Where(a => !a.Active))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  plug-in {failed.Name} did not activate: {failed.Error}");
        }

        var session = AiPlugin.Current ?? throw new InvalidOperationException("The AI plug-in did not activate.");
        long mcpStart = Stopwatch.GetTimestamp();
        await session.StartMcpAsync(CancellationToken.None).ConfigureAwait(false);
        text.AppendLine(CultureInfo.InvariantCulture, $"  MCP: {(session.Mcp is { } mcp ? mcp.Address : "not started")} in {Stopwatch.GetElapsedTime(mcpStart).TotalMilliseconds:F0} ms");

        var panel = session.Agent;
        long start = Stopwatch.GetTimestamp();
        double firstStep = -1;
        // The first step is the model's own: its first text or tool call (the task and the panel's notes come at once).
        // A question the panel asks inline (a command to approve, X part 10) is answered as the user's click would: its
        // first choice, once.
        var answered = new HashSet<ConversationEntry>(ReferenceEqualityComparer.Instance);
        int inlineAnswers = 0;
        void Watch(object? sender, EventArgs e)
        {
            var entries = panel.Entries;
            if (firstStep < 0 && entries.Any(x => x.Role is ConversationRole.Assistant or ConversationRole.Tool))
            {
                firstStep = Stopwatch.GetElapsedTime(start).TotalSeconds;
            }

            foreach (var entry in entries)
            {
                // An approval asked inline: its choices on the waiting command's step, the first (primary) one approving.
                if (entry.Role == ConversationRole.Approval && entry.Actions.Count > 1 && entry.Actions[0].Primary && answered.Add(entry))
                {
                    Interlocked.Increment(ref inlineAnswers);
                    var choice = entry.Actions[0];
                    _ = Task.Run(() => choice.RunAsync(CancellationToken.None));
                }
            }
        }

        // Looked at twice a second, outside the panel's own updates, as a user's click comes.
        using var watching = new CancellationTokenSource();
        var watcher = Task.Run(async () =>
        {
            while (!watching.IsCancellationRequested)
            {
                lock (gate)
                {
                    Watch(null, EventArgs.Empty);
                }

                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }
        });
        await panel.SubmitAsync(task).ConfigureAwait(false);
        bool ended = await Task.WhenAny(panel.Run, Task.Delay(TimeSpan.FromMinutes(minutes + 1))).ConfigureAwait(false) == panel.Run;
        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        await watching.CancelAsync().ConfigureAwait(false);
        await watcher.ConfigureAwait(false);
        if (!ended)
        {
            panel.Stop();
            await Task.WhenAny(panel.Run, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
        }

        // In "ask" mode the user chooses: every diff is accepted here, as a user pressing Accept would.
        int beforeReview = panel.Changes.Count;
        long reviewStart = Stopwatch.GetTimestamp();
        if (!auto && panel.Changes.Count > 0)
        {
            await panel.ReviewAsync().ConfigureAwait(false);
        }

        double reviewMs = Stopwatch.GetElapsedTime(reviewStart).TotalMilliseconds;
        foreach (var document in host.Documents.Where(d => d.Document.IsDirty && d.FilePath is not null).ToList())
        {
            await host.SaveAsync(document, null, CancellationToken.None).ConfigureAwait(false);   // what the user would save
        }

        var entries = panel.Entries;
        int tools = entries.Count(e => e.Role == ConversationRole.Tool);
        int toolsFailed = entries.Count(e => e is { Role: ConversationRole.Tool, Succeeded: false });
        int errors = entries.Count(e => e.Role == ConversationRole.Error);
        var assistant = entries.Where(e => e.Role == ConversationRole.Assistant).Select(e => e.Text.Trim()).Where(t => t.Length > 0).ToList();
        int repeated = assistant.Count - assistant.Distinct(StringComparer.Ordinal).Count();
        var (built, buildOutput) = await BuildAsync(workspace).ConfigureAwait(false);
        bool calculator = File.Exists(Path.Combine(workspace, "Calculator.cs"));
        string program = await File.ReadAllTextAsync(Path.Combine(workspace, "Program.cs")).ConfigureAwait(false);
        bool uses = program.Contains("Calculator.", StringComparison.Ordinal);

        text.AppendLine(CultureInfo.InvariantCulture, $"  ended by itself: {ended}; time {seconds:F1} s; first step {(firstStep < 0 ? "none" : firstStep.ToString("F1", CultureInfo.InvariantCulture) + " s")}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  timeline: {entries.Count} entries, {assistant.Count} assistant messages ({repeated} repeated), {tools} tool calls ({toolsFailed} failed), {errors} errors");
        int applied = entries.Count(e => e.Role == ConversationRole.Note && e.Text.Contains("applied automatically", StringComparison.Ordinal));
        text.AppendLine(CultureInfo.InvariantCulture, $"  changes: {(auto ? $"{applied} applied automatically" : $"{beforeReview} listed, reviewed in {reviewMs:F0} ms, {presenter.Proposals.Count} diffs accepted")}, {panel.Changes.Count} left; {presenter.Questions.Count} questions answered ({string.Join("; ", presenter.Questions.Distinct())}), {inlineAnswers} inline approvals");
        text.AppendLine(CultureInfo.InvariantCulture, $"  result: Calculator.cs {(calculator ? "made" : "missing")}, Program.cs {(uses ? "uses it" : "unchanged")}, dotnet build {(built ? "succeeded" : "FAILED")}");
        if (!built)
        {
            text.AppendLine("  build output (end):").AppendLine(Tail(buildOutput, 1500));
        }

        string timeline = string.Join("\n", entries.Select(e => $"[{e.Role}] {e.Text}{(e.Detail is { Length: > 0 } d ? " · " + d : "")}{(e.Succeeded is { } ok ? (ok ? " ✓" : " ✗") : "")}"));
        await File.WriteAllTextAsync(Path.Combine(folder, "timeline.txt"), timeline).ConfigureAwait(false);
        string[] lines;
        lock (log)
        {
            lines = [.. log];
        }

        await File.WriteAllLinesAsync(Path.Combine(folder, "host-log.txt"), lines).ConfigureAwait(false);

        text.AppendLine(CultureInfo.InvariantCulture, $"  timeline and log: {folder}");
        bool passed = ended && built && calculator && uses && repeated == 0 && errors == 0;
        text.AppendLine(CultureInfo.InvariantCulture, $"  {(passed ? "PASS" : "FAIL")}");
        panel.DiscardAll();
        await AiAgentPanel.DiscardsDone().ConfigureAwait(false);
        return (passed, text.ToString());
    }

    /// <summary>
    /// The chat end to end (CRAFT_BENCH_CHAT_MODEL, the chat's model): two questions, each its reply's time, length,
    /// tokens per second (the plug-in's own measures), whether it ended and how many of its lines repeat.
    /// </summary>
    public static async Task<int> ChatAsync()
    {
        string model = Variable("CRAFT_BENCH_CHAT_MODEL") ?? "Qwen/Qwen2.5-1.5B-Instruct";
        string[] prompts =
        [
            "In two sentences: what does the C# keyword 'async' do?",
            "Write a C# method that returns the n-th Fibonacci number iteratively. Only the method, in one code block.",
        ];
        var (host, session, measures, folder) = await HostAsync("chat", new() { ["CRAFT_PLUGIN_AI_MODEL"] = model }).ConfigureAwait(false);
        using var _ = host;
        var text = new StringBuilder().AppendLine(CultureInfo.InvariantCulture, $"Chat bench: model {model}");
        bool passed = true;
        foreach (string prompt in prompts)
        {
            int before = session.Chat.Entries.Count;
            measures.Clear();
            long start = Stopwatch.GetTimestamp();
            await session.Chat.SubmitAsync(prompt).ConfigureAwait(false);
            bool ended = await Task.WhenAny(session.Chat.LastReply, Task.Delay(TimeSpan.FromMinutes(5))).ConfigureAwait(false) == session.Chat.LastReply;
            double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
            var replies = session.Chat.Entries.Skip(before).Where(e => e.Role is ConversationRole.Assistant or ConversationRole.Code).Select(e => e.Text).ToList();
            var errors = session.Chat.Entries.Skip(before).Where(e => e.Role == ConversationRole.Error).Select(e => e.Text).ToList();
            string reply = string.Join("\n", replies);
            string rate = session.Chat.Entries.Skip(before).LastOrDefault(e => e.Role == ConversationRole.Assistant)?.Detail ?? "no rate";
            text.AppendLine("  rate: " + rate);
            var lines = reply.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 12).ToList();
            int repeated = lines.Count - lines.Distinct(StringComparer.Ordinal).Count();
            bool ok = ended && errors.Count == 0 && reply.Length > 0 && repeated == 0;
            passed &= ok;
            text.AppendLine(CultureInfo.InvariantCulture, $"  Q: {prompt}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  ended {ended}, {seconds:F1} s, {reply.Length} characters, {repeated} repeated lines, {errors.Count} errors{(errors.Count > 0 ? ": " + string.Join(" | ", errors) : "")}");
            text.AppendLine("  measures: " + Measures(measures));
            text.AppendLine("  A: " + Tail(reply.Replace("\n", "\n     ", StringComparison.Ordinal), 900));
            text.AppendLine(ok ? "  PASS" : "  FAIL");
        }

        return await FinishAsync(text, folder, passed).ConfigureAwait(false);
    }

    /// <summary>
    /// Inline completion (CRAFT_BENCH_COMPLETION_MODEL): ghost text at a caret in a C# file, once cold and five times
    /// warm: the time to the first piece and to the end, the tokens, the text.
    /// </summary>
    public static async Task<int> CompletionAsync()
    {
        string model = Variable("CRAFT_BENCH_COMPLETION_MODEL") ?? "Qwen/Qwen2.5-1.5B-Instruct";
        var (host, session, measures, folder) = await HostAsync("completion", new() { ["CRAFT_PLUGIN_AI_COMPLETIONMODEL"] = model, ["CRAFT_PLUGIN_AI_MODEL"] = model }).ConfigureAwait(false);
        using var _ = host;
        const string code = "using System;\n\npublic static class Numbers\n{\n    // Returns true when n is a prime number.\n    public static bool IsPrime(int n)\n    {\n        ";
        const string after = "\n    }\n}\n";
        var snapshot = TextDocument.Create(code + after).Buffer.Current;
        var request = new InlineCompletionRequest(snapshot, code.Length, Path.Combine(host.WorkspaceFolder!, "Numbers.cs"), "csharp", host.WorkspaceFolder);
        var text = new StringBuilder().AppendLine(CultureInfo.InvariantCulture, $"Completion bench: model {model}");
        bool passed = true;
        for (int i = 0; i < 6; i++)
        {
            measures.Clear();
            long start = Stopwatch.GetTimestamp();
            double first = -1;
            int tokens = 0;
            var completion = new StringBuilder();
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(i == 0 ? 10 : 1));
            await foreach (var piece in session.Completion.CompleteAsync(request, limit.Token).ConfigureAwait(false))
            {
                if (first < 0 && piece.Text.Length > 0)
                {
                    first = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }

                tokens += piece.Tokens;
                completion.Append(piece.Text);
            }

            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            passed &= completion.Length > 0;
            text.AppendLine(CultureInfo.InvariantCulture, $"  {(i == 0 ? "cold" : "warm")}: first piece {(first < 0 ? "none" : first.ToString("F0", CultureInfo.InvariantCulture) + " ms")}, end {ms:F0} ms, {tokens} tokens, {completion.Length} characters; {session.Completion.Mode?.Text ?? "no mode"}{(session.Completion.UnavailableReason is { } why ? "; unavailable: " + why : "")}");
            if (i is 0 or 1)
            {
                text.AppendLine("    " + completion.ToString().Replace("\n", "\n    ", StringComparison.Ordinal));
            }
        }

        text.AppendLine("  measures (last): " + Measures(measures));
        if (await session.Models.CompletionAsync(CancellationToken.None).ConfigureAwait(false) is { } lease)
        {
            var (prompt, stops) = session.Completion.Layout(request, lease, session.Settings);
            text.AppendLine(CultureInfo.InvariantCulture, $"  the prompt ({prompt.Length} characters, stops: {string.Join(" | ", stops)}):");
            text.AppendLine("    " + prompt.Replace("\n", "\n    ", StringComparison.Ordinal));
            // The ids the model sampled, each decoded alone, against the streamed text (a first token lost shows here).
            if (lease.Text is { } textModel && lease.Tokenizer is { } tokenizer)
            {
                var options = new Idrak.Abstraction.Generation.GenerationOptions { NumPredict = 12, Temperature = 0, ReturnTokens = true, Stop = stops };
                var streamed = new StringBuilder();
                Idrak.Abstraction.Generation.ChatTokens? ids = null;
                await foreach (var chunk in textModel.StreamAsync(prompt, options, CancellationToken.None).ConfigureAwait(false))
                {
                    streamed.Append(chunk.Text);
                    ids ??= chunk.Tokens;
                }

                if (ids is not null)
                {
                    text.AppendLine("  sampled: " + string.Join(" ", ids.Reply.Select(id => "[" + tokenizer.Decode([id]).Replace("\n", "\\n", StringComparison.Ordinal) + "]")));
                    text.AppendLine("  all decoded: [" + tokenizer.Decode(ids.Reply).Replace("\n", "\\n", StringComparison.Ordinal) + "]");
                    text.AppendLine("  prompt's last ids: " + string.Join(" ", ids.Prompt[^4..].Select(id => "[" + tokenizer.Decode([id]).Replace("\n", "\\n", StringComparison.Ordinal) + "]")));
                }

                text.AppendLine("  streamed: [" + streamed.ToString().Replace("\n", "\\n", StringComparison.Ordinal) + "]");
            }
        }

        text.AppendLine(passed ? "  PASS" : "  FAIL");
        return await FinishAsync(text, folder, passed).ConfigureAwait(false);
    }

    // A headless host with the plug-ins the app loads, on a copy of the owner's test project, its measures collected.
    private static async Task<(EditorHost Host, AiSession Session, List<string> Measures, string Folder)> HostAsync(string name, Dictionary<string, string> variables)
    {
        IdrakPlugins.Load(typeof(AiPackage).Assembly);
        IdrakPlugins.Load(typeof(Craft.Plugins.Tools.ToolsPackage).Assembly);
        IdrakPlugins.Load(typeof(Craft.Plugins.Sandbox.SandboxPackage).Assembly);
        string folder = Path.Combine(BenchSupport.OutputFolder(), name, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        string workspace = Path.Combine(folder, "workspace");
        Copy(Variable("CRAFT_BENCH_AGENT_SOURCE") ?? Path.Combine(BenchSupport.RepositoryRoot(), "test-output", "CarftTestPOC"), workspace);
        var gate = new Lock();
        var host = new EditorHost(dispatch: action => { lock (gate) { action(); } }, presenter: new AcceptingPresenter(),
            readVariable: n => variables.TryGetValue(n, out var value) ? value : Environment.GetEnvironmentVariable(n))
        {
            WorkspaceFolder = workspace,
        };
        host.OpenFile = async (path, token) => File.Exists(path)
            ? host.Open(await TextDocument.OpenAsync(path, cancellationToken: token).ConfigureAwait(false), null)
            : host.Open(TextDocument.Create(""), null);
        var measures = new List<string>();
        host.Recorded += (_, m) =>
        {
            lock (measures)
            {
                measures.Add(string.Create(CultureInfo.InvariantCulture, $"{m.Metric}={m.Value:F1}{m.Unit}"));
            }
        };
        host.RefreshPlugins();
        var session = AiPlugin.Current ?? throw new InvalidOperationException("The AI plug-in did not activate.");
        await Task.Yield();
        return (host, session, measures, folder);
    }

    private static string Measures(List<string> measures)
    {
        lock (measures)
        {
            return measures.Count == 0 ? "none" : string.Join(", ", measures.Where(m => m.StartsWith("ai.", StringComparison.Ordinal)).Distinct());
        }
    }

    private static async Task<int> FinishAsync(StringBuilder text, string folder, bool passed)
    {
        Console.Write(text);
        string file = Path.Combine(folder, "report.txt");
        await File.WriteAllTextAsync(file, text.ToString()).ConfigureAwait(false);
        Console.WriteLine($"Report: {file}");
        return passed ? 0 : 1;
    }

    private static async Task<(bool Built, string Output)> BuildAsync(string folder)
    {
        var info = new ProcessStartInfo("dotnet", "build -nologo -v q")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return (process.ExitCode == 0, await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, file);
            string first = relative.Split(Path.DirectorySeparatorChar)[0];
            if (first is "bin" or "obj" or ".vs")
            {
                continue;
            }

            string target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string Tail(string text, int characters) => text.Length <= characters ? text : text[^characters..];

    private static string? Variable(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    // The user pressing Accept on every hunk of every diff (the "ask" mode's choice, made here).
    private sealed class AcceptingPresenter : IEditorPresenter
    {
        public List<EditProposal> Proposals { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<bool>> ChooseHunksAsync(EditProposal proposal, CancellationToken cancellationToken)
        {
            lock (Proposals)
            {
                Proposals.Add(proposal);
            }

            return Task.FromResult<IReadOnlyList<bool>>(Enumerable.Repeat(true, Math.Max(1, proposal.Diff.Hunks.Count)).ToArray());
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public List<string> Questions { get; } = [];

        // A command the agent wants to run: the user's "Allow for this run" (Claude Code's second choice; a sandbox's
        // approval its first); any other question: its first choice.
        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            lock (Questions)
            {
                Questions.Add(question.Title);
            }

            int choice = question.Kind == EditorQuestionKinds.Command && question.Choices.Count > 2 ? 1 : 0;
            return Task.FromResult(choice);
        }
    }
}
