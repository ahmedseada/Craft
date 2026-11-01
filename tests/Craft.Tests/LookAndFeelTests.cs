// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.RegularExpressions;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The owner's look-and-feel notes from the Windows run, headless: 10 (one Craft dialog for every question, message and
/// choice, its behaviour, and no platform message box left), 11 (the model and its device picked at the composer), 14 (the
/// code font checked by its glyph widths) and 6 (every plug-in that loads a model sees Idrak.Gpu's devices). The look
/// itself is the owner's on Windows.
/// </summary>
[TestGroup("LookAndFeel")]
public static class LookAndFeelTests
{
    [Test]
    public static void ADialogAnswersByItsKeysAndKeepsTheFocusInside()
    {
        var dialogs = new DialogQueue(action => action());
        int changed = 0;
        dialogs.Changed += (_, _) => changed++;
        var unsaved = dialogs.ShowAsync(DialogRequest.Unsaved("Program.cs"));
        Check.True(dialogs.IsOpen && changed == 1, "the dialog shows");
        Check.Equal(0, dialogs.Focused, "the focus starts on the primary action (Save)");
        Check.Equal(1, dialogs.MoveFocus(1), "Tab moves to Don't save");
        Check.Equal(2, dialogs.MoveFocus(1), "then to Cancel");
        Check.Equal(0, dialogs.MoveFocus(1), "and wraps to Save: the focus stays in the dialog");
        Check.Equal(2, dialogs.MoveFocus(-1), "Shift+Tab wraps back to Cancel");
        Check.True(dialogs.HandleKey(EditorKey.S, KeyModifiers.Control, null), "while it shows, a chord is the dialog's (nothing behind it runs)");
        Check.True(dialogs.HandleKey(EditorKey.Escape, KeyModifiers.None, null) && unsaved.IsCompleted, "Escape answers");
        Check.Equal(UnsavedChoice.Cancel, DialogRequest.UnsavedAnswer(unsaved.Result), "Escape is the way out (Cancel)");
        Check.True(!dialogs.IsOpen && changed == 2, "and the dialog goes");

        var save = dialogs.ShowAsync(DialogRequest.Unsaved("Program.cs"));
        dialogs.HandleKey(EditorKey.Enter, KeyModifiers.None, null);
        Check.Equal(UnsavedChoice.Save, DialogRequest.UnsavedAnswer(save.Result), "Enter takes the default action");
        var discard = dialogs.ShowAsync(DialogRequest.Unsaved("Program.cs"));
        dialogs.HandleKey(EditorKey.Tab, KeyModifiers.None, null);
        dialogs.HandleKey(EditorKey.Enter, KeyModifiers.None, null);
        Check.Equal(UnsavedChoice.Discard, DialogRequest.UnsavedAnswer(discard.Result), "Enter takes the focused action");

        // The window's keys with the dialogs first: closed, only chords and function keys reach the bindings from the window.
        var inner = new RecordingKeys();
        var keys = dialogs.Before(inner);
        Check.True(!keys.HandleKey(EditorKey.Escape, KeyModifiers.None, null) && inner.Keys.Count == 0, "with no dialog, a plain key from the window is not the bindings'");
        keys.HandleKey(EditorKey.P, KeyModifiers.Control, null);
        keys.HandleKey(EditorKey.F12, KeyModifiers.None, null);
        Check.Equal(2, inner.Keys.Count, "a chord and a function key reach the bindings");
        var open = dialogs.ShowAsync(DialogRequest.Message("About Craft", "A code editor."));
        Check.True(keys.HandleKey(EditorKey.P, KeyModifiers.Control, null) && inner.Keys.Count == 2, "with a dialog open, the bindings get nothing");
        keys.HandleKey(EditorKey.Enter, KeyModifiers.None, null);
        Check.True(open.IsCompleted && open.Result.Index == 0, "Enter takes OK");
    }

    [Test]
    public static void DialogsWaitInOrderAndACancelledOneGoes()
    {
        var dialogs = new DialogQueue(action => action());
        var first = dialogs.ShowAsync(DialogRequest.Message("One", ""));
        using var cancel = new CancellationTokenSource();
        var second = dialogs.ShowAsync(DialogRequest.Message("Two", ""), cancel.Token);
        var third = dialogs.ShowAsync(DialogRequest.Pick("Three", ["a", "b", "c"]));
        Check.True(dialogs.Current?.Title == "One" && dialogs.Waiting == 2, "one shows, two wait");
        cancel.Cancel();
        Check.True(second.IsCompleted && second.Result.Index == -1 && dialogs.Waiting == 1, "a waiting dialog whose request is cancelled goes, answered with none");
        dialogs.Answer(0);
        Check.True(first.IsCompleted && dialogs.Current?.Title == "Three", "answered, the next one shows");
        Check.True(dialogs.Current!.IsList && dialogs.Focused == 0, "a picker is a list, its first row focused");
        dialogs.HandleKey(EditorKey.Down, KeyModifiers.None, null);
        dialogs.HandleKey(EditorKey.Down, KeyModifiers.None, null);
        dialogs.HandleKey(EditorKey.Down, KeyModifiers.None, null);
        Check.Equal(0, dialogs.Focused, "the arrows move along the rows and wrap");
        dialogs.HandleKey(EditorKey.Up, KeyModifiers.None, null);
        dialogs.HandleKey(EditorKey.Enter, KeyModifiers.None, null);
        Check.Equal(2, third.Result.Index, "Enter picks the focused row");

        var prompt = dialogs.ShowAsync(DialogRequest.Prompt("Open", "", "A file's name"));
        Check.Equal(-1, dialogs.Focused, "a prompt's focus starts in its field");
        dialogs.HandleKey(EditorKey.Enter, KeyModifiers.None, null);
        Check.True(!prompt.IsCompleted, "Enter in the field is the field's own (its Completed confirms with the text), not answered twice");
        dialogs.Confirm("Program");
        Check.True(prompt.Result is { Index: 0, Text: "Program" }, "the field's text is the answer");
        var pickNone = dialogs.ShowAsync(DialogRequest.Pick("Tabs", ["a"]));
        dialogs.Cancel();
        Check.Equal(-1, pickNone.Result.Index, "Escape on a picker picks nothing");
    }

    [Test]
    public static void APluginsQuestionKeepsItsAnswersAndItsDismissal()
    {
        var dialogs = new DialogQueue(action => action());
        var approval = new EditorQuestion("The agent wants to run a command", "dotnet test", ["Allow once", "Allow dotnet for this run", "Deny and tell the agent"])
        {
            Details = ["Program: dotnet", "Time limit: 120 s", "Network: off"],
            Dismissed = 2,
        };
        var request = DialogRequest.Question(approval);
        Check.True(request.Actions.Count == 3 && request.Actions[0].Role == DialogActionRole.Primary && !request.IsList && request.Details.Count == 3,
            "the approval card: its three answers as buttons (the first the primary), its facts, no extra Cancel since its dismissal is Deny");
        var asked = dialogs.ShowAsync(request);
        dialogs.Cancel();
        Check.Equal(2, DialogRequest.QuestionAnswer(approval, asked.Result), "Escape gives the question's dismissal (Deny)");
        var consent = new EditorQuestion("Share these items?", "Two episodes.", ["Share", "Don't share"]);
        var consentRequest = DialogRequest.Question(consent);
        Check.True(consentRequest.Actions.Count == 3 && consentRequest.Actions[^1].Role == DialogActionRole.Cancel, "a question whose dismissal is none gets a Cancel");
        var answered = dialogs.ShowAsync(consentRequest);
        dialogs.Cancel();
        Check.Equal(-1, DialogRequest.QuestionAnswer(consent, answered.Result), "and Cancel is -1");
        var many = DialogRequest.Question(new EditorQuestion("Pick", "", ["a", "b", "c", "d", "e"]));
        Check.True(many.IsList, "more than three answers are a list");
    }

    // The owner's note 10: no platform message box is left; every question goes through Craft's dialog.
    // The platform's boxes only: MAUI's DisplayAlert/DisplayActionSheet/DisplayPrompt, WinUI's ContentDialog and
    // MessageDialog, WinForms' and WPF's MessageBox.Show, and Win32's MessageBox by its W/A name or a P/Invoke declaration
    // (extern, a LibraryImport partial, or an EntryPoint naming it). Craft's own PanelMessageBox block and a method named
    // MessageBox that draws it are not platform boxes.
    private static readonly Regex PlatformDialog = new(
        @"\b(DisplayAlert|DisplayActionSheet|DisplayPrompt)(Async)?\s*\(|\bMessageBox(W|A)\s*\(|\bMessageBox\s*\.\s*Show\s*\(|\b(extern|partial)\b[^;={]*\bMessageBox(W|A)?\s*\(|EntryPoint\s*=\s*""MessageBox|\bContentDialog\b|\bMessageDialog\b",
        RegexOptions.Compiled);

    [Test]
    public static void ThePlatformDialogPatternCatchesOnlyThePlatformsBoxes()
    {
        string[] platform =
        [
            "await DisplayAlert(\"Title\", \"Text\", \"OK\");",
            "string answer = await page.DisplayPromptAsync(\"Name\", \"Enter it\");",
            "await DisplayActionSheet(\"Pick\", \"Cancel\", null, \"a\");",
            "var dialog = new ContentDialog { Title = \"x\" };",
            "await new MessageDialog(\"x\").ShowAsync();",
            "System.Windows.Forms.MessageBox.Show(\"x\");",
            "MessageBox.Show(owner, \"x\");",
            "_ = MessageBoxW(IntPtr.Zero, text, caption, 0);",
            "static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);",
            "private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);",
            "[DllImport(\"user32.dll\", EntryPoint = \"MessageBoxW\")]",
        ];
        string[] craft =
        [
            "PanelMessageBox box => MessageBox(box),",
            "private VerticalStackLayout MessageBox(PanelMessageBox box)",
            "blocks.Add(MessageBox(state));",
            "private static PanelMessageBox MessageBox(SourceControlViewState state)",
            "return new PanelMessageBox(Message, state.Message)",
            "public sealed record PanelMessageBox(string ActionId, string Text) : PanelBlock",
            "await _dialog.ShowMessageAsync(title, text);",
        ];
        var missed = platform.Where(line => !PlatformDialog.IsMatch(line)).ToArray();
        Check.True(missed.Length == 0, "a platform box the pattern missed:\n  " + string.Join("\n  ", missed));
        var wrong = craft.Where(line => PlatformDialog.IsMatch(line)).ToArray();
        Check.True(wrong.Length == 0, "Craft's own message block taken for a platform box:\n  " + string.Join("\n  ", wrong));
    }

    private static readonly (string File, string Code, string Why)[] AllowedDialogs =
    [
        ("src/Craft.App/Platforms/Windows/CrashDialog.cs", "MessageBoxW(IntPtr.Zero", "the crash report: Win32's own box shows even when the window's XAML is what failed"),
        ("src/Craft.App/Platforms/Windows/CrashDialog.cs", "private static partial int MessageBoxW(", "its declaration"),
    ];

    [Test]
    public static void NoPlatformMessageBoxIsLeft()
    {
        string root = TestRunner.RepositoryPath();
        var found = new List<string>();
        var used = new HashSet<int>();
        int files = 0;
        foreach (var folder in (string[])["src", "plugins"])
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
                {
                    continue;
                }

                files++;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = lines[i].TrimStart();
                    if (code.StartsWith("//", StringComparison.Ordinal) || !PlatformDialog.IsMatch(lines[i]))
                    {
                        continue;
                    }

                    int allowed = Array.FindIndex(AllowedDialogs, a => a.File == relative && lines[i].Contains(a.Code, StringComparison.Ordinal));
                    if (allowed >= 0)
                    {
                        used.Add(allowed);
                        continue;
                    }

                    found.Add($"{relative}:{i + 1}: {code}");
                }
            }
        }

        Check.True(files > 100, $"the sources were read ({files} files)");
        Check.True(found.Count == 0, "a platform message box (use Craft's dialog: CraftDialog, through IEditorPresenter or IShellUi):\n  " + string.Join("\n  ", found));
        var stale = AllowedDialogs.Where((_, i) => !used.Contains(i)).Select(a => $"{a.File}: {a.Code}").ToArray();
        Check.True(stale.Length == 0, "an allowed message box that is no longer there (take it off the list):\n  " + string.Join("\n  ", stale));
        string dialog = File.ReadAllText(Path.Combine(root, "src", "Craft.Maui", "CraftDialog.cs"));
        string presenter = File.ReadAllText(Path.Combine(root, "src", "Craft.Maui", "PluginViews.cs"));
        Check.True(dialog.Contains("Queue.Before", StringComparison.Ordinal) || File.ReadAllText(Path.Combine(root, "src", "Craft.Maui", "ShellPage.cs")).Contains("_dialog.Queue.Before(_host)", StringComparison.Ordinal),
            "the window's keys go through the dialogs first");
        Check.True(presenter.Contains("_dialog.AskAsync(question", StringComparison.Ordinal) && presenter.Contains("_dialog.ShowMessageAsync(", StringComparison.Ordinal),
            "the host's presenter asks and tells through Craft's dialog");
    }

    [Test]
    public static void TheBundledCodeFontIsCheckedByItsGlyphWidths()
    {
        string fonts = TestRunner.RepositoryPath("src", "Craft.App", "Resources", "Fonts");
        var mono = FontFileMetrics.ReadFile(Path.Combine(fonts, "JetBrainsMono-Regular.ttf"));
        Check.True(mono is { UnitsPerEm: 1000, AdvanceWidthMax: 600, IsFixedPitch: true }, $"JetBrains Mono's file: 1000 units an em, every advance 600, fixed pitch ({mono})");
        Check.True(FontFileMetrics.ReadFile(Path.Combine(fonts, "JetBrainsMono-Bold.ttf")) is { AdvanceWidthMax: 600, IsFixedPitch: true }, "its bold has the same cells");
        Check.True(FontFileMetrics.ReadFile(Path.Combine(fonts, "Inter-Regular.ttf")) is { IsFixedPitch: false }, "Inter is not fixed pitch");
        Check.True(FontFileMetrics.Read("not a font"u8) is null, "a file that is not a font gives nothing");

        using var temp = new TempFolder();
        foreach (var file in Directory.GetFiles(fonts, "JetBrainsMono-*.ttf"))
        {
            File.Copy(file, Path.Combine(temp.Path, Path.GetFileName(file)));
        }

        var bundled = CodeFonts.BundledFaces(temp.Path, (path, family) => "file:" + Path.GetFileName(path) + "#" + family);
        Check.True(bundled is { AdvanceEm: 0.6, Regular: "file:JetBrainsMono-Regular.ttf#JetBrains Mono", Bold: "file:JetBrainsMono-Bold.ttf#JetBrains Mono", BoldItalic: "file:JetBrainsMono-BoldItalic.ttf#JetBrains Mono" },
            $"the bundled faces name each file, with the file's advance ({bundled})");
        var candidates = CodeFonts.Candidates("JetBrains Mono", "Cascadia Mono, Consolas, cascadia mono", [bundled!]);
        Check.True(candidates.Select(c => c.Regular).SequenceEqual(["file:JetBrainsMono-Regular.ttf#JetBrains Mono", "JetBrains Mono", "Cascadia Mono", "Consolas"]),
            $"the order tried: the files, the family by name, each fallback once ({string.Join(" | ", candidates.Select(c => c.Regular))})");
        Check.True(CodeFonts.Candidates("Fira Code", "Consolas", [bundled!]).Select(c => c.Regular).SequenceEqual(["Fira Code", "Consolas"]), "another family set by CRAFT_FONT is asked for by name, no bundled file");
        Check.True(CodeFonts.BundledFaces(Path.Combine(temp.Path, "missing"), (p, f) => p) is null, "no files, no bundled faces (the names are tried)");

        // 64 "M"s at 13: JetBrains Mono's cell is 0.6 em, 7.8 units; a substitute (Consolas 0.55, Cascadia 0.586, a
        // proportional default) is not taken for the bundled file.
        Check.True(CodeFonts.Accepts(64 * 7.8, 64 * 7.8, 64, 13, 0.6), "the bundled file's own cell is accepted");
        Check.True(!CodeFonts.Accepts(64 * 7.15, 64 * 7.15, 64, 13, 0.6), "a monospaced substitute is not taken for the file");
        Check.True(!CodeFonts.Accepts(64 * 11.2, 64 * 3.4, 64, 13, null), "a proportional font is not taken");
        Check.True(CodeFonts.Accepts(64 * 7.15, 64 * 7.15, 64, 13, null), "a monospaced fallback by name is");
        Check.True(new Craft.Core.CraftOptions() is { FontFamily: "JetBrains Mono", FontSize: 13, FontFallback: "Cascadia Mono, Consolas" }
                   && Math.Ceiling(13 * new Craft.Core.CraftOptions().LineSpacing) == 22, "the design's code type: JetBrains Mono at 13 on 22-unit lines");
    }

    [Test]
    public static void TheModelChipShowsTheDeviceAndThePickerItsModelsAndDevices()
    {
        var setting = new ModelSettingField("CRAFT_PLUGIN_AI_DEVICE", "Run on");
        var before = new ModelProviderStatus
        {
            Where = "local",
            Roles = [new ModelRole("Chat and agent", "Qwen/Qwen2.5-Coder-7B-Instruct", "not loaded")],
            Models = ["Qwen/Qwen2.5-Coder-7B-Instruct", "store:coder-q4"],
            Devices = ["auto", "cpu", "cuda:0"],
            AutoDevice = "cuda:0",
            DeviceChoice = "auto",
            DeviceSetting = setting,
            ModelSetting = new ModelSettingField("CRAFT_PLUGIN_AI_MODEL", "Model"),
        };
        Check.Equal("Qwen/Qwen2.5-Coder-7B-Instruct · cuda:0 (auto)", ShellText.ModelChip(before), "before a load the chip names what auto takes (the owner's note 6)");
        Check.Equal("local · cuda:0 (auto)", ShellText.ModelUsage(before), "the status bar too");
        var loaded = before with { Device = "cuda:0" };
        Check.Equal("Qwen/Qwen2.5-Coder-7B-Instruct · cuda:0", ShellText.ModelChip(loaded), "loaded, the device in use");
        Check.Equal("cpu", ShellText.PlannedDevice(before with { DeviceChoice = "cpu" }), "a device the user set is the one shown");
        var picks = ShellText.ModelPicks(before);
        Check.True(picks.Select(p => p.Label).SequenceEqual([
                "✓ Qwen/Qwen2.5-Coder-7B-Instruct", "store:coder-q4", "✓ Run on auto (the fastest found: cuda:0)", "Run on cpu", "Run on cuda:0", "Another model… (Models and devices)"]),
            $"the picker: the models (the one in use marked), the devices (the chosen marked, auto saying what it takes), the page ({string.Join(" | ", picks.Select(p => p.Label))})");
        Check.True(picks[1].Kind == ModelPickKind.Model && picks[4] is { Kind: ModelPickKind.Device, Value: "cuda:0" } && picks[^1].Kind == ModelPickKind.Page, "each row says what it does");
        Check.True(ShellText.ModelPicks(null) is [{ Kind: ModelPickKind.Page }], "with no provider, only the page");
    }

    [Test]
    public static void TheLocalProviderListsThisMachinesDevicesOffTheUiThread()
    {
        using var provider = new LocalModelProvider();
        using var listed = new ManualResetEventSlim();
        provider.StatusChanged += (_, _) => listed.Set();
        var first = provider.Status;
        Check.True(listed.Wait(TimeSpan.FromSeconds(60)), "the devices are listed on the thread pool and the status says so (StatusChanged)");
        var status = provider.Status;
        Check.True(status.Devices.Count >= 2 && status.Devices[0] == "auto" && status.Devices.Contains("cpu"), $"auto, then the CPU and every GPU found ({string.Join(", ", status.Devices)})");
        Check.True(status.AutoDevice is { Length: > 0 } && status.Devices.Contains(status.AutoDevice), $"what auto takes is one of them ({status.AutoDevice})");
        Check.True(first.Devices.Count == 0 || first.Devices.SequenceEqual(status.Devices), "the first read never waited for the drivers");
        Check.Equal("auto", status.DeviceChoice, "no device set: auto");
    }

    [Test]
    public static void EveryPluginThatLoadsAModelSeesTheGpuDevices()
    {
        // The owner's note 6: Craft.Ai (the chat, the agent, completion, P2's families through it), the learning plug-in and
        // the voice plug-in carry Idrak.Gpu; loaded from a copied folder in a fresh process (nothing of the plug-in's
        // Idrak in the default load context, as in the app), each sees CUDA's, Vulkan's and HIP's providers.
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        (string Name, string Output)[] plugins =
        [
            ("Craft.Ai", TestRunner.RepositoryPath("src", "Craft.Ai", "bin", testOutput.Parent!.Name, testOutput.Name)),
            ("Craft.Plugins.Learning", TestRunner.RepositoryPath("plugins", "Craft.Plugins.Learning", "bin", testOutput.Parent!.Name, testOutput.Name)),
            ("Craft.Voice", TestRunner.RepositoryPath("plugins", "Craft.Voice", "bin", testOutput.Parent!.Name, testOutput.Name)),
        ];
        foreach (var (name, output) in plugins)
        {
            using var temp = new TempFolder();
            var folder = Path.Combine(temp.Path, "plugins", name);
            Check.True(File.Exists(Path.Combine(output, name + ".deps.json")), $"{name}'s build output is at {output}");
            Directory.CreateDirectory(folder);
            foreach (var file in Directory.GetFiles(output))
            {
                var fileName = Path.GetFileName(file);
                if (!fileName.StartsWith("Craft.Abstractions.", StringComparison.Ordinal) && !fileName.StartsWith("Idrak.Abstraction.", StringComparison.Ordinal)
                    && !fileName.StartsWith("Craft.Core.", StringComparison.Ordinal))
                {
                    File.Copy(file, Path.Combine(folder, fileName));
                }
            }

            Check.True(File.Exists(Path.Combine(folder, "Idrak.Gpu.dll")), $"{name} carries Idrak.Gpu");
            var lines = PluginProbe.RunInFreshProcess(folder);
            string Line(string key) => lines.FirstOrDefault(l => l.StartsWith(key + ": ", StringComparison.Ordinal))?[(key.Length + 2)..] ?? "(missing)";
            Check.True(Line("loaded").StartsWith("True", StringComparison.Ordinal), $"{name} loads from its folder ({string.Join(" | ", lines)})");
            var names = Line("device provider names").Split(", ");
            Check.True(names.Contains("cuda") && names.Contains("vulkan") && names.Contains("hip"),
                $"{name}: DeviceProviders lists cuda, vulkan and hip ({Line("device provider names")}; from {Line("device providers from")})");
            Check.True(Line("default device") != "(missing)", $"{name}: the default device is chosen among them ({Line("default device")})");
        }
    }

    private sealed class RecordingKeys : IEditorKeyHandler
    {
        public List<EditorKey> Keys { get; } = [];

        public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor)
        {
            Keys.Add(key);
            return true;
        }
    }
}
