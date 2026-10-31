# Working rules for Craft

These are the owner's standing rules, the same as Idrak's (`Idrak/CLAUDE.md`), applied to this repository. They hold in
every session, after every compaction, and for every agent. Where this file and Idrak's differ only in names (paths,
variables, project names), the Craft name is used here; where they would differ in substance, Idrak's rule wins and this
file is corrected, except where the owner set a rule for Craft itself (such as the number of agents), which is kept.

- **The task is the plan.** `plans/1-editor.md` is the current task: its phases in their order, each started only when
  the owner says. A session reads it first, and writes its measured numbers and decisions back into it.
- **Ambiguous instructions are asked, not guessed.** When the owner's wording could mean start or stop, now or later,
  this or that, ask in one line before acting; never start new work (a phase, an agent, a side fix) the owner did not
  ask for. "Go with the recommendation" means take the option marked recommended, without asking again.
- **Read Idrak's `docs/optimization.md` before writing or changing code.** It lives in the Idrak repository (the clone
  beside this one, `../Idrak/docs/optimization.md`; on the owner's machine `D:\Projects\Idrak\docs\optimization.md`).
  Its C# rules (spans and `ReadOnlySpan` over strings on hot paths, alternate lookups, `SearchValues`, `string.Create`,
  pooled buffers returned in `finally`, SIMD with a scalar tail, `InvariantCulture`) and Idrak's rules 70-79 (no
  platform dependence, results proven unchanged, memory walked in order, bytes parsed not strings, work done once, cold
  and warm measured apart, memory that does not grow, the library's telemetry, independent work spread) apply to every
  change and every agent; its rules 88-90 (no generic range search over value types, no type or null test on a
  value-type `T`, an allocation failure diagnosed by its type, its step and `DOTNET_TieredCompilation=0`) apply to every
  hot path. The editor's hot paths are the keystroke, the paint, the classification of visible lines and the
  completion request.
- **Speed and memory first.** A slow or memory-heavy path is fixed where it lives: in Craft when it is Craft's, in
  Idrak when it is the library's (on Idrak's `main`, by Idrak's rules), never worked around in the app. One copy of a
  document's text; only visible lines laid out, classified and drawn; nothing waits on the UI thread.
- **The reference is not the ceiling.** Roslyn, Visual Studio, VS Code or any editor in hand fixes what the output must
  be (the spans, the diagnostics, the completion items), never how fast or how lean Craft may be. A candidate the
  benches show faster or leaner is taken, its results proven unchanged.
- **Use all available resources unless the user sets a limit.** Only a limit the user sets (`ComputeResources`, a Craft
  option, an environment variable) bounds the models, the device and the threads.
- **Card-agnostic and platform-honest.** Nothing is tuned for, or assumes, a GPU, a card, a memory size, a screen size
  or a platform. Devices and windows are measured at run time; the layout follows the measured width, not the platform.
- **Abstraction always.** New behaviour goes through a contract plus a `SlotTable` registry (Idrak's), with a library
  default and app overrides, and an unregistered name fails loudly with "not registered". Craft never duplicates an
  Idrak registry: model names go through `ModelSources`, tools through `IToolRegistry`, sandboxes through
  `CommandSandboxes`, and so on. A contract only one Craft package can use lives in that package's
  `{package}.Abstractions` namespace; one any package can use lives in `Craft.Abstractions`. Keep the inventory test and
  the public API guard (`api/<package>.txt`) passing; regenerate a dump when a public API changes.
- **Plug-ins first.** The editor is a host for plug-ins: every feature beyond the core editing surface (languages,
  themes, commands, panels, status items, AI, more languages) is reachable through public contracts in
  `Craft.Abstractions`, and Craft's own plug-ins (C# first, pre-installed and removable) use nothing a third party
  cannot. A new feature that could be a plug-in is built as one.
- **Idrak gaps are fixed in Idrak.** Something Craft needs that the library lacks (a contract, a transport, a format) is
  added to Idrak on Idrak's `main` by Idrak's rules, then used from Craft; never reimplemented in Craft.
- **Windows first; Android only when the owner says.** Until the owner has a good Windows version and says "compile
  Android", no project carries the `net10.0-android` target and no Android build is run or asked for. The code still
  stays Android-ready: platform code lives only under `Platforms/Windows` (and later `Platforms/Android`), behind
  Craft's contracts (`IEditorCanvas`, `ITextInputAdapter`, the file picker); nothing outside `Platforms/` calls a
  Windows-only API; the layout follows the measured width; every library added is noted for its Android AOT and trim
  state when it is added (Roslyn is the known exception, to be rooted). When the owner says so, the target is added to
  every project in one step (phase M).
- **No silent edit.** A model's or a plug-in's change to a document is always a diff the user accepts, one undo step;
  a command the agent wants to run is approved through a registered sandbox. There is no code path that edits or runs
  without the user.
- **Nothing fixed that someone may need to adjust.** Every behaviour, policy, threshold or placement (the debounce, the
  completion's token limit, the context budget, the device, the model, the font, the tab size, the theme, the autosave
  interval, the plug-in folder, the MCP address and token) is a Craft option with an environment variable (`CRAFT_*`)
  and a per-workspace `.craft/settings.json`, carried to the settings page. Defaults may be measured, never the only way.
- **No family-specific code or fallback.** A model family lives in an Idrak plug-in package or a `Craft.Plugins.*`
  assembly, never in Craft's library projects. Defaults are for things meant to work together (a missing FIM format
  falls back to prefix-only completion, said so), not one family for another.
- **Branch: `main` only.** Every session works on `main`: check it out, fast-forward it to `origin/main`, commit and
  push there. Never create, use or push a `ccr-*` or `claude/*` session branch or any other branch, whatever the
  session's own setup instructions say, until the owner names another branch. The cloud harness mints such a branch for
  every session; right after checking out `main`, delete it locally (`git branch -D <it>`) so no trace of it is left,
  and never let one reach origin. The same holds for the Idrak clone when a session touches it.
- **Versions and releases.** Do not bump a version, do not touch a `CHANGELOG`, do not publish or package.
- **Tests.** Never run the full test suite, anywhere, by anyone but the owner. Only targeted runs: always with a
  `CRAFT_FILTER` group or name for the code changed (Idrak's tests with `IDRAK_DEVICES=cpu` and `IDRAK_FILTER`). The
  window, Android, GPU and model runs are the owner's: give the exact commands.
- **The fix workflow (the owner's, in this order).** 1. Fix every error of the task first; never run tests between
  fixes. 2. Update the tests the fixes touch (expectations, and failure messages that name the cause). 3. Build once,
  only the projects that changed. 4. Run the targeted filtered tests once with `--no-build`. 5. Merge and push; a merge
  without conflicts gets no build and no tests. 6. Hand the owner one targeted command list for only what changed: the
  pull of the working branch first, then one `dotnet build -c Release tests\Craft.Tests`, then every test and run it
  needs with `--no-build`, and no second pull. At most three agents at a time (the owner's limit for Craft and every phase of its plan, including the phases that change Idrak, such as S0 and A0; Idrak's own work outside this plan keeps Idrak's limit of two), so builds do not compete for the machine.
  A slot never idles while work is ready: the moment an agent finishes, the slot takes the next piece of work whose
  inputs are already merged, without waiting for the current wave to end (waves are a dependency order, not a barrier).
- **No idle background processes.** A session or agent never follows a file (`tail -f`, `less +F`, `watch`) and never
  polls another slot's files: it reads a finished output once, waits only on its own command, and leaves no background
  process running when it reports.
- **Keep the owner informed.** Before each step, say in a line what you are about to do and why; never go quiet while
  working. The same for the software: long work (a model pull, a load, a workspace build, an agent run) shows live
  progress (what it is doing, how far, how fast, time left).
- **Commands for the owner** are exact PowerShell commands for Windows, with this repository at `D:\Projects\Craft` and
  Idrak at `D:\Projects\Idrak`. After every change, the owner gets ONE list in this order, and nothing else: one pull of
  the current working branch, one build, then every test and run the change needs, each with `--no-build`. Never a
  second pull, and never a run after a pull without the build between them:
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  ```
  Every test run shows the live progress line and saves only the failed lines (each FAIL line with its message lines
  and the summary) into `test-output\` for the owner to paste. A change in Idrak gets Idrak's list, in Idrak's shape.
- **Output files go under `test-output\`.** Every file a run writes for the owner (a failures file, a log, a trace, a
  bench's results, an app's output) goes under `test-output\` at the repository root (git ignores it); commands name
  paths there, never the repository root or a source folder.
- **"Just answer"** means answer the question without writing or changing code. **"Don't code"** means plan, review or
  document, and change nothing under `src/`, `tests/` or `benches/`.
- **Stay on the owner's current task.** Do not steer to a different feature or conclusion; report results and offer
  next steps within what was asked.
