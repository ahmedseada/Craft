# Plan 1: Craft, a code editor (C# first) with AI plug-ins on Idrak's contracts

**Status:** planned 2026-10-31 against Idrak `main` d01a3b1 and Craft `main` 39f26ab; reviewed and verified against
Idrak's code the same day (the "Verified" section). Nothing is built. The owner's answers (2026-10-31): the editor lives
in Craft; .NET MAUI with Craft's own drawn editor control; Windows only until the owner has a good Windows version and
says "compile Android" (the Android target, builds and runs are phase M; the code Android-ready from E0); Roslyn from
the start; the plug-ins of phase P test Idrak's plug-in
points; the Idrak.Mcp gap (serving MCP from a running app) is fixed first, phase S0. Next: S0, when the owner
says.

## Why

The owner's request (October 2026): a code editor, mainly for C# for now, with syntax highlighting, that runs on the
desktop and on a phone, and whose AI features are plug-ins built on Idrak's contracts.

There are two parts. **The editor** is the buffer, the highlighting, the editing surface and the app shell. **The AI
plug-ins** are completion, chat, code actions and the agent. The editor never names a model, a family or a vendor: it
talks to `IChatModel`, `ITextModel`, `ITool` and `IToolRegistry`, so any model Idrak loads (local, on the device, or
served) drives it.

## Verified against Idrak (2026-10-31, `main` d01a3b1)

Every Idrak capability this plan leans on, checked in the source. A claim that did not hold was corrected in the plan.

| Capability | Where | Holds? |
|---|---|---|
| Plug-in loading from an assembly | `IdrakPlugins.Load(Assembly)`, `IIdrakPlugin.Register/Unregister`, `IIdrakPluginPackage.All`; the CLI's `--plugin`/`-P` and the config's `plugins` list (`CommandContext`) | Yes |
| KV cache reused across prompts that share a prefix | `TextGenerator.KeepCache` (default true): "the next prompt that starts with the same tokens only processes what follows them"; the reuse at TextGenerator.cs:1176 | Yes, by design; the editor's pattern is still unmeasured (A0) |
| A cancelled generation stops at the next token | `TextGenerator` decoding loops check the token each step (lines 772, 1207) | Yes |
| Tools as a contract | `ITool`, `Tool`, `IToolRegistry`, `IToolProvider`, `[assembly: ToolProvider]`, `ToolProviders.FromMethods` | Yes |
| Tool assemblies in the CLI | `idrak tools list`, `idrak tools test`, `--tools` on `chat`, `run`, `agent`; `idrak mcp serve TOOLS.dll` over standard input and output | Yes, out of process (see P1) |
| Coding agent | `CodingAgent(IChatModel, AgentOptions).RunAsync(prompt, CodingTools)`; `CodingTools(workspace, options, sandbox)`; `CommandSandboxes` ("process" built in; "container" in Idrak.Reinforcement.Plugins) | Yes |
| C# as a coding language | `CSharpLanguagePlugin` in **Idrak.Text.Plugins** ("csharp"), not in the library: the app must register it | Yes, with that note |
| `git` for the agent | `CodingTools` allows `git` with a subcommand list; optional on the machine | Yes |
| Model names | `ModelSources` (Idrak.Abstraction.Formats): folder, `store:`, `.gguf`, Hugging Face id (Idrak.Data); newest source asked first | Yes |
| Models kept loaded | `ModelHost<TModel>`; Idrak.Maui's `IChatModelService` returning a `ChatLease` with its `IChatModel`; memory pressure on Android **and Windows** (`WindowsMemoryPressure`) | Yes |
| Context length | `DecoderSpec.MaxPositions` | Yes |
| Images in chat | `ChatPart.Kind` "image", `ChatImage.FromFile/FromBytes`, `IChatModel.PartKinds`, `ChatParts.ThrowIfUnsupported` | Yes |
| Retrieval | `VectorIndex`, `KeywordIndexes` (BM25), `PretrainedEncoders`, `SequencePoolers` | Yes |
| Speech and capture | `SpeechFamilies`, `AudioCaptures` with `AAudioCapture` (Android), winmm, alsa | Yes |
| Conformance suites | `Idrak.Abstraction.Testing/Contracts`: `TokenSamplerSuite`, `TextMatcherSuite`, `ProgressReporterSuite`, `KeywordIndexSuite`, `ChatPartKindSuite`, `SpeechRecognizerSuite`, `VisionEncoderSuite` | Yes: P3, P4, P9, P6, P8, P7 each have one |
| From-source switch | `build/IdrakFromSource.targets`, imported by an app's `Directory.Build.targets` when `IdrakFromSource` is set; its package list includes `Idrak.Maui` | Yes |
| Test runner | `tests/Idrak.Tests.Library/TestRunner` (`IDRAK_FILTER` split at `\|`, `--progress`, `--failures-only`, `--failures-file`); `IsPackable=false`, so it is not a package | Yes, from source only |
| API guard | A test per package reads `api/<package>.txt` through `RepositoryPath` | Yes |
| MAUI version | `MauiVersion` is set per project (Idrak.Maui.csproj, Idrak.Mobile.csproj), not centrally | Craft sets its own |
| Fill-in-the-middle | Nothing in Idrak (no FIM type, token or format) | **Missing** (A0) |
| Remote OpenAI-compatible client | Idrak.AspNetCore serves `/v1`; no client `IChatModel` | **Missing** (A3, held) |
| MCP into a running app | `Idrak.Mcp` has the client over stdio and HTTP (`ConnectStdioAsync`, `ConnectHttpAsync`) and server tools from an `IToolRegistry` (`McpTools.ServerTools`), but the only server transport is `StreamServerTransport` over standard input/output, built inside the CLI (`McpServeCommand.cs:100`); no app can serve | **Missing** (S0, first) |
| MCP client in the CLI | `--mcp URL` or `--mcp "command args"` on `chat` and `run` (`RunSupport.cs:88`) | Yes: the end-to-end check of S0 |

## What exists today (surveyed 2026-10-31)

- **Craft** (`ahmedseada/Craft`) holds this plan and nothing else. It is the editor's home (the owner's answer).
- **Idrak.Maui** (plan 34) is a MAUI library with Android and Windows targets: `UseIdrak`, the options as saved
  settings with a settings page, `IMobileFeature` in `MobileFeatures` (a `SlotTable`), models kept loaded and released
  on memory pressure, a model store with pull progress, device information, light and dark themes. Craft reuses it.
- **Chat, tools and the agent**: see the table above.
- **Missing in Idrak**: a way for an MCP client to reach a running app's tools (S0, first); a fill-in-the-middle
  contract (A0); a remote `IChatModel` (A3).

## The shape

### Projects (in Craft, referencing Idrak)

| Project | What it holds |
|---|---|
| `Craft.Abstractions` | The editor's contracts and their registries (`SlotTable` / `KindTable`). Reference it alone to write a plug-in. |
| `Craft.Core` | Text buffer, documents, undo, autosave and recovery, workspace, the classification layers, themes, the plug-in host. No UI, `net10.0`, AOT-compatible, tested on any machine. |
| `Craft.CSharp` | C# on Roslyn (`Microsoft.CodeAnalysis.CSharp.Workspaces` and `.Features`): syntactic and semantic classification, diagnostics, completion, signature help, hover, go to definition, formatting. Registered as the "csharp" language (".cs") and "csharp-script" (".csx") on both platforms, as a **pre-installed, removable plug-in**: an Idrak plug-in package (`CSharpPlugin`, `[assembly: IdrakPluginPackage]`) that the app never references in code and loads from `plugins/Craft.CSharp/` beside the exe (`CraftPlugins.LoadInstalled`, its own `AssemblyLoadContext`, the contracts shared with the host); without it, or with `CRAFT_PLUGINS_DISABLED=Craft.CSharp`, a .cs file opens as plain text. Not AOT-compatible (Roslyn's MEF uses reflection): `IsAotCompatible` false, its assemblies rooted for the trimmer; Craft never uses NativeAOT. |
| `Craft.Editor` | The editor without a UI: caret, selection, commands, layout, hit-testing, find and replace, bracket matching, and the drawing of the visible lines through `IEditorCanvas`. The UI hosts it; the tests drive it headless. (Split from `Craft.Maui` in E0 so the control's logic is tested without MAUI.) |
| `Craft.Ai` | The AI plug-ins on Idrak's contracts: inline completion, chat panel, code actions, agent (the editor's tools moved to `Craft.Plugins.Tools` in P1, reached through `EditorToolSets`). |
| `Craft.Maui` | The editor control and the pages: the drawn surface behind `IEditorCanvas`, the input adapters per platform, features registered into Idrak.Maui. Targets `net10.0` (headless, for tests) and the Windows target; `net10.0-android` is added in M. |
| `Craft.App` | The app: Windows only until the owner says "compile Android" (M). It makes the registrations (Idrak's plug-in packages, Craft's plug-ins) and hosts the models. |
| `Craft.Plugins.*` | The plug-ins of phase P, each its own assembly. Each implements `IIdrakPlugin` too, so one loader (`IdrakPlugins.Load`) serves Idrak's and Craft's registries alike. |
| `Craft.Tests` | Targeted groups (`CRAFT_FILTER`), with the same runner flags as Idrak's tests (see "Tests and benches"). |
| `Craft.Benches` | The measurements of "Speed and memory", results under `test-output\`. |

Idrak is built from the clone beside this repository (`../Idrak`, on the owner's machine `D:\Projects\Idrak`), through
the clone's `build/IdrakFromSource.targets`: the contracts Craft uses (`IKind`, `KindTable`, and S0's MCP host) are on
Idrak's `main` and not in the published 0.4.1 package, and publishing is not Craft's to do. `-p:IdrakFromSource=FOLDER`
names another clone; `-p:IdrakFromPackages=true -p:IdrakVersion=VERSION` takes published packages once they carry what
Craft needs. Craft's `Directory.Build.props` carries Idrak's settings (net10.0, nullable, implicit usings, warnings as
errors, the performance analyzers, `using Idrak.Abstraction`); MAUI's version is the installed workload's
(`$(MauiVersion)`), as in Idrak.

### Editor contracts (`Craft.Abstractions`)

| Contract | Registry | What it is |
|---|---|---|
| `ITextBuffer` / `ITextSnapshot` | n/a | A piece table with a line index. A snapshot is immutable, read as `ReadOnlySpan<char>` by line or range, and is **also Roslyn's `SourceText`** (a `SourceText` subclass over the snapshot: indexer, `Length`, `CopyTo`, `GetSubText`, `WithChanges` returning the next snapshot), so Roslyn reads the piece table and nothing is copied per keystroke. An edit is a `TextEdit(start, length, text)` list applied atomically, giving a new version. |
| `ILanguage : IKind` | `EditorLanguages` | A language: file patterns, comment and bracket rules, and its language service. "csharp" is registered by `Craft.CSharp`. A language that is not registered fails with "not registered"; a file of no registered language opens as plain text, said so. |
| `ILanguageService` | (from the language) | Two layers: a syntactic one (classified spans of a line range, from an incremental parse, fast enough for the keystroke) and a semantic one (async and off the UI thread: semantic spans, diagnostics, completion, signature help, hover, definition, formatting). Every async method takes a cancellation token. |
| `IWorkspaceReader : IKind` | `WorkspaceReaders` | How a folder becomes projects: sources, references, defines. "csproj" reads the project file itself (default items, `Compile` globs, `ProjectReference`) and takes resolved references from `obj/project.assets.json` when a restore left one, else the running runtime's assemblies (`TRUSTED_PLATFORM_ASSEMBLIES`); "loose" wraps stray files as one script-like project. No `MSBuildWorkspace`: it needs the SDK and does not exist on Android. |
| `ITheme : IKind` | `EditorThemes` | Token class to colour and style. Light and dark built in, following Idrak.Maui's themes. |
| `IEditorCanvas` | n/a | What the control draws with: measure a run, draw a run, fill, clip. Platform text layout (DirectWrite through Win2D on Windows, `android.graphics` on Android) shapes and reorders bidirectional text; tests use a fixed-metric fake. |
| `ITextInputAdapter` | n/a | Keys, composition (IME), selection handles, the clipboard, per platform under `Platforms/`. |
| `IEditorPlugin : IKind` | `EditorPlugins` | A plug-in: what it needs (a chat model, a text model, the workspace, a sandbox, a network) and `Activate(IEditorHost)`. |
| `IEditorHost` | n/a | What a plug-in reaches: documents, selection, `ProposeEdits` (always a diff the user accepts), diagnostics, commands, panels, ghost text, settings, progress and telemetry. There is no silent edit path. |
| `IInlineCompletionProvider` | `CompletionProviders` | Ghost text at the caret, streamed and cancelled by the next keystroke. |
| `ICodeActionProvider` | `CodeActionProviders` | Actions over a selection or a diagnostic (explain, fix, refactor, document, write tests), returning edits. |
| `IModelProvider : IKind` | `ModelProviders` | How the plug-ins' `IChatModel` and `ITextModel` are reached: "local" (a name resolved by Idrak's `ModelSources`, loaded once by `ModelHost`, leased through Idrak.Maui's `IChatModelService`) or "remote" (A3). It never resolves names itself. |

Model names are always resolved by Idrak's `ModelSources`, the same list the `idrak` tool uses, so a name that works in
the CLI works in the editor. A new kind of name (such as `craft:`, plug-in P10) is a source registered into
`ModelSources`.

Each registry is a `SlotTable` with library defaults and app overrides. Craft gets its own inventory test
(`plans/inventory.md`, generated) and an API guard per package (`api/<package>.txt`), as Idrak has.

### Plug-ins first (the owner's priority)

The editor is a host for plug-ins, and its own features prove it. Everything after the core editing surface (languages,
themes, commands, panels, status bar items, the AI features, the phase P plug-ins, phase L's languages) reaches the editor
through public contracts in `Craft.Abstractions`, so a plug-in author needs only `Craft.Abstractions` (and
`Idrak.Abstraction`), never Craft's internals:

- **The contracts (phase K):** `IEditorPlugin : IKind` with `Activate(IEditorHost)` and its contributions; `IEditorHost`
  with the documents and their lifecycle events (opened, changed, saved, closed), the active editor's selection,
  `ProposeEdits` (a diff the user accepts), commands with key bindings (`EditorCommands`), status bar items, panels,
  settings a plug-in declares (shown on the settings page, each with a `CRAFT_*`-style variable named after the plug-in),
  progress and telemetry; languages (`EditorLanguages`) and themes (`EditorThemes`) as today.
- **Packaging:** a plug-in is a folder `plugins/<Name>/` with `<Name>.dll`, its dependencies and a `plugin.json` (name,
  version, summary, the Craft contract version it was built against, the capabilities it asks for: files, network,
  a sandbox, a model). One loader (`CraftPlugins`, through `IdrakPlugins.Load`) for Craft's and Idrak's registries alike;
  each plug-in in its own load context, sharing the contracts. Pre-installed plug-ins (C# first) ship in the app's
  `plugins/`, are removable and can be disabled; user folders are off until the user names one.
- **For authors:** a sample plug-in (`Craft.Plugins.Sample`: a word-count status item and a command), a `dotnet new`
  template, and a guide (`docs/plugins.md`): the contracts, the folder layout, how to debug one against the app.
- **Versioning:** the plug-in contracts carry a version; a plug-in built against an older one is marked, one against a
  newer one is refused with the reason, never loaded half-way.
- **Proof:** C# is the first plug-in (E2, pre-installed); the AI features (A2), phase P and phase L are all plug-ins on
  the same contracts; nothing Craft ships as a plug-in uses a contract a third party cannot.

### Threading and safety

- The UI thread owns the buffer. Snapshots are immutable, so Roslyn, the plug-ins and the benches read them on any
  thread; an edit is applied on the UI thread only, either typed, or accepted from a diff.
- Autosave writes a recovery copy under the app's data folder on a timer (a setting) and on a crash the next start
  offers it; the file on disk is written only on Save.
- A plug-in loads once per process (no unloading); one that throws in `Register` is reported in the Plug-ins page and
  skipped, never crashing the editor. Loading from a folder is a setting that is off until the user names the folder: a
  plug-in is code. A plug-in built against an older Idrak than the one running is marked (`SlotOverride.Outdated`).
- Roslyn's workspace holds large memory; its size shows in telemetry and the workspace is released on the memory-pressure
  signal (Windows and Android) and rebuilt on the next request.

### AI plug-ins (`Craft.Ai`), each only on Idrak's contracts

- **Inline completion.** Prefix and suffix around the caret, laid out by the model's FIM format (A0), streamed from
  `ITextModel`. It starts after a pause in typing (a setting) and is cancelled at the next keystroke. The layout puts
  the prefix first so `KeepCache` reuses everything up to the caret. The context budget comes from the model's
  `MaxPositions`, never a fixed number. A model with no FIM format falls back to prefix-only completion, said so in the
  status bar, never silently. **Measured in A0 (Idrak b7c5222):** a prefix-first FIM layout (qwen, deepseek, gemma: the
  prefix, then the suffix, then the middle) puts the suffix after the caret's text, so each keystroke changes the prompt
  before the suffix and the whole suffix is processed again: about 25% of the prompt reused from the kept cache; a
  suffix-first layout (mistral/Codestral: the suffix, then the prefix) keeps everything up to the caret, about 98% reused.
  So the suffix budget (how much text after the caret is sent) is a `CRAFT_*` setting, because it sets the per-keystroke
  cost on a prefix-first model, and A2 takes the layout from the model through `FillInMiddleFormats.Detect` / `For`
  (`src/Idrak.Abstraction/Generation/FillInMiddleFormats.cs`) and reuses up to `FillInMiddlePrompt.PrefixEnd`, never a
  family named in Craft.
- **Chat panel.** `IChatModel` with the open document, the selection, Roslyn's diagnostics and the symbols in scope as
  context, and the editor's tools through `IToolRegistry`. Replies stream with tokens per second. Code blocks have
  "apply", which goes through the diff view.
- **Code actions.** Explain, fix this diagnostic, refactor, add doc comments, write a test. Each is an
  `ICodeActionProvider` whose edits go through the diff view. A model's snippet is placed by a `TextMatchers` matcher
  (P4), and Roslyn is asked for errors before the diff is offered.
- **Agent.** Idrak's `CodingAgent` and `CodingTools` over the open folder, in a registered sandbox. `Craft.App`
  registers Idrak.Text.Plugins' `CSharpLanguagePlugin` so "csharp" is a coding language. Every edit it makes is shown
  as a diff and its progress (turn, tool, tokens) is live. Where no sandbox is registered (a phone), the agent is
  refused with the registry's "not registered" message.
- **The editor's tools** (`IToolProvider`), with two backings behind the same `ToolDefinition`s:
  - the **live host** inside the app (`IEditorHost`: open documents, the selection, Roslyn's diagnostics,
    `propose_edit` into the diff view);
  - the **folder host** for any other process (`idrak tools test`, `idrak mcp serve`, `idrak chat --tools`): the
    files under a folder given as an option, `find_symbol` through `Craft.CSharp`, `propose_edit` written as a patch
    file the user applies.

  Tools: `open_documents`, `read_document`, `get_selection` (live only), `get_diagnostics`, `find_symbol`,
  `propose_edit`. The app serves the **live** tools to MCP clients itself through S0's `McpHost` over the "http"
  transport (loopback, a token), so an outside agent or `idrak chat --mcp http://127.0.0.1:PORT` works on the open
  documents; the folder host covers a process that is not the editor.

### Idrak library gap, first: MCP served from a running app (in Idrak, on `main`, phase S0)

Today `idrak mcp serve TOOLS.dll` builds an MCP server over standard input/output inside the CLI, and that is the only
way Idrak serves MCP. A GUI app that is already running cannot be started by an MCP client, so its tools (Craft's live
documents, or any app's) are unreachable. The owner's order: fix this before the editor starts.

- **The contract.** `IMcpServerTransport : IKind` in `Idrak.Mcp.Abstractions` (only Idrak.Mcp can use it, so it lives
  in the package's own Abstractions namespace, as `CLAUDE.md` says), registered in `McpServerTransports` (a `SlotTable`).
  A transport accepts MCP sessions for a server from the options it is given (`IKind<McpTransportRequest, ...>`), reports
  where it listens (a string a client config can copy), and stops on dispose.
- **The built-in kinds.**
  - `stdio`: today's `StreamServerTransport` over the process's standard streams, moved behind the contract.
  - `http`: MCP's streamable HTTP on a loopback listener (`HttpListener`, no ASP.NET dependency in Idrak.Mcp), the
    port given or 0 for one the OS picks, a bearer token required unless the option turns it off. One session per
    client; sessions and their cancellation shown in telemetry. Whether the SDK's core package carries the
    streamable-HTTP server transport without ASP.NET is confirmed on the first day of S0; if not, Idrak.Mcp implements
    the transport over the SDK's `ITransport`, as `StreamServerTransport` is.
  - `Idrak.AspNetCore` registers `kestrel` later, so a served model and its MCP endpoint share one server (not S0).
- **The host.** `McpHost` in Idrak.Mcp: `await using var host = await McpHost.StartAsync(transportName, registry,
  options)`, serving an `IToolRegistry` (and optionally an `IModelCatalog`) from any process; `host.Address` for the
  client config; a transport that is not registered fails with "not registered". The CLI's `mcp serve` and
  `serve --mcp` move onto it with `--transport` (default `stdio`), `--mcp-address`, `--mcp-token`, each with an
  environment variable (`IDRAK_MCP_TRANSPORT`, `IDRAK_MCP_ADDRESS`, `IDRAK_MCP_TOKEN`) listed by `idrak env`; nothing
  fixed that someone may need to adjust.
- **Tests.** Group `Mcp` in `tests/Idrak.Tests` (`IDRAK_DEVICES=cpu`, `IDRAK_FILTER=Mcp`): over each transport, start,
  list tools, call a tool, cancel a long call, stop; `http` refuses a wrong token and a non-loopback bind unless asked;
  the "not registered" message; the `stdio` path still passes the existing MCP tests. The end-to-end check the owner
  runs: `idrak mcp serve TOOLS.dll --transport http` in one window and `idrak chat MODEL --mcp http://127.0.0.1:PORT`
  in another.
- **Measured.** Per-call overhead of `http` against `stdio` on a trivial tool, cold and warm apart (rule 75); the
  listener holds no buffers between calls (rule 76). The public API dump (`api/Idrak.Mcp.txt`) is regenerated.
- **Android.** `HttpListener` is managed code and should run on Android; it is measured in M like the rest.

### Idrak library gaps (in Idrak, on `main`, phase A0)

- **FIM as a contract**, in the pattern of `ToolCallFormats`: `IFillInMiddleFormat : IKind<FillInMiddleContext,
  IFillInMiddlePrompt>` in `Idrak.Abstraction.Generation`, registered in `FillInMiddleFormats` (a `SlotTable`). A
  context is the prefix, the suffix and optional repository snippets; a prompt is the text (or ids) to feed and the
  stop tokens to end on. Two layouts built in as building blocks (prefix-suffix-middle, suffix-prefix-middle); the
  tokens come from the tokenizer's special tokens, named by each family's plug-in in `Idrak.Text.Plugins`. The
  library registers no family; a model whose family registers none gets "not registered". A conformance suite
  (`FillInMiddleFormatSuite`) in `Idrak.Abstraction.Testing`.
- **Measured reuse.** A bench of the editor's pattern: one request per pause, each cancelled by the next, the prefix
  growing by a few tokens. Cold and warm apart (rule 75); it checks that `KeepCache` reuses the shared prefix, that a
  cancelled generation stops at the next token and frees what it holds, and that device memory does not grow across a
  thousand requests (rule 76). Anything slow is fixed in the library.
- **Preference learning from episodes (phase R0, added 2026-10-31).** Idrak already has the preference losses:
  `FineTuningLosses.Dpo`, `Orpo` and `SimPo` over `PreferencePair` (chosen and rejected after the same prompt), trained by
  `FineTuning.Train` and `idrak tune` from prompt/chosen/rejected rows. What it lacks is the road from reinforcement
  episodes to them, which the editor needs because its richest signal is a choice (a suggestion rejected, then the text
  the user wrote or kept instead; two answers to the same context, one accepted). Added in Idrak, on `main`, by its
  rules: (1) **pairs from episodes**: a contract and registry (`IPreferencePairSource`, `PreferencePairSources`) that turns
  a `RolloutBatch` or recorded `Trajectory`s into `PreferencePair`s, with two built in: "group" (the best and the worst
  answer of a group to one prompt by reward, a margin setting) and "verdict" (an episode's rejected answer against the
  text the episode records as kept); (2) **the preference losses by name**: `FineTuningLosses` gains a registry
  ("dpo", "orpo", "simpo", the library defaults, options as `IOptionSchema`) so an app or a setting chooses one by name
  and a missing name fails "not registered", alongside the existing factories; (3) **iterative preference training**: a
  trainer step that collects a rollout, builds pairs, and trains the adapter against the reference (the previous
  adapter), repeatable, with `idrak reinforce` able to use it (`--pairs group --loss dpo`); (4) a conformance suite
  (`PreferencePairSourceSuite`) and a bench (pairs per second, a step's time and memory, cold and warm apart, memory flat
  over many rounds). Checks: on a tiny random-weight model, pairs built from a scored rollout train with the DPO loss
  falling and the chosen answer's margin over the rejected rising; the built-ins' pairs equal hand-made ones.
- **R0 as built (Idrak f419399, 2026-10-31).** `IPreferencePairSource` and `PreferencePairSources` in
  `Idrak.Abstraction.Reinforcement` ("group": best against worst return per group, `margin`; "verdict": the kept text
  against the sampled answer from the episode-info keys `PreferenceInfoKeys` `verdict` (accepted, edited, rejected,
  undone; none counts as rejected), `kept`, `kept_step`, option `verdicts`); `PreferencePairs.Build` in Idrak.Text turns
  recorded trajectories into pairs for `FineTuner.Train` (what phase R calls without the trainer); `PreferenceLosses`
  ("dpo", "orpo", "simpo" over the existing factories; `FineTuningLosses.Preference(name)`); preference rounds in
  `ReinforcementTrainer` (`ReinforcementOptions.Pairs`, `PreferenceLoss`, `PreferenceReference` "previous" or "base",
  `IDRAK_RL_PAIRS`, `IDRAK_RL_PREFERENCE_*`), `idrak reinforce --pairs --loss --pair-option --loss-option --reference`;
  `PreferencePairSourceSuite`; the bench `--bench-preference`. Measured (CPU, 4 threads): choosing pairs from 1,024
  episodes warm 120 µs ("group", 128 pairs) and 167 µs ("verdict", 413 pairs), 0 bytes per warm round; 40 DPO rounds on
  the tiny decoder: update warm median 11 ms (cold 61), rollout warm 8 ms, device memory flat (191,872 B round 2 and
  round 40), managed 18.6 to 18.7 MB; the loss falls and the chosen answer's margin rises by more than 0.5.
- **Held:** a remote `IChatModel`/`ITextModel` over an OpenAI-compatible endpoint (generic, so `Idrak.Text`). A3, the
  owner's call.

### Windows first, Android-ready code

Windows is built and run first; the Android target is added only when the owner has a good Windows version and says
"compile Android" (M). Until then no project carries `net10.0-android` and no Android build is run. The code never shuts
Android out:

- Platform code lives only under `Platforms/Windows` (and later `Platforms/Android`), behind `IEditorCanvas`,
  `ITextInputAdapter`, the file picker and the window or screen size. Windows implementations in E3, Android in M.
- The layout is chosen from the measured window width, not the platform, so a narrow Windows window already shows the
  phone layout, built and checked on Windows before M.
- Nothing calls a Windows-only API outside `Platforms/Windows`; the agent's need for a shell is a registered
  `CommandSandboxes` entry, not an assumption.
- Every library added is noted for its Android AOT and trim state when it is added, so M finds no surprise. Roslyn is
  the known exception, to be rooted.

### Speed and memory (docs/optimization.md applies to every phase)

- One copy of the text: the piece table is the `SourceText`. Memory per open file is the text plus its pieces, not
  twice the text (rule 76). A keystroke costs the edit plus the re-parse Roslyn does incrementally, never the file.
- Only visible lines are laid out and drawn. Glyph runs are cached per line and version; lines are read as spans.
- Only the visible lines are classified; spans go into a pooled array returned in `finally`. Until a line's spans
  arrive it is drawn in the default colour, so a frame never waits. If the measured keystroke-to-paint shows Roslyn's
  syntactic pass too slow on a device, a hand-written incremental lexer for the visible lines is the candidate (rule
  71: its spans proven equal to Roslyn's).
- Semantic work and AI work run off the UI thread, debounced and cancellable. One workspace per open folder; its
  references loaded once.
- Bidirectional text (Arabic strings and comments) is shaped by the platform layout; the editor keeps logical caret
  positions and maps them through the layout's hit-testing. An Arabic file is one of the bench inputs. If the platform
  layout measures slow, Idrak's own shaper (`TextShapers`, the Idrak.Imaging font engine) is the candidate (P11).
- Roslyn on Android: rooted for the trimmer; APK size, cold start (first classification, first completion) and warm
  costs measured with full and profiled AOT (`IdrakAndroidAot`), written into this plan (M).
- The benches (`Craft.Benches`), cold and warm apart, results under `test-output\`: keystroke to paint; scrolling a
  100,000-line file; opening a 100,000-line file; re-classifying after an edit that opens a block comment or raw string;
  first and warm completion list and diagnostics; completion latency, first token and per token; memory per open file.
- Every threshold is a setting with a measured or sensible default: the debounce, the completion's token limit, the
  context budget (from `MaxPositions`), the device, the model, the font, the tab size, the theme, the autosave interval,
  the plug-in folder, the file size above which the semantic layer waits for a click. Each is a Craft option with an
  environment variable (`CRAFT_*`) and a per-workspace `.craft/settings.json`, carried to the settings page.

- **Measured (E1, 2026-10-31, `benches/Craft.Benches -- buffer`, a 4-core x64 Linux container, .NET 10.0.14):** a
  generated C# file of 100,005 lines (2,676,961 characters) opens in 25.4 ms cold, 10.1 ms warm; the open document holds
  5.88 MB = 1.10 x the text as UTF-16 (the text, kept once, plus its line index). 10,000 random edits through the
  document (typing runs, backspaces, newlines, pastes, caret jumps; with undo): first edit 10.0 ms cold (JIT), first 100
  112 µs mean, warm 2.24 µs mean, 0.70 µs median, 10.7 µs p99, 1,550 B allocated an edit (the path of new tree nodes,
  the event, the history). `GetLine` warm 0.069 µs on the opened file and 0.265 µs after the edits (many pieces), 0 B a
  call; 60 visible lines read (`GetLine` + `CopyTo`) 9.4 µs. After the 10,000 edits the document holds 7.03 MB = 1.31 x
  the text (pieces, the add buffer's chunk, the undo history). Decision taken from the measures: typing that continues
  the piece before the caret and a deletion at a piece's edge rebuild one path of the tree (no split and join); it took
  the warm edit from 14.5 µs to 2.24 µs mean and 2,809 to 1,550 B an edit, the random-edit tests unchanged.

- **Measured (E2, 2026-10-31, `benches/Craft.Benches -- csharp`, the same container and file):** the first full parse
  of the 100,005-line file takes 948 ms cold, and its syntax tree holds 27.6 MB = 5.16 x the text (Roslyn's green tree
  carries its tokens' own text; the buffer's text is still read once, through `SnapshotSourceText`). A keystroke in a
  method body (the buffer's edit plus Roslyn's incremental parse, on the UI thread): 51.6 ms cold (JIT), warm 1.16 ms
  median, 9.4 ms p99, 125 KB allocated. The reference, Roslyn's own `SourceText.WithChanges` with the same parse:
  1.18 ms median, 130 KB, so the cost is Roslyn's incremental parse of a file of some 2,000 types, not the text. The 60
  visible lines (414 spans) classify in 47 ms cold, 344 µs warm, 1.5 KB a call (Roslyn's red nodes). Typing `/*` at line
  10 re-parses in 9.5 ms and then the top 60 lines classify in 28 µs, the commented-out middle in 0.7 µs (one span);
  deleting that `/*` again re-lexes everything after it: **305-561 ms on the UI thread**. A keystroke inside a raw
  string: 0.8 ms median; a raw string opened at line 10: 25 ms. Candidate, measured here: parse off the UI thread on a
  snapshot and classify from the last finished tree (the plan's "nothing waits on the UI thread"), because an edit that
  changes the meaning of the rest of a large file costs a full parse. **Taken (below).**

- **Parse off the UI thread (E2 fix, 2026-10-31, the same bench and file, a 4-core x64 Linux container, .NET 10.0.14).**
  `CSharpLanguageSession` keeps the last landed parse (tree + the snapshot it is of, one immutable value swapped
  atomically) and one worker on the thread pool that runs only while there is work. `Changed` (UI thread) composes the
  change into one covering `TextChangeRange` against the last parsed version (`SnapshotSourceText.Collapse`, `Compose`,
  `Since`: several versions become one incremental parse, no text chained) and wakes the worker; it parses the newest
  snapshot, again while newer changes are pending, and raises `ILanguageSession.Reclassified` (new in the contract, on
  the worker's thread; `EditorModel.Reclassified` forwards it, `EditorView` dispatches to the UI thread and calls
  `ApplyReclassification`, which drops the bracket match and invalidates; the run cache keys on spans, so only the lines
  whose spans changed are rebuilt). A frame waits for the parse up to `CraftOptions.ParseWaitMilliseconds`
  (`CRAFT_PARSE_WAIT_MS`, default 8: the typing parse's p99 is 2.2-2.3 ms, so typing paints classified in the same frame
  with 3.5 x room, and 8 ms is half a 60 Hz frame; 0 never waits) through `ILanguageSession.WaitUntilClassified` (new,
  with a default that returns true). Past the budget, `Classify` maps the range through the pending change into the old
  tree: spans before and after it kept (shifted), those touching it dropped (drawn plain until `Reclassified`). A parse
  that throws is kept in `ParseError`, the last tree stays in use and the next change parses afresh; `Dispose` stops the
  worker. The classifier now walks from the range's first token (`FindToken`, `GetNextToken`) instead of
  `DescendantTokens(range)`, which made a red node for every type above the visible lines on each fresh tree: the same
  tokens (proven against `DescendantTokens` on 12,000 random ranges), 107 KB to 32 KB allocated on the UI thread a
  keystroke. Before (the old code, measured again on this container) and after, UI thread = the edit and its `Changed`,
  the wait, and the classification of the 60 visible lines:

  | What | Before, on the UI thread | After, UI thread | After, the parse in the background |
  |---|---|---|---|
  | Open the 100,005-line file (cold) | 711 ms (the full parse) | 8.3 ms `Open` + 14.5 ms first frame (budget spent, drawn plain) | 866 ms, then repainted by `Reclassified` |
  | Keystroke in a method body, cold | 41.9 ms | 18.2 ms (over the budget, drawn plain once) | 21.4 ms |
  | Keystroke, warm | 1.11 ms median, 2.1 ms p99 (parse only; + classify 0.2-0.6 ms) | 2.1 ms median, 3.6 ms p99, 6.1 ms max; 0 of 350 over the budget | 1.29 ms median, 2.2 ms p99 |
  | Keystroke, warm, `CRAFT_PARSE_WAIT_MS=0` | | 0.52 ms median, 1.7 ms p99 (classified through the pending change) | the same |
  | `/*` typed at line 10 | 9.7 ms | 8.7 ms cold, 7.3 ms warm median (classified within the budget) | 14.4 ms cold, 7.0 ms warm |
  | `/*` deleted again (the freeze) | **258 ms** (305-561 ms in the first E2 run) | **8.7 ms cold, 8.1 ms warm median, 8.7 ms max** (lines above the change classified, the rest plain) | 349 ms cold, 321 ms warm median, then repainted |
  | Raw string opened at line 10 | 25.7 ms | 8.3 ms (over the budget) | 29.6 ms |
  | Keystroke inside a raw string | 1.8 ms median | 1.4 ms median | 0.73 ms median |

  The warm keystroke's UI cost is now the wait for the background parse plus the hand-off (the parse itself is the
  same Roslyn incremental parse); with a budget of 0 the UI thread never waits and draws the typed token plain for one
  frame. The worst case on the UI thread is the budget plus one classification, whatever the edit.

- **Measured (E5, 2026-10-31, `benches/Craft.Benches -- semantic`, a 4-core x64 Linux container, .NET 10.0.14; results in
  `test-output/benches/semantic.txt`).** The workspace of Craft's own `src/Craft.Editor` (the "csproj" reader: 4 projects,
  Idrak.Abstraction, Craft.Abstractions, Craft.Core and Craft.Editor, through `obj/project.assets.json`; 245 documents, 167
  references, 5.8 MB of reference files): built in 1,247 ms cold (the projects and references read, Roslyn's MEF host
  composed once per process), 21.6 ms warm after a release (references read again), 7.7 ms for a second workspace sharing
  the references. Craft.Editor's compilation: 2,577 ms cold, 622 ms warm. Every project's diagnostics: 7 errors, 0
  warnings, all CS8795 in Idrak.Abstraction (partial methods that `LibraryImport` and `GeneratedRegex` source generators
  implement; generators and analyzers are not run yet), so the reader's model of Craft's projects compiles clean. With the
  compilations the workspace holds 102.8 MB of managed heap (the references' metadata included); released, 56 MB stays over
  the start (the MEF host composed once per process and Roslyn's own static caches, not measured apart). A loose
  generated file: 1,000 lines, first diagnostics 308 ms, warm after a keystroke 68.5 ms median (138 ms max), 4.8 MB; 10,000
  lines (267 K chars), first 1,499 ms, warm 542 ms median (1,277 ms max), 4.4 MB over the shared references. Completion after
  '.' (126 items): first 1,156 ms (JIT and the completion providers), warm 15-44 ms median with nothing else running, 27-62 ms
  with each keystroke's diagnostics pass beside it. **Decision taken from the measures:** the first version called Roslyn's
  `GetChangeAsync` for each "complex" item when the list was made (12 of the 126): 98-328 ms a list; now an item says
  `NeedsResolve` and only the committed one is resolved (`ISemanticLanguageSession.ResolveCompletionAsync`): 15-44 ms, the
  same items (the `Semantic` and `Editor` tests). `CRAFT_SEMANTIC_MAX_CHARS` defaults to 1,000,000 characters (some
  37,000 lines; the warm pass scales with the file: about 2 s a pass there on this container). Candidates, measured here,
  not taken yet: the warm pass re-binds the whole tree after each change (542 ms at 10,000 lines), so the diagnostics of the
  changed member first (`SemanticModel.GetDiagnostics(span)`), then the rest; and one parse for both layers (the workspace
  parses the open document again beside the syntactic worker: a second tree of the same text).

- **Measured (E5, Windows, the owner's run, 2026-10-31, `benches\Craft.Benches -- semantic`, 24 cores, .NET 10.0.12, Windows
  10.0.26200; before the source generators).** The workspace of `src/Craft.Editor` (4 projects, 264 documents, 167 references,
  5.8 MB): build cold 588.9 ms; Craft.Editor's compilation cold 789.1 ms (21 trees); every project's diagnostics 1,833.7 ms,
  **11 errors, all CS8795** (the first Linux run 7: the 4 more, `PowerSource.GetSystemPowerStatus`, `KeepAlive.Unit` and
  `TuningMetricImplementations`' `FinalMarker`, `NumberPattern`, `WordPattern`, are partials of a newer Idrak.Abstraction,
  compiled on every OS, not Windows-only code), 0 warnings; 111.8 MB held with the
  compilations; build warm after a release 29.1 ms, another workspace sharing the references 13.7 ms; compilation warm
  287.0 ms; 63.2 MB still counted after both were released (the bench's own stack, see below). A loose 1,000-line file
  (26,564 chars): first diagnostics 125.7 ms, 4.9 MB; warm after a keystroke 8.3 ms median, 32.7 ms max (the pass 8.2 ms);
  completion after '.' first 299.7 ms, warm 7.4 ms median, 22.3 ms max beside the diagnostics, 6.3 ms median, 11.7 ms max
  alone (126 items); the workspace 18.8 ms. A 10,000-line file (266,739 chars): first diagnostics 94.3 ms, 3.7 MB; warm
  75.9 ms median, 85.6 ms max (the pass 75.8 ms); completion first 36.1 ms, warm 27.5 ms median, 43.8 ms max beside the
  diagnostics, 12.6 ms median, 24.8 ms max alone. The Linux container is 4 to 9 times slower on every compile-bound row.

- **Measured (E5 completed, 2026-10-31, the same bench, the 4-core Linux container while another agent built beside it:
  load average 10 to 20, so the times below are upper bounds and the owner's Windows run is the reference; results in
  `test-output/benches/semantic.txt`).**
  - **Source generators.** The reader now gives each project its analyzers (the targeting packs' `data/FrameworkList.xml`
    Analyzer entries in C#, the packages' `analyzers/dotnet[/roslynX.Y][/cs]` files from `project.assets.json`, `Analyzer`
    items), and the workspace adds them as analyzer references loaded through one load context of the plug-in's (Roslyn and
    its dependencies bound to the plug-in's copies; once per process). Every project of `src/Craft.Editor`'s workspace:
    **0 errors with the generators, 11 without** (the same 11 CS8795 on this container with the same Idrak clone: 265 documents, 20 more
    than the first run's), 8 generated documents; every compilation with its diagnostics 5.4-5.8 s with them against
    11.7 s without on this loaded machine (the generators' run is lost in the noise: their cost is under the variation
    between runs); held 91.8-92.2 MB against 88.3 MB (some 4 MB for the generators' state and documents). The generator
    assemblies load once per process (their types are never unloaded, as in the compiler server). `CRAFT_SOURCE_GENERATORS`
    (true) turns them off.
  - **Semantic colours.** 60 visible lines of a loose file (and a screen either side, so a small scroll stays coloured),
    Roslyn's `Classifier.GetClassifiedSpansAsync` on the thread pool: 1,000-line file first pass 273 ms (JIT and the
    classifiers), warm after a keystroke 47 ms median, 178 ms max; 10,000-line file 64 ms first, 43 ms median, 297 ms max.
    The UI thread's `Classify` with the colours merged: 158 µs and 143 µs median against 161 µs and 128 µs for the syntactic
    layer alone (304 and 313 spans): the merge costs nothing measurable on the frame. `CRAFT_SEMANTIC_COLORS_DELAY_MS`
    defaults to 30 (a keystroke burst is one pass; the colours land within a frame or two of a pause); -1 turns them off.
  - **Memory after a release (what held the "56 MB").** It was the bench, not Roslyn: with `ConfigureAwait(false)` the
    measuring code ran as an inline continuation under the frames that completed it, and a `Solution` in one of their stack
    slots stayed live to the collector (a `dotnet-gcdump` taken at an `await` showed 23.7 MB live while `GC.GetTotalMemory`
    counted 89.8 MB over the start). Measured after the stack unwound (`Task.Run`), over a start with nothing held:
    built and every project compiled 90.2 MB (89.9 MB live after a compacting collection); the workspace and the shared
    references released **0.7 MB**; the MEF host released too -0.3 MB (it holds some 2 MB); built again with the host
    composed again 342 ms warm (545 ms in another run), 1.2 MB. The first workspace of a process leaves 22.6 MB that no
    release frees: Roslyn's process-wide statics (the lexer's text-keyed token and trivia caches, 2 MB; the green-node cache;
    the pooled builders and dictionaries of the binder and flow passes; a `dotnet-gcdump` shows nothing larger than 1 MB
    there), with no public API to clear them. **Decision taken from the measures:** the memory-pressure release drops the
    workspaces and the references and keeps the MEF host (2 MB for a 0.3-0.6 s recomposition on the next request).

### Tests and benches

- `Craft.Tests` has its own small runner with the same flags as Idrak's (`--progress`, `--failures-only`,
  `--failures-file`), `CRAFT_FILTER` split at `|`, failures saved under `test-output\`. Idrak's `TestRunner` is not a
  package, so it is copied in shape, not referenced (unless the owner makes `Idrak.Tests.Library` packable).
- `Craft.Maui`'s `net10.0` target tests the control headless: layout, caret, selection, scrolling and hit-testing
  against the fixed-metric `IEditorCanvas` fake, so most of E3 is proven before the owner runs the window.
- Random-edit equivalence everywhere: the buffer against a plain string, the snapshot-`SourceText` against
  `SourceText.From`, the incremental spans against a fresh parse.
- The owner's runs (the window, Android, GPU) are listed as exact commands per phase.

## Plug-ins that test Idrak's plug-in points (phase P)

Each plug-in is a real feature of the editor and, at the same time, a test of one of Idrak's registries. Together they
check five things:

1. **A new kind:** a name added to a registry works end to end.
2. **An override:** an app registration shadows a library default, the slot's guard reports it, `Unregister` brings
   the default back.
3. **A missing name:** an unregistered name fails with "not registered"; nothing falls back silently.
4. **Loaded from outside:** a plug-in assembly in the plug-in folder loads through `IdrakPlugins.Load` (what the
   CLI's `-P` uses), with no rebuild of the editor.
5. **One assembly, every host:** the same assembly works in the editor (its live tools served to MCP clients by the
   app through S0's `McpHost`) and in the `idrak` CLI (`tools test`, `mcp serve`, `chat --tools`), through the folder
   host where the process is not the editor.

| # | Plug-in | Idrak contract | Checks | Conformance suite |
|---|---|---|---|---|
| P1 | **Editor tools** (live and folder hosts; the live ones served by the app over S0's `http` transport) | `IToolProvider`, `[assembly: ToolProvider]`, `McpHost` | 5 | `idrak tools test`; `idrak chat --mcp` against the running app |
| P2 | **A coder model family** not yet in `Idrak.Text.Plugins`, with its fill-in-the-middle format | `IIdrakPlugin`, `FillInMiddleFormats` (A0) | 4, 3 | `FillInMiddleFormatSuite` (A0) |
| P3 | **Bracket-balanced completion sampler**: masks tokens that close a brace or a string the prefix never opened | `TokenSamplers` (over the built-in default) | 2 on the hottest path; cost in tokens per second | `TokenSamplerSuite` |
| P4 | **C#-aware edit placement**: places a model's snippet by Roslyn tokens, ignoring whitespace | `TextMatchers` | 1, 2 against `SemiGlobal` | `TextMatcherSuite` |
| P5 | **Approve-each-command sandbox**: the agent's commands run in the editor's terminal panel, each approved | `CommandSandboxes` | 1, 3 | none in Idrak; Craft's own |
| P6 | **Code search**: Roslyn-shaped chunks (type, member), camelCase-split identifiers, BM25 plus embeddings in `VectorIndex` | `KeywordIndexes`, `PretrainedEncoders`, `SequencePoolers` | 1 in retrieval; feeds the chat's context | `KeywordIndexSuite` |
| P7 | **Voice dictation and commands**: built as phase V's first plug-in ("Voice coding" below) | `SpeechFamilies`, `AudioCaptures`, `LiveTranscriber` | another domain through the same host | `SpeechRecognizerSuite` |
| P8 | **Photo or screenshot to code** | `VisionFamilies`, `IChatModel.PartKinds` "image" | a text-only model refuses, never ignores the picture | `ChatPartKindSuite`, `VisionEncoderSuite` |
| P9 | **Status-bar progress and telemetry**: pull and load progress, tokens per second, device memory | `ProgressReporters`, telemetry | 1 on a registry without a guard | `ProgressReporterSuite` |
| P10 | **Workspace model names**: `craft:` names from the project's `.craft/model.json` | `ModelSources` | 1 on name resolution | Craft's own |
| P11 | **Idrak's text shaper on the editor surface** (optional, only if the platform layout measures slow) | `TextShapers`, `FontFormats` | 2 against the platform layout | Craft's own, pixels compared |

P1, P2, P3 and P5 come first: together they cover all five checks. Each plug-in ships its targeted tests and runs the
conformance suite named.

On Android, whether an assembly loaded at run time works under the app's ahead-of-time setting (`IdrakAndroidAot`), and
whether trimming removes what a plug-in needs, is measured in M. If it does not work, Android plug-ins are compiled into
the app and registered through `RegisterAll()`, and loading from a folder stays a desktop feature.

## Voice coding on Idrak.Audio (phase V, added 2026-10-31 at the owner's request)

The owner asked that Craft support coding by voice, through Idrak.Audio. It is built as plug-ins on K's contracts and
A1's host, like every AI feature; Craft's libraries reference no audio package, and a family (Whisper, a TTS voice) is
whatever Idrak.Audio's plug-ins register, never named in Craft.

| Piece | What it does | Idrak.Audio contract |
|---|---|---|
| Capture | The microphone, opened on a key (push to talk) or toggled; the level shown in the status bar; the device a setting. | `AudioCaptures` (`ForThisMachine`, `Devices`: "winmm" on Windows, "aaudio" ready for phase M), `IAudioInput` |
| Live transcription | Words as they are spoken, the open words greyed, the committed ones final; a pause ends a phrase. | `LiveTranscriber`, `LiveTranscriptionOptions`, `LiveCommitPolicies` ("agreement"), `SpeechFamilies`, `ISpeechRecognizer` |
| Dictation | Committed text inserted at the caret, one undo step per phrase; spoken punctuation and symbols ("open paren", "semicolon", "arrow") mapped by a table per language the language plug-in may extend. | the transcript; Craft's document edit |
| Voice commands | A phrase that matches a registered command's spoken name ("go to line 40", "save", "find", "next error") runs it; the command palette's names are the vocabulary, so every plug-in's commands are speakable. | K's `EditorCommands` |
| Spoken code | "make a method that returns the sum of the list": the phrase, the selection and the file's context go to the chat model (A1), the answer arrives as a diff the user accepts (no silent edit); a model that hears takes the sound itself. | `IChatModel`, `ChatAudio` / `AudioPartKind` ("audio" part kind), else the transcript as text |
| Read back | The selection, a diagnostic, a hover or a chat reply spoken, stoppable by any key. | `SpeechSynthesisFamilies`, `ISpeechSynthesizer`, `AudioPlaybacks`, `VoicedSynthesizer` |

Settings (each `CRAFT_VOICE_*` and in Settings): the recognizer and the synthesizer model names (through `ModelSources`),
the capture device, push-to-talk or toggle and its key, the language, the live step and window seconds, the commit
policy, and whether spoken code goes as sound or as text. Speed: the first words on screen within the recognizer's step
of speaking, measured and written here; nothing on the UI thread but the insert. Privacy: the sound never leaves the
machine unless the model the user chose is a served one, and the status bar says when the microphone is open.

Checks: a recorded WAV played through `PcmStreamInput` dictates the expected text into a document as one undo step per
phrase; a spoken command runs the matching command; a spoken-code phrase arrives as a diff from a fake chat model; read
back stops on a key; the owner speaks into the microphone on Windows. Idrak gaps met on the way (a phrase-grammar or
keyword biasing contract for the recognizer, if the measured accuracy on code words needs one) are fixed in Idrak first.

## The approved design (phase D, added 2026-10-31)

The owner approved the design screens shown on 2026-10-31 ("Craft design screens"); their sources are in
`docs/design/` (one `.dc.html` per screen). They are the look of the whole app, and the spec of every screen still to
come:

| Screen | `docs/design/` | Built in |
|---|---|---|
| Editor: title bar with menus (File, Edit, View, Run, Plug-ins, Help), the search box (Ctrl+P), the model chip; the activity rail (Explorer, Search, Source control, Agent, Plug-ins, Settings); the explorer with the Roslyn card (workspace state, projects, memory); tabs with the accent line; the breadcrumb; the bottom panel (Problems, Terminal, Output, Idrak telemetry); the chat column; the status bar (branch, problems, Roslyn state, position, indentation, encoding, line ending, language, the model's device, memory and tokens per second) | `Main.dc.html` | D (the shell), A2 (chat, inline completion's hint bar) |
| Diff review: per-hunk Accept and Reject, how the edit was placed, Roslyn's result before accepting, one undo step | `Diff.dc.html` | A1 (the diff-and-accept view) |
| Agent run: turns, tokens, the approve-each-command card, changed files, the sandbox's terminal | `Agent.dc.html` | A2 (agent), P5 (the sandbox) |
| Plug-ins and Idrak registries | `Plugins.dc.html` | D (the page over K's list), P (the registries' checks) |
| Models and devices | `Models.dc.html` | A1 ("local" provider), D (the page) |
| Android editor and chat sheet | `PhoneEditor.dc.html`, `PhoneChat.dc.html` | E4's narrow layout now; M on a phone |

The look, as theme tokens (the dark theme the design's; a light theme derived with the same contrast checks): ground
`#101217`, surface `#16181D`, raised `#1A1D23`/`#1F232B`, selection `#252A33`, border `#2A2E37`/`#343944`, text `#D5D9E0`,
secondary `#9AA2B1`, accent `#E5A54B` (the design's alternatives `#5FC9B4`, `#82AAFF`, `#E58BA0`: `CRAFT_ACCENT`), success
`#5FC9B4`, warning `#E3B27A`, error `#F07178`; code colours as in `Main.dc.html` (keyword `#82AAFF`, type `#5FC9B4`,
string `#E3B27A`, comment `#8790A0` italic, method `#E8D58A`, variable `#B8C7DC`); Inter for the interface and JetBrains
Mono for code (both SIL Open Font License, bundled as MAUI fonts; `CRAFT_FONT` still overrides the code font); 13 px
interface text, 22 px code lines; 44 px touch targets in the narrow layout.

The logo and app icon (the owner's choice, 2026-10-31): `docs/design/logo.svg`, the rounder dark square with the thick
amber C, a white caret and a white spark; it is `src/Craft.App/Resources/AppIcon/appicon.svg` and the title bar's mark.

Phase D builds the shell's look and layout from `Main.dc.html` on E4's shell and K's contracts (the menus and the rail
are commands, so plug-ins add to them), the Plug-ins and Models pages, and the theme tokens; each later screen follows
its design file. Checks: the Windows window beside the design at 1440x900 and at the narrow width, the owner the judge;
every colour pair meets 4.5:1 (3:1 for large text and lines); the keystroke and paint numbers measured again, unchanged.

## The terminal (phase T, added 2026-10-31 at the owner's request)

An integrated terminal in the bottom panel's Terminal tab (`docs/design/Main.dc.html`), the user's own shell, several
at once (one tab each, named by the shell and its folder; new, split later, close, kill).

- **Where it starts (the owner's rule):** in the open folder's root when a folder is open; when only files are open and
  no folder, in the terminal's default path (the shell's own start folder: the user's home); `CRAFT_TERMINAL_CWD`
  overrides both. A terminal opened from the explorer's context menu ("Open in terminal") starts in that folder.
- **Which shell:** `CRAFT_TERMINAL_SHELL` (default on Windows: `pwsh` when on the PATH, else Windows PowerShell, else
  `cmd`), its arguments `CRAFT_TERMINAL_ARGS`, the environment the app's plus `CRAFT_TERMINAL_ENV`; the font and colours
  the editor's theme (the design's tokens), the scrollback `CRAFT_TERMINAL_SCROLLBACK` lines.
- **How:** a contract in `Craft.Abstractions` (`ITerminalHost`, `ITerminalSession`: start with a folder, a size and an
  environment; write input; output as a byte stream; resize; exit code), registered in a `TerminalHosts` table; the
  Windows implementation over ConPTY under `Platforms/Windows`; the screen is Craft's own drawn grid over
  `IEditorCanvas` (a VT parser for the sequences shells use: colours, cursor moves, erase, alternate screen, bracketed
  paste, OSC titles), so nothing Windows-only leaks out and phase M can add Android's (or report that a phone has no
  shell). Output is parsed off the UI thread; only the visible rows are drawn; memory stays flat with the scrollback cap.
- **Commands and keys (on K's contracts):** New terminal (Ctrl+Shift+`), Toggle panel (Ctrl+`), Next/previous
  terminal, Kill, Clear, Copy and paste (Ctrl+Shift+C / Ctrl+Shift+V), Run selection in terminal; plug-ins can open a
  terminal and write to it through the host (`IEditorHost.Terminals`), never read the user's keystrokes.
- **The agent's terminal stays apart:** P5's sandbox terminal (`docs/design/Agent.dc.html`) runs only approved commands;
  the user's terminal is never driven by a model.

Checks: headless VT parser tests (colours, cursor, erase, wide characters, alternate screen) against fixed byte
streams; a session starts in the right folder in the three cases (folder open, file only, the variable); the owner
runs `dotnet build` in the terminal on Windows, resizes it, and opens two; output of a million lines keeps memory flat
and the keystroke budget unchanged.

## Learning in the editor: reinforcement learning on Idrak (phase R, added 2026-10-31 at the owner's request)

The owner asked that the editor learn, through Idrak's reinforcement learning, and that it test that library through
the C# plug-in Idrak already has. Everything it needs is in Idrak today: the contracts (`EnvironmentKinds`,
`RewardFunctions`, `AdvantageEstimators`, `PolicyLosses`, `IPolicy`, `Trajectory`, `RolloutBatch`), the trainer
(`Idrak.Reinforcement`: `ReinforcementTrainer`, `RolloutCollector`, `OffPolicyTrainer`), the text side
(`Idrak.Text.Reinforcement`: `LanguageModelPolicy`, `PromptsEnvironmentKind`, `TextRewards`, `JudgeReward`,
`OnPolicyDistiller`; `FineTuning` for the adapters), and the environments (`Idrak.Reinforcement.Plugins`: "coding" with
`CodingTask`, `CodingSuite`, `CodingVerifier`, the `container` sandbox; "csharp" from `CSharpCodingPlugin`, with the
`csharp-tests` and `csharp-build` rewards, over Idrak.Text.Plugins' "csharp-language", whose checks are `dotnet build`
and `dotnet test`). Craft builds only plug-ins on K and A1; nothing of it is in Craft's libraries, and Craft never
reimplements a trainer, a reward or an environment (a gap is fixed in Idrak first).

| Piece | What it does | Idrak contract |
|---|---|---|
| Episodes from the editor | Only after the learning consent (below), local unless the sharing consent is also given: each inline completion, chat diff, code action and agent task becomes an episode (the context, the model's answer, what the user did: accepted, edited after accepting and how far, rejected, undone). Stored under the app's data folder per workspace, with a size cap; the user sees, exports and deletes them on the Learning panel. | `Trajectory`, `EpisodeInfo` keys, `PromptsEnvironmentKind`'s prompt data |
| Rewards | The user's verdict (accepted and kept, edited, rejected, undone) as an editor reward registered by the plug-in; for code, whether it builds and its tests pass, through `csharp-build` and `csharp-tests` run in a registered sandbox (approved, or `container`); a judge model optional. Weights are settings. | `RewardFunctions` (Craft's "editor-verdict" registered by the plug-in; Idrak's `csharp-*`, `tests-passed`, `JudgeReward`) |
| Tasks from the workspace | The workspace's own tests make tasks: a test that fails (or one the user marks) becomes a `CodingTask` whose hidden check is `dotnet test`; the suite is written in `task.json` form so `idrak reinforce` reads it too. | `CodingTask`, `CodingSuite`, `CodingWorkspace`, the "csharp" environment |
| Training | In the background when the machine is idle (or on demand), bounded by `ComputeResources` and the settings: the policy is the user's local model, trained as an adapter (never the base weights), with the loss and the advantage estimator chosen by name; live progress (episodes, reward, loss, time left) in the status bar and the panel; stoppable at any step. | `ReinforcementTrainer`, `LanguageModelPolicy`, `PolicyLosses`, `AdvantageEstimators`, `FineTuning`, `OnPolicyDistiller`; for the editor's choices, preference training (DPO, ORPO, SimPO) on pairs from R0's `PreferencePairSources` |
| Promotion | A trained adapter replaces the current one only if it scores better on a held-out set of the workspace's tasks and episodes, measured and shown; one click rolls back; every version kept until deleted. | `CodingVerifier`, the coding suite's evaluation |
| Testing the library | The same plug-in is Idrak's reinforcement stack's test from a real host: the "csharp" environment on a small suite built from Craft's own repository and from fixtures, run headless; the five checks of phase P hold (registered by name, overridden, "not registered" for a missing one, loaded from the plug-in folder, the same assembly under `idrak reinforce`). | `EnvironmentKinds`, `RewardFunctions`, `IdrakPlugins` |

**Consent (the owner's requirement, 2026-10-31: learning enabled with clear sharing consent).** Learning is offered,
never assumed: the first time an AI plug-in answers, a consent screen asks, in plain words and in one place, for two
separate choices, each with its own switch and nothing pre-ticked:

1. **Learn on this machine:** what is recorded (the context sent to the model, its answer, and what the user did with
   it), where it is stored (the folder shown, openable), how much (the cap), how long it is kept, what training runs
   and when (idle only, the compute limits), and that a learned model still only proposes. Yes turns learning on;
   No keeps the editor exactly as it was. Per workspace, with "all workspaces" as the default scope the user can change.
2. **Share what was learned:** a separate consent, asked only after the first one and never bundled with it, naming
   exactly what would leave the machine (episodes, an adapter, or only scores), to where (the destination is a setting
   with no default: nothing is shared until the user names one), and that it can be withdrawn. Before each share the
   user sees the items about to go (count, size, a preview of each, files and paths it touches) and confirms; secrets
   and files matched by `.gitignore`, `.craft/learn-ignore` and a secrets scanner are left out, and the list says so.

Each consent is stored with its time, its scope and the version of the consent text; a changed text (what is recorded,
what is shared, where) asks again. Withdrawing is one switch in Settings and on the Learning panel: recording stops at
once, nothing more is shared, and the user chooses whether the stored episodes and adapters are deleted (a delete
removes them and says how much was freed). The status bar shows a recording mark while episodes are being recorded and a
sharing mark while a share runs. Without both consents nothing is shared; without the first, nothing is recorded. Tests:
no episode is written and no network request is made before consent; a changed consent text asks again; withdrawal stops
recording on the next answer and deletes on request; a share sends exactly the previewed items and nothing ignored.

Settings (each `CRAFT_LEARN_*` and in Settings): the two consents (recorded, never set by a variable alone: a variable can only turn them off), what is recorded (completions, chat,
agent), the episode store's folder and cap, the reward weights, the sandbox, idle-only or on demand, the compute limits,
the loss and estimator names, the held-out share and the promotion margin. Privacy: episodes and adapters leave the machine only under the sharing consent, to the destination the
user named, after the preview; the status bar shows when a run is training. No silent edit still holds: a learned model proposes, the
user accepts.

Checks: a fake policy over a fixture C# workspace with a failing test runs episodes through the "csharp" environment
and gets `csharp-tests` rewards; recorded editor episodes replay into a `RolloutBatch`; a tiny random-weight model's
adapter trains a few steps with the loss falling and memory flat; promotion refuses a worse adapter and rolls back; the
same plug-in assembly runs under `idrak reinforce`; the owner trains on a real model on Windows and the numbers
(episodes per minute, reward before and after, the held-out score) are written here.

## Phases

Phases are a dependency order, not a barrier. At most three agents work at a time, in every phase of this plan, including S0 and A0 in Idrak (the owner's exception for Craft's plan; Idrak's own work keeps its limit of two). "Done" is what the owner can check.

| Phase | What | Done when | Needs |
|---|---|---|---|
| S0 | Idrak.Mcp served from a running app: `IMcpServerTransport` and `McpServerTransports` (`stdio`, `http`), `McpHost`, the CLI's `mcp serve` and `serve --mcp` on it with `--transport` and the variables, the `Mcp` test group, the API dump. | `IDRAK_FILTER=Mcp` passes on the CPU; the owner's two-window check (`mcp serve --transport http`, `chat --mcp http://127.0.0.1:PORT`) lists and calls a tool; the `http` overhead against `stdio` is written here. | the owner's go (first) |
| E0 | Craft skeleton: solution, `Directory.Build.props/targets` with Idrak's settings and the `IdrakFromSource` switch, `MauiVersion`, `Craft.Abstractions` with the registries, the inventory test, the API guard per package, the test runner, `Craft.Benches` empty. Windows and `net10.0` targets only. | `dotnet build` passes; the inventory and API-guard tests pass. | the owner's go |
| E1 | `Craft.Core`: piece table, snapshots, edits, undo and redo (grouped by typing pause), file open and save (encodings and line endings kept), autosave and recovery, workspace folder. | Random-edit equivalence against a plain string; a 100,000-line file opens and edits in the bench with memory ≈ text + pieces. | E0 |
| E2 | `Craft.CSharp`, syntactic layer: the snapshot as `SourceText`, the incremental parse, classified spans for a line range, Roslyn's classification names mapped to theme classes, light and dark themes; C# shipped as a pre-installed, removable plug-in loaded from `plugins/` (`Craft.Core.Plugins.CraftPlugins`, `CRAFT_PLUGIN_FOLDERS`, `CRAFT_PLUGINS_DISABLED`). | Spans of fixed C# 14 files (every string kind, comments, preprocessor lines, contextual keywords) match; spans after random edits equal a fresh parse; the snapshot-`SourceText` equals `SourceText.From` on random edits. | E1 |
| E3 | `Craft.Maui` editor control over `IEditorCanvas`: drawn surface, caret, selection, scrolling, gutter, bracket match, find and replace; Windows `ITextInputAdapter` (keyboard, mouse, IME, clipboard). **A minimal window in `Craft.App` opens one file**, so the owner runs the control before the shell exists. | Headless tests of layout, caret, selection and hit-testing pass; the owner opens, edits and saves a file on Windows. | E2 |
| E4 | **Built (2026-10-31, "E4 as built" below).** `Craft.App` shell on Windows: folder tree, tabs, settings page (every setting above), recent files, light and dark, the narrow layout chosen by width. | The owner opens a folder, switches files, changes settings, resizes to the phone layout. | E3 |
| E5 | `Craft.CSharp`, semantic layer: `WorkspaceReaders` ("csproj", "loose"), semantic colours, diagnostics as squiggles and a list, completion, signature help, hover, go to definition, format document. **Built (2026-10-31, "E5 as built" below), semantic colours and source generators included; Craft's own solution compiles with 0 errors in the reader's model (the bench and the `Semantic` tests). Left: the window on Windows (the owner's run).** | Diagnostics and completion on Craft's own solution; memory and cold start measured on Windows and written here. | E3 (parallel with E4) |
| D | **Built (2026-10-31, "D as built" below); left: the window beside the design on Windows (the owner's run).** The approved design ("The approved design" above): the shell's look and layout from `docs/design/Main.dc.html` (title bar and menus, search, model chip, activity rail, explorer with the Roslyn card, tabs, breadcrumb, bottom panel, status bar), the theme tokens and fonts, the Plug-ins and Models pages; later screens follow their design files. | The window beside the design at 1440x900 and narrow, the owner the judge; contrast checked; the keystroke and paint numbers unchanged. | E4, K (runs beside A1; A1 and A2 build their screens from the same files) |
| T | The terminal ("The terminal" above): the Terminal tab, the user's shell over ConPTY behind `ITerminalHost`, Craft's own VT grid, several terminals; it starts in the open folder, else (a file only) in the shell's default path, `CRAFT_TERMINAL_CWD` overriding. | The VT tests pass; the start folder is right in the three cases; the owner builds in it on Windows. | D (the bottom panel), K |
| A0 | Idrak: the FIM contract, registry and suite; the coder families already in `Idrak.Text.Plugins` register their formats; the reuse and cancellation bench, with fixes. | The bench shows prefix reuse, a stop within one token of cancellation and flat memory over a thousand requests. | the owner's go (parallel with E1-E2) |
| K | **Built (2026-10-31, "K as built" below).** The plug-in contracts and kit ("Plug-ins first" above): `IEditorPlugin`, `IEditorHost`, commands and key bindings, status bar items, panels, plug-in settings, `plugin.json` and contract versioning, the sample plug-in, the template, `docs/plugins.md`. | The sample plug-in, built outside Craft's solution against `Craft.Abstractions` alone, dropped into `plugins/`, adds its command and status item; removed, the app runs without it. | E3, and the owner's first try of the Windows version; E4's shell is built on it |
| A1 | **Built (2026-10-31, "A1 as built" below).** `Craft.Ai` host on the plug-in contracts (K): the diff-and-accept view, the editor's tools with both hosts, the app serving the live tools through `McpHost` ("http", a setting with address and token shown in Settings), `IModelProvider` "local". | A fake chat model proposes an edit that arrives as a diff and is accepted into one undo step; `idrak tools test` lists the tools from the built assembly; `idrak chat --mcp` against the running app reads an open document. | E4, K, A0, S0 |
| A2 | **Built (2026-10-31, "A2 as built" below; Idrak 6eb11a2 for the agent's steering note). Left: the owner's run on a real model.** The plug-ins: inline completion, chat panel, code actions, agent (desktop). | On a real model the owner names: ghost text appears after the pause and dies on a keystroke; a chat reply applies as a diff; the agent runs a task with each command approved. | A1 |
| P | P1, P2, P3 and P5 first, then P4, P6, P9, P10, then P8 (P7 is built in phase V); P11 only if measured necessary. **First batch built (2026-10-31, "P as built (first batch)" below): P1, P2, P3, P5 and P4. Left: P6, P9, P10, P8, P11.** | Each plug-in's targeted tests and its conformance suite pass; the five checks hold on the Plug-ins page. | A1 (P2 also A0) |
| V | Voice coding on Idrak.Audio ("Voice coding" above): capture, live transcription, dictation, voice commands, spoken code as a diff, read back; P7 is its first part. | A recorded WAV dictates the expected text, runs a command and asks for code that arrives as a diff; the owner codes by voice on Windows; the latency written here. | A2 (the chat model and the diff view), K |
| R0 | **Built (2026-10-31, Idrak f419399).** Idrak: preference learning from episodes ("Idrak library gaps", R0): `PreferencePairSources` ("group", "verdict"), the preference losses by name ("dpo", "orpo", "simpo") over the existing `FineTuningLosses`, iterative preference training usable by `idrak reinforce`, the suite and the bench. | Pairs from a scored rollout train a tiny model with the DPO loss falling and the margin rising; memory flat over many rounds; `idrak reinforce --pairs group --loss dpo` runs. | A0 (a slot in Idrak); before R |
| R | Learning in the editor on Idrak's reinforcement learning ("Learning in the editor" above): episodes from the editor, the editor-verdict reward with `csharp-build`/`csharp-tests`, tasks from the workspace's tests, background adapter training, promotion and rollback, the Learning panel; and the plug-in as the test of Idrak's reinforcement stack through its C# environment. | A fixture workspace's failing test becomes a task the "csharp" environment rewards; recorded episodes replay; an adapter trains with flat memory and is promoted only when better; the same assembly runs under `idrak reinforce`; the owner trains on a real model on Windows. | A2 (the episodes come from its plug-ins), P5 (the sandbox), R0 |
| M | Android, when the owner says "compile Android": the `net10.0-android` target added to every project in one step; `IEditorCanvas` and `ITextInputAdapter` for the soft keyboard, IME and touch selection; the file picker; Roslyn on Android (APK size, cold start, memory, full and profiled AOT); plug-in loading under AOT, or compiled in. | Every project builds for Android; the owner edits a file on a phone; the numbers are written here. | the owner's word, after a good Windows version |
| L | Held, far later: more languages, each its own pre-installed, removable plug-in like C# (an `ILanguage` in `EditorLanguages`, loaded from `plugins/`): TypeScript, JavaScript, HTML, CSS. The parser for each is chosen when the phase starts, measured against the keystroke budget; nothing native (no tree-sitter binaries), so a C# incremental parser or lexer per language. | Each opens, colours and matches brackets like C#; removing its folder turns its files to plain text. | the owner's word, after a good Windows version |
| A3 | Held: the "remote" model provider, the `kestrel` MCP transport in Idrak.AspNetCore, screen-reader access to the drawn editor, iOS and macOS. | the owner's call | the owner |

## E3 as built (2026-10-31)

- **The control's logic is in `Craft.Editor`, UI-free**: `EditorModel` (one selection, kept as a `SelectionState` value the
  commands take and return, so several carets later map the same commands), `EditorFind`, bracket matching, and
  `EditorRenderer`. `Craft.Maui` holds only what needs MAUI: `EditorView`, `EditorDrawable`, `MauiEditorCanvas`,
  `MauiTextMeasurer`, `MauiEditorClipboard`, and `Platforms/Windows/WindowsTextInputAdapter` (namespace `Craft.Maui.WinUI`,
  so `Windows.*` names stay unambiguous). The clipboard is `Craft.Editor.Abstractions.IEditorClipboard` (only the editor
  uses it). The headless tests drive `Craft.Editor` (group `Editor`), not `Craft.Maui`'s `net10.0` target.
- **Scrolling**: a `ScrollView` (both ways) over a spacer of the content's size, with the `GraphicsView` kept over the
  viewport (translated by the scroll offsets), so the native scroll bars and the wheel work with no platform code.
  `ContentWidth` grows with the longest line drawn (never a whole-file scan); the gutter stays fixed on the left.
- **Text**: one run per change of style in a line, tabs expanded, drawn left-aligned and centred in the line's height.
  The font is measured inside the first frame (64 "M"s on the platform canvas) and the model re-measures then.
- **Run cache**: per line, kept while the line's characters and its clipped spans are the same (compared as spans), not
  while the snapshot's version is: a keystroke rebuilds its own line only. Measured: median keystroke-to-render 17.9 us
  with 43 run strings and 11.4 KB per keystroke when cleared on every version, 6.6 us with 1 run string and 1 KB after.
- **Keyboard**: `PreviewKeyDown` (so Tab never moves the focus when the editor handles it) and `CharacterReceived`
  (control characters dropped, Ctrl without Alt dropped so AltGr types, surrogate pairs joined). IME composition beyond
  committed characters is not handled yet.
- **Settings added** (each a `CRAFT_*` variable): caret blink and width, double-click time and distance, bracket scan
  lines, scroll margins (lines and columns), bottom margin lines, gutter padding, auto-close brackets (off by default),
  find's count limit. Enter's line break follows the document, else E1's `CRAFT_LINE_ENDING`.
- **`Craft.App`**: code only (no XAML pages). C# is not referenced: it is a pre-installed plug-in copied after Build and
  Publish into `plugins\Craft.CSharp\` beside the program (Craft.CSharp's own output folder, without the host's
  Craft.Abstractions, Craft.Core and Idrak.Abstraction) and loaded by `CraftPlugins.LoadInstalled`; deleting that folder
  or `CRAFT_PLUGINS_DISABLED=csharp` turns .cs files to plain text, said so in the status bar. Save is for a file that
  has a path (Save As waits for E4's shell).

E3's bench (`dotnet run -c Release --project benches/Craft.Benches -- editor`, a 4-core Linux container, the generated
100,000-line file as plain text, a counting canvas with fixed metrics, viewport 1600x900, 49 lines drawn):

| What | Cold | Warm median | Warm p99 | Allocated |
|---|---|---|---|---|
| Keystroke to render (type or Backspace, then the frame) | 29 ms | 6.6 us | 23 us | 1.0 KB, 1 run string per keystroke |
| Caret-blink frame | | 5.0 us | 13.5 us | 0 bytes |
| Scroll frame, one line each | | 5.3 us | 33 us | 0.3 KB, 0.9 run strings per frame |

## E4 as built (2026-10-31)

- **Headless and tested** (group `Shell`): `Craft.Core.Settings` (`SettingsFile`: a JSON object of `CRAFT_*` names to
  text, read as UTF-8 bytes with comments, trailing commas and a byte-order mark allowed, a bad key or value reported and
  skipped, written sorted through a temporary file; `CraftSettings`: the layers environment > the workspace's
  `.craft/settings.json` > the user's `settings.json` in the data folder > the default, `BuildOptions` taking every value
  that parses and naming each one that does not with its source, `Set` validating before it writes, the page's
  `Rows`). Decision: a workspace (a folder someone else may have written) sets neither `CRAFT_PLUGIN_FOLDERS` (code to
  load) nor `CRAFT_PLUGINS_DISABLED`, and `CRAFT_DATA_FOLDER` (where the user's file lives) is from the environment only;
  each is said so on the page. `Craft.Core.Workspaces`: `RecentList` (`recent.json` in the data folder, latest first,
  `CRAFT_RECENT_LIMIT`, 0 keeps none), `Workspace.ListFolder` (one folder, folders first, ignored ones left out),
  `PathKeys`. `Craft.Editor.Shell`: `DocumentTabs` (the tabs own their documents, the active tab is the host's active
  document, one tab per file, titles with the folder when two files share a name, `TabChanged` only when the dirty
  marker or the file changes: 20 edits redraw a tab once), `FolderTree` (a folder is listed when first expanded, never
  below a closed one), `ShellLayout` (narrow below `CRAFT_NARROW_WIDTH`, default 720, of the measured width; each layout
  keeps its own tree choice), `ShellModel` (the actions, the settings' reload, the theme).
- **The shell's actions are commands** (`ShellCommandIds`, registered in `EditorCommands` with bindings in
  `EditorKeyBindings`, on K's public contracts, taken back on dispose): new (Ctrl+N), open file (Ctrl+O), open folder
  (Ctrl+Shift+O), recent (Ctrl+R), close folder, save (Ctrl+S; the editor's own Ctrl+S runs it), save as (Ctrl+Shift+S),
  close tab (Ctrl+W, asks Save / Don't save / Cancel), next and previous tab (Ctrl+Tab, Ctrl+Shift+Tab), settings
  (Ctrl+Comma), toggle tree (Ctrl+B), refresh tree, switch light and dark. Every button runs a command, so the palette
  lists them all with their chords and `CRAFT_KEY_BINDINGS` rebinds them. `EditorKey.Comma` was added at the enum's end
  (the contracts stay 1.0: nothing was released against K's 1.0 yet).
- **Contracts for the UI** (`Craft.Editor.Abstractions`): `IFileDialogs` (folder, file, save path) and `IShellUi`
  (unsaved question, pick from a list, settings page, message). `Craft.Maui.IShellPlatform` adds the keys pressed outside
  the editor (sent to the bindings, so Ctrl+O works from the tree) and the close guard. Windows:
  `Craft.Maui/Platforms/Windows/WindowsShellPlatform.cs` (the Windows App SDK's pickers with `InitializeWithWindow`, a
  KeyDown handler on the window's content for chords and function keys no control handled, `AppWindow.Closing` asking
  about every unsaved tab); `Craft.App/ShellPlatforms.cs` declares the factory, `Platforms/Windows` implements it.
- **Views** (`Craft.Maui`, built here on net10.0): `FolderTreeView` (a virtualized `CollectionView` of the visible rows,
  typed bindings), `TabStripView` (a strip in the wide layout, one switcher in the narrow one), `MauiShellUi` (MAUI's
  dialogs), `SettingsPage` (every option and every plug-in setting: variable, meaning, default, value in effect and its
  source, the environment's shown as overriding; written to the workspace's or the user's file, refused with the reason
  when it does not parse; a change applies at once, the six read at start say so; search). **`Craft.App`**: `EditorPage`
  is now the shell: one `EditorView` per tab, only the active one in the page (a tab keeps its parse, caret and find
  state; a switch makes no new model and no new parse), the tree in the wide layout beside the editor and in the narrow
  one in its place, the side panels and most buttons only in the wide layout. The app takes a folder or a file on the
  command line. Settings added: `CRAFT_DATA_FOLDER`, `CRAFT_RECENT_LIMIT`, `CRAFT_NARROW_WIDTH`, `CRAFT_TREE_WIDTH`,
  `CRAFT_TREE_INDENT`, `CRAFT_PANEL_WIDTH`.
- **Tests**: `Shell`, `Workspace`, `ApiGuard`, `Inventory`, `PluginKit`, `Editor`: 43 passed on the CPU here. Craft.App and
  the Windows files are not compiled here (Windows only).
- **Not yet**: the recovery copies are not offered at start yet (`RecoveryStore` exists, the shell does not use it); the
  tree does not watch the disk (Refresh); a settings value is typed as text (no per-type editors); the tab strip has no
  drag to reorder; the save picker cannot start in the document's folder (a Windows picker limit).

## K as built (2026-10-31)

- **Contracts** (`Craft.Abstractions`, namespace `Craft.Abstractions`, new files `Plugins.cs`, `Host.cs`, `Commands.cs`,
  `Contributions.cs`): `IEditorPlugin : IKind` (`Settings`, `Activate(IEditorHost)` returning the deactivation) in
  `EditorPlugins`; `IEditorHost` (documents as `IEditorDocument`, the active one and its selection, six events,
  `ProposeEditsAsync`, `Commands`/`ExecuteCommandAsync`, `AddStatusItem`, `AddPanel`, `Settings`, `StartProgress`,
  `ShowMessageAsync`, `Log`, `Record`, `Dispatch`); `IEditorCommand : IKind` (its `Name` is its id) and `EditorCommand` in
  `EditorCommands`; `KeyChord`, `KeyBinding : IKind` (named by its chord's text) in `EditorKeyBindings`; `IStatusItem` /
  `StatusItem`; `IEditorPanel` / `ListPanel` / `PanelItem` (a panel is a title and lines with optional commands, UI-free);
  `PluginSetting` (variable `CRAFT_PLUGIN_<PLUGIN>_<KEY>`), `IPluginSettings`; `CraftContracts.Version` 1.0 and
  `ContractFit`; `PluginCapabilities` (files, network, sandbox, model). `EditorKey` gained the other letters, `D0`-`D9` and
  `F1`-`F12`, added at its end so the earlier values stay. A plug-in author needs `Craft.Abstractions` alone.
- **Key precedence** (`EditorHost.HandleKey`, the one router; `EditorView.KeyHandler` sends keys to it): the user's
  binding (`CRAFT_KEY_BINDINGS`) first; then a registered binding with `OverridesEditor`; then `EditorModel`'s own keys;
  then a registered binding for a chord the editor declined. The built-ins keep working, plug-ins add chords, and only an
  explicit override takes a built-in key. A user's empty command unbinds a plug-in's chord. The lookup writes the chord
  into a stack buffer and reads `KindTable` by span: no string per keystroke.
- **The host** (`Craft.Editor.EditorHost`, UI-free, tested headless): each plug-in is activated through a scope of its
  own (`IEditorHost` forwarded under its name) that records every handler, status item, panel and progress it adds, so
  deactivation (dispose, `Deactivate`, or `RefreshPlugins` after it was unregistered) takes back what it left. An
  `Activate` that throws is reported in `Plugins` and the log, its partial contributions taken back. Events are raised
  from copy-on-write handler arrays, each handler alone: one that throws is logged and the others (and the keystroke)
  go on; `DocumentChanged` makes nothing when no one listens. The user is asked through
  `Craft.Editor.Abstractions.IEditorPresenter`; without one every proposal is rejected. A proposal is validated
  (in order, inside the text), summarised (edits, lines, characters removed and added, a preview cut at
  `CRAFT_PROPOSAL_PREVIEW_CHARS`), applied as one `EditKind.Command` undo step, and dropped when the document changed or
  closed while the user was asked. Log lines kept: `CRAFT_HOST_LOG_LINES`.
- **Packaging** (`Craft.Core.Plugins`): `PluginManifest` read with a source-generated `JsonSerializerContext` (comments and
  trailing commas allowed, a UTF-8 byte-order mark skipped: Windows editors write one), checked before any of the
  plug-in's code loads, every problem named. A newer contract (major, or minor of the same major) is refused; an older
  major is loaded and marked `Outdated`; no plugin.json loads with a `Note`. `CraftPlugins.Installed` keeps the latest
  result per folder for the Plug-ins page. Craft.CSharp has `src/Craft.CSharp/plugin.json`, copied into its own output by
  its `CraftCopyPluginManifest` target (a target, not content, so it does not flow into the tests' output); Craft.App's
  existing copy of that folder carries it, and the app's target now fails the build if it is missing.
- **MAUI** (`Craft.Maui/PluginViews.cs`, built here): `StatusItemsView`, `PanelsView`, `MauiEditorPresenter` (the
  confirmation is `DisplayAlertAsync` with Apply/Reject until A1's diff view), `CommandPalette` (`DisplayActionSheetAsync`
  of the commands that can run, with their chords). **Craft.App**: `EditorPage` holds one `EditorHost`, registers its own
  `craft.commandPalette` (Ctrl+Shift+P) and `craft.plugins` on the same contracts, and adds the Commands and Plug-ins
  buttons; the Windows adapter maps the new keys.
- **Kit**: `samples/Craft.Plugins.Sample` (outside `Craft.slnx`, referencing `Craft.Abstractions` with `Private=false` and
  `ExcludeAssets=runtime`): the word count follows each change from the lines it touched (the incremental count equals a
  full count over 400 random edits in the test) and the status text is made only when a count changes;
  `sample.wordCount` on Ctrl+Shift+W; the setting `showCharacters`. `templates/craft-plugin` (`dotnet new craft-plugin -n
  NAME --craftPath CRAFT --pluginName SHORT`): checked by installing into a temporary hive and building a generated
  project inside this repository and outside it. `dotnet new` evaluates MSBuild `Condition` attributes in a template's
  project file, so the template removes the shared contracts with `WithMetadataValue` instead. Guide: `docs/plugins.md`.
- **Tests**: group `PluginKit` (the sample loaded from a copied folder, activated, its status item, command and binding,
  removed and loaded again, disabled; plugin.json refused for a newer contract or bad fields, an older major marked; a
  throwing `Activate` and handler; proposals; key precedence; settings), with `Plugins`, `Editor`, `ApiGuard` and
  `Inventory`: 35 passed on the CPU here.
- **Minimal now**: capabilities are recorded and shown, not enforced; a panel is text lines; the confirmation is a
  dialog with a summary (the diff is A1); plug-in settings come from the environment (the settings page and
  `.craft/settings.json` are E4); one document at a time in the window (tabs are E4); a plug-in loads once per process.

## E5 as built (2026-10-31)

- **Contracts (`Craft.Abstractions`, `Semantics.cs`, `WorkspaceReaders.cs`):** `ISemanticLanguageSession : ILanguageSession`,
  optional (a language that only classifies implements nothing more): `Diagnostics` (a `DiagnosticSet`: the snapshot they
  are of, `EditorDiagnostic` items with span, severity Hidden/Info/Warning/Error, id, message, and the counts) and
  `DiagnosticsChanged` (raised on the session's worker; the subscriber marshals), `GetCompletionsAsync` (a `CompletionList`:
  its snapshot, position and span start, `CompletionItem`s with label, kind, detail, edit, filter and sort text, caret, and
  `NeedsResolve`), `ResolveCompletionAsync`, `GetSignatureHelpAsync`, `GetHoverAsync`, `GetDefinitionAsync` (a path and span,
  the same document, or a metadata symbol's description), `FormatAsync` (edits in the snapshot's positions), `EnableSemantics`
  (the command for a large file), `IsSemanticDeferred`, `Telemetry` (`SemanticTelemetry`: the workspace's projects, documents,
  references and their bytes, the heap its build added, build and last diagnostics time, generation) and `Release`. Every
  request is a task, cancellable, run off the calling thread. `ILanguage.Open(ITextBuffer, LanguageOpenContext)` is a
  default-interface overload (the old `Open(buffer)` stays): the file, the workspace root and the `LanguageSettings` (the
  diagnostics pause, the size limit, the readers in order, the configuration, tab size, spaces, line break), made from
  `CraftOptions.ToLanguageSettings()`, since a plug-in reads no `CraftOptions`. `IWorkspaceReader : IKind` in
  `WorkspaceReaders` (a `KindTable`, no library default: the C# plug-in registers "csproj" and "loose"; an unknown name
  fails with "not registered") turns a folder into `WorkspaceProject`s, plain data any language can build its model from.
  `EditorColors` gained `Error`, `Warning`, `Info` and the popups' colours (init properties, so the record's constructor is
  unchanged), contrast-checked: squiggles 6.2-8.3:1 on the dark ground, 4.9-5.4:1 on the light one, popup text 10.7 and 14.8.
  `EditorKey` gained `F12`, `Space` (a key only with a modifier; typed, it stays text), `D`, `K`.
- **Craft.CSharp** (now also on `Microsoft.CodeAnalysis.CSharp.Workspaces` and `.Features` at `$(RoslynVersion)`; with them come
  System.Composition, Humanizer, Elfie, DiaSymReader and System.Configuration.ConfigurationManager, all in the plug-in's
  folder; none is AOT-compatible, MEF composes by reflection: rooted for the trimmer in M). The "csproj" reader evaluates
  the project file itself (`ProjectEvaluator`: Directory.Build.props, the project, Directory.Build.targets and their plain
  imports; `$(Name)`; conditions of the common shapes and the property functions `IsOSPlatform`, `GetTargetPlatformIdentifier`,
  `GetTargetFrameworkIdentifier`, `Path.GetFullPath` and `Combine`; another condition counts as false and is noted): target
  framework (the first of several), language version, nullable, unsafe, warnings as errors, documentation diagnostics,
  warning level, NoWarn, defines (the SDK's for the configuration and framework), implicit usings (Microsoft.NET.Sdk's set,
  the Web SDK's, `Using` items), `InternalsVisibleTo`, `Compile` (the default glob without bin, obj and hidden folders, then
  Include and Remove), `ProjectReference`, and references from `obj/project.assets.json` (packages' compile assets for the
  framework, its projects, its framework references) plus the framework's targeting pack beside the running runtime
  (`dotnet/packs/*.Ref`, with XML documentation), else the running runtime's trusted platform assemblies of its folder. The
  "loose" reader makes one "Loose files" project on the running framework. `CSharpWorkspace`: one `AdhocWorkspace` per
  workspace root (and configuration and readers), counted by its sessions; MEF host services composed once from the
  plug-in's own assemblies, loaded through the plug-in's load context; projects added as files need them; the open
  document's text is its session's `SnapshotSourceText`, brought to the requested snapshot in one incremental step from the
  version the solution holds (the changes composed into one range, as the parse worker does); other files load from disk
  when Roslyn needs them (`FileTextLoader`), and a closed document goes back to its file; metadata references are read once
  per process and shared; the SDK's generated global usings and `InternalsVisibleTo` become one generated document.
  Diagnostics run on the thread pool after `CRAFT_DIAGNOSTICS_DELAY_MS` without a change (one timer reset per change, the
  running pass cancelled), syntax and semantic diagnostics of the open document's tree (analyzers and source generators
  later); above `CRAFT_SEMANTIC_MAX_CHARS` nothing runs on its own (nor completion while typing) until `EnableSemantics`.
  Completion: `CompletionService` and, on commit only, `GetChangeAsync`; signature help built from the semantic model
  (invocations, object creations, indexers; the overloads from the member group, the active one from the binding, the
  argument from the commas); hover: the symbol's display and its documentation's summary; definition:
  `SymbolFinder.FindSymbolAtPositionAsync` and `FindSourceDefinitionAsync`; format: `Formatter.FormatAsync` with the
  editor's indentation, its text changes as edits. `Release` drops the workspace (a request made after it never sees the
  old one) and the shared references; `CSharpLanguage.ReleaseWorkspaces()` releases all. Scripts (.csx) stay syntactic only.
- **Craft.Editor:** `EditorModel` opens the language with the context (a new optional `workspaceRoot`), takes the session's
  diagnostics on the UI thread (`Dispatch`, set by the host; else the synchronization context; else at once) and maps a set
  computed for an older snapshot through the edits since (kept per version; one an edit touches is dropped, as the
  syntactic layer does); every edit moves the current ones. `CompletionController` (open after an identifier character or
  '.' and `CRAFT_COMPLETION_DELAY_MS`, or Ctrl+Space; filter by prefix, case first, then camel humps; Up, Down, Page Up and
  Page Down; Enter and Tab commit one `EditKind.Command` step, mapped onto what was typed since; Escape; a click selects a
  row, a second commits; an answer for an older request or a word the caret left is dropped and counted). Signature help on
  '(' and ',' (Ctrl+Shift+Space), hover after `CRAFT_HOVER_DELAY_MS` (the problems there first), F12 (the same document
  selects the name, another file raises `DefinitionRequested`, metadata shows its description), Shift+Alt+F and Ctrl+K
  Ctrl+D format (applied as one step if the text is still the one formatted). The renderer draws squiggles (a wave for
  errors and warnings, dots for information, hidden ones not at all) only for the visible lines (a binary search from the
  first line, bounded by the longest diagnostic), then the popups through `IEditorCanvas` (the completion list under or
  over the word, the tooltip with the active parameter bold; the tooltip's wrapped lines kept while its text and width are).
- **Craft.Maui / Craft.App:** `EditorView` sets `Dispatch`, forwards `DiagnosticsChanged` and `DefinitionRequested`, takes the
  pointer's hover (`MoveHoverInteraction`, `EndHoverInteraction`), has `WorkspaceRoot` and `ReleaseSemanticMemory()`; the
  Windows adapter maps F12, Space (with Ctrl or Alt only), D and K. The app's page has a collapsible problems list under the
  editor (`ProblemsPanel`: plain labels, errors first, `CRAFT_PROBLEMS_LIMIT` rows, a tap moves the caret), the error and
  warning counts in the status bar (a tap opens the list), and F12 into another file opens it with the name selected.
- **Source generators (completed 2026-10-31).** `WorkspaceProject.Analyzers` (plain data): the "csproj" reader fills it from
  the targeting packs' `data/FrameworkList.xml` (`ReferenceAssemblies`' `FrameworkReferences.Analyzers`: Analyzer entries in
  C# or no language, else the pack's `analyzers/dotnet/cs`), the packages' analyzers in `project.assets.json` (a package's
  `analyzers/...dll` files, not another language's folder nor a satellite, the highest `roslynX.Y` folder the running
  Roslyn loads) and `Analyzer` items; a package's analyzer of a name the pack has gives way to the pack's; the "loose" reader
  gives the running framework's. `AnalyzerLoader` (an `IAnalyzerAssemblyLoader`): one non-collectible load context per
  process whose parent is the plug-in's, Microsoft.CodeAnalysis*, System.Collections.Immutable and System.Reflection.Metadata
  resolved through the parent (a generator built against an older Roslyn binds to the plug-in's), an analyzer's own folder
  for its dependencies, the framework from the default context; an `AnalyzerFileReference` per path and write time. The
  workspace adds them to each project; Roslyn runs their generators when it makes a compilation, and only the generators
  count (their diagnostic analyzers are never run). Generated code works as the build's does: no false CS8795 on Craft's
  own solution, on every host OS.
- **Semantic colours (completed 2026-10-31).** `CSharpLanguageSession.Colors.cs`: when `Classify` is asked for lines the
  colours do not cover, or the colours are older than the snapshot, it notes the range (and a screen either side); after
  `CRAFT_SEMANTIC_COLORS_DELAY_MS` one pass on the thread pool brings the workspace's document to the newest snapshot and
  runs Roslyn's `Classifier.GetClassifiedSpansAsync` over it, keeps the spans (in order, one per range: Roslyn's additive
  "static symbol" joined to the name's class as `TokenClasses.WithStaticSymbol`, its other additive classes left out) and
  raises `Reclassified`. `Classify` (UI thread, never waiting) gives Roslyn's spans inside the classified range, mapped
  through the edits since (composed into one range as the parse worker does: the spans before and after the changed text
  kept and shifted, the changed text given the syntactic layer's fresh spans), and the syntactic spans outside it; fresh
  colours are exactly Roslyn's spans (the `Semantic` test compares a whole file with Roslyn's own classification). A change
  the session did not see the start of drops them until the next pass. Not for a script, nor above `CRAFT_SEMANTIC_MAX_CHARS`
  until `EnableSemantics`. `TokenClasses` gained the extension method, event, enum member, constant, label and static symbol
  names; the dark and light themes give structs and enums, interfaces, type parameters, members (property, field, event),
  parameters, namespaces and constants hues of their own (methods and extension methods the method hue, locals the local
  hue), and a static name its class's colour in italic (`TableTheme` makes the joined styles once). Every class of both
  themes reads at 4.5:1 or more on the ground and the caret line (dark 7.5-12.1:1, light 4.8-9.3:1; checked by the
  `Inventory` test), excluded code staying dimmed by design.
- **Memory pressure (completed 2026-10-31).** `IMemoryPressureMonitor : IKind` and `MemoryPressureMonitors` (a `KindTable`;
  the library's "gc" and "none"; an unknown name fails with "not registered") in `Craft.Abstractions`; `ILanguage.ReleaseMemory()`
  (a default that frees nothing; any thread, never blocking) and `MemoryPressureMonitors.ReleaseLanguages()`. "gc" reads,
  after each full collection, the memory load the runtime measures against its high-load threshold (the system's memory or a
  container's limit; high at the threshold, critical half-way to all of it): an object the collector finalizes re-registers
  itself, so no thread or timer runs, and the report goes to the thread pool, one at a time. Because the runtime measures the
  OS's own memory load on Windows as elsewhere, no Windows-only monitor is needed under `Platforms/Windows`; a platform's own
  (an app memory-usage notification) can be registered under another name and chosen with `CRAFT_MEMORY_MONITOR`.
  `ShellModel` starts the options' monitor (again when the setting changes; an unknown name is an options problem, never a
  crash), and on pressure every language frees what it can build again (`MemoryReleases` counts it). `CSharpLanguage.ReleaseMemory`
  asks every workspace to drop its solution before its next request and clears the shared references, off the calling thread.
- **Settings added** (each a `CRAFT_*` variable): `CRAFT_DIAGNOSTICS_DELAY_MS` (400), `CRAFT_SEMANTIC_MAX_CHARS` (1,000,000),
  `CRAFT_WORKSPACE_READERS` (csproj,loose), `CRAFT_WORKSPACE_CONFIGURATION` (Debug), `CRAFT_COMPLETION_DELAY_MS` (50),
  `CRAFT_COMPLETION_ROWS` (10), `CRAFT_HOVER_DELAY_MS` (500), `CRAFT_PROBLEMS_LIMIT` (500); then `CRAFT_SOURCE_GENERATORS` (true),
  `CRAFT_SEMANTIC_COLORS_DELAY_MS` (30; -1 off), `CRAFT_MEMORY_MONITOR` (gc).
- **Tests:** group `Semantic` (a temporary project of a .csproj and two files: the reader's model, diagnostics that appear and
  clear, waited on with `WhenDiagnosticsIdle`; completion after `Console.` and on a local of the other file's type, applied;
  signature help, hover with the summary, definition into the other file, in the same document and in metadata; format;
  release and rebuild; a large file waiting for the command), `Editor` additions (completion filtering, selection, commit as
  one step, resolve on commit, late answers dropped, camel humps, popup clicks, diagnostics of an older snapshot mapped,
  squiggles only on visible lines, popups drawn, definition and format through the model), and the `Plugins` test copies the
  whole plug-in output and asks the plug-in's own semantic layer for `Console.` completion. Then (`SemanticGeneratorTests`):
  the targeting pack's generators found; a temporary project with a `LibraryImport` and a `GeneratedRegex` partial compiles
  clean with the generators and gives both CS8795 without them; `src/Craft.Editor`'s workspace (Idrak.Abstraction,
  Craft.Abstractions, Craft.Core, Craft.Editor) compiles with 0 errors; the session's spans of a whole file equal Roslyn's
  `Classifier` output, with the expected class, static method and local names; colours shifted by an edit, then a new pass;
  the merge on fixed spans; `ReleaseMemory` and the rebuilt workspace. `ThemeAndMemoryTests` (`Inventory`): every class's
  contrast in both themes, the static style, the monitors' registry and the languages' release.
- **Not built (a candidate, measured before):** diagnosing the changed member first; one parse for both layers.

## A1 as built (2026-10-31)

- **`Craft.Ai`** (new, in `Craft.slnx`): a pre-installed, removable plug-in like Craft.CSharp (plug-in "ai",
  `plugins\Craft.Ai\` beside the program, `CRAFT_PLUGINS_DISABLED=Craft.Ai` turns it off), built only on
  `Craft.Abstractions` and Idrak (Idrak.Mcp, Idrak.Text, and Idrak.Text.Plugins as the model families it registers on
  the first load, by the setting `idrakPlugins`: no family is named in Craft). Its own output keeps Craft.Abstractions so
  the idrak tool loads the assembly alone; the app's copy leaves out the host's contracts. Android/AOT note: Idrak.Mcp is
  AOT-compatible; the folder provider is made by reflection (the idrak tool's loader); the MCP SDK brings
  Microsoft.Extensions.* assemblies, which the plug-in shares with the app when MAUI loaded them first (to watch on the
  first Windows run).
- **The editor's tools** (`EditorTools`, Idrak `ITool`s with explicit schemas): live over `IEditorHost` (`open_documents`,
  `read_document`, `get_selection`, `get_diagnostics`, `search_workspace`, `propose_edit`) and over a folder
  (`list_files`, `read_document`, `search_workspace`, `propose_edit` written as a unified patch under `.craft/patches`,
  the file never changed). Live tools read the host on its UI thread and read snapshots off it; an open document's
  unsaved text is what they see; paths outside the folder are refused; search reads a file's bytes once and skips it
  without decoding when a case-sensitive query is not in them. `[assembly: ToolProvider(FolderToolProvider)]` over
  `CRAFT_PLUGIN_AI_TOOLSFOLDER` (else the current folder): `idrak tools list|test Craft.Ai.dll` lists and calls the four
  folder tools (4 of 4 answered here; since P1 the tools are `Craft.Plugins.Tools`, with `find_symbol`). `find_symbol` waits for a contract a plug-in can reach Roslyn's symbols through
  (a seam for P/A2).
- **Contracts added** (Craft.Abstractions, all additive, contracts still 1.0 as K and E4 kept them, nothing released):
  `IModelProvider : IKind` with `ModelProviders` ("local"), `ModelRequest`, `ModelLease`, `ModelLoadStatus`; `TextDiff`
  (a Myers line diff between the common start and end, bounded by `CRAFT_DIFF_LINE_LIMIT`, hunks with
  `CRAFT_DIFF_CONTEXT_LINES`, unified patches); `IEditorHost.WorkspaceFolder`, `IEditorDocument.Diagnostics` (E5's
  diagnostics as the editor shows them, set by the app through `EditorHost.SetDiagnostics`),
  `IEditorHost.ProposeAsync(ProposedEdits)` (placement and origin beside the edits; the user may accept some hunks);
  `ISemanticLanguageSession.DiagnoseEditsAsync` (Craft.CSharp forks the workspace's document with `Document.WithText`,
  the workspace untouched); `EditorColors` gains the approved design's tokens (ground, surface, border, secondary text,
  accent, success, the diff's added and removed backgrounds; dark error and warning now the design's #F07178/#E3B27A).
- **The diff-and-accept view** (`Craft.Maui.ProposalDiffPage`, from `docs/design/Diff.dc.html`, replacing K's summary
  dialog through `IEditorPresenter.ChooseHunksAsync`): the hunks (number, lines, how the edit was placed) each with
  Accept and Reject, Accept all / Reject all / Apply accepted hunks, the lines with old and new numbers, and the column
  "Before you accept" (the language's errors and warnings after the change against now, from `EditProposal.Check`, run
  while the user looks; `CRAFT_DIFF_CHECK`), "How the edit was placed", "one undo step". Colours only from the theme. The
  host applies the accepted hunks' edits as one `EditKind.Command` undo step; all rejected, or the document changed
  meanwhile, nothing changes.
- **MCP from the running app**: `EditorMcpServer` on Idrak's `McpHost` "http" (loopback, a token): off by default; the
  command "AI: Serve the editor's tools over MCP" (`ai.mcpStart`, or `CRAFT_PLUGIN_AI_MCPONSTART=true`) starts it, the
  status bar shows "MCP http://127.0.0.1:PORT/mcp" (a click shows the token and the `idrak chat --mcp` line),
  `ai.mcpStop` stops it. Address and token: `CRAFT_PLUGIN_AI_MCPADDRESS` (127.0.0.1:0), `CRAFT_PLUGIN_AI_MCPTOKEN`
  (empty: random each start; "none": no token), on the settings page with the plug-in's other settings.
- **"local" model provider** (`LocalModelProvider`): the name through Idrak's `ModelSource.Resolve` (`ModelSources`),
  `PretrainedModel.Load` on the device setting (auto, cpu, vulkan:0, cuda:0), its chat template's `ChatGenerator` and the
  `TextGenerator` (for FIM), the context window the model's own unless set, kept by `ModelHost` for
  `keepAliveMinutes`; `threads`, `cpuMemoryMb`, `gpuMemoryMb` set Idrak's `ComputeResources` (0: none, the device's
  own). Loading shows its stage and elapsed time in the status bar (the library reports no fraction for a load). The
  design's "Models and devices" is a panel for now (where models run, the chat model and its state, device, limits, how
  a name is resolved, what is loaded); its page is phase D's.
- **Tests** (group `Ai`): the diff (hunks, unified patch, no-newline marker, the line limit; 300 random edits rebuilt
  from the hunks); a `FakeChatModel` reads the document and proposes an edit through `propose_edit`, which arrives as a
  one-hunk diff, is accepted and undone in one step; per-hunk answers; an MCP client (`McpTools.ConnectHttpAsync`)
  against the in-process server lists the live tools and reads an open document, a wrong token refused; the folder
  tools (bin skipped, a path outside refused, the patch exact, the file unchanged); the plug-in registered from its
  assembly, activated, MCP started and stopped by command with the token setting, a folder with no model failing, all
  taken back on unregister; the settings. With `ApiGuard`, `Inventory`, `PluginKit`, `Shell`, `Editor`, `Semantic`: 65
  passed here (after one fix: the server lists tools in its own order).
- **Not yet**: no real model was loaded here (the owner's run); the check before accepting covers all the hunks, not
  the accepted subset; `find_symbol`; a load's fraction (Idrak's loader reports none); the MCP server serves the tools,
  not the models.

## D as built (2026-10-31)

- **Theme tokens** (`Craft.Abstractions/Themes.cs`, all additive, the record's constructor unchanged as E5 and A1 kept
  it): beside A1's ground, surface, border, secondary text, accent and success, `EditorColors` gained `Raised`,
  `RaisedHigh`, `InterfaceSelection`, `BorderStrong`, `Text` and `AccentInk`, each defaulting from the constructor's
  colours (a plug-in's theme made before them draws the shell in its own colours). The dark theme is the design's exact
  values (ground #101217, surface #16181D, raised #1A1D23/#1F232B, selection #252A33, borders #2A2E37/#343944, text
  #D5D9E0, secondary #9AA2B1, accent #E5A54B on ink #1A1206, success #5FC9B4, warning #E3B27A, error #F07178) and the
  design's code classes: `.k` keyword #82AAFF, `.t` every type name (classes, records, interfaces, structs, enums,
  delegates) #5FC9B4, `.s` #E3B27A, `.c` #8790A0 italic, `.m` #E8D58A, `.v` locals and parameters #B8C7DC, `.p` plain
  and punctuation #D5D9E0; the classes the design does not show keep E5's hues. The light theme is derived with the
  same roles: its status colours, secondary text and accent are moved by `ColorContrast.Ensure` (public: WCAG ratio,
  a colour mixed toward black or white in 1/64 steps until it reads on every given background) until they read at
  4.5:1 on its ground, surfaces, selected row and caret line. `CRAFT_ACCENT` (amber, teal, blue, rose: the design's
  four, `AccentColors`; or any #RRGGBB) goes through `EditorThemes.WithAccent`: the accent and the caret, moved to
  4.5:1 on the theme's grounds (it marks text: the explorer's "M", "Completion"), its ink the theme's background or
  text, whichever reads on it; a design accent on the dark theme is kept exactly. The test (`Design`) checks every
  interface pair of both themes and of each accent (text, secondary, accent, success, warning, error on the ground, the
  surfaces, the selected row and the caret line; ink on accent; line numbers on the editor) at 4.5:1. Borders and
  dividers are decorative, not checked. Every view takes its colours from `ShellPalette` (the theme's tokens as MAUI
  colours); no colour of the shell is written in a view.
- **Fonts**: Inter 4.1 (Regular, Medium, SemiBold) and JetBrains Mono 2.304 (Regular, Italic, Bold, BoldItalic), the
  official release files (github.com/rsms/inter, github.com/JetBrains/JetBrainsMono), both SIL OFL 1.1, in
  `src/Craft.App/Resources/Fonts` with `Inter-OFL.txt` and `JetBrainsMono-OFL.txt` (copied beside the program under
  `licenses\`), `MauiFont` items registered by `MauiProgram` under `Craft.Maui.CraftFonts`' aliases. Defaults:
  `CRAFT_FONT` JetBrains Mono at 13 on 1.69 (the design's 22-unit lines), `CRAFT_INTERFACE_FONT` Inter at
  `CRAFT_INTERFACE_FONT_SIZE` 13. A code font the platform does not draw monospaced (a run of "i"s measured against
  the "M"s, once per family, inside the first frame) gives way to `CRAFT_FONT_FALLBACK` (Cascadia Mono), so a missing
  font never breaks the columns. Android AOT/trim: fonts are data, nothing to root.
- **The shell** (`Craft.Maui/ShellPage.cs`, moved from `Craft.App/EditorPage.cs` so it compiles here, with nullable
  analysis, on Craft.Maui's net10.0 target; `Craft.App` now only builds the `ShellStartup` and the window): the title
  bar's row (the logo drawn from `logo.svg`'s geometry with the accent's C, "Craft", the menus, the search box, the
  model chip) in the window's caption through MAUI's `TitleBar` (`CRAFT_TITLE_BAR=window`, the default) or as a page
  row (`page`); the menus are `EditorMenus` (new registry, `MenuItem` named "Menu/command") over registered commands,
  shown only while their command is registered, with its chord, in groups, in a popup layer of the page; the Edit menu
  sends the editor's own keys to the active editor (`ShellEditCommands`), so menu and keys do one thing. Ctrl+P is
  `craft.quickOpen` (`QuickOpen`: the workspace's files listed once off the UI thread through
  `Workspace.EnumerateFiles`, at most `CRAFT_QUICK_OPEN_FILES`, said so when capped; a query keeps only the best
  `CRAFT_QUICK_OPEN_ROWS` by insertion, no sort of the list; `FuzzyMatch` scores word starts, camel humps and runs, two
  walks without allocating; '>' gives the commands that can run, with their chords). The model chip and the status
  bar's model text read `IModelProvider.Status` (new, a default member: where, the models by role, device, memory,
  tokens per second, first token, the settings the page edits), "no model" until a provider has one; A1's "local"
  fills it (its chat model and state from the settings and the load's progress, the device a load used) and raises
  `StatusChanged`. The activity rail is `EditorRailItems` (new registry, `RailItem` with SVG path data,
  `RailIcons` the design's): Explorer (the tree), Search (quick open), Agent (`craft.agent`, hidden until A2 registers
  it), Plug-ins, Settings at the foot; source control has no item until a plug-in brings one. The explorer: the
  folder's name in capitals, the tree (the active file's row on the selection), and the semantic card from
  `SemanticTelemetry` through the language's contract (`Engine` "Roslyn", set by Craft.CSharp; Ready with projects and
  memory, Loading, Waiting for a large file; hidden for a language without a semantic layer). The tabs on the ground,
  the active one on the surface under the accent's 2-unit line, a dot for unsaved changes. The breadcrumb: the folder,
  the file, then `ILanguageSession.ScopeAt` (new, a default member returning none): Craft.CSharp walks its last parse
  from the caret's token up, reusing one buffer and returning the same array while the scopes are the same (a caret
  move makes nothing). The bottom panel (`CRAFT_BOTTOM_PANEL_HEIGHT`, Ctrl+J): Problems with the count (virtualized,
  errors first, a row moves the caret), Terminal (phase T's placeholder), Output (the host's log, live, bounded by
  `CRAFT_HOST_LOG_LINES`), Idrak telemetry (`TelemetryLog`: one row per source and metric of `IEditorHost.Record`, so
  memory follows the metrics' names, not the measures; a placeholder until something is recorded). The status bar:
  the branch (`GitBranch`: `.git/HEAD` read as bytes, a worktree's `gitdir:` file followed, a detached head's short
  hash; no git program), the problems in the warning or error colour (a tap opens them), the semantic state, the
  position, the indentation, the encoding, the line break, the language, the plug-ins' items, the model's text; each
  label is set only when its text changed. Narrow (below `CRAFT_NARROW_WIDTH`, the measured width): the phone's top
  bar (files, the file's title and place, undo, commands), the tree in the editor's place, the tab switcher, the
  status bar's position and problems only; every control there at least `CRAFT_TOUCH_TARGET` (44) tall; no rail,
  panels, breadcrumb or bottom panel. Commands added: `craft.quickOpen`, `craft.togglePanel`, `craft.showProblems`,
  `craft.showTerminal`, `craft.showOutput`, `craft.showTelemetry`, `craft.models`, `craft.pluginGuide`, `craft.about`,
  the ten edit commands.
- **Pages** (`Craft.Maui/DesignPages.cs`): the Plug-ins page from `Plugins.dc.html` over `PluginList` (K's load results,
  the host's activations, what each plug-in's assembly registered in the public registries by the registered object's
  type, and Idrak's plug-ins from `IdrakPlugins`): All / Overrides / Problems with counts, a filter by name or registry,
  "Load from folder…" (`CRAFT_PLUGIN_FOLDERS` in the user's settings, at the next start), the table, the chosen one's
  details, its problem, and "Disable/Enable at the next start" (`CRAFT_PLUGINS_DISABLED`). The Models and devices page
  from `Models.dc.html` over the first provider's status: where models run (Local; Remote later, A3), the models by
  role, "Add a model" (the provider's model setting, then its load command, the host's progress bars), how a name is
  resolved (Idrak's `ModelSources`, in order, with who registered each), the device setting and what was measured
  (nothing unmeasured is shown as a number), the limits (the provider's settings: Idrak's ComputeResources). With no
  provider it says so.
- **Measured** (the editor bench, `-- editor`, the same 4-core container and workload as E3's): keystroke to render
  median 6.6 us, p99 24.2 us, 1,028 bytes and 1 run string per keystroke (E3: 6.6 us, 23 us, 1.0 KB, 1); blink frame
  5.0 us, 0 bytes (E3: 5.0 us); scroll frame 5.3 us, 0.3 KB (E3: 5.3 us); cold 32 ms (E3: 29 ms): unchanged. The shell's
  work per caret move (labels set when changed, the breadcrumb's cached scopes) is outside the bench's editor path.
- **Tests**: group `Design` (the tokens and every pair's contrast in both themes and each accent, the dark code classes,
  the options, the menus and rail as registries of commands taken back on dispose, quick open, the branch, the status
  texts, the C# scopes, the plug-in list, the telemetry), with `Ai`, `Editor`, `Shell`, `PluginKit`, `Semantic`,
  `ApiGuard`, `Inventory`: 84 ran here; two failed on the first run (E4's test expected the old 14-unit font; greedy
  fuzzy matching missed a camel hump, fixed with the word-start walk) and passed after the fix.
- **Not compiled here** (Windows only): `src/Craft.App/MauiProgram.cs`, `src/Craft.App/App.cs`, `src/Craft.App/Craft.App.csproj`
  (the `MauiFont` and licence items). Nothing under `Platforms/Windows` changed. Not run here: the window itself (the
  `TitleBar` in the caption, the bundled fonts in the drawn editor on Windows, the popups' places).
- **Not yet**: quick open is clicked or takes the first row on Enter (no arrow keys in the list, no Escape: a tap
  outside closes it); the search box finds files and commands, not symbols; the problems' "Fix with Roslyn" and
  "Explain" (code actions, A2), the chat column and the completion hint bar (A2); the phone's key row (M); the Models
  page's device is a setting typed as text (no device list from the library yet), and tokens per second and device
  memory stay empty until the provider measures them (A2's generation); the settings page has no sections yet (the
  Models page's other sections open it whole); A1's "Models and devices" side panel is still shown beside the page.

## A2 as built (2026-10-31)

- **Idrak (6eb11a2, on `main`, by its rules):** `CodingAgent.Steer(note)` and `PendingNotes`: a note from any thread is
  added as a user message ("Note from the user: …") before the agent's next model request and stays in the conversation
  (test "coding agent: a steering note…", `IDRAK_DEVICES=cpu IDRAK_FILTER="coding agent|public API"`: 6 passed; the
  `api/Idrak.Text.txt` dump regenerated). Nothing else was missing: FIM through `FillInMiddleFormats.Detect`/`Create`,
  `KeepCache`, the generator's per-step cancellation, `ChatTools.WithTools`, `CodingAgent`/`CodingTools`,
  `CommandSandboxes` and `TextMatchers` are used as they are.
- **Contracts added (Craft.Abstractions, additive, contracts still 1.0):** `IInlineCompletionProvider : IKind` in
  `InlineCompletionProviders` (`DelayMilliseconds`, `Accepts`, `CompleteAsync` streaming `InlineCompletionPiece`s for an
  `InlineCompletionRequest`; the plan's `CompletionProviders`, named so it is not taken for E5's completion list);
  `ICodeActionProvider : IKind` in `CodeActionProviders` (`CodeActionContext`, `CodeAction` with `CodeActionKinds`,
  `ActionsFor` gathering every provider's, one that throws left out); `IConversationPanel : IEditorPanel` (entries by
  `ConversationRole`, `PanelAction`, `ContextChip`, status, header buttons, `SubmitAsync`, `ConversationChanged` with one
  `EntryUpdated` per streamed piece) so a plug-in describes a chat-like panel UI-free and the host draws it;
  `IEditorHost.OpenDocumentAsync` (default null; `EditorHost.OpenFile`, set by the shell: the file's tab, or a new document
  for a path with no file yet); `IEditorHost.ShowPanel(id)` (default nothing; `EditorHost.PanelShowRequested`, which the
  page answers by showing the chat column or sheet with that panel); `ModelLease.Tokenizer` (the local provider passes the model's `IPretrainedTokenizer`, from
  which the FIM format is detected); `EditorColors.GhostText` (dark #7D8696: the design's #717A8A lifted to 4.8:1 on the
  editor ground; light #6E7781, 4.5:1). Options: `CRAFT_INLINE_COMPLETION_PROVIDER` (empty: the first that accepts;
  `none`: off), `CRAFT_CHAT_WIDTH` (360).
- **Inline completion.** `Craft.Editor.InlineCompletionController` (`EditorModel.InlineCompletion`): after typed text or
  a deletion it waits the provider's pause (a debounce timer, the request built on the UI thread from the immutable
  snapshot), runs the provider on the thread pool and posts each piece back; any other key, text that is not the ghost's
  start, a click or a change from elsewhere cancels the request at once and removes the ghost (late pieces are counted,
  never shown); Tab inserts it all as one `EditKind.Command` step, Ctrl+Right the next word (white space then a word,
  one other character, or a line break with the next line's indentation; the rest keeps streaming), Escape dismisses,
  typing what the ghost starts with types through it. The completion list, when open, hides it and keeps its keys.
  `GhostTextRenderer` draws the first line after the caret and the others over the lines below (an overlay: the text
  moves only on accept), italic in `GhostText`, then the design's hint bar (Completion · Tab accept · Ctrl+→ word · Esc
  dismiss · tok/s · first token ms) under it, only inside the viewport. `AiInlineCompletion` ("ai"): the model of
  `completionModel` (else `model`) through the provider of `modelProvider`; the format by `FillInMiddleFormats.Detect` on
  the lease's tokenizer, else prefix-only (stop at a blank line) and the status bar says "Completion: prefix-only" with the
  reason (no tokenizer, or the registered formats none of whose tokens it has); the suffix is `completionSuffixChars`
  (512: A0's per-request cost on a prefix-first model) cut to whole lines; the prefix's budget is the model's context less
  the answer and the suffix (counted by the tokenizer), and it starts at a block boundary (a quarter of the budget, then a
  line start), so consecutive requests share their prompt up to the format's `PrefixEnd` and `KeepCache` reuses it (over
  200 characters typed the start moved once, in the test); `completionChunk` 1 streams each token; `completionMaxTokens`,
  `completionMaxLines`, `completionTemperature`; the prompt's, cached and rate figures recorded as telemetry.
- **Chat panel** (`AiChatPanel`, "ai.chat"): the chat model through `AiModels` (one lease per provider and name, kept for
  the session, a failed load not kept), the editor's live tools offered through `ChatTools.WithTools` over an observing
  registry, so each call is a row ("Called get_diagnostics → its first line"); the reply streams into one entry, then is
  split at its fenced blocks: each a Code entry with "Apply as diff" (placed by `SnippetPlacement`: the setting's Idrak
  `TextMatchers` matcher, `placement` "semi-global", within `placementMaxError` of the snippet; else the selection; else the
  caret; placed again if the text changed meanwhile; proposed through `ProposeAsync`, its placement and origin in the diff
  view) and Copy; then "N tokens · R tok/s". Chips: the active file (±40 lines around the caret go with the message; the
  rest through `read_document`), the selection, its problems; a tap removes one for the next message; `@selection`,
  `@problems`, `@file`, `@PATH` add; `/explain`, `/fix`, `/doc`, `/refactor`, `/tests` run the code action. Stop cancels;
  `chatHistory` messages are sent back; kept per workspace in `chatFile` (`.craft/chat.json`) when `chatKeep`, read back
  with its code blocks still applicable; New chat clears it.
- **Code actions** (`AiCodeActions`, "ai"): for a diagnostic, Fix (its lines rewritten by the chat model, no tools, with
  ±15 lines of context, the first fenced block proposed as one edit over those lines) and Explain (a chat turn); for a
  selection, Explain, Add doc comments, Refactor (both rewrites, as Fix) and Write tests (a chat turn whose code applies as
  a diff). Commands in the palette: `ai.explainSelection`, `ai.documentSelection`, `ai.refactorSelection`, `ai.writeTests`,
  `ai.fixProblem`, `ai.explainProblem` (at the caret), `ai.newChat`, `ai.showChat`, and `craft.agent` (D's rail item runs
  it, so the Agent item shows while the plug-in is active); the bottom panel's Problems rows show Fix and Explain while a
  provider is registered and run the first action of that kind every registered provider offers. Roslyn's check before accepting is the diff view's (A1).
- **Agent** (`AiAgentPanel`, "ai.agent", desktop): Idrak's `CodingAgent` with `CodingTools` over a working copy of the open
  folder (`AgentWorkspace`: copied once into `agentFolder`, else the temporary folder's `craft-agent`, leaving out
  `agentCopyIgnore`, obj kept so a build needs no restore; the open documents' unsaved text written over their copies), so
  every edit is held: after each `edit_file`/`write_file` the file's change against the workspace (+added −removed, new)
  is listed under CHANGED FILES with Review diff (each file proposed in the editor through `OpenDocumentAsync` and
  `ProposeAsync`, one edit per changed run of lines so hunks are accepted one by one; a new file opens as a new document
  to save; what is accepted leaves the list, a part keeps the rest) and Discard all (the copy goes; the user's files were
  never touched). Commands run through the sandbox `agentSandbox` names in `CommandSandboxes` ("approve-each", P5's);
  none registered under it, a refusing sandbox answers every command with why and the panel says so (the seam: P5
  registers its sandbox under that name and asks the user; the agent panel shows the command waiting, its limits, and
  then its exit or refusal). Header: Turn N of `agentMaxTurns` · tokens · tok/s · Elapsed m:ss (a one-second tick while
  running) · Sandbox; "Waiting for you" while a command waits; Stop; what is sent while it runs is a steering note
  (`CodingAgent.Steer`). `agentMinutes`, `agentMaxTokens`. The model is leased before the tools are made, so the Idrak
  libraries and the families' coding languages are registered by then (`IdrakLibraries.EnsureIncluded`).
- **The window** (Craft.Maui, compiled here with nullable analysis since D moved the page there: `ConversationsView` and
  D's `ShellPage`): the host's conversation panels as the design's chat column (a tab per panel, title, subtitle, header
  buttons, entries drawn by role, status, chips, the input and Send; colours from the theme; a streamed piece replaces
  one entry's view), `CRAFT_CHAT_WIDTH` wide at the body's end after a divider; in the narrow layout a sheet over the
  body's lower 62%. `craft.chat` (Ctrl+Alt+I) shows or hides it; a plug-in's `ShowPanel` brings a panel forward;
  `PanelsView` leaves conversation panels to it. Rebased onto D (a9285d9): the ghost colour joined D's tokens (the light
  one through `ColorContrast.Ensure`).
- **Settings added** (plug-in "ai", `CRAFT_PLUGIN_AI_*`, on the settings page): `modelProvider` (local), `completion`
  (true), `completionModel`, `completionDelayMs` (300), `completionMaxTokens` (64), `completionMaxLines` (8),
  `completionSuffixChars` (512), `completionPrefixChars` (0: from the context), `completionChunk` (1),
  `completionTemperature` (0), `chatMaxTokens` (1024), `chatToolRounds` (6), `chatKeep` (true), `chatFile`
  (.craft/chat.json), `chatHistory` (40), `placement` (semi-global), `placementMaxError` (0.35), `agentSandbox`
  (approve-each), `agentMaxTurns` (20), `agentMinutes` (20), `agentMaxTokens` (2048), `agentFolder`, `agentCopyIgnore`.
- **Tests** (group `AiPlugins`, headless): with a fake text model and a test FIM format its tokenizer has, the ghost text
  appears after the pause, laid out prefix-suffix-middle, is drawn after the caret in the ghost colour with the hint bar,
  and Tab inserts it as one undo step; a keystroke cancels the request and the model samples at most one token after the
  cancellation; Ctrl+Right takes a word, typing through keeps the rest, Escape dismisses, prefix-only is said; the prefix
  start moves once a block; with a fake chat model the reply streams (several entry updates), a get_diagnostics row shows,
  the code block's Apply as diff arrives in the diff view (semi-global placement) and is one undo step, the conversation is
  kept and read back; a code action's diff is accepted as one undo step; the agent's edit is held (the user's file
  unchanged) and listed +1 −1, its command waits in a fake approving sandbox ("Waiting for you"), is refused when denied
  and the agent is told, the steering note reaches the next turn, Review diff accepts the file into the editor in one
  step, Discard all drops the copy; with no sandbox registered under the setting the agent runs no command and says so.
  With `Ai`, `ApiGuard`, `Inventory`, `PluginKit`, `Editor`, `Shell`, `Design`: 83 passed here after the rebase onto D
  (`api/*.txt` and `plans/inventory.md` regenerated: two new registries).
- **Not yet / the owner's:** no real model was run here (the owner's run: ghost text and its rate on a FIM model, a chat
  reply applied, an agent task with each command approved once P5's sandbox is registered); the window itself (the chat
  column, the sheet, the ghost text drawn by Win2D) is the owner's to see; the model chip's tok/s (D's
  `IModelProvider.Status`) is not fed from the AI plug-ins' generation yet; the editor has no
  context menu yet, so the selection actions are palette commands; Roslyn's own code fixes are not offered (Craft.CSharp
  could register a provider); the chat's apply goes to the active document; the agent's working copy is a full copy of the
  folder (fine for a project, heavy for a very large tree: `agentCopyIgnore` trims it).

## P as built (first batch, 2026-10-31)

Idrak `main` 4b296ca first (two gaps, by Idrak's rules): `CommandLimits.Output` (each line of a command's output handed
over while it runs, on the sandbox's reader thread, untrimmed; the process sandbox calls it, the container sandbox passes
it on; a watcher that throws never stops the command) and `SamplerRequest.Tokenizer` (the generator's tokenizer, for a
sampler that decides by the tokens' text; `TextGenerator` and `Seq2SeqGenerator` set it; a kept sampler is reused only
for the same tokenizer). Idrak's `public API`, `custom ITokenSampler`, `command sandboxes` and token-sampler tests: 7
passed on the CPU.

Each plug-in is a folder `plugins/<Name>/` (its project, its `plugin.json`, a summary naming its registry), built on
`Craft.Abstractions` and Idrak alone, pre-installed by Craft.App's one new target (every `plugins/*/*.csproj`, its output
folder copied to `plugins\<Name>\` beside the program, the shared contracts left out) and removable (delete the folder, or
`CRAFT_PLUGINS_DISABLED`). The tests load each one from a copy of its build output through `CraftPlugins.LoadFolder`, into
its own load context, and check it only through the registry its consumer reads.

- **Loader** (`CraftPlugins`): each Idrak library a plug-in carries (Idrak, Idrak.Text, …) is loaded once per process,
  by the first plug-in context that needs it, and handed to every other (`CraftPlugins.SharedIdrakLibraries`): a library's
  registries are statics, so two copies would be two registries and P2's family would be invisible to Craft.Ai's "local"
  provider. `Idrak.Abstraction` stays the host's; other dependencies (Roslyn) stay each plug-in's own (P4 carries its own
  copy of Roslyn's C# parser: its memory to measure on the first Windows run). In the tests the default context already
  holds Idrak, so this sharing rule itself is proven on Windows (the owner's run: P2's family reaches the model provider).
- **P1, the editor's tools** (`plugins/Craft.Plugins.Tools`, split out of Craft.Ai as the design's Agent screen names it):
  the A1 tools with `find_symbol` added on both backings, registered as the tool set "editor" in the new
  `Craft.Abstractions.EditorToolSets` (`IEditorToolSet : IKind`, `Live(host)`, `Folder(path)`; the registry joins every
  set's tools by name, the newest set first). Craft.Ai's MCP server now serves `EditorToolSets.Live(host)` and never
  references the tools' assembly (with no set registered it refuses, naming the plug-in). The tools' settings moved from
  "ai" to "tools": `CRAFT_PLUGIN_TOOLS_READLINES`, `SEARCHRESULTS`, `SYMBOLRESULTS` (new, 50), `SEARCHFILEKB`, `IGNORE`,
  `PATCHFOLDER`, `TOOLSFOLDER`. `find_symbol`: `ISemanticLanguageSession.FindSymbolsAsync` (live: an open document's
  session, the workspace with every open document's unsaved text; `IEditorDocument.SemanticSession` reaches it) and
  `ILanguage.FindSymbolsAsync(root, …)` (a folder; it shares the workspace a session of that folder holds in the same
  process), both Roslyn's `SymbolFinder.FindSourceDeclarationsWithPatternAsync` in Craft.CSharp, ranked exact name, then
  name start, then the rest. The five checks: a new kind ("editor", and any plug-in's set), an override (a newer set's
  `read_document` shadows the editor's; unregistered, the editor's is back), "not registered" for a missing set, loaded
  from its folder, one assembly every host (the app's MCP server lists the 7 live tools and reads an open document; the
  same dll's `[assembly: ToolProvider]` gives the idrak tool 5 folder tools: `idrak tools test Craft.Plugins.Tools.dll`
  here, 5 of 5 answered). Under the idrak tool `find_symbol` answers that it needs a language plug-in (Craft.CSharp
  through `-P`) rather than guessing.
- **P2, StarCoder2** (`plugins/Craft.Plugins.StarCoder2`, an Idrak plug-in package): `Starcoder2ForCausalLM` in
  `PretrainedArchitectures` (from transformers' `configuration_starcoder2.py` and `modular_starcoder2.py`: layer norms with
  biases at `norm_epsilon`, `c_fc`/`c_proj` without a gate, GELU's tanh form, biases on every projection when `use_bias`,
  grouped-query attention, the sliding window in every layer, tied embeddings; `rope_parameters.rope_theta` read too; the
  dropouts noted, not applied; it writes its own config.json back, checked by reading it again) and the FIM format
  "starcoder2" in `FillInMiddleFormats` (BigCode's own evaluation harness, its StarCoder FIM task:
  `<fim_prefix>prefix<fim_suffix>suffix<fim_middle>`, stops `<|endoftext|>` and `<file_sep>`; repository files as
  `<file_sep>path\ntext`; `<file_sep>` is required, so StarCoder (1)'s tokenizer is not taken for it). Hugging Face itself
  was not reachable from here: the tokenizer's own `tokenizer_config.json` is to be checked on the owner's machine (the
  repository-name token `<repo_name>` is not written: its spacing was not verifiable). Checks: "not registered" for the
  format and the architecture before it loads; loaded from its folder; `FillInMiddleFormatSuite` passes; a tiny
  StarCoder2 made by `PretrainedModel.Create` has exactly transformers' tensor names, reads its spec back, and completes a
  FIM prompt. GGUF (llama.cpp's "starcoder2") is not mapped yet.
- **P3, the bracket-balanced sampler** (`plugins/Craft.Plugins.Sampler`): registered as Idrak's `TokenSamplers`
  "default" (the slot shadows the library's, which it wraps and `Unregister` brings back). On a completion in a registered
  FIM layout (the prefix found by the layout's tokens; mode `CRAFT_PLUGIN_SAMPLER_MODE`: fim, always, off) it sets to -∞
  the tokens that would close a brace, parenthesis or bracket the prefix never opened (brackets inside a string or a
  character literal are text; a backslash escapes; a line break ends a literal), on a copy of the last position's logits;
  where nothing is forbidden it passes the logits as they are, and a prompt that is not a completion is not watched (and
  stays recordable as a graph). The vocabulary's table is made once per tokenizer and compacted to distinct token
  behaviours. Checks: `TokenSamplerSuite` exact against the library's; the origin reported, the library's back after
  unregistering; a generation with nothing masked gives the library's very tokens; a masking one closes nothing unopened.
  **Measured** (`benches/Craft.Benches -- sampler`, CPU, 4 threads, a 2-layer decoder of width 64 with a 49,152-token
  vocabulary, 128 tokens, warm the median of 9 interleaved runs): nothing masked, library 819 tok/s warm (719 cold) against
  828 (618 cold), the same tokens; masked every step (384 tokens over 128 steps), library 873 warm (709 cold) against 881
  (639 cold): the warm cost is within the run-to-run noise on this decoder; cold pays the table, 10 ms and 192 KiB (11
  distinct behaviours; 1.7 MiB before it was compacted). On a real model the sampler's share of a step is smaller still;
  the owner's GPU run measures it there.
- **P4, C#-aware placement** (`plugins/Craft.Plugins.Placement`): `TextMatchers` "csharp-tokens" (a new kind): a
  snippet goes where the text holds the same run of Roslyn tokens (whitespace and comments ignored), the run nearest by
  edit distance, with "exact" or "whitespace differed"; no such run, the library's semi-global. With
  `CRAFT_PLUGIN_PLACEMENT_REPLACESEMIGLOBAL=true` it also takes the "semi-global" slot (shadowing the library's, back on
  deactivation). Checks: `TextMatcherSuite` passes agreeing with semi-global on prose; on code it places `Log(y);` at
  `Log( y );` where semi-global takes the nearer letters `Log(x);`; "not registered" before it loads; loaded from its
  folder. A2's chat and code actions reach it by name only (`CRAFT_PLUGIN_AI_PLACEMENT`, "csharp-tokens" by default):
  with this plug-in removed they get "not registered" from `TextMatchers`.
- **P5, the approve-each sandbox** (`plugins/Craft.Plugins.Sandbox`): `CommandSandboxes` "approve-each" (a new kind;
  A2's agent names it, nothing else is shared). Each command goes to the user through the new
  `IEditorHost.AskAsync(EditorQuestion)` (title, the command line, the facts "Program: dotnet (allowed: …)", "Time limit:
  120 s", "Network: on/off", "Folder: the workspace", "Runs in: the process sandbox"; answers Allow once, Allow PROGRAM
  for this run, Deny and tell the agent; a dismissed question is a denial; the MAUI presenter shows an action sheet until
  A2's Agent card), then runs in the inner sandbox (`CRAFT_PLUGIN_SANDBOX_INNER`, "process"; "container" turns the network
  off) with every output line in the panel "Terminal (sandbox)" as it comes (`CommandLimits.Output`, a batch per UI turn,
  the last `PANELLINES` 400 kept), and its end (exit code, elapsed). "For this run" lasts until `sandbox.endRun` or a gap
  longer than `RUNGAPMINUTES` (15). Denied, the agent gets a refused result saying the user said no. With no editor to
  ask, `Check` says why and every command is refused. Checks: "not registered" before it loads; loaded from its folder;
  allow once, deny, dismissed, allowed for the run (not asked again), asked again after `sandbox.endRun`; Idrak's
  `CodingTools` with `Sandbox = "approve-each"` asks first; network off is refused naming "container".
- **Tests**: group `PhaseP` (9), with `Ai` (A2's chat and agent tests now register the tools plug-in's set, as the app's
  loader does), `ApiGuard`, `Inventory`, `PluginKit`, `Plugins`, `Design`, `Shell`, `Editor`, `Semantic`: 103 passed here
  after the rebase onto A2 and D; the API dumps of the five plug-ins added. A2's chat now offers `EditorToolSets.Live`
  (every registered set) and its file chip has its own limit, `CRAFT_PLUGIN_AI_CHATFILELINES` (2000). **Not yet**: the Plug-ins page of the design (registry, slot, guard, cost columns) is phase D's; the
  list the Plug-ins button shows names each plug-in's registry in its summary; the real StarCoder2 on the owner's machine.

## Commands for the owner (once a phase is built)

Given per phase with the exact project names, in the shape of `CLAUDE.md`: one pull, one build, then the targeted runs
with `--no-build`, outputs under `test-output\`.

## Decisions taken on the recommendation (2026-10-31)

1. **E3 includes a minimal window** in `Craft.App` that opens one file, so the owner runs the editor control before the
   shell of E4.
2. **The test runner is copied in shape, not referenced**: `Craft.Tests` gets its own small runner with Idrak's flags
   (`--progress`, `--failures-only`, `--failures-file`) and `CRAFT_FILTER`; `Idrak.Tests.Library` stays unpackaged.
3. **A3 stays held**: the remote model provider, the `kestrel` MCP transport, screen-reader access, iOS and macOS wait
   for the owner.

## The owner's answers (2026-10-31)

1. **Where:** the Craft repository, referencing Idrak as packages, with a switch to a local clone.
2. **UI stack:** .NET MAUI with Craft's own drawn editor control. Avalonia and a Monaco web view were weighed and set
   aside.
3. **Platforms first:** Windows only, until the owner has a good Windows version and says "compile Android"; then
   M adds the Android target, builds and runs. The code is Android-ready from E0. iOS and macOS are held (A3).
4. **C# intelligence:** Roslyn from the start, on both platforms. There is no hand-written lexer unless a measurement
   asks for one.
5. **Plug-ins:** the phase P set, to test Idrak's plug-in points.
6. **Model names:** resolved by Idrak's `ModelSources`; Craft's own contract (`IModelProvider`) only chooses local or
   remote.
7. **MCP first:** the Idrak.Mcp gap (serving from a running app) is fixed before the editor starts (S0).
8. **Rules:** Idrak's working rules apply to Craft, written into this repository's `CLAUDE.md` for every later session.
