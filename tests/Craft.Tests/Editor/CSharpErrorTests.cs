// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.CSharp;
using Craft.CSharp.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests.Editor;

/// <summary>
/// The owner's crash "when I introduced a C# error" (2026-10-31): a real C# document opened through the shell's own path
/// (ShellModel, the C# plug-in's language), typed into keystroke by keystroke on a UI thread of its own, with everything
/// that reacts to diagnostics driven as the app does (the squiggles painted, the Problems rows, the status bar's count,
/// hover over the error, the code actions there), and every event the views handle checked to arrive on the UI thread.
/// </summary>
[TestGroup("Diagnostics")]
public static class CSharpErrorTests
{
    private const string Program = """
        namespace Demo;

        public static class Program
        {
            public static void Run()
            {
                int count = 1;
                System.Console.WriteLine(count);
            }
        }
        """;

    [Test]
    public static async Task TypingACSharpErrorKeystrokeByKeystrokeNeverThrowsAndStaysOnTheUiThread()
    {
        using var folder = new TempFolder();
        await File.WriteAllTextAsync(folder.File("Demo.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{ReferenceAssemblies.RunningFramework}</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        string path = folder.File("Program.cs");
        await File.WriteAllTextAsync(path, Program.ReplaceLineEndings("\n"));
        CSharpLanguage.Register();

        using var ui = new UiThread();
        var options = new CraftOptions { DiagnosticsDelayMilliseconds = 0, SemanticColorsDelayMilliseconds = 0, HoverDelayMilliseconds = 0, CompletionDelayMilliseconds = 0, NewLine = "\n" };
        var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
        var recent = RecentList.Load(folder.File("data/recent.json"), 10);
        var host = await ui.Invoke(() => new EditorHost(options, dispatch: ui.Post));
        var shell = await ui.Invoke(() => new ShellModel(host, settings, recent, new NoDialogs(), new QuietShellUi()));
        EditorModel? model = null;
        try
        {
            await ui.Invoke(() => shell.OpenFolderAsync(folder.Path));
            var tab = await ui.Invoke(() => shell.OpenFileAsync(path));
            Check.True(tab?.Language?.Name == CSharpLanguage.RegularName, $"the file opens as C# ({tab?.Language?.Name})");

            model = await ui.Invoke(() =>
            {
                var made = new EditorModel(tab!.Document, tab.Language, options, null, new FixedMeasurer(), new FakeClipboard(), workspaceRoot: folder.Path)
                {
                    Dispatch = ui.Post,
                };
                made.UpdateViewport(0, 0, 900, 600);
                // As ShellPage does: each change of the diagnostics reaches the host (the plug-ins read them there) and the Problems rows.
                made.DiagnosticsChanged += (_, _) =>
                {
                    ui.AssertOnThread("EditorModel.DiagnosticsChanged");
                    host.SetDiagnostics(tab.HostDocument, made.Diagnostics);
                    host.SetSemanticSession(tab.HostDocument, made.SemanticSession);
                    ProblemsOf(made);
                    _ = ShellText.Problems(made.ErrorCount, made.WarningCount);
                    _ = ShellText.Semantic(made);
                };
                made.Invalidated += (_, _) => ui.AssertOnThread("EditorModel.Invalidated");
                made.CaretMoved += (_, _) => ui.AssertOnThread("EditorModel.CaretMoved");
                made.ContentSizeChanged += (_, _) => ui.AssertOnThread("EditorModel.ContentSizeChanged");
                made.Reclassified += (_, _) => ui.Post(made.ApplyReclassification);
                return made;
            });

            var session = (CSharpLanguageSession)model.LanguageSession!;
            await session.WhenDiagnosticsIdle().WaitAsync(TimeSpan.FromMinutes(2));
            await ui.Settle();
            Check.Equal(0, await ui.Invoke(() => model.ErrorCount), $"the project compiles clean ({session.SemanticError})");

            using var renderer = new EditorRenderer();
            var canvas = new RecordingCanvas();
            void Paint()
            {
                canvas.Clear();
                renderer.Draw(canvas, model, 0, 0, 900, 600, caretVisible: true);
            }

            // Each error typed a key at a time, the diagnostics landing between the keys (a pass on every pause).
            string text = model.Snapshot.GetText(0, model.Snapshot.Length);
            int semicolon = text.IndexOf("1;", StringComparison.Ordinal) + 1;
            await ui.Invoke(() => model.Select(semicolon + 1, semicolon + 1));
            await ui.Invoke(() => model.OnKey(EditorKey.Backspace, KeyModifiers.None));       // the ';' removed: CS1002
            await Drive(ui, model, session, Paint, host);
            Check.True(await ui.Invoke(() => model.ErrorCount) > 0, "the missing ';' is an error");

            int writeLine = await ui.Invoke(() => model.Snapshot.GetText(0, model.Snapshot.Length).IndexOf("System.Console", StringComparison.Ordinal));
            await ui.Invoke(() => model.Select(writeLine, writeLine));
            foreach (char c in "undefinedName.Call(\"unclosed")
            {
                await ui.Invoke(() => model.OnText(c.ToString()));
                await Drive(ui, model, session, Paint, host, settle: false);
            }

            await Drive(ui, model, session, Paint, host);
            await ui.Invoke(() => model.OnText("{"));                                        // an unclosed brace
            await Drive(ui, model, session, Paint, host);

            // Deleting back over the errors, a key at a time, the earlier sets still landing.
            for (int i = 0; i < 30; i++)
            {
                await ui.Invoke(() => model.OnKey(EditorKey.Backspace, KeyModifiers.None));
                await Drive(ui, model, session, Paint, host, settle: i % 7 == 0);
            }

            // Hover over an error, then the text shrinks under the pointer before the hover's delay ends.
            await ui.Invoke(() =>
            {
                var snapshot = model.Snapshot;
                int last = snapshot.LineCount - 1;
                model.PointerHovered(model.TextLeft + (40 * FixedMeasurer.Metrics.CellWidth), (last * FixedMeasurer.Metrics.LineHeight) + 2);
            });
            await Drive(ui, model, session, Paint, host);
            await ui.Invoke(() =>
            {
                model.Select(0, model.Snapshot.Length);
                model.OnKey(EditorKey.Backspace, KeyModifiers.None);
                model.OnText("class C { void M() { int x = 1 } }");
            });
            await Drive(ui, model, session, Paint, host);

            Check.True(ui.Failures.IsEmpty, "nothing threw on the UI thread and every view event arrived on it:\n" + string.Join("\n", ui.Failures));
            Check.True(session.SemanticError is null, $"the diagnostics pass did not throw ({session.SemanticError})");
            Check.True(model.LastSemanticError is null, $"no semantic request threw ({model.LastSemanticError})");
        }
        finally
        {
            await ui.Invoke(() =>
            {
                model?.Dispose();
                shell.Dispose();
                host.Dispose();
            });
            CSharpLanguage.Unregister();                                       // the registries as the run found them
        }
    }

    [Test]
    public static async Task AHoverWhoseTextShrankBeforeItsDelayIsDropped()
    {
        // The hover's timer fires after the pause; by then a delete can have taken the text under the pointer away.
        var language = new FakeSemanticLanguage();
        var (model, _, _) = Editors.Make("int x = 1;\nint y = 2;\n", new CraftOptions { HoverDelayMilliseconds = 30 }, language);
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        model.PointerHovered(model.TextLeft + (9 * FixedMeasurer.Metrics.CellWidth), FixedMeasurer.Metrics.LineHeight + 2);
        model.Select(0, model.Snapshot.Length);
        model.OnKey(EditorKey.Backspace, KeyModifiers.None);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (ui.IsEmpty && watch.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(5);
        }

        while (ui.TryDequeue(out var action))
        {
            action();                                                    // threw ArgumentOutOfRangeException from the session's Validate
        }

        Check.True(model.Tooltip is null, "no tooltip for text that is gone");
    }

    [Test]
    public static void DiagnosticsPastTheSnapshotAreClampedToIt()
    {
        // A set whose spans run past its own snapshot (a language's slip) is clamped, never drawn or listed past the end.
        var language = new FakeSemanticLanguage();
        var (model, _, _) = Editors.Make("int x = 1\n", null, language);
        var ui = new Queue<Action>();
        model.Dispatch = ui.Enqueue;
        var session = language.Last!;
        session.Diagnostics = new DiagnosticSet(model.Snapshot,
        [
            new(5, 400, DiagnosticSeverity.Error, "E1", "runs past the end"),
            new(900, 0, DiagnosticSeverity.Error, "E2", "starts past the end"),
            new(-3, 2, DiagnosticSeverity.Warning, "W1", "starts before the start"),
        ]);
        session.Raise();
        while (ui.TryDequeue(out var action))
        {
            action();
        }

        int length = model.Snapshot.Length;
        Check.True(model.Diagnostics.ToArray().All(d => d.Start >= 0 && d.Length >= 0 && d.End <= length), "every span inside the text: " + string.Join(", ", model.Diagnostics.ToArray().Select(d => $"{d.Id} {d.Start}+{d.Length}")));
        Check.Equal(3, model.Diagnostics.Length, "clamped, not dropped");
        using var renderer = new EditorRenderer();
        renderer.Draw(new RecordingCanvas(), model, 0, 0, 800, 400, caretVisible: false);
        Check.True(renderer.SquiggleSegmentsDrawn > 0, "drawn");
    }

    [Test]
    public static void AProblemsRowsActionTakesTheProblemAsItIsNow()
    {
        // A row listed before an edit carries the old span: its Fix runs on the editor's current diagnostic, or not at all.
        var listed = new EditorDiagnostic(40, 1, DiagnosticSeverity.Error, "CS1002", "; expected");
        EditorDiagnostic[] now =
        [
            new(3, 4, DiagnosticSeverity.Error, "CS0103", "The name 'x' does not exist"),
            new(10, 1, DiagnosticSeverity.Error, "CS1002", "; expected"),
            new(44, 1, DiagnosticSeverity.Error, "CS1002", "; expected"),
        ];
        Check.Equal(44, ShellText.CurrentProblem(now, listed)?.Start, "the same problem, the nearest to where it was listed");
        Check.True(ShellText.CurrentProblem(now.AsSpan(0, 1), listed) is null, "gone: nothing runs");
        var clamped = EditorModel.ClampTo([new(5, 400, DiagnosticSeverity.Error, "E", "m"), new(-2, 1, DiagnosticSeverity.Error, "E", "m")], 10);
        Check.True(clamped is [{ Start: 5, Length: 5 }, { Start: 0, Length: 0 }], "clamped to the text");
    }

    // Lets the language's passes land and the UI thread run what they posted, then paints, lists and asks as the app does.
    private static async Task Drive(UiThread ui, EditorModel model, CSharpLanguageSession session, Action paint, EditorHost host, bool settle = true)
    {
        if (settle)
        {
            await session.WhenDiagnosticsIdle().WaitAsync(TimeSpan.FromMinutes(2));
            await session.WhenColorsIdle().WaitAsync(TimeSpan.FromMinutes(2));
        }
        else
        {
            await Task.Delay(3);
        }

        await ui.Settle();
        await ui.Invoke(() =>
        {
            paint();
            ProblemsOf(model);
            var snapshot = model.Snapshot;
            foreach (var diagnostic in model.Diagnostics)
            {
                Check.True(diagnostic.Start >= 0 && diagnostic.Length >= 0 && diagnostic.End <= snapshot.Length,
                    $"{diagnostic.Id} at {diagnostic.Start}+{diagnostic.Length} lies inside the text of {snapshot.Length} (version {snapshot.Version})");
                _ = model.DiagnosticsAt(diagnostic.Start);
                var context = new CodeActionContext(host.ActiveDocument!, diagnostic.Start, diagnostic.End, [diagnostic]);
                _ = CodeActionProviders.ActionsFor(context, (name, error) => ui.Failures.Enqueue($"code actions of {name}: {error}"));
            }

            if (Craft.Ai.AiCodeActions.AtSelection(host) is { } atCaret)
            {
                _ = CodeActionProviders.ActionsFor(atCaret, (name, error) => ui.Failures.Enqueue($"code actions of {name}: {error}"));
            }
        });
    }

    // The Problems panel's rows (BottomPanelView.ShowProblems' arithmetic, headless): each row's line and column.
    private static void ProblemsOf(EditorModel model)
    {
        var snapshot = model.Snapshot;
        foreach (var diagnostic in model.Diagnostics)
        {
            int start = Math.Clamp(diagnostic.Start, 0, snapshot.Length);
            int line = snapshot.GetLineIndex(start);
            _ = start - snapshot.GetLine(line).Start;
        }
    }

    /// <summary>A UI thread of its own: actions posted to it run there in order; what throws there, or an event raised elsewhere, is a failure.</summary>
    private sealed class UiThread : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = [];
        private readonly Thread _thread;
        private int _pending;

        public UiThread()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "test UI thread" };
            _thread.Start();
        }

        public ConcurrentQueue<string> Failures { get; } = new();

        public void Post(Action action)
        {
            Interlocked.Increment(ref _pending);
            _queue.Add(action);
        }

        public void AssertOnThread(string what)
        {
            if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
            {
                Failures.Enqueue($"{what} raised on thread {Environment.CurrentManagedThreadId} ({Thread.CurrentThread.Name ?? "pool"}), not the UI thread:\n{Environment.StackTrace}");
            }
        }

        public Task<T> Invoke<T>(Func<T> func)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try
                {
                    done.SetResult(func());
                }
                catch (Exception error)
                {
                    done.SetException(error);
                }
            });
            return done.Task;
        }

        public async Task<T> Invoke<T>(Func<Task<T>> func) => await (await Invoke<Task<T>>(func));

        public async Task Invoke(Func<Task> func) => await (await Invoke<Task>(func));

        public Task<bool> Invoke(Action action) => Invoke(() =>
        {
            action();
            return true;
        });

        /// <summary>Waits until the UI thread has nothing left to run (what it ran may post more).</summary>
        public async Task Settle()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (Volatile.Read(ref _pending) > 0 && watch.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(2);
            }
        }

        public void Dispose() => _queue.CompleteAdding();

        private void Run()
        {
            SynchronizationContext.SetSynchronizationContext(new Context(this));
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception error)
                {
                    // On Windows an exception out of a dispatched action ends the app: here it is the test's failure.
                    Failures.Enqueue("threw on the UI thread: " + error);
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        }

        private sealed class Context(UiThread ui) : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) => ui.Post(() => d(state));
        }
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class QuietShellUi : IShellUi
    {
        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Discard);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }
}
