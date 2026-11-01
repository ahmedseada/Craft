// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core.Documents;
using Craft.Core.Plugins;
using Craft.CSharp;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Plugins.Placement;
using Craft.Plugins.Sampler;
using Craft.Plugins.StarCoder2;
using Craft.Plugins.Tools;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;
using Idrak.Abstraction.Testing;
using Idrak.Generation;
using Idrak.Layers;
using Idrak.Mcp;
using Idrak.Models;
using Idrak.Models.Abstractions;
using Idrak.Text.Abstractions;
using Idrak.Text.Generation;

namespace Craft.Tests;

/// <summary>
/// Phase P, the first batch: the plug-ins that test Idrak's plug-in points from a real host. Each is loaded the way the app
/// loads it, from a copy of its own build output through <see cref="CraftPlugins.LoadFolder"/> into its own load context,
/// and checked through the registry its consumer reads (never through its types): P1 the editor's tools (EditorToolSets,
/// [assembly: ToolProvider], McpHost), P2 StarCoder2 (PretrainedArchitectures, FillInMiddleFormats), P3 the bracket-balanced
/// sampler (TokenSamplers over the library's default), P5 the approve-each sandbox (CommandSandboxes). The five checks: a
/// new kind works end to end, an override shadows the default and Unregister brings it back, a missing name fails "not
/// registered", the assembly loads from the plug-in folder, the same assembly serves every host.
/// </summary>
[TestGroup("PhaseP")]
public static class PhasePTests
{
    // ---- P1: the editor's tools ----

    [Test]
    public static async Task P1ToolsLoadFromTheirFolderAndServeTheEditorAndTheIdrakTool()
    {
        Check.True(EditorToolSets.Find(EditorToolSets.Editor) is null, "before the plug-in loads, no tool set is registered");
        var missing = Check.Throws<NotRegisteredException>(() => EditorToolSets.Get("no-such-tools"), "a tool set nobody registered");
        Check.True(missing.Message.Contains("not registered", StringComparison.Ordinal), $"it fails as not registered ({missing.Message})");

        using var temp = new TempFolder();
        var loaded = LoadFromFolder(temp, "Craft.Plugins.Tools");
        try
        {
            var set = EditorToolSets.Get(EditorToolSets.Editor);
            Check.True(AssemblyLoadContext.GetLoadContext(set.GetType().Assembly) != AssemblyLoadContext.Default, "the set comes from the plug-in's own load context");
            Check.Equal("Craft.Plugins.Tools", EditorToolSets.Origin(EditorToolSets.Editor), "its origin is the plug-in");
            Check.True(EditorPlugins.Find("tools") is not null, "the editor plug-in that declares the tools' settings is registered");

            // One assembly, every host: the editor's MCP server (Craft.Ai reads the registry, never the assembly) ...
            using var host = new EditorHost();
            using var document = TextDocument.Create("public sealed class Served;\n");
            host.Open(document, null);
            await using (var server = await EditorMcpServer.StartAsync(host, new AiSettings()))
            {
                await using var client = await McpTools.ConnectHttpAsync(new Uri(server.Address), server.Token);
                var names = (await client.ListToolsAsync()).Select(t => t.Definition.Name).ToHashSet(StringComparer.Ordinal);
                Check.True(names.SetEquals(["open_documents", "read_document", "get_selection", "get_diagnostics", "search_workspace", "find_symbol", "propose_edit"]),
                    $"the running editor serves the plug-in's live tools over MCP ({string.Join(", ", names)})");
                var read = await client.CallAsync("read_document", new JsonObject { ["path"] = "untitled-1" });
                Check.True(read.Contains("class Served", StringComparison.Ordinal), $"read_document over MCP reads the open document ({read})");
            }

            // ... and the idrak tool (tools list/test, mcp serve), which reads [assembly: ToolProvider] from the same dll.
            var forIdrak = ToolProviders.Of(loaded);
            Check.SequenceEqual(["list_files", "read_document", "search_workspace", "find_symbol", "propose_edit"], [.. forIdrak.Select(t => t.Definition.Name)],
                "the assembly declares the folder tools to the idrak tool");

            // An override: a plug-in's set with a tool of the same name shadows the editor's; unregistered, the editor's is back.
            var mine = Tool.Create("read_document", "the override's read", Schema(), (_, _) => Task.FromResult("from the override"));
            var overriding = new TestToolSet("test-override", [mine]);
            EditorToolSets.Register(overriding);
            try
            {
                var read = EditorToolSets.Live(host).Single(t => t.Definition.Name == "read_document");
                Check.Equal("the override's read", read.Definition.Description, "the newer set's tool shadows the editor's of the same name");
                Check.Equal(7, EditorToolSets.Live(host).Count, "the other tools stay the editor's");
            }
            finally
            {
                EditorToolSets.Unregister("test-override");
            }

            Check.True(EditorToolSets.Live(host).Single(t => t.Definition.Name == "read_document").Definition.Description != "the override's read",
                "unregistered, the editor's read_document is back");
        }
        finally
        {
            IdrakPlugins.UnregisterAll(IdrakPlugins.PackageOf(loaded)!);
        }

        Check.True(EditorToolSets.Names.Count == 0 && EditorPlugins.Find("tools") is null, "unregistered, the plug-in leaves nothing");
    }

    [Test]
    public static async Task P1FindSymbolUsesTheSemanticSessionLiveAndTheLanguageOverAFolder()
    {
        using var project = new SemanticTests.Project();
        project.Replace("public static void Run()", "public static void RunAll()");   // unsaved: only the live tools see it

        using var host = new EditorHost();
        using var document = TextDocument.Create(project.Text);
        var open = host.Open(document, EditorLanguages.Get("csharp"));
        host.SetSemanticSession(open, project.Session);
        var live = ToolRegistry.Create().Add(EditorTools.Live(host, new ToolSettings())).Build();
        var circle = await Call(live, "find_symbol", new JsonObject { ["query"] = "Circle" });
        Check.True(circle.StartsWith("Shapes.cs:4:", StringComparison.Ordinal) || circle.Contains("Shapes.cs:4:", StringComparison.Ordinal), $"the class first, by its file and line ({circle})");
        Check.True(circle.Contains("class Circle", StringComparison.Ordinal), $"its kind and name ({circle})");
        var unsaved = await Call(live, "find_symbol", new JsonObject { ["query"] = "RunAll" });
        Check.True(unsaved.Contains("method RunAll", StringComparison.Ordinal) && unsaved.Contains("Program.cs", StringComparison.Ordinal),
            $"the live tool sees the open document's unsaved text ({unsaved})");

        var folder = ToolRegistry.Create().Add(EditorTools.Folder(project.Root, new ToolSettings())).Build();
        var area = await Call(folder, "find_symbol", new JsonObject { ["query"] = "area" });
        Check.True(area.Contains("Shapes.cs:", StringComparison.Ordinal) && area.Contains("method Area", StringComparison.Ordinal), $"the folder tool finds a method from disk ({area})");
        var shared = await Call(folder, "find_symbol", new JsonObject { ["query"] = "RunAll" });
        Check.True(shared.Contains("method RunAll", StringComparison.Ordinal), $"in a process with a session open on the folder, the folder tool shares its workspace, unsaved text included ({shared})");
        var none = await Call(folder, "find_symbol", new JsonObject { ["query"] = "NoSuchSymbolAnywhere" });
        Check.True(none.StartsWith("No symbol", StringComparison.Ordinal), $"a name nothing declares ({none})");
    }

    // ---- P2: StarCoder2 ----

    [Test]
    public static void P2StarCoder2LoadsFromItsFolderAndItsFormatPassesTheSuite()
    {
        var formatMissing = Check.Throws<NotRegisteredException>(() => FillInMiddleFormats.Get(StarCoder2Plugin.FillInMiddleFormat), "the format before the plug-in loads");
        Check.True(formatMissing.Message.Contains("starcoder2", StringComparison.Ordinal), $"it names the format ({formatMissing.Message})");
        Check.Throws<NotRegisteredException>(() => PretrainedArchitectures.Get(StarCoder2Plugin.Starcoder2ForCausalLM), "the architecture before the plug-in loads");
        var tokenizer = FimTokenizer(StarCoder2Tokens);
        Check.True(FillInMiddleFormats.Detect(tokenizer) != StarCoder2Plugin.FillInMiddleFormat, "a tokenizer with StarCoder2's tokens detects no starcoder2 format before it loads");

        using var temp = new TempFolder();
        var loaded = LoadFromFolder(temp, "Craft.Plugins.StarCoder2");
        try
        {
            Check.Equal("Craft.Plugins.StarCoder2", FillInMiddleFormats.Origin(StarCoder2Plugin.FillInMiddleFormat), "the format's origin is the plug-in");
            var format = FillInMiddleFormats.Get(StarCoder2Plugin.FillInMiddleFormat);
            Check.True(AssemblyLoadContext.GetLoadContext(format.GetType().Assembly) == AssemblyLoadContext.Default
                       && AssemblyLoadContext.GetLoadContext(loaded) != AssemblyLoadContext.Default,
                "the plug-in is in its own context; the layout it registered is the shared library's type");
            Check.Equal(StarCoder2Plugin.FillInMiddleFormat, FillInMiddleFormats.Detect(tokenizer), "StarCoder2's tokenizer is detected");
            Check.True(FillInMiddleFormats.Detect(FimTokenizer(["<fim_prefix>", "<fim_suffix>", "<fim_middle>", "<filename>", "<|endoftext|>"])) != StarCoder2Plugin.FillInMiddleFormat,
                "StarCoder (1)'s tokenizer, without <file_sep>, is not taken for StarCoder2's");

            var report = Conformance.CheckFillInMiddleFormat(StarCoder2Plugin.FillInMiddleFormat, tokenizer);
            Check.True(report.Passed, $"FillInMiddleFormatSuite passes ({report.Cases} cases): {string.Join("; ", report.Failures.Take(3))}");

            var prompt = FillInMiddleFormats.Create(StarCoder2Plugin.FillInMiddleFormat, new FillInMiddleContext(tokenizer, "def f():\n    ", "\n    return x")
            {
                Snippets = [new("a.py", "A = 1")], Path = "b.py",
            });
            Check.Equal("<file_sep>a.py\nA = 1\n<file_sep>b.py\n<fim_prefix>def f():\n    <fim_suffix>\n    return x<fim_middle>", prompt.Text, "the layout BigCode's harness writes, files first");
            Check.SequenceEqual(["<|endoftext|>", "<file_sep>"], prompt.Stop, "it ends on the harness's stop words");
        }
        finally
        {
            IdrakPlugins.UnregisterAll(IdrakPlugins.PackageOf(loaded)!);
        }

        Check.True(!FillInMiddleFormats.Names.Contains(StarCoder2Plugin.FillInMiddleFormat), "unregistered, the format is gone");
    }

    [Test]
    public static void P2StarCoder2ModelIsMadeSavedWithTransformersNamesAndRuns()
    {
        IdrakPlugins.Register(StarCoder2Plugin.Plugin);
        try
        {
            using var temp = new TempFolder();
            var tokenizerFolder = temp.File("tokenizer");
            Directory.CreateDirectory(tokenizerFolder);
            File.WriteAllText(Path.Combine(tokenizerFolder, "tokenizer.json"), FimTokenizerJson(StarCoder2Tokens).ToJsonString());
            var spec = new DecoderSpec
            {
                Vocabulary = 263, Dim = 32, Layers = 2, Heads = 4, KvHeads = 2, HeadDim = 8, FfDim = 64, MaxPositions = 128, Rope = new RopeSettings(10000f),
                Norm = DecoderNorm.Layer, NormEpsilon = 1e-5f, Gated = false, Activation = FeedForwardActivation.Gelu, QkvBias = true, OutputBias = true,
                FeedForwardBias = true, TieEmbeddings = true, SlidingWindow = 64,
            };
            var files = new ModelFiles
            {
                Tokenizer = PretrainedTokenizers.Load(tokenizerFolder), EosToken = "<|endoftext|>",
                ChatTemplate = "{%- for message in messages -%}{{- message['content'] -}}{%- endfor -%}",       // a base model: no chat turns
            };
            var folder = temp.File("model");
            using var model = PretrainedModel.Create(folder, spec, StarCoder2Plugin.Starcoder2ForCausalLM, files, new CreateOptions { Device = Idrak.Abstraction.Device.Cpu, Seed = 3, Overwrite = true });
            var config = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "config.json")))!.AsObject();
            Check.True((string?)config["model_type"] == "starcoder2" && (bool?)config["use_bias"] == true && config["norm_epsilon"] is not null && (string?)config["hidden_act"] == "gelu_pytorch_tanh",
                $"config.json in StarCoder2's keys ({config.ToJsonString()})");
            Check.Equal(spec, model.Spec with { MaxPositions = spec.MaxPositions }, "read back, the spec is the one written");

            using var weights = SafeTensorsReader.Open(Path.Combine(folder, "model.safetensors"));
            var expected = new HashSet<string>(StringComparer.Ordinal) { "model.embed_tokens.weight", "model.norm.weight", "model.norm.bias" };
            for (int i = 0; i < spec.Layers; i++)
            {
                foreach (var part in (string[])["self_attn.q_proj", "self_attn.k_proj", "self_attn.v_proj", "self_attn.o_proj", "mlp.c_fc", "mlp.c_proj", "input_layernorm", "post_attention_layernorm"])
                {
                    expected.Add($"model.layers.{i}.{part}.weight");
                    expected.Add($"model.layers.{i}.{part}.bias");
                }
            }

            Check.True(weights.Tensors.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected),
                $"the checkpoint has transformers' Starcoder2 names, and only those (modular_starcoder2.py): extra {string.Join(", ", weights.Tensors.Keys.Except(expected))}; missing {string.Join(", ", expected.Except(weights.Tensors.Keys))}");

            var generator = new TextGenerator(model.Network, model.Tokenizer!, 128);
            var prompt = FillInMiddleFormats.For((IPretrainedTokenizer)model.Tokenizer!).Create(new FillInMiddleContext((IPretrainedTokenizer)model.Tokenizer!, "int f() {", "}"));
            var (text, _, stats) = generator.Generate(prompt.Text, new GenerationOptions { NumPredict = 8, Seed = 1, Temperature = 1f, Stop = prompt.Stop });
            Check.True(stats.GeneratedTokens > 0, $"the tiny StarCoder2 completes a FIM prompt ({stats.GeneratedTokens} tokens: '{text}')");
        }
        finally
        {
            IdrakPlugins.Unregister(StarCoder2Plugin.Plugin);
        }
    }

    // ---- P3: the bracket-balanced sampler ----

    [Test]
    public static void P3SamplerShadowsTheDefaultPassesTheSuiteAndUnregisterBringsItBack()
    {
        Check.Equal(Idrak.Abstraction.Overrides.Library, TokenSamplers.Origin(TokenSamplers.DefaultName), "before it loads, the default sampler is the library's");
        Check.Throws<NotRegisteredException>(() => TokenSamplers.Get("bracket-balanced"), "a sampler name nobody registered");
        using var temp = new TempFolder();
        var loaded = LoadFromFolder(temp, "Craft.Plugins.Sampler");
        try
        {
            Check.Equal("Craft.Plugins.Sampler", TokenSamplers.Origin(TokenSamplers.DefaultName), "the plug-in shadows the library's default");
            Check.True(TokenSamplers.Default(TokenSamplers.DefaultName) is { } library && library.Summary != TokenSamplers.Get(TokenSamplers.DefaultName).Summary,
                "the library's stays behind it");
            var row = PluginList.Rows(CraftPlugins.Installed, []).Last(r => r.Name == "Craft.Plugins.Sampler");
            Check.True(row.Registries.Contains("TokenSamplers \"default\"", StringComparison.Ordinal) && row.State == PluginState.Outdated
                       && row.StateText.Contains("shadows the library's default", StringComparison.Ordinal),
                $"the Plug-ins page shows its registry and that it shadows the library's ({row.Registries}; {row.StateText})");
            var report = Conformance.Check(TokenSamplers.Get(TokenSamplers.DefaultName), new TokenSamplerSuite { Exact = true });
            Check.True(report.Passed, $"TokenSamplerSuite, exact against the library's (same tokens, same probabilities: {report.Cases} cases): {string.Join("; ", report.Failures.Take(3))}");
        }
        finally
        {
            IdrakPlugins.UnregisterAll(IdrakPlugins.PackageOf(loaded)!);
        }

        Check.Equal(Idrak.Abstraction.Overrides.Library, TokenSamplers.Origin(TokenSamplers.DefaultName), "unregistered, the library's default is back");
    }

    [Test]
    public static void P3SamplerMasksUnopenedClosersAndChangesNothingWhereNothingIsMasked()
    {
        FillInMiddleFormats.Register(StarCoder2Plugin.FillInMiddle);
        var mode = BracketBalancedSamplerKind.Mode;
        try
        {
            BracketBalancedSamplerKind.Mode = BracketMode.Fim;
            var tokenizer = FimTokenizer(StarCoder2Tokens);
            tokenizer.PadVocabulary(264);
            var spec = new DecoderSpec { Vocabulary = 264, Dim = 32, Layers = 2, Heads = 2, KvHeads = 1, HeadDim = 16, FfDim = 64, MaxPositions = 1024, Rope = new RopeSettings(10000f) };
            var network = spec.Build(options: new DecoderBuildOptions { Device = Idrak.Abstraction.Device.Cpu });
            var options = new GenerationOptions { NumPredict = 400, Seed = 11, Temperature = 2f, TopK = 0, TopP = 1f, RepeatPenalty = 1f };

            // Nothing to mask (every kind opened deeper than the answer can go): the very built-in tokens.
            string deep = "<fim_prefix>" + new string('(', 150) + new string('[', 150) + new string('{', 150) + "<fim_suffix><fim_middle>";
            var (builtIn, _, _) = new TextGenerator(network, tokenizer, 1024) { KeepCache = false }.Generate(deep, options);
            BracketBalancedSampler? made = null;
            var balanced = new TextGenerator(network, tokenizer, 1024)
            {
                KeepCache = false,
                CreateSampler = request => (ITokenSampler)(made = (BracketBalancedSampler)BracketBalancedSamplerKind.Instance.Create(request)),
            };
            var (same, _, _) = balanced.Generate(deep, options);
            Check.True(made is { Active: true, MaskedSteps: 0 }, $"the completion is watched and nothing was masked ({made?.MaskedSteps} steps)");
            Check.Equal(builtIn, same, "where nothing is masked the tokens are the built-in's");

            // A shallow prefix: one parenthesis open, no bracket and no brace; the built-in closes what was never opened.
            string shallow = "<fim_prefix>call(\"}\", <fim_suffix>;\n<fim_middle>";
            var (unchecked_, _, _) = new TextGenerator(network, tokenizer, 1024) { KeepCache = false }.Generate(shallow, options);
            var (kept, _, _) = balanced.Generate(shallow, options);
            Check.True(made is { MaskedSteps: > 0, MaskedTokens: > 0 }, $"tokens were masked ({made?.MaskedSteps} steps, {made?.MaskedTokens} tokens)");
            Check.True(kept != unchecked_, "masking changed the answer (the built-in's chose a token it forbids)");
            Check.True(!Underflow("call(\"}\", " + kept), $"the bracket-balanced answer closes nothing the prefix never opened: '{kept}'");

            // Not a completion (no FIM layout in the prompt): the built-in, recordable, nothing watched.
            var (chat, _, _) = balanced.Generate("hello )", options);
            Check.True(made is { Active: false, Recordable: true }, "a prompt that is not a completion is not watched, and stays recordable");
            Check.Equal(new TextGenerator(network, tokenizer, 1024) { KeepCache = false }.Generate("hello )", options).Text, chat, "and its tokens are the built-in's");
        }
        finally
        {
            BracketBalancedSamplerKind.Mode = mode;
            FillInMiddleFormats.Unregister(StarCoder2Plugin.FillInMiddleFormat);
        }

        // Whether text closes a bracket of any kind that it never opened (outside strings and character literals).
        static bool Underflow(string text)
        {
            Span<int> depth = stackalloc int[3];
            Span<int> lowest = stackalloc int[3];
            BracketTable.Walk(text, LexState.Code, depth, lowest);
            return lowest[0] < 0 || lowest[1] < 0 || lowest[2] < 0;
        }
    }

    // ---- P5: the approve-each sandbox ----

    [Test]
    public static async Task P5ApproveEachAsksBeforeEachCommandAndRefusesWithoutTheUser()
    {
        var missing = Check.Throws<NotSupportedException>(() => CommandSandboxes.Get("approve-each"), "the sandbox before the plug-in loads");
        Check.True(missing.Message.Contains("not registered", StringComparison.Ordinal), $"it is not registered ({missing.Message})");

        using var temp = new TempFolder();
        var loaded = LoadFromFolder(temp, "Craft.Plugins.Sandbox");
        var workspace = temp.File("workspace");
        Directory.CreateDirectory(workspace);
        try
        {
            var sandbox = CommandSandboxes.Get("approve-each");
            Check.Equal("Craft.Plugins.Sandbox", CommandSandboxes.Origin("approve-each"), "a new kind in CommandSandboxes, from the plug-in");
            Check.True(AssemblyLoadContext.GetLoadContext(sandbox.GetType().Assembly) != AssemblyLoadContext.Default, "from the plug-in's own load context");
            var row = PluginList.Rows(CraftPlugins.Installed, []).Last(r => r.Name == "Craft.Plugins.Sandbox");
            Check.True(row.Registries.Contains("CommandSandboxes \"approve-each\"", StringComparison.Ordinal) && row.State == PluginState.Active,
                $"the Plug-ins page shows a new kind in its registry ({row.Registries}; {row.StateText})");
            var limits = new CommandLimits { Programs = ["dotnet"], Root = workspace, Time = TimeSpan.FromSeconds(60) };

            // No editor to ask: refused, never run.
            Check.True(sandbox.Check(limits) is { } nobody && nobody.Contains("no editor", StringComparison.Ordinal), "with no editor the check says why");
            var refused = await sandbox.RunAsync(["dotnet", "--version"], workspace, limits);
            Check.True(refused is { Refused: true, ExitCode: null }, $"with no editor the command is refused ({refused.Output})");

            var presenter = new AskingPresenter();
            var options = new Craft.Core.CraftOptions();
            using var host = new EditorHost(presenter: presenter);
            using var terminals = new Craft.Editor.Terminal.TerminalManager(host, () => options, new Craft.Tests.Editor.FakeClipboard());
            host.Terminals = terminals;
            host.RefreshPlugins();
            Check.True(host.Plugins.Any(p => p is { Name: "sandbox", Active: true }), "the editor plug-in activates");
            Check.True(!host.Panels.Any(p => p.Id == "sandbox.terminal") && terminals.Terminals.Count == 0,
                "no side panel (phase U2), and no terminal tab until a command comes");

            presenter.Answers.Enqueue(0);                                     // Allow once
            var once = await sandbox.RunAsync(["dotnet", "--version"], workspace, limits);
            Check.True(once is { Succeeded: true, Refused: false } && once.Output.Length > 0, $"allowed once, it runs in the inner sandbox ({once.Output})");
            var question = presenter.Questions.Single();
            Check.True(question.Title == "The agent wants to run a command" && question.Message == "dotnet --version", $"the card: {question.Title} / {question.Message}");
            Check.SequenceEqual(["Allow once", "Allow dotnet for this run", "Deny and tell the agent"], question.Choices, "its answers");
            Check.SequenceEqual(["Program: dotnet (allowed: dotnet)", "Time limit: 60 s", "Network: on", "Folder: the workspace", "Runs in: the process sandbox"], question.Details, "its facts");
            var output = terminals.Terminals.Single(t => t.Title == "Agent sandbox");
            Check.True(output.IsReadOnly && !output.HandleKey(EditorKey.A, KeyModifiers.None), "the sandbox's output is its own read-only terminal tab, \"Agent sandbox\"");
            string text = "";
            for (int i = 0; i < 200 && !(text.Contains("$ dotnet --version", StringComparison.Ordinal) && text.Contains("exit code 0", StringComparison.Ordinal)); i++)
            {
                await Task.Delay(10);
                lock (output.Screen.Sync)
                {
                    text = output.Screen.TextOf(output.Screen.FirstLine, 0, output.Screen.ScreenTop + output.Screen.Rows - 1, output.Screen.Columns);
                }
            }

            Check.True(text.Contains("$ dotnet --version", StringComparison.Ordinal) && text.Contains(once.Output.Trim(), StringComparison.Ordinal) && text.Contains("exit code 0", StringComparison.Ordinal),
                $"the Agent sandbox tab shows the command, its output and its end ({text.Trim()})");

            presenter.Answers.Enqueue(2);                                     // Deny
            var denied = await sandbox.RunAsync(["dotnet", "--info"], workspace, limits);
            Check.True(denied is { Refused: true } && denied.Output.Contains("denied", StringComparison.Ordinal), $"denied, the agent is told ({denied.Output})");

            presenter.Answers.Enqueue(-1);                                    // dismissed
            Check.True((await sandbox.RunAsync(["dotnet", "--version"], workspace, limits)).Refused, "a dismissed question is a denial");

            presenter.Answers.Enqueue(1);                                     // Allow dotnet for this run
            int asked = presenter.Questions.Count;
            Check.True((await sandbox.RunAsync(["dotnet", "--version"], workspace, limits)).Succeeded, "allowed for the run, it runs");
            Check.True((await sandbox.RunAsync(["dotnet", "--version"], workspace, limits)).Succeeded && presenter.Questions.Count == asked + 1,
                "the program's next command in the run is not asked again");
            Check.True(await host.ExecuteCommandAsync("sandbox.endRun"), "the command ends the run");
            presenter.Answers.Enqueue(2);
            Check.True((await sandbox.RunAsync(["dotnet", "--version"], workspace, limits)).Refused && presenter.Questions.Count == asked + 2, "after the run ends, it asks again");

            // The agent's own tools (Idrak's CodingTools) consume it by name, as phase A2's agent does.
            presenter.Answers.Enqueue(0);
            var tools = new CodingTools(workspace, new CodingToolOptions { Sandbox = "approve-each", Commands = ["dotnet"] });
            var ran = await tools.RunCommandAsync("dotnet --version", ".", 30);
            Check.True(presenter.Questions[^1].Message == "dotnet --version" && !ran.Contains("denied", StringComparison.Ordinal) && !ran.Contains("refused", StringComparison.OrdinalIgnoreCase),
                $"CodingTools runs through the sandbox it names, asked first ({ran})");

            // X part 6b: an interactive start is asked once, as a run is, then passed through to the inner sandbox's streams.
            Check.True(sandbox.CheckStart(limits) is null, "with an editor, its interactive start is its inner sandbox's (process starts commands interactively)");
            int beforeStart = presenter.Questions.Count;
            presenter.Answers.Enqueue(0);                                     // Allow once
            var started = await sandbox.StartAsync(["dotnet", "--version"], workspace, limits);
            Check.True(started.Started && presenter.Questions.Count == beforeStart + 1 && presenter.Questions[^1].Message == "dotnet --version",
                $"an interactive start is asked once, then started ({started.Refusal})");
            await using (var session = started.Session!)
            {
                using var reader = new StreamReader(session.Output);
                string version = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(60));
                var end = await session.Completion.WaitAsync(TimeSpan.FromSeconds(60));
                Check.True(end is { ExitCode: 0, Output.Length: 0 } && version.Trim() == once.Output.Trim(), $"its output is the caller's stream, passed through as it is ({version.Trim()}, exit {end.ExitCode})");
            }

            presenter.Answers.Enqueue(2);                                     // Deny
            var deniedStart = await sandbox.StartAsync(["dotnet", "--version"], workspace, limits);
            Check.True(!deniedStart.Started && deniedStart.Refusal!.Contains("denied", StringComparison.Ordinal), $"a denied interactive start is refused, saying so ({deniedStart.Refusal})");

            // What it cannot enforce, its inner sandbox's check says, naming one that can.
            Check.True(sandbox.Check(limits with { Network = false }) is { } network && network.Contains("container", StringComparison.Ordinal), "network off needs the container sandbox");
        }
        finally
        {
            IdrakPlugins.UnregisterAll(IdrakPlugins.PackageOf(loaded)!);
        }

        Check.True(CommandSandboxes.Find("approve-each") is null && EditorPlugins.Find("sandbox") is null, "unregistered, the plug-in leaves nothing");
    }

    // ---- P4: C#-aware edit placement ----

    [Test]
    public static void P4CSharpTokensPlacesBySyntaxPassesTheSuiteAndCanTakeTheSemiGlobalSlot()
    {
        Check.True(TextMatchers.Find(CSharpTokenMatcher.MatcherName) is null, "before the plug-in loads, csharp-tokens is not there");
        var missing = Check.Throws<NotSupportedException>(() => TextMatchers.Get(CSharpTokenMatcher.MatcherName), "a matcher nobody registered");
        Check.True(missing.Message.Contains("not registered", StringComparison.Ordinal), $"it fails as not registered ({missing.Message})");

        using var temp = new TempFolder();
        var loaded = LoadFromFolder(temp, "Craft.Plugins.Placement");
        try
        {
            var matcher = TextMatchers.Get(CSharpTokenMatcher.MatcherName);
            Check.Equal("Craft.Plugins.Placement", TextMatchers.Origin(CSharpTokenMatcher.MatcherName), "a new kind in TextMatchers, from the plug-in");
            Check.True(AssemblyLoadContext.GetLoadContext(matcher.GetType().Assembly) != AssemblyLoadContext.Default, "from the plug-in's own load context");

            var options = new TextMatchOptions();
            var semiGlobal = TextMatchers.Get(TextMatchers.SemiGlobal);
            TextMatchSearch Search(ITextMatcher m) => (draft, text, from) => m.Best(draft, text, from, options) is { } found ? (found.Start, found.Length, found.Distance) : null;
            var report = Conformance.CheckTextMatcher(Search(matcher), Search(semiGlobal));
            Check.True(report.Passed, $"TextMatcherSuite, agreeing with semi-global on prose ({report.Cases} cases): {string.Join("; ", report.Failures.Take(3))}");

            // On code it places by tokens, where the library's edit distance alone picks the nearer letters.
            const string Code = "class A\n{\n    void F(int x)\n    {\n        Log(x);\n        Log( y );\n    }\n}\n";
            var spaced = CSharpTokenMatcher.Place("void F( int x )", Code);
            Check.True(spaced is { How: "whitespace differed", Match: { } m1 } && Code.Substring(m1.Start, m1.Length) == "void F(int x)",
                $"the same tokens, other spacing: {spaced}");
            Check.Equal("placed by csharp-tokens · whitespace differed", spaced.ToString(), "what the diff view shows");
            var exact = CSharpTokenMatcher.Place("Log(x);", Code);
            Check.True(exact is { How: "exact", Match.Distance: 0 }, $"an exact run: {exact}");
            var tokens = matcher.Best("Log(y);", Code, 0, options)!.Value;
            var letters = semiGlobal.Best("Log(y);", Code, 0, options)!.Value;
            Check.Equal("Log( y );", Code.Substring(tokens.Start, tokens.Length), "csharp-tokens finds the call with y");
            Check.True(Code.Substring(letters.Start, letters.Length).Contains("Log(x);", StringComparison.Ordinal),
                $"semi-global takes the nearer letters, the call with x ('{Code.Substring(letters.Start, letters.Length)}')");
            var none = CSharpTokenMatcher.Place("Lgo(z)", Code);
            Check.True(none.How == "semi-global" && none.Match is not null, $"no run of the same tokens: the library's span ({none})");

            // An override: with the setting, it takes the semi-global slot; deactivated, the library's is back.
            var variables = new Dictionary<string, string> { [PluginSetting.VariableName("placement", "replaceSemiGlobal")] = "true" };
            using (var host = new EditorHost(readVariable: name => variables.GetValueOrDefault(name)))
            {
                host.RefreshPlugins();
                Check.Equal("Craft.Plugins.Placement", TextMatchers.Origin(TextMatchers.SemiGlobal), "with replaceSemiGlobal it shadows the library's semi-global");
                Check.True(TextMatchers.Default(TextMatchers.SemiGlobal) is { } library && library.GetType().Name == "SemiGlobalMatcher", "the library's stays behind it");
            }

            Check.Equal(Idrak.Abstraction.Overrides.Library, TextMatchers.Origin(TextMatchers.SemiGlobal), "deactivated, the library's semi-global is back");
        }
        finally
        {
            IdrakPlugins.UnregisterAll(IdrakPlugins.PackageOf(loaded)!);
        }

        Check.True(TextMatchers.Find(CSharpTokenMatcher.MatcherName) is null, "unregistered, csharp-tokens is gone");
    }

    // ---- the folder layout of every plug-in ----

    [Test]
    public static void EveryPluginOfPhasePHasItsFolderItsManifestAndNoSharedContracts()
    {
        foreach (var name in (string[])["Craft.Plugins.Tools", "Craft.Plugins.StarCoder2", "Craft.Plugins.Sampler", "Craft.Plugins.Sandbox", "Craft.Plugins.Placement"])
        {
            Check.True(File.Exists(TestRunner.RepositoryPath("plugins", name, "plugin.json")) && File.Exists(TestRunner.RepositoryPath("plugins", name, name + ".csproj")),
                $"plugins/{name}/ holds its project and plugin.json");
            var output = PluginOutput(name);
            Check.True(File.Exists(Path.Combine(output, "plugin.json")), $"{name}'s build output carries its plugin.json");
            Check.True(!File.Exists(Path.Combine(output, "Idrak.Abstraction.dll")), $"{name} carries no copy of Idrak.Abstraction (the host's)");
            var manifest = PluginManifest.Read(File.ReadAllBytes(Path.Combine(output, "plugin.json")), name, out var problems);
            var stated = StatedContractVersion(name);
            Check.True(manifest is not null && manifest.ContractVersion == stated, $"{name}'s plugin.json reads, at the contract its source states ({stated}; {string.Join("; ", problems)})");
        }
    }

    // ---- helpers ----

    internal static readonly string[] StarCoder2Tokens = ["<|endoftext|>", "<fim_prefix>", "<fim_middle>", "<fim_suffix>", "<fim_pad>", "<repo_name>", "<file_sep>"];

    /// <summary>The build output of the plug-in <paramref name="name"/> (plugins/NAME/bin/CONFIGURATION/net10.0/).</summary>
    internal static string PluginOutput(string name)
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var output = TestRunner.RepositoryPath("plugins", name, "bin", testOutput.Parent!.Name, testOutput.Name);
        Check.True(File.Exists(Path.Combine(output, name + ".dll")), $"{name}'s build output is at {output} (the test project builds it first)");
        return output;
    }

    /// <summary>The contract version plugins/NAME/plugin.json states, checked to be one this Craft loads (its major, not newer):
    /// a plug-in steps it when it uses a newer minor's members, so the tests follow the plug-in rather than a fixed "1.0".</summary>
    internal static string StatedContractVersion(string name)
    {
        var manifest = PluginManifest.Read(File.ReadAllBytes(TestRunner.RepositoryPath("plugins", name, PluginManifest.FileName)), name, out var problems);
        Check.True(manifest is not null, $"plugins/{name}/plugin.json reads ({string.Join("; ", problems)})");
        var version = CraftContracts.Parse(manifest!.ContractVersion);
        Check.True(version is not null && version.Major == CraftContracts.Version.Major && version <= CraftContracts.Version,
            $"plugins/{name}/plugin.json states contract {manifest.ContractVersion}, one this Craft ({CraftContracts.Version.ToString(2)}) loads");
        return manifest.ContractVersion;
    }

    /// <summary>Copies the plug-in's build output to TEMP/plugins/NAME/ and loads it from there, as the app does; its assembly.</summary>
    internal static System.Reflection.Assembly LoadFromFolder(TempFolder temp, string name)
    {
        var folder = Path.Combine(temp.Path, "plugins", name);
        var output = PluginOutput(name);
        foreach (var file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(output, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        File.Delete(Path.Combine(folder, "Craft.Abstractions.dll"));         // the host's, as the app's copy leaves it out
        var result = CraftPlugins.LoadFolder(folder);
        var stated = StatedContractVersion(name);
        Check.True(result.Loaded && result.Manifest?.ContractVersion == stated, $"{name} loads from its folder at the contract it states, {stated} ({result.Error})");
        var assembly = AssemblyLoadContext.All.Where(c => c != AssemblyLoadContext.Default).SelectMany(c => c.Assemblies)
            .Last(a => a.GetName().Name == name && a.Location.StartsWith(folder, StringComparison.Ordinal));
        return assembly;
    }

    /// <summary>A byte-level BPE tokenizer.json (no merges: one token per byte) with <paramref name="special"/> added as special tokens.</summary>
    internal static JsonObject FimTokenizerJson(IReadOnlyList<string> special)
    {
        var printable = Enumerable.Range('!', '~' - '!' + 1).Concat(Enumerable.Range('¡', '¬' - '¡' + 1)).Concat(Enumerable.Range('®', 'ÿ' - '®' + 1)).ToHashSet();
        var vocab = new JsonObject();
        for (int b = 0, n = 0; b < 256; b++)
        {
            vocab[((char)(printable.Contains(b) ? b : 256 + n++)).ToString()] = b;
        }

        var added = new JsonArray();
        for (int i = 0; i < special.Count; i++)
        {
            added.Add((JsonNode)new JsonObject { ["id"] = 256 + i, ["content"] = special[i], ["special"] = true });
        }

        return new JsonObject
        {
            ["added_tokens"] = added,
            ["model"] = new JsonObject { ["type"] = "BPE", ["vocab"] = vocab, ["merges"] = new JsonArray() },
            ["pre_tokenizer"] = new JsonObject { ["type"] = "ByteLevel", ["add_prefix_space"] = false, ["use_regex"] = true },
            ["decoder"] = new JsonObject { ["type"] = "ByteLevel" },
        };
    }

    internal static BpeTokenizer FimTokenizer(IReadOnlyList<string> special) => BpeTokenizer.FromJson(FimTokenizerJson(special));

    private static JsonNode Schema() => JsonNode.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""")!;

    private static async Task<string> Call(ToolRegistry tools, string name, JsonObject arguments)
    {
        var result = await tools.InvokeAsync(new ToolCall(name, arguments));
        Check.True(result.Succeeded, $"{name} answered ({result.Error})");
        return result.Content;
    }

    private sealed class TestToolSet(string name, IReadOnlyList<ITool> tools) : IEditorToolSet
    {
        public string Name => name;

        public string Summary => "a test's tools";

        public IReadOnlyList<ITool> Live(IEditorHost host) => tools;

        public IReadOnlyList<ITool> Folder(string folder) => tools;
    }

    /// <summary>A presenter that answers each question from a queue (none queued: dismissed) and records the questions.</summary>
    private sealed class AskingPresenter : IEditorPresenter
    {
        public Queue<int> Answers { get; } = new();

        public List<EditorQuestion> Questions { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            lock (Questions)
            {
                Questions.Add(question);
                return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : -1);
            }
        }
    }
}
