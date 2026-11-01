// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json.Nodes;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Run;
using Craft.Editor.Shell;
using Craft.Plugins.Tools;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;
using Idrak.Text.Generation;

namespace Craft.Tests;

/// <summary>
/// X part 11 item 5, Fix with AI: a Run-view build that fails with errors keeps them with Fix with AI and Run again; several
/// errors go to the agent with the code around each (Idrak's scripted fake chat model's edits on its copy), one error to
/// the chat's model; the fix arrives as diffs the user accepts; Run again saves what the fix changed and runs the same
/// configuration; a build's Problems row says "Fix with AI"; turned off, nothing is offered.
/// </summary>
[TestGroup("BuildFix")]
public static class BuildFixTests
{
    private const string ProviderName = "build-fix-fake";

    [Test]
    public static async Task AFailedBuildsErrorsReachTheAgentTheFixArrivesAsADiffAndRunAgainReruns()
    {
        using var folder = new TempFolder();
        string root = folder.Path;
        string a = Path.Combine(root, "a.cs");
        string b = Path.Combine(root, "b.cs");
        await File.WriteAllTextAsync(a, "int x = 1\n");
        await File.WriteAllTextAsync(b, "class B\n{\n    int z = y;\n}\n");
        var chat = FakeChatModel.Script(
            FakeChatModel.ToolCall("edit_file", new JsonObject { ["path"] = "a.cs", ["old_text"] = "int x = 1", ["new_text"] = "int x = 1;" }),
            FakeChatModel.ToolCall("edit_file", new JsonObject { ["path"] = "b.cs", ["old_text"] = "int z = y;", ["new_text"] = "int z = 0;" }),
            FakeChatModel.Answer("Done."), FakeChatModel.Answer("Done."));
        using var fixture = new Fixture(root, chat,
        [
            $"{a}(1,10): error CS1002: ; expected",
            $"{b}(3,13): error CS0103: The name 'y' does not exist in the current context",
        ]);
        var view = fixture.View;
        var outcome = await view.RunAsync(fixture.Configuration).WaitAsync(TimeSpan.FromSeconds(20));
        Check.True(outcome.End == RunEnd.Failed && view.LastFailure is { Title: "Build: App", Errors: 2 }, $"the build failed with two errors ({outcome.End})");
        var card = view.Blocks.OfType<PanelCard>().Single(c => c.Title == "Build: App failed");
        var buttons = card.Blocks.OfType<PanelButtons>().Single().Buttons;
        Check.True(buttons is [{ Title: "Fix with AI", Enabled: true }, { Title: "Run again", Enabled: true }] && view.CanFixWithAi, "the view offers Fix with AI and Run again");

        var fix = await view.FixWithAiAsync(null).WaitAsync(TimeSpan.FromSeconds(30));
        string task = chat.Requests[0].Messages.Last(m => m.Role == "user").Content;
        Check.True(task.Contains("a.cs(1,10): error CS1002: ; expected", StringComparison.Ordinal) && task.Contains("b.cs(3,13): error CS0103", StringComparison.Ordinal)
                   && task.Contains("    1| int x = 1", StringComparison.Ordinal) && task.Contains("    3|     int z = y;", StringComparison.Ordinal),
            $"several errors go to the agent with the code around each ({task})");
        Check.True(fix is { Accepted: true, Changed.Count: 2 }, $"each file's fix arrived as a diff and was accepted ({fix?.Summary})");
        Check.True(fixture.Presenter.Proposals.Count == 2 && fixture.Presenter.Proposals.All(p => p.Origin!.StartsWith("Agent", StringComparison.Ordinal)), "through the diff view, from the agent");
        Check.True(view.LastFix is { Accepted: true } && view.Blocks.OfType<PanelCard>().Single().Blocks.OfType<PanelButtons>().Single().Buttons[1] is { Title: "Run again", Kind: PanelButtonKind.Primary },
            "Run again is the next step");

        fixture.Sandbox.Lines = [];
        fixture.Sandbox.ExitCode = 0;
        var again = await view.RunAgainAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Check.True(again is { End: RunEnd.Succeeded } && fixture.Sandbox.Runs == 2 && view.LastFailure is null, "Run again runs the same configuration; it succeeds and the failure goes");
        Check.Equal("int x = 1;\n", await File.ReadAllTextAsync(a), "the accepted fix was on disk before the run");
        Check.Equal("class B\n{\n    int z = 0;\n}\n", await File.ReadAllTextAsync(b), "both files");
    }

    [Test]
    public static async Task OneErrorGoesToTheChatsFixItsRowSaysFixWithAiAndTurnedOffNothingIsOffered()
    {
        using var folder = new TempFolder();
        string root = folder.Path;
        string a = Path.Combine(root, "a.cs");
        await File.WriteAllTextAsync(a, "int x = 1\n");
        var chat = FakeChatModel.Script(FakeChatModel.Answer("```csharp\nint x = 1;\n```"));
        using var fixture = new Fixture(root, chat, [$"{a}(1,10): error CS1002: ; expected"]);
        var view = fixture.View;
        await view.RunAsync(fixture.Configuration).WaitAsync(TimeSpan.FromSeconds(20));
        Check.True(view.LastFailure is { Errors: 1 } && view.ProblemFiles.Count == 1, "the build failed with one error, in Problems");

        // A build's Problems row says "Fix with AI"; another file's row "Fix".
        var problems = new ProblemsList();
        var built = view.ProblemFiles[0];
        var other = TextDocument.Create("x\n").Buffer.Current;
        problems.Update([new ProblemSource(built.Path + "|run", "a.cs (build)", built.Snapshot, built.Diagnostics, built), new ProblemSource("o", "o.cs", other, [new EditorDiagnostic(0, 1, DiagnosticSeverity.Error, "E1", "bad")])], 100, hasActions: true);
        Check.True(ProblemsList.FixTitleOf(problems.Groups[0].Items[0]) == "Fix with AI" && ProblemsList.FixTitleOf(problems.Groups[1].Items[0]) == "Fix", "the build's row offers Fix with AI");

        var fix = await view.FixProblemAsync(built, built.Diagnostics[0]).WaitAsync(TimeSpan.FromSeconds(20));
        Check.True(fix is { Accepted: true, Changed.Count: 1 } && chat.Requests.Single().Tools is null && chat.Requests[0].Messages[^1].Content.Contains("CS1002", StringComparison.Ordinal),
            $"one error: the chat's model rewrites its lines, no agent ({fix?.Summary})");
        Check.True(fixture.Presenter.Proposals.Single().Title.Contains("Fix CS1002", StringComparison.Ordinal), "a diff the user accepted");
        var changed = fix!.Changed[0];
        Check.Equal("int x = 1;\n", changed.Document.Buffer.Current.GetText(0, changed.Document.Buffer.Current.Length), "applied in the editor");
        changed.Document.Undo();
        Check.Equal("int x = 1\n", changed.Document.Buffer.Current.GetText(0, changed.Document.Buffer.Current.Length), "one undo step");
        changed.Document.Redo();
        Check.True(changed.Document.IsDirty && await File.ReadAllTextAsync(a) == "int x = 1\n", "not saved yet");
        fixture.Sandbox.ExitCode = 0;
        fixture.Sandbox.Lines = [];
        var again = await view.RunAgainAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Check.True(again is { End: RunEnd.Succeeded } && await File.ReadAllTextAsync(a) == "int x = 1;\n" && !changed.Document.IsDirty, "Run again saves the accepted fix, then runs");

        // Turned off (CRAFT_PLUGIN_AI_BUILDFIX): nothing is offered.
        fixture.Sandbox.ExitCode = 1;
        fixture.Sandbox.Lines = [$"{a}(1,1): error CS1000: broken"];
        fixture.Settings = fixture.Settings with { BuildFix = false };
        await view.RunAsync(fixture.Configuration).WaitAsync(TimeSpan.FromSeconds(20));
        Check.True(!view.CanFixWithAi && view.Blocks.OfType<PanelCard>().Single().Blocks.OfType<PanelButtons>().Single().Buttons[0] is { Title: "Fix with AI", Enabled: false },
            "turned off: Fix with AI is not offered");
        Check.True(await view.FixWithAiAsync(null) is null && chat.Requests.Count == 1, "and asks nothing");
    }

    /// <summary>A workspace with one target, a sandbox playing a build's output, a host that accepts every diff, and the AI plug-in's providers over a fake model.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly AiChatPanel _chat;
        private readonly AiAgentPanel _agent;
        private readonly AiBuildFix _fix;
        private readonly string _root;

        public Fixture(string root, FakeChatModel chat, IReadOnlyList<string> lines)
        {
            _root = root;
            Sandbox = new PlayingSandbox { Lines = lines, ExitCode = 1 };
            CommandSandboxes.Register(Sandbox);
            RunProviders.Register(new OneTarget());
            ModelProviders.Register(new FakeProvider(chat));
            EditorToolSets.Register(EditorToolSet.Instance);
            Settings = new AiSettings
            {
                ModelProvider = ProviderName, Model = "fake-7b", CompletionModel = "", Completion = false, ChatKeep = false,
                AgentSandbox = "none-here", AgentFolder = root + "-copies", AgentMaxTurns = 8, BuildFixContextLines = 2,
            };
            Models = new AiModels(() => Settings);
            var options = new CraftOptions { RunSandbox = Sandbox.Name, WorkspaceWatcher = FolderWatchers.None };
            Host = new EditorHost(options, action => action(), Presenter) { WorkspaceFolder = root };
            Host.OpenFile = async (path, token) => Host.Open(await TextDocument.OpenAsync(path, cancellationToken: token), null);
            _chat = new AiChatPanel(Host, () => Settings, Models);
            _agent = new AiAgentPanel(Host, () => Settings, Models);
            _fix = new AiBuildFix(Host, () => Settings, new AiCodeActions(Host, () => Settings, Models, _chat), _agent);
            BuildFixProviders.Register(_fix);
            View = new RunView(Host, () => root, () => options);
            View.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }

        public AiSettings Settings { get; set; }

        public AiModels Models { get; }

        public EditorHost Host { get; }

        public RunView View { get; }

        public PlayingSandbox Sandbox { get; }

        public AcceptingPresenter Presenter { get; } = new();

        public RunConfiguration Configuration { get; } = new("App", OneTarget.ProviderName, "app") { Action = RunActions.Build };

        public void Dispose()
        {
            View.Dispose();
            BuildFixProviders.Unregister(_fix.Name);
            _agent.Dispose();
            _chat.Dispose();
            Host.Dispose();
            Models.Dispose();
            AiAgentPanel.DiscardsDone().GetAwaiter().GetResult();
            ModelProviders.Unregister(ProviderName);
            EditorToolSets.Unregister(EditorToolSets.Editor);
            RunProviders.Unregister(OneTarget.ProviderName);
            CommandSandboxes.Unregister(Sandbox.Name);
            if (Directory.Exists(_root + "-copies"))
            {
                Directory.Delete(_root + "-copies", recursive: true);
            }
        }
    }

    private sealed class FakeProvider(IChatModel chat) : IModelProvider
    {
        public string Name => ProviderName;

        public string Summary => "the build fix tests' models";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelLease(request.Model, chat, null, 4096, TimeSpan.Zero));
    }

    /// <summary>One target that builds ("app", in the workspace's root).</summary>
    private sealed class OneTarget : IRunProvider
    {
        public const string ProviderName = "build-fix-target";

        public string Name => ProviderName;

        public string Summary => "the build fix tests' target";

        public string Title => "Test";

        public bool Watches(ReadOnlySpan<char> fileName) => false;

        public IReadOnlyList<RunTarget> Find(RunWorkspace workspace, CancellationToken cancellationToken = default) =>
            [new RunTarget(ProviderName, "app", "App", "app.proj", RunActions.Build | RunActions.Run)];

        public RunCommand Command(RunTarget target, RunActions action, RunConfiguration? configuration, string root) => new(["build"], root);
    }

    /// <summary>A sandbox that plays the lines it is given and ends with its exit code.</summary>
    internal sealed class PlayingSandbox : ICommandSandbox
    {
        public IReadOnlyList<string> Lines { get; set; } = [];

        public int ExitCode { get; set; }

        public int Runs { get; private set; }

        public string Name => "build-fix-play";

        public string Summary => "plays a build's output";

        public string? Check(CommandLimits limits) => null;

        public ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            Runs++;
            foreach (var line in Lines)
            {
                limits.Output?.Invoke(line);
            }

            return ValueTask.FromResult(new CommandResult(ExitCode, false, string.Join('\n', Lines), TimeSpan.FromMilliseconds(5), false));
        }
    }

    /// <summary>Runs every run asked (the first answer) and accepts every hunk of every diff, recording the proposals.</summary>
    internal sealed class AcceptingPresenter : IEditorPresenter
    {
        public List<EditProposal> Proposals { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => throw new InvalidOperationException("the host asks per hunk");

        public Task<IReadOnlyList<bool>> ChooseHunksAsync(EditProposal proposal, CancellationToken cancellationToken)
        {
            lock (Proposals)
            {
                Proposals.Add(proposal);
            }

            return Task.FromResult<IReadOnlyList<bool>>(Enumerable.Repeat(true, Math.Max(1, proposal.Diff.Hunks.Count)).ToArray());
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
