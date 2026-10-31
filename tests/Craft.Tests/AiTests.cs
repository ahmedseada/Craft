// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Plugins.Tools;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;
using Idrak.Mcp;
using Idrak.Text.Generation;

namespace Craft.Tests;

/// <summary>
/// Phase A1: the diff (TextDiff), the diff-and-accept path (a model's edit arrives as a diff, accepted as one undo step;
/// per-hunk answers), the editor's tools in both backings, the live tools over MCP, the AI plug-in and the "local"
/// model provider's registration.
/// </summary>
[TestGroup("Ai")]
public static class AiTests
{
    [Test]
    public static void DiffShowsTheChangedLinesWithContext()
    {
        var diff = TextDiff.OfTexts("a\nb\nc\nd\ne\nf\ng\nh\ni\nj\n", "a\nb\nC\nd\ne\nf\ng\nh\nI\nj\nk\n", contextLines: 1);
        Check.Equal(2, diff.Hunks.Count, "two changes far apart are two hunks");
        Check.Equal(new DiffHunk(2, 3, 2, 3, diff.Hunks[0].Lines), diff.Hunks[0], "the first hunk covers lines 2-4 on both sides");
        Check.SequenceEqual(
            [new DiffLine(DiffLineKind.Context, 2, 2, "b"), new DiffLine(DiffLineKind.Removed, 3, 0, "c"), new DiffLine(DiffLineKind.Added, 0, 3, "C"), new DiffLine(DiffLineKind.Context, 4, 4, "d")],
            diff.Hunks[0].Lines, "the first hunk's lines");
        Check.True(diff is { Removed: 2, Added: 3, Coarse: false }, "the counts");
        var unified = diff.ToUnified("a/x.txt", "b/x.txt");
        Check.True(unified.StartsWith("--- a/x.txt\n+++ b/x.txt\n@@ -2,3 +2,3 @@\n b\n-c\n+C\n d\n@@ -8,3 +8,4 @@\n", StringComparison.Ordinal), $"a unified patch ({unified})");
        Check.True(TextDiff.OfTexts("same\n", "same\n").IsEmpty, "no change, no hunk");
        var noBreak = TextDiff.OfTexts("x\ny", "x\nz").ToUnified("a", "b");
        Check.True(noBreak.Contains("-y\n\\ No newline at end of file\n+z\n\\ No newline at end of file\n", StringComparison.Ordinal), $"a last line without a break is marked ({noBreak})");
        var coarse = TextDiff.OfTexts("1\n2\n3\n", "4\n5\n6\n", lineLimit: 2);
        Check.True(coarse is { Coarse: true, Removed: 3, Added: 3 }, "past the line limit the change is shown whole");
    }

    [Test]
    public static void DiffRebuildsTheNewTextFromRandomEdits()
    {
        var random = new Random(1031);
        for (int round = 0; round < 300; round++)
        {
            var before = Enumerable.Range(0, random.Next(0, 40)).Select(_ => ((char)('a' + random.Next(5))).ToString()).ToList();
            var after = new List<string>(before);
            for (int e = random.Next(1, 6); e > 0; e--)
            {
                int at = random.Next(after.Count + 1);
                switch (random.Next(3))
                {
                    case 0:
                        after.Insert(at, ((char)('a' + random.Next(5))).ToString());
                        break;
                    case 1 when at < after.Count:
                        after.RemoveAt(at);
                        break;
                    default:
                        if (at < after.Count)
                        {
                            after[at] = "z";
                        }

                        break;
                }
            }

            string oldText = before.Count == 0 ? "" : string.Join('\n', before) + "\n";
            string newText = after.Count == 0 ? "" : string.Join('\n', after) + "\n";
            var diff = TextDiff.OfTexts(oldText, newText, contextLines: random.Next(0, 4));
            Check.Equal(newText, Apply(before, diff), $"round {round}: the hunks applied to the old lines give the new text");
            Check.True(diff.Removed + diff.Added <= before.Count + after.Count, "no more changed lines than there are lines");
        }

        static string Apply(List<string> lines, TextDiff diff)
        {
            var result = new List<string>();
            int next = 1;
            foreach (var hunk in diff.Hunks)
            {
                int start = hunk.OldCount == 0 ? hunk.OldStart + 1 : hunk.OldStart;
                while (next < start)
                {
                    result.Add(lines[next++ - 1]);
                }

                foreach (var line in hunk.Lines)
                {
                    if (line.Kind != DiffLineKind.Removed)
                    {
                        result.Add(line.Text);
                    }

                    if (line.Kind != DiffLineKind.Added)
                    {
                        Check.Equal(lines[next - 1], line.Text, "a kept or removed line is the old line");
                        next++;
                    }
                }
            }

            while (next <= lines.Count)
            {
                result.Add(lines[next++ - 1]);
            }

            return result.Count == 0 ? "" : string.Join('\n', result) + "\n";
        }
    }

    [Test]
    public static async Task AFakeChatModelsEditArrivesAsADiffAndIsOneUndoStep()
    {
        var presenter = new DiffPresenter();
        using var host = new EditorHost(presenter: presenter);
        using var document = TextDocument.Create("class A\n{\n    int x;\n}\n");
        var open = host.Open(document, null);
        var tools = ToolRegistry.Create().Add(EditorTools.Live(host, new ToolSettings())).Build();
        var model = FakeChatModel.Script(
            FakeChatModel.ToolCall(EditorToolNames.ReadDocument, new JsonObject { ["path"] = "untitled-1" }),
            FakeChatModel.ToolCall(EditorToolNames.ProposeEdit, new JsonObject
            {
                ["path"] = "untitled-1", ["old_text"] = "    int x;", ["new_text"] = "    /// <summary>The count.</summary>\n    int x;", ["title"] = "Add a doc comment",
            }),
            FakeChatModel.Answer("Done."));
        var reply = await model.WithTools(tools, 4).ChatAsync(new ChatRequest([new ChatMessage("user", "Document the field.")]));

        Check.Equal("Done.", reply.Message!.Content, "the model answered after its tool calls");
        Check.True(model.Requests[1].Messages[^1] is { Role: "tool" } read && read.Content.Contains("int x;", StringComparison.Ordinal), "read_document gave the model the text");
        Check.True(model.Requests[2].Messages[^1].Content.StartsWith("Accepted by the user", StringComparison.Ordinal), $"the model is told the user accepted ({model.Requests[2].Messages[^1].Content})");
        var proposal = presenter.Proposals.Single();
        Check.True(proposal.Diff is { Hunks.Count: 1, Added: 1, Removed: 0 }, "the edit arrived as a diff of one added line");
        Check.True(proposal.Diff.Hunks[0].Lines.Any(l => l is { Kind: DiffLineKind.Added, Text: "    /// <summary>The count.</summary>" }), "the added line is in the diff");
        Check.True(proposal.Placement == "exact text match" && proposal.Title == "Add a doc comment", "how it was placed and its title travel with it");
        Check.Equal("class A\n{\n    /// <summary>The count.</summary>\n    int x;\n}\n", document.Buffer.Current.GetText(0, document.Buffer.Current.Length), "accepted, it is applied");
        document.Undo();
        Check.Equal("class A\n{\n    int x;\n}\n", document.Buffer.Current.GetText(0, document.Buffer.Current.Length), "one undo step takes it all back");
        Check.True(!document.CanUndo, "and it was one step");
        Check.True(open.Document == document, "the same document");
    }

    [Test]
    public static async Task TheUserAcceptsSomeHunksOnly()
    {
        var presenter = new DiffPresenter { Answers = [false, true] };
        using var host = new EditorHost(new CraftOptions { DiffContextLines = 1 }, presenter: presenter);
        using var document = TextDocument.Create("1\n2\n3\n4\n5\n6\n7\n8\n9\n");
        var open = host.Open(document, null);
        bool applied = await host.ProposeAsync(open, new ProposedEdits([new TextEdit(0, 1, "one"), new TextEdit(16, 1, "nine")], "Two changes") { Placement = "given" });
        Check.True(applied, "a part accepted is applied");
        Check.True(presenter.Proposals[0].Diff.Hunks.Count == 2 && presenter.Proposals[0].HunkOfEdit is [0, 1], "two hunks, one edit each");
        Check.Equal("1\n2\n3\n4\n5\n6\n7\n8\nnine\n", document.Buffer.Current.GetText(0, document.Buffer.Current.Length), "only the second hunk is applied");
        document.Undo();
        Check.True(!document.CanUndo && document.Buffer.Current.GetText(0, 2) == "1\n", "as one undo step");
        presenter.Answers = [false, false];
        Check.True(!await host.ProposeAsync(open, new ProposedEdits([new TextEdit(0, 1, "one"), new TextEdit(16, 1, "nine")], "Two changes")), "all rejected, nothing applied");
        Check.True(await presenter.Proposals[1].Check is null, "plain text has no semantic check");
    }

    [Test]
    public static async Task AnMcpClientReadsAnOpenDocument()
    {
        using var host = new EditorHost(presenter: new DiffPresenter());
        using var document = TextDocument.Create("namespace Demo;\n\npublic sealed class Served;\n");
        host.Open(document, null);
        var none = Check.Throws<NotRegisteredException>(() => _ = EditorMcpServer.StartAsync(host, new AiSettings()), "no tool set registered");
        Check.True(none.Message.Contains("Craft.Plugins.Tools", StringComparison.Ordinal), $"the refusal names the tools plug-in ({none.Message})");
        EditorToolSets.Register(EditorToolSet.Instance);
        try
        {
            await ServeAsync(host);
        }
        finally
        {
            EditorToolSets.Unregister(EditorToolSets.Editor);
        }
    }

    private static async Task ServeAsync(EditorHost host)
    {
        await using var server = await EditorMcpServer.StartAsync(host, new AiSettings());
        Check.True(server.Address.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) && server.Token is { Length: > 0 }, $"a loopback address and a token ({server.Address})");
        await using var client = await McpTools.ConnectHttpAsync(new Uri(server.Address), server.Token);
        var tools = await client.ListToolsAsync();
        Check.SequenceEqual(
            [EditorToolNames.FindSymbol, EditorToolNames.GetDiagnostics, EditorToolNames.GetSelection, EditorToolNames.OpenDocuments, EditorToolNames.ProposeEdit, EditorToolNames.ReadDocument,
                EditorToolNames.SearchWorkspace],
            [.. tools.Select(t => t.Definition.Name).Order(StringComparer.Ordinal)], "the live tools are served (the server lists them in its own order)");
        var listed = await client.CallAsync(EditorToolNames.OpenDocuments, []);
        Check.True(listed.Contains("untitled-1", StringComparison.Ordinal), $"open_documents names it ({listed})");
        var read = await client.CallAsync(EditorToolNames.ReadDocument, new JsonObject { ["path"] = "untitled-1" });
        Check.True(read.Contains("public sealed class Served;", StringComparison.Ordinal), $"read_document gives the open document's text ({read})");
        var found = await client.CallAsync(EditorToolNames.SearchWorkspace, new JsonObject { ["query"] = "Served" });
        Check.True(found.Contains("untitled-1:3:", StringComparison.Ordinal), $"search_workspace finds it ({found})");
        await ThrowsAsync(() => McpTools.ConnectHttpAsync(new Uri(server.Address), "wrong"), "a wrong token is refused");
    }

    [Test]
    public static async Task TheFolderToolsReadSearchAndWriteAPatch()
    {
        var folder = Directory.CreateTempSubdirectory("craft-ai-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "src"));
            Directory.CreateDirectory(Path.Combine(folder, "bin"));
            await File.WriteAllTextAsync(Path.Combine(folder, "src", "A.cs"), "class A\n{\n}\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "bin", "A.cs"), "class Skipped\n");
            var tools = ToolRegistry.Create().Add(EditorTools.Folder(folder, new ToolSettings())).Build();
            Check.SequenceEqual([EditorToolNames.ListFiles, EditorToolNames.ReadDocument, EditorToolNames.SearchWorkspace, EditorToolNames.FindSymbol, EditorToolNames.ProposeEdit],
                [.. tools.Definitions.Select(d => d.Name)], "the folder tools");
            var listed = await Call(tools, EditorToolNames.ListFiles, []);
            Check.Equal("src/A.cs\n", listed, "list_files skips bin/");
            var read = await Call(tools, EditorToolNames.ReadDocument, new JsonObject { ["path"] = "src/A.cs", ["start_line"] = 2 });
            Check.Equal("src/A.cs: lines 2-4 of 4\n{\n}\n\n", read, "read_document from line 2");
            var found = await Call(tools, EditorToolNames.SearchWorkspace, new JsonObject { ["query"] = "class", ["ignore_case"] = true });
            Check.Equal("src/A.cs:1: class A", found, "search_workspace skips bin/");
            var outside = await Call(tools, EditorToolNames.ReadDocument, new JsonObject { ["path"] = "../secret.txt" });
            Check.True(outside.StartsWith("No file", StringComparison.Ordinal), "a path outside the folder is not read");
            var proposed = await Call(tools, EditorToolNames.ProposeEdit, new JsonObject { ["path"] = "src/A.cs", ["old_text"] = "class A", ["new_text"] = "sealed class A" });
            Check.True(proposed.StartsWith("Written as a patch", StringComparison.Ordinal), proposed);
            Check.Equal("class A\n{\n}\n", await File.ReadAllTextAsync(Path.Combine(folder, "src", "A.cs")), "the file is not changed");
            var patch = Directory.GetFiles(Path.Combine(folder, ".craft", "patches")).Single();
            Check.Equal("--- a/src/A.cs\n+++ b/src/A.cs\n@@ -1,3 +1,3 @@\n-class A\n+sealed class A\n {\n }\n", await File.ReadAllTextAsync(patch), "the patch");
            var twice = await Call(tools, EditorToolNames.ProposeEdit, new JsonObject { ["path"] = "src/A.cs", ["old_text"] = "\n", ["new_text"] = "" });
            Check.True(twice.Contains("more than once", StringComparison.Ordinal), "an old_text that is not unique is refused");
            var failed = await tools.InvokeAsync(new ToolCall(EditorToolNames.SearchWorkspace, new JsonObject { ["query"] = "" }));
            Check.True(!failed.Succeeded && failed.Content.Contains("must not be empty", StringComparison.Ordinal), "bad arguments fail with the reason");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        static async Task<string> Call(ToolRegistry tools, string name, JsonObject arguments)
        {
            var result = await tools.InvokeAsync(new ToolCall(name, arguments));
            Check.True(result.Succeeded, $"{name} answered ({result.Error})");
            return result.Content;
        }
    }

    [Test]
    public static async Task ThePluginRegistersServesAndTakesBack()
    {
        Check.True(IdrakPlugins.Load(typeof(AiPlugin).Assembly), "the assembly declares its package");
        try
        {
            Check.True(EditorPlugins.Find("ai") is not null && ModelProviders.Find(ModelProviders.Local) is LocalModelProvider, "the plug-in and the local provider are registered");
            Check.True(EditorCommands.Find(AiPlugin.McpStartCommand) is not null, "its commands are registered");
            Check.True(ToolProviders.Of(typeof(AiPlugin).Assembly).Count == 0, "the tools are the tools plug-in's (Craft.Plugins.Tools), not the AI host's");
            var missing = Check.Throws<NotRegisteredException>(() => ModelProviders.Get("remote"), "a provider that is not registered");
            Check.True(missing.Message.Contains("local", StringComparison.Ordinal), $"the message names those that are ({missing.Message})");

            EditorToolSets.Register(EditorToolSet.Instance);                  // the tools plug-in's set, which the MCP server serves
            var variables = new Dictionary<string, string> { [PluginSetting.VariableName("ai", "mcpToken")] = "secret-token" };
            using var host = new EditorHost(presenter: new DiffPresenter(), readVariable: name => variables.GetValueOrDefault(name));
            host.RefreshPlugins();
            Check.True(host.Plugins.Any(p => p is { Name: "ai", Active: true }), "it activates");
            Check.True(!host.Panels.Any(p => p.Id == "ai.models") && ModelProviders.Get(ModelProviders.Local).Status.Roles.Count > 0,
                "no models side panel (phase U2): the model state is the provider's status, which the Models page reads");
            Check.True(host.Settings.All.Count(s => s.Plugin == "ai") == AiSettings.Declared.Count, "its settings are on the settings page");
            Check.True(!host.StatusItems.Any(i => i.Id == "ai.mcp"), "the MCP server is off by default");
            Check.True(await host.ExecuteCommandAsync(AiPlugin.McpStartCommand), "the command starts it");
            Check.True(host.StatusItems.Single(i => i.Id == "ai.mcp").Text.StartsWith("MCP http://127.0.0.1:", StringComparison.Ordinal), "the status bar shows the address");
            Check.Equal("secret-token", AiPlugin.Current!.Mcp!.Token, "the token setting is used");
            Check.True(await host.ExecuteCommandAsync(AiPlugin.McpStopCommand) && !host.StatusItems.Any(i => i.Id == "ai.mcp"), "the command stops it");

            var empty = Directory.CreateTempSubdirectory("craft-model-").FullName;
            try
            {
                await ThrowsAsync(() => ModelProviders.Get(ModelProviders.Local).AcquireAsync(new ModelRequest(empty) { Device = "cpu" }), "a folder that holds no model fails");
            }
            finally
            {
                Directory.Delete(empty, recursive: true);
            }
        }
        finally
        {
            IdrakPlugins.UnregisterAll(IdrakPlugins.PackageOf(typeof(AiPlugin).Assembly)!);
            EditorToolSets.Unregister(EditorToolSets.Editor);
        }

        Check.True(EditorPlugins.Find("ai") is null && ModelProviders.Find(ModelProviders.Local) is null && EditorCommands.Find(AiPlugin.McpStartCommand) is null,
            "unregistered, it leaves nothing");
    }

    [Test]
    public static void TheSettingsReadEveryVariable()
    {
        var values = new Dictionary<string, string>
        {
            ["mcpAddress"] = "127.0.0.1:7777", ["mcpToken"] = "none", ["mcpOnStart"] = "true", ["model"] = "store:coder", ["device"] = "cpu", ["context"] = "4096",
            ["weights"] = "int8", ["keepAliveMinutes"] = "0", ["threads"] = "2", ["cpuMemoryMb"] = "512", ["gpuMemoryMb"] = "1024", ["idrakPlugins"] = "",
            // Phase A2: completion, chat, placement, agent.
            ["modelProvider"] = "remote", ["completion"] = "false", ["completionModel"] = "store:fim", ["completionDelayMs"] = "150", ["completionMaxTokens"] = "40",
            ["completionMaxLines"] = "3", ["completionSuffixChars"] = "128", ["completionPrefixChars"] = "4000", ["completionChunk"] = "2", ["completionTemperature"] = "0.2",
            ["chatMaxTokens"] = "300", ["chatToolRounds"] = "2", ["chatKeep"] = "false", ["chatFile"] = "c.json", ["chatHistory"] = "10", ["chatFileLines"] = "50", ["placement"] = "csharp-tokens",
            ["placementMaxError"] = "0.1", ["agentSandbox"] = "container", ["agentMaxTurns"] = "5", ["agentMinutes"] = "3", ["agentMaxTokens"] = "500",
            ["agentFolder"] = "/copies", ["agentCopyIgnore"] = "bin",
        };
        Check.Equal(AiSettings.Declared.Count, values.Count, "every declared setting is read");
        var settings = AiSettings.Read(key => values.GetValueOrDefault(key));
        Check.True(settings is
        {
            McpAddress: "127.0.0.1:7777", McpToken: "none",
            McpOnStart: true, Model: "store:coder", Device: "cpu", Context: 4096, Weights: "int8", KeepAliveMinutes: 0, Threads: 2, CpuMemoryMegabytes: 512,
            GpuMemoryMegabytes: 1024, IdrakPlugins.Count: 0,
            ModelProvider: "remote", Completion: false, CompletionModel: "store:fim", CompletionDelayMilliseconds: 150, CompletionMaxTokens: 40, CompletionMaxLines: 3,
            CompletionSuffixCharacters: 128, CompletionPrefixCharacters: 4000, CompletionChunk: 2, ChatMaxTokens: 300, ChatToolRounds: 2, ChatKeep: false,
            ChatFile: "c.json", ChatHistory: 10, ChatFileLines: 50, Placement: "csharp-tokens", PlacementMaxError: 0.1, AgentSandbox: "container", AgentMaxTurns: 5, AgentMinutes: 3,
            AgentMaxTokens: 500, AgentFolder: "/copies",
        } && settings.AgentCopyIgnore.SequenceEqual(["bin"]) && Math.Abs(settings.CompletionTemperature - 0.2f) < 1e-6,
            "each value is read");
        var bad = Check.Throws<ArgumentException>(() => AiSettings.Read(key => key == "threads" ? "many" : null), "a bad value");
        Check.True(bad.Message.Contains("CRAFT_PLUGIN_AI_THREADS", StringComparison.Ordinal), "the error names the variable");
    }

    private static async Task ThrowsAsync(Func<Task> action, string what)
    {
        try
        {
            await action();
        }
#pragma warning disable CA1031 // Any exception is the expected outcome.
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        Check.True(false, $"{what}: it threw nothing");
    }

    /// <summary>A presenter that records each proposal and answers each hunk as told (all accepted by default).</summary>
    private sealed class DiffPresenter : IEditorPresenter
    {
        public bool[]? Answers { get; set; }

        public List<EditProposal> Proposals { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => throw new InvalidOperationException("the host asks per hunk");

        public Task<IReadOnlyList<bool>> ChooseHunksAsync(EditProposal proposal, CancellationToken cancellationToken)
        {
            Proposals.Add(proposal);
            var answers = Answers ?? Enumerable.Repeat(true, Math.Max(1, proposal.Diff.Hunks.Count)).ToArray();
            return Task.FromResult<IReadOnlyList<bool>>(answers);
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }
}
