// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.Loader;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Plugins;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Tests.Runner;
using Idrak.Abstraction.Plugins;

namespace Craft.Tests;

/// <summary>
/// Phase K: the plug-in contracts and kit. The sample plug-in (samples/Craft.Plugins.Sample, built against
/// Craft.Abstractions alone) loaded from a plug-in folder and activated by an EditorHost; plugin.json and the contract
/// version; the host's activation, proposals, key precedence, settings and contributions, headless.
/// </summary>
[TestGroup("PluginKit")]
public static class PluginKitTests
{
    private const string SampleName = "Craft.Plugins.Sample";

    [Test]
    public static async Task SampleLoadsActivatesAndWorks()
    {
        using var temp = new TempFolder();
        var plugins = Path.Combine(temp.Path, "plugins");
        var folder = Path.Combine(plugins, SampleName);
        CopySample(folder);
        try
        {
            var result = CraftPlugins.LoadFolder(folder);
            Check.True(result.Loaded, $"the sample loads from its folder ({result.Error})");
            Check.Equal("1.0.0", result.Version, "its plugin.json's version is read");
            Check.True(!result.Outdated && result.Note is null && result.Manifest!.Capabilities.Count == 0, "built against this contract: current, nothing to note");
            Check.True(CraftPlugins.Installed.Any(r => r.Folder == result.Folder && r.Loaded), "CraftPlugins.Installed lists it");
            var plugin = EditorPlugins.Find("sample");
            Check.True(plugin is not null && AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly) != AssemblyLoadContext.Default,
                "the editor plug-in is registered, from the plug-in's own load context");
            Check.True(EditorCommands.Find("sample.wordCount") is not null, "its command is registered");
            Check.Equal("sample.wordCount", EditorKeyBindings.Find(new KeyChord(EditorKey.W, KeyModifiers.Control | KeyModifiers.Shift))?.CommandId, "Ctrl+Shift+W is bound to it");

            var presenter = new FakePresenter();
            using var host = new EditorHost(presenter: presenter, readVariable: _ => null);
            using var document = TextDocument.Create("one two  three\nfour");
            var open = host.Open(document, null);
            var activations = host.RefreshPlugins();
            Check.True(activations.Single(a => a.Name == "sample").Active, "the host activates it");
            var item = host.StatusItems.Single(i => i.Id == "sample.wordCount");
            Check.Equal("4 words", item.Text, "its status item counts the open document's words");

            int changes = 0;
            item.Changed += (_, _) => changes++;
            document.Buffer.Apply([TextEdit.Insert(document.Buffer.Current.Length, " five")]);
            Check.Equal("5 words", item.Text, "a change updates the count");
            document.Buffer.Apply([TextEdit.Insert(document.Buffer.Current.Length, "s")]);
            Check.Equal(1, changes, "a keystroke inside a word changes no text (no string made)");

            // The count follows random edits exactly (the incremental count against a full one, rule 71).
            var random = new Random(7);
            string[] pieces = ["a", "bc ", " ", "\n", "\r\n", "word", "  x", "\t", "y\nz"];
            for (int i = 0; i < 400; i++)
            {
                var snapshot = document.Buffer.Current;
                int start = random.Next(snapshot.Length + 1);
                int length = random.Next(Math.Min(6, snapshot.Length - start) + 1);
                string text = random.Next(3) == 0 ? "" : pieces[random.Next(pieces.Length)];
                document.Buffer.Apply([new TextEdit(start, length, text)]);
                int expected = Words(document.Buffer.Current);
                Check.Equal($"{expected} {(expected == 1 ? "word" : "words")}", item.Text, $"the count after random edit {i}");
            }

            Check.True(host.HandleKey(EditorKey.W, KeyModifiers.Control | KeyModifiers.Shift, new FakeSink()), "Ctrl+Shift+W is handled through its binding");
            await Task.Yield();
            Check.True(presenter.Messages.Count == 1 && presenter.Messages[0].Contains("words", StringComparison.Ordinal), $"the command shows the counts ({string.Join(" | ", presenter.Messages)})");
            Check.True(await host.ExecuteCommandAsync("sample.wordCount"), "the command runs by its id");
            Check.True(host.Settings.All.Any(s => s.Variable == "CRAFT_PLUGIN_SAMPLE_SHOWCHARACTERS" && s.Value == "false"), "its declared setting shows with its variable and default");

            // Removed (unregistered, as disabling or deleting it does on the next start): the host goes on without it.
            Check.True(IdrakPlugins.Unregister("sample"), "the sample unregisters");
            host.RefreshPlugins();
            Check.True(host.StatusItems.All(i => i.Id != "sample.wordCount"), "removed, its status item is gone");
            Check.True(EditorCommands.Find("sample.wordCount") is null && EditorKeyBindings.Find(SampleBinding) is null, "removed, its command and binding are gone");
            Check.True(!host.HandleKey(EditorKey.W, KeyModifiers.Control | KeyModifiers.Shift, new FakeSink()), "removed, Ctrl+Shift+W is not handled");
            document.Buffer.Apply([TextEdit.Insert(0, "more ")]);
            using var other = TextDocument.Create("x");
            host.Close(open);
            host.Open(other, null);
            Check.True(!await host.ExecuteCommandAsync("sample.wordCount"), "the command is not registered any more");

            Check.True(CraftPlugins.LoadFolder(folder).Loaded, "loading the folder again registers it again");
            host.RefreshPlugins();
            Check.Equal("1 word", host.StatusItems.Single(i => i.Id == "sample.wordCount").Text, "activated again, it counts the open document");
            IdrakPlugins.Unregister("sample");

            var disabled = CraftPlugins.LoadInstalled(new CraftOptions { PluginFolders = [plugins], DisabledPlugins = [SampleName.ToUpperInvariant()] })
                .Single(r => r.Name == SampleName);
            Check.True(disabled is { Loaded: false, Disabled: true } && disabled.Error!.Contains("disabled", StringComparison.Ordinal), "a disabled plug-in is skipped and said so");
            Check.True(EditorPlugins.Find("sample") is null, "a disabled plug-in registers nothing");
        }
        finally
        {
            IdrakPlugins.Unregister("sample");
        }
    }

    [Test]
    public static void PluginJsonIsCheckedAndVersioned()
    {
        using var temp = new TempFolder();
        try
        {
            var newer = Path.Combine(temp.Path, "newer", SampleName);
            CopySample(newer);
            WriteManifest(newer, "2.0");
            var refused = CraftPlugins.LoadFolder(newer);
            Check.True(!refused.Loaded && refused.Error!.Contains($"newer than this Craft's {CraftContracts.Version.Major}.{CraftContracts.Version.Minor}", StringComparison.Ordinal), $"a newer major contract is refused with the reason ({refused.Error})");
            Check.True(EditorPlugins.Find("sample") is null, "a refused plug-in registers nothing");
            WriteManifest(newer, "1.5");
            Check.True(CraftPlugins.LoadFolder(newer) is { Loaded: false, Error: not null }, "a newer minor contract is refused too (it may use members this Craft lacks)");

            var older = Path.Combine(temp.Path, "older", SampleName);
            CopySample(older);
            WriteManifest(older, "0.9");
            var outdated = CraftPlugins.LoadFolder(older);
            Check.True(outdated is { Loaded: true, Outdated: true } && outdated.Note!.Contains("outdated", StringComparison.Ordinal), $"an older major is loaded and marked ({outdated.Error})");
            IdrakPlugins.Unregister("sample");

            var bad = Path.Combine(temp.Path, "bad", SampleName);
            CopySample(bad);
            File.WriteAllText(Path.Combine(bad, PluginManifest.FileName), """{ "name": "Other", "version": "", "contractVersion": "one", "capabilities": ["files", "telepathy"] }""");
            var badResult = CraftPlugins.LoadFolder(bad);
            Check.True(!badResult.Loaded, "a bad plugin.json refuses the plug-in");
            foreach (var expected in (string[])["\"name\" is 'Other'", "\"version\" is missing", "\"contractVersion\" is 'one'", "capability 'telepathy'"])
            {
                Check.True(badResult.Error!.Contains(expected, StringComparison.Ordinal), $"the reason names {expected}: {badResult.Error}");
            }

            File.WriteAllText(Path.Combine(bad, PluginManifest.FileName), "{ not json");
            Check.True(CraftPlugins.LoadFolder(bad).Error!.Contains("not valid JSON", StringComparison.Ordinal), "JSON that does not parse is said so");

            var bare = Path.Combine(temp.Path, "bare", SampleName);
            CopySample(bare);
            File.Delete(Path.Combine(bare, PluginManifest.FileName));
            var bareResult = CraftPlugins.LoadFolder(bare);
            Check.True(bareResult is { Loaded: true, Manifest: null } && bareResult.Note!.Contains("no plugin.json", StringComparison.Ordinal), "a folder without plugin.json still loads, said so");
        }
        finally
        {
            IdrakPlugins.Unregister("sample");
        }

        Check.Equal(ContractFit.Current, CraftContracts.Fit(new Version(1, 0)), "the same version is current");
        Check.Equal(ContractFit.Outdated, CraftContracts.Fit(new Version(0, 3)), "an older major is outdated");
        Check.Equal(ContractFit.Newer, CraftContracts.Fit(new Version(2, 0)), "a newer major is newer");
        Check.Equal(new Version(1, 0), CraftContracts.Parse("1"), "a major alone is MAJOR.0");
        Check.True(CraftContracts.Parse("1.x") is null && CraftContracts.Parse("-1.0") is null, "a version that is not MAJOR.MINOR does not parse");
    }

    [Test]
    public static void ActivationFailuresAreReportedAndContributionsTakenBack()
    {
        var good = new TestPlugin("kit-good");
        var bad = new TestPlugin("kit-bad") { Throw = true };
        EditorPlugins.Register(good);
        EditorPlugins.Register(bad);
        try
        {
            using var host = new EditorHost(readVariable: _ => null);
            var activations = host.RefreshPlugins();
            var badActivation = activations.Single(a => a.Name == "kit-bad");
            Check.True(!badActivation.Active && badActivation.Error!.Contains("activation broke", StringComparison.Ordinal), "an Activate that throws is reported with its error");
            Check.True(activations.Single(a => a.Name == "kit-good").Active, "the other plug-in still activates");
            Check.True(host.RecentLog.Any(l => l.Level == EditorLogLevel.Error && l.Message.Contains("kit-bad", StringComparison.Ordinal)), "the failure is logged");
            Check.True(host.StatusItems.All(i => i.Id != "kit-bad.item"), "what a failed activation added is taken back");

            using var document = TextDocument.Create("abc");
            host.Open(document, null);
            document.Buffer.Apply([TextEdit.Insert(0, "x")]);
            Check.Equal(1, good.Changes, "a handler after a throwing one still runs");
            Check.True(host.RecentLog.Any(l => l.Message.Contains("DocumentChanged", StringComparison.Ordinal)), "the throwing handler is logged, the edit stands");
            Check.Equal("xabc", document.Buffer.Current.GetText(0, 4), "the edit was applied");

            Check.True(host.StatusItems.Any(i => i.Id == "kit-good.item") && host.Panels.Any(p => p.Id == "kit-good.panel"), "its status item and panel show");
            Check.True(host.Deactivate("kit-good"), "deactivated");
            Check.True(host.StatusItems.Count == 0 && host.Panels.Count == 0 && host.Progress.Count == 0, "what it left behind (it disposes nothing) is taken back");
            document.Buffer.Apply([TextEdit.Insert(0, "y")]);
            Check.Equal(1, good.Changes, "its handlers are removed");
            Check.True(good.Scope!.AddStatusItem(new StatusItem("late", "late")) is not null && host.StatusItems.Count == 0, "after deactivation, what it adds is refused");
        }
        finally
        {
            EditorPlugins.Unregister("kit-good");
            EditorPlugins.Unregister("kit-bad");
        }
    }

    [Test]
    public static async Task ProposedEditsNeedTheUser()
    {
        using var document = TextDocument.Create("hello world");
        using (var silent = new EditorHost())
        {
            var open = silent.Open(document, null);
            Check.True(!await silent.ProposeEditsAsync(open, [new TextEdit(0, 5, "HELLO")], "Shout"), "with no way to ask the user, nothing is applied");
            Check.Equal("hello world", document.Buffer.Current.GetText(0, 11), "the text is unchanged");
        }

        var presenter = new FakePresenter { Answer = false };
        using var host = new EditorHost(presenter: presenter);
        var doc = host.Open(document, null);
        host.UpdateSelection(doc, SelectionState.At(11));
        Check.True(!await host.ProposeEditsAsync(doc, [new TextEdit(0, 5, "HELLO")], "Shout"), "rejected, nothing is applied");
        Check.True(presenter.Proposals[0].Summary.Contains("1 edit", StringComparison.Ordinal) && presenter.Proposals[0].Summary.Contains("HELLO", StringComparison.Ordinal),
            $"the user sees a summary and a preview ({presenter.Proposals[0].Summary})");

        presenter.Answer = true;
        Check.True(await host.ProposeEditsAsync(doc, [new TextEdit(0, 5, "HELLO"), TextEdit.Insert(11, "!")], "Shout"), "accepted, it is applied");
        Check.Equal("HELLO world!", document.Buffer.Current.GetText(0, 12), "both edits applied");
        Check.Equal(SelectionState.At(12), host.Selection, "the selection follows the edits");
        document.Undo();
        Check.Equal("hello world", document.Buffer.Current.GetText(0, 11), "one undo step takes the whole proposal back");

        presenter.OnAsk = () => document.Buffer.Apply([TextEdit.Insert(0, "typed ")]);
        Check.True(!await host.ProposeEditsAsync(doc, [new TextEdit(0, 5, "HELLO")], "Shout"), "a document changed while the user was asked is not edited");
        Check.True(document.Buffer.Current.GetText(0, 11) == "typed hello", "the user's typing stands");
        presenter.OnAsk = null;
        Check.Throws<ArgumentException>(() => host.ProposeEditsAsync(doc, [new TextEdit(5, 1, "a"), new TextEdit(0, 6, "b")], "Bad").GetAwaiter().GetResult(), "overlapping edits are refused");
        Check.Throws<ArgumentException>(() => host.ProposeEditsAsync(doc, [new TextEdit(0, 1000, "")], "Bad").GetAwaiter().GetResult(), "an edit outside the text is refused");
    }

    [Test]
    public static async Task KeyBindingsKeepTheEditorsKeysAndAddChords()
    {
        int ran = 0;
        EditorCommands.Register(new EditorCommand("kit.run", "Kit: run", (_, _) =>
        {
            ran++;
            return Task.CompletedTask;
        }));
        var selectAll = new KeyChord(EditorKey.A, KeyModifiers.Control);
        var free = new KeyChord(EditorKey.K, KeyModifiers.Control | KeyModifiers.Alt);
        try
        {
            using var host = new EditorHost(new CraftOptions { KeyBindings = KeyBinding.ParseList("Ctrl+F5=kit.run;ctrl+alt+k=") });
            var sink = new FakeSink();

            EditorKeyBindings.Register(new KeyBinding(selectAll, "kit.run"));
            Check.True(host.HandleKey(EditorKey.A, KeyModifiers.Control, sink) && sink.Keys == 1 && ran == 0, "a plug-in's binding never steals a key the editor handles");
            EditorKeyBindings.Register(new KeyBinding(selectAll, "kit.run", OverridesEditor: true));
            Check.True(host.HandleKey(EditorKey.A, KeyModifiers.Control, sink) && sink.Keys == 1, "a binding that overrides the editor runs before it");
            await Task.Yield();
            Check.Equal(1, ran, "the overriding binding ran its command");

            EditorKeyBindings.Register(new KeyBinding(new KeyChord(EditorKey.F5, KeyModifiers.None), "no.such.command"));
            Check.True(host.HandleKey(EditorKey.F5, KeyModifiers.Control, sink) && sink.Keys == 1, "a user's binding runs before the editor");
            await Task.Yield();
            Check.Equal(2, ran, "the user's binding ran its command");

            EditorKeyBindings.Register(new KeyBinding(free, "kit.run"));
            Check.True(!host.HandleKey(EditorKey.K, KeyModifiers.Control | KeyModifiers.Alt, sink) && ran == 2, "a user's empty binding unbinds the plug-in's chord");
            Check.True(!host.HandleKey(EditorKey.Q, KeyModifiers.Control, sink), "a chord nobody binds goes to the editor, which declines it");
            Check.True(host.HandleKey(EditorKey.F5, KeyModifiers.None, sink), "a binding to an unregistered command still takes its chord");
            await Task.Yield();
            Check.True(host.RecentLog.Any(l => l.Message.Contains("'no.such.command' is not registered", StringComparison.Ordinal)), "and the missing command is logged");
        }
        finally
        {
            EditorCommands.Unregister("kit.run");
            EditorKeyBindings.Unregister(selectAll);
            EditorKeyBindings.Unregister(free);
            EditorKeyBindings.Unregister(new KeyChord(EditorKey.F5, KeyModifiers.None));
        }

        Check.Equal("Ctrl+Shift+Alt+W", new KeyChord(EditorKey.W, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt).ToString(), "a chord's text");
        Check.Equal(new KeyChord(EditorKey.D1, KeyModifiers.Alt), KeyChord.Parse(" alt + 1 "), "a digit names its key");
        Check.Throws<FormatException>(() => KeyChord.Parse("Ctrl+Hyper+W"), "an unknown modifier");
        Check.Throws<FormatException>(() => KeyChord.Parse("Ctrl+"), "no key");
        var list = KeyBinding.ParseList("Ctrl+Shift+W=sample.wordCount; Ctrl+D=!dup ;");
        Check.True(list.Count == 2 && list[1] is { CommandId: "dup", OverridesEditor: true, Name: "Ctrl+D" }, "bindings parse, '!' overrides the editor");
        var error = Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(n => n == "CRAFT_KEY_BINDINGS" ? "Ctrl+W" : null), "a binding without '='");
        Check.True(error.Message.Contains("CRAFT_KEY_BINDINGS", StringComparison.Ordinal), "the error names the variable");
    }

    [Test]
    public static void SettingsComeFromVariablesElseDefaults()
    {
        var plugin = new TestPlugin("kit-settings");
        EditorPlugins.Register(plugin);
        try
        {
            var values = new Dictionary<string, string> { ["CRAFT_PLUGIN_KIT_SETTINGS_LIMIT"] = " 7 " };
            using var host = new EditorHost(readVariable: name => values.GetValueOrDefault(name));
            Check.Equal("7", host.Settings.Get("kit-settings", "limit"), "a set variable gives the value (trimmed)");
            Check.Equal("on", host.Settings.Get("KIT-SETTINGS", "Mode"), "an unset one gives the default, names ignoring case");
            var undeclared = Check.Throws<ArgumentException>(() => host.Settings.Get("kit-settings", "colour"), "an undeclared key");
            Check.True(undeclared.Message.Contains("declared: limit, mode", StringComparison.Ordinal), $"the error lists the declared keys ({undeclared.Message})");
            Check.Throws<NotRegisteredException>(() => host.Settings.Get("no-such-plugin", "x"), "a plug-in that is not registered");
            Check.Equal("CRAFT_PLUGIN_A_B_C_D", PluginSetting.VariableName("a.b", "c-d"), "a variable's name");
        }
        finally
        {
            EditorPlugins.Unregister("kit-settings");
        }
    }

    private static KeyChord SampleBinding => new(EditorKey.W, KeyModifiers.Control | KeyModifiers.Shift);

    private static int Words(ITextSnapshot snapshot) =>
        snapshot.GetText(0, snapshot.Length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static void WriteManifest(string folder, string contractVersion) =>
        File.WriteAllText(Path.Combine(folder, PluginManifest.FileName),
            $$"""{ "name": "{{SampleName}}", "version": "1.0.0", "summary": "test", "contractVersion": "{{contractVersion}}" }""", Encoding.UTF8); // with a byte-order mark

    /// <summary>Copies the sample's build output (its dll, deps.json and plugin.json) into <paramref name="folder"/>.</summary>
    private static void CopySample(string folder)
    {
        // tests/Craft.Tests/bin/<Configuration>/net10.0/ -> samples/Craft.Plugins.Sample/bin/<Configuration>/net10.0/
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var output = TestRunner.RepositoryPath("samples", SampleName, "bin", testOutput.Parent!.Name, testOutput.Name);
        Check.True(File.Exists(Path.Combine(output, SampleName + ".dll")), $"the sample's build output is at {output} (the test project builds it first)");
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(output))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }

        Check.True(File.Exists(Path.Combine(folder, PluginManifest.FileName)), "the sample's output carries its plugin.json");
        Check.True(!File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")) && !File.Exists(Path.Combine(folder, "Idrak.Abstraction.dll")), "the sample carries no copy of the shared contracts");
    }

    /// <summary>A plug-in for the host's own tests: a status item, a panel, progress and a handler; it disposes nothing on deactivation.</summary>
    private sealed class TestPlugin(string name) : IEditorPlugin
    {
        public string Name => name;

        public string Summary => "test";

        public bool Throw { get; init; }

        public int Changes { get; private set; }

        public IEditorHost? Scope { get; private set; }

        public IReadOnlyList<PluginSetting> Settings { get; } = [new("limit", "3", "a limit"), new("mode", "on", "a mode")];

        public IDisposable Activate(IEditorHost host)
        {
            Scope = host;
            host.AddStatusItem(new StatusItem(name + ".item", "here"));
            if (Throw)
            {
                host.DocumentChanged += (_, _) => throw new InvalidOperationException("a handler broke");
                throw new InvalidOperationException("activation broke");
            }

            host.DocumentChanged += (_, _) => throw new InvalidOperationException("a handler broke");
            host.DocumentChanged += (_, _) => Changes++;
            var panel = new ListPanel(name + ".panel", "Test");
            panel.SetItems([new PanelItem("one"), new PanelItem("two", "detail", "kit.run")]);
            host.AddPanel(panel);
            host.StartProgress("working").Report(0.5, "half");
            return new Nothing();
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>The editor's keys: handles Ctrl+A (select all) and counts what it handled.</summary>
    private sealed class FakeSink : IEditorInputSink
    {
        public int Keys { get; private set; }

        public bool OnKey(EditorKey key, KeyModifiers modifiers)
        {
            if (key == EditorKey.A && modifiers == KeyModifiers.Control)
            {
                Keys++;
                return true;
            }

            return false;
        }

        public void OnText(string text)
        {
        }
    }

    private sealed class FakePresenter : IEditorPresenter
    {
        public bool Answer { get; set; } = true;

        public Action? OnAsk { get; set; }

        public List<EditProposal> Proposals { get; } = [];

        public List<string> Messages { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken)
        {
            Proposals.Add(proposal);
            OnAsk?.Invoke();
            return Task.FromResult(Answer);
        }

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add($"{title}: {message}");
            return Task.CompletedTask;
        }
    }
}
