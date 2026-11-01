# Plan 1: Craft, a code editor (C# first) with AI plug-ins on Idrak's contracts

**Status (2026-11-01, Craft `main` c3cfaed, Idrak `main` 5b5854d):** E0-E5, S0, K, A0-A2, V, R0, R, D, T, U, U2, the
owner's notes 1-26 and X slices 1-3 are built (see "Now" below). Next, in this order: the bugs B1-B5 of the third
Windows run, then the X restart (note 27, X parts 4 and 5, the rest of X), then phase W (the web languages), then P
batch 2 last, once W is done (the owner: the Idrak plug-ins in the editor are a later priority). See "The owner's third
Windows run (2026-11-01)" at the end.

## Now (2026-11-01)

| # | Phase or item | Status |
|---|---|---|
| 1 | E0-E5, S0, K, A0-A2, V, R0, R, D, T, U, U2 | Built; the owner's Windows checks listed in each "as built" |
| 2 | Notes 1-26, X slices 1-3, the fixes after them (completion 600 ms, search 200 ms, tree click and icons, three test fixes) | Built |
| 3 | B1-B11: the Learning view redesigned to explain and guide, the terminal's single shell and design, "Roslyn ready" to the notification centre and a global loader, hard splitter drags, learning runs that fail (out of memory or any), paint and scroll lag, the WinUI crash, the sample plug-in, learning's Qwen2, Idrak's start-up probe, the agent's file never written to the workspace | Next (planned below) |
| 4 | Note 27, X part 4, X part 5 (split editor first), X part 6 (Search view, Run view), X part 7 (rich context menus), X part 8 (Source Control), X part 9 (26 modern-editor gaps), X part 10 (chat and agent redesigned, expandable) | Parts 6-10 asked 2026-11-01; 27, 4 and 5 lost when the other account's limit stopped its agents (nothing was committed); restart after B1-B5 |
| 5 | X rest: minimap, completion at several carets, peek, measured response times on Windows | After 4 |
| 6 | W: HTML, CSS, JavaScript, TypeScript, Angular, file icons, npm, ESLint (TSLint where a project has it), latest syntax of each | Starts the moment X is done (replaces L) |
| 7 | P batch 2: P6, P9, P10, P8 on `wip/p-batch-2` (45 commits behind `main`), P11 if measured necessary | Last: after W is done (the owner, 2026-11-01: the Idrak plug-ins in the editor are a later priority) |
| 8 | The AI test that fails only on Windows (the agent's working copy goes missing) | The owner's run with `$env:CRAFT_TRACE="1"` |
| 9 | M (Android) | Only on the owner's clear decision (2026-11-01: "needs my clear decision, don't start"); never on a recommendation |
| 9b | A3 (remote models) | Only on the owner's clear decision; never on a recommendation |
| 10 | Does voice dictation go through the diff view? | Only on the owner's clear decision; never on a recommendation |

## The original status (2026-10-31)

Planned 2026-10-31 against Idrak `main` d01a3b1 and Craft `main` 39f26ab; reviewed and verified against
Idrak's code the same day (the "Verified" section). The owner's answers (2026-10-31): the editor lives
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

## The UI never freezes; every operation shows (phase U, added 2026-10-31 at the owner's request)

The owner's window froze ("Not responding") while the agent started on a 7B model that was still loading. The owner
asked: a status bar that shows any operation running, and a UI thread that never freezes. Both are standing rules
(`CLAUDE.md`), and this phase builds what enforces them.

- **Activities** (`Craft.Abstractions`): `IEditorHost.Activities` with `Start(title, kind, cancellable)` returning an
  `IActivity` (`Report(fraction or null, text, rate)`, `Complete`, `Fail`, cancellation token), raised and read on any
  thread, shown on the UI thread through the dispatcher at most once a frame. The status bar shows the newest
  running activity with a spinner or a bar, its text, rate and time left, and "+N" for others; a click opens the list
  of all of them (each with Cancel) and the recent finished and failed ones. Idrak's `ProgressReporters` and the
  model provider's load status feed it; every long operation reports through it: model download and load (stage,
  bytes, speed), agent runs (turn, tokens, tok/s), chat and completion generation, training rounds, code-search
  indexing, Roslyn workspace builds and generator runs, saves of large files, terminal and sandbox commands, plug-in
  loads. Plug-ins report through the same contract (a third party sees and uses the same thing).
- **Notifications (the owner's request, 2026-10-31, first of U):** a notification centre: a bell in the status bar with a
  count, and a window (a panel the user opens and closes) listing every operation and event as an entry: the running
  ones with their live progress and Cancel (the activities above), the finished ones with their outcome and time
  taken, failures with their reason and a way to retry or open the log, and messages plug-ins post
  (`IEditorHost.Notify(title, text, severity, actions)`), each dismissable, grouped by source, kept for the session
  (`CRAFT_NOTIFICATIONS_KEEP`). A short toast shows only what needs the user (a failure, a question); the rest waits in
  the centre. What the right-hand "Models and devices" and "Terminal (sandbox)" side panel says today (model state,
  device, sandbox commands) moves into the centre and the Models page; that side panel goes.

- **The UI watchdog**: a background thread posts a tick through the dispatcher every `CRAFT_UI_WATCHDOG_MS` (100) and
  measures how late it runs; a stall over `CRAFT_UI_STALL_MS` (200) is recorded with the label of the UI work that was
  running (every Craft dispatch carries a label; MAUI's own events are labelled by their handler), shown in the status
  bar ("the window was blocked 3.2 s by …") and in Output, written as a stall report under `CRAFT_CRASH_FOLDER`, and
  counted in the Idrak telemetry tab. A test drives a fake dispatcher and a blocking action and sees the report.
- **The audit**: every UI-thread path is checked and fixed: synchronous waits (`.Result`, `.Wait()`,
  `GetAwaiter().GetResult()`, `SemaphoreSlim.Wait`, `Monitor.Wait`) on or reachable from the UI thread; file and
  network I/O; model loads and the agent's start (the folder copy, the first lease); recovery and autosave writes;
  Roslyn workspace mutations; large allocations that can force a blocking full collection while a model loads (load
  into native or pooled memory, `GCSettings.LatencyMode` and `GCLargeObjectHeapCompactionMode` where measured to
  help, the model loaded in a way that does not stop the UI). A Roslyn analyzer (a small one in the repository, or a
  test that scans the sources) fails the build on a synchronous wait in Craft.Maui, Craft.App and the plug-ins'
  UI-facing code.
- **The owner's freeze reproduced and fixed**: the stacks the owner takes (`dotnet-stack report`) name the blocking
  frame; until they arrive, the 7B int8 load beside an agent start is reproduced on the CPU here with a smaller model
  and the UI watchdog's numbers.

Checks: the watchdog reports a deliberate block and stays silent in normal use (typing, scrolling, a chat stream, a
model load, an agent run, a terminal flood); the status bar lists every activity of a scripted session with its
progress and cancels each; the owner loads a 7B model and runs the agent on Windows with the window answering
throughout, and the watchdog's worst stall is written here.

### Panels and layout (phase U2, added 2026-10-31 from the owner's run)

The owner's run of D's shell found five faults, fixed in U2:

1. **Pages cover the menu.** The settings and the Models page open as modal pages over the window, hiding the title
   bar's menus. They open instead as tabs in the editor area (like a document tab, closable, one each), so the menus,
   the rail and the status bar stay usable.
2. **A new file does not show in the tree.** The tree follows the disk: a `FileSystemWatcher` on the open folder (off
   the UI thread, batched, `CRAFT_WORKSPACE_IGNORE` applied) adds, removes and renames entries; a file the editor
   saves, creates or renames shows at once; the agent's accepted changes too.
3. **The right-hand side panel** (A1's "Models and devices", P5's "Terminal (sandbox)") goes: its content moves into the
   notification centre and the Models page; the sandbox's output shows in the bottom panel's Terminal tab as its own
   terminal tab ("Agent sandbox"), read-only.
4. **The bottom panel** collapses (its tab strip's chevron, Ctrl+J, a click on the active tab) and resizes (a splitter
   drag; the height kept per workspace).
5. **Every panel collapses and resizes**: the explorer, the chat and agent column (a close button and a splitter), the
   bottom panel; one `SplitPanel` control for all, sizes in `.craft/settings.json` per workspace and
   `CRAFT_PANEL_*` defaults, the narrow layout unchanged.

Checks: the menus answer with the settings and the Models page open; a file created by the editor, by the terminal and
by the agent shows in the tree within a second; every panel collapses, resizes and keeps its size after a restart; the
owner's eye on Windows.

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
| T | **Built (2026-10-31, "T as built" below); left: the owner's run on Windows (ConPTY: `dotnet build` in it, a resize, two terminals, a file-only start in the home folder).** The terminal ("The terminal" above): the Terminal tab, the user's shell over ConPTY behind `ITerminalHost`, Craft's own VT grid, several terminals; it starts in the open folder, else (a file only) in the shell's default path, `CRAFT_TERMINAL_CWD` overriding. | The VT tests pass; the start folder is right in the three cases; the owner builds in it on Windows. | D (the bottom panel), K |
| U | **Built (2026-10-31, "U as built" below); left: the owner's 7B load and agent run on Windows with the window answering, and the worst stall written here.** The UI never freezes; every operation shows ("The UI never freezes" above): the notification centre (bell, window, toasts for what needs the user), `IEditorHost.Activities` and the status bar's activity list with cancel, every long operation reporting, the UI watchdog with stall reports, the audit of every UI-thread path and the build check against synchronous waits. | The watchdog stays silent through typing, a model load, an agent run and a terminal flood, and reports a deliberate block; the owner loads a 7B model and runs the agent with the window answering throughout. | the owner's request (first in the next free slot) |
| U2 | **Built (2026-10-31, "U2 as built" below); left: the owner's eye on Windows (the five points, the splitters' drag with a mouse).** Panels and layout ("Panels and layout" above): settings and the Models page as tabs (the menus never covered), the tree following the disk, the right-hand side panel gone (its content in the notification centre and the Models page), every panel collapsible and resizable with sizes kept. | The owner's five points checked on Windows. | the owner's request (beside U) |
| A0 | Idrak: the FIM contract, registry and suite; the coder families already in `Idrak.Text.Plugins` register their formats; the reuse and cancellation bench, with fixes. | The bench shows prefix reuse, a stop within one token of cancellation and flat memory over a thousand requests. | the owner's go (parallel with E1-E2) |
| K | **Built (2026-10-31, "K as built" below).** The plug-in contracts and kit ("Plug-ins first" above): `IEditorPlugin`, `IEditorHost`, commands and key bindings, status bar items, panels, plug-in settings, `plugin.json` and contract versioning, the sample plug-in, the template, `docs/plugins.md`. | The sample plug-in, built outside Craft's solution against `Craft.Abstractions` alone, dropped into `plugins/`, adds its command and status item; removed, the app runs without it. | E3, and the owner's first try of the Windows version; E4's shell is built on it |
| A1 | **Built (2026-10-31, "A1 as built" below).** `Craft.Ai` host on the plug-in contracts (K): the diff-and-accept view, the editor's tools with both hosts, the app serving the live tools through `McpHost` ("http", a setting with address and token shown in Settings), `IModelProvider` "local". | A fake chat model proposes an edit that arrives as a diff and is accepted into one undo step; `idrak tools test` lists the tools from the built assembly; `idrak chat --mcp` against the running app reads an open document. | E4, K, A0, S0 |
| A2 | **Built (2026-10-31, "A2 as built" below; Idrak 6eb11a2 for the agent's steering note). Left: the owner's run on a real model.** The plug-ins: inline completion, chat panel, code actions, agent (desktop). | On a real model the owner names: ghost text appears after the pause and dies on a keystroke; a chat reply applies as a diff; the agent runs a task with each command approved. | A1 |
| P | P1, P2, P3 and P5 first, then P4, P6, P9, P10, then P8 (P7 is built in phase V); P11 only if measured necessary. **First batch built (2026-10-31, "P as built (first batch)" below): P1, P2, P3, P5 and P4. Left: P6, P9, P10, P8, P11.** | Each plug-in's targeted tests and its conformance suite pass; the five checks hold on the Plug-ins page. | A1 (P2 also A0) |
| V | **Built (2026-10-31, "V as built" below); left: the owner codes by voice on Windows (a real recognizer and microphone).** Voice coding on Idrak.Audio ("Voice coding" above): capture, live transcription, dictation, voice commands, spoken code as a diff, read back; P7 is its first part. | A recorded WAV dictates the expected text, runs a command and asks for code that arrives as a diff; the owner codes by voice on Windows; the latency written here. | A2 (the chat model and the diff view), K |
| R0 | **Built (2026-10-31, Idrak f419399).** Idrak: preference learning from episodes ("Idrak library gaps", R0): `PreferencePairSources` ("group", "verdict"), the preference losses by name ("dpo", "orpo", "simpo") over the existing `FineTuningLosses`, iterative preference training usable by `idrak reinforce`, the suite and the bench. | Pairs from a scored rollout train a tiny model with the DPO loss falling and the margin rising; memory flat over many rounds; `idrak reinforce --pairs group --loss dpo` runs. | A0 (a slot in Idrak); before R |
| R | **Built (2026-10-31, "R as built" below); left: the owner's training on a real model on Windows, and the plug-in under `idrak reinforce`.** Learning in the editor on Idrak's reinforcement learning ("Learning in the editor" above): episodes from the editor, the editor-verdict reward with `csharp-build`/`csharp-tests`, tasks from the workspace's tests, background adapter training, promotion and rollback, the Learning panel; and the plug-in as the test of Idrak's reinforcement stack through its C# environment. | A fixture workspace's failing test becomes a task the "csharp" environment rewards; recorded episodes replay; an adapter trains with flat memory and is promoted only when better; the same assembly runs under `idrak reinforce`; the owner trains on a real model on Windows. | A2 (the episodes come from its plug-ins), P5 (the sandbox), R0 |
| M | Android, when the owner says "compile Android": the `net10.0-android` target added to every project in one step; `IEditorCanvas` and `ITextInputAdapter` for the soft keyboard, IME and touch selection; the file picker; Roslyn on Android (APK size, cold start, memory, full and profiled AOT); plug-in loading under AOT, or compiled in. | Every project builds for Android; the owner edits a file on a phone; the numbers are written here. | the owner's word, after a good Windows version |
| W | **Planned 2026-11-01, starts the moment X is done (the owner's word; it replaces L for these languages).** The web languages, each a pre-installed, removable plug-in on the public contracts: HTML, CSS (and .scss, .less), JavaScript, TypeScript, Angular, each with its colours (the latest syntax, the project's version read where it is set), brackets, folding and file icons for its files and related files; npm (package.json scripts through the sandbox, install as an activity); ESLint 9 (flat config) as the linter, TSLint where a project still has `tslint.json`. See "The owner's third Windows run". | Each opens, colours and matches brackets like C#; random edits equal a fresh session; the keystroke and paint numbers written here; removing its folder turns its files to plain text; the owner's check on a web project on Windows. | X done |
| L | Replaced for HTML, CSS, JavaScript and TypeScript by W (2026-11-01); held for any other language. Held, far later: more languages, each its own pre-installed, removable plug-in like C# (an `ILanguage` in `EditorLanguages`, loaded from `plugins/`): TypeScript, JavaScript, HTML, CSS. The parser for each is chosen when the phase starts, measured against the keystroke budget; nothing native (no tree-sitter binaries), so a C# incremental parser or lexer per language. | Each opens, colours and matches brackets like C#; removing its folder turns its files to plain text. | the owner's word, after a good Windows version |
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
## T as built (2026-10-31)

- **Contracts** (`Craft.Abstractions/Terminals.cs`, all additive, contracts still 1.0): `TerminalSize`, `TerminalShell`
  (program and arguments as one command line), `TerminalStartInfo` (shell, folder, size, the whole environment);
  `ITerminalSession` (`ReadAsync` of the output bytes, 0 at the end; `Write` of input bytes; `Resize`; `Exited` with the
  exit code; `Dispose` ends the shell); `ITerminalHost : IKind` (`DefaultShell`, `Start`, `Unavailable` for a platform with
  no shell) in `TerminalHosts` (a `KindTable`; the library's "pipe": System.Diagnostics.Process with redirected pipes, any
  platform, stdout and stderr merged in arrival order through a bounded channel of pooled chunks, Enter's CR given to the
  shell as LF and its LF drawn as CR LF, as a tty's line discipline does; `Resolve("auto")` takes the last registered host
  other than "pipe" that is available, else "pipe"; an unknown name fails with "not registered"). For plug-ins:
  `IEditorHost.Terminals` (a default member, null without terminals) → `IEditorTerminals` (`All`, `Active`,
  `Open(EditorTerminalRequest)`: folder, name, shell, environment additions, show) → `IEditorTerminal` (`Title`,
  `WorkingDirectory`, `Exited`, `Write(text)`, `Show()`): writing and showing only, never the user's keys; the plug-in's
  scope (`EditorHost.PluginScope`) wraps them so a deactivated plug-in's handles write nothing and open nothing, the
  terminal staying the user's. `EditorKey.Backquote` added at the enum's end ("`" in a chord's text).
- **Screen and parser** (`Craft.Editor.Terminal`, UI-free, AOT-compatible, tested headless; Decision: in Craft.Editor, not a
  new library, so no new project, dump or reference): `TerminalCell` (12 bytes: a scalar, wide/tail bits and the
  renditions in one `uint`, two packed colours: default, 256 indexed, or true colour, resolved only when drawn so a theme
  change recolours the screen); `TerminalRow` (cells, wrapped flag, a version per write); `TerminalScreen`: the screen is
  the last rows of a ring of `CRAFT_TERMINAL_SCROLLBACK` + rows lines, so a full-screen scroll moves the ring's start and,
  once full, reuses the oldest line's row (0 bytes per line, memory flat); a scroll region rotates its rows; the alternate
  screen has its own rows and no scrollback. The parser (Paul Williams' states over bytes): printable ASCII found with a
  `SearchValues<byte>` and written a row stretch at a time; UTF-8 kept across reads, bad or cut sequences as U+FFFD;
  `TerminalCharWidth` (Unicode 15's East Asian W/F and emoji presentation ranges, a binary search; marks, ZWJ and
  variation selectors zero-width); C0; ESC 7/8/D/E/M/H/c/=/>, G0/G1 with DEC line drawing, DECALN; CSI @ A B C D E F G H I J
  K L M P S T X Z ` a b c d e f g h l m n r s u, DECSET/DECRST 1, 6, 7, 25, 47, 1047, 1048, 1049, 1000-1003 (kept), 2004,
  SGR (bold, faint, italic, underline incl. 4:0, blink, inverse, hidden, strike, 30-37/90-97/40-47/100-107, 38/48 in ';'
  and ':' forms incl. a colour space, 58 read and dropped), DSR 5/6 and DA/DA2 answered through `Respond`, DECSTBM,
  SCOSC/SCORC, DECSCUSR, DECSTR; OSC 0/2 title, 7 (file URI) and 9;9 working folder; DCS/SOS/PM/APC skipped. OSC strings are
  capped at 4 KB and titles at 256 characters. Resize: narrower rows are cut (no reflow: ConPTY repaints); fewer rows drop
  the blank rows under the cursor first, then move the top rows into the scrollback; more rows bring lines back from it.
  Not done (recorded): combining marks are dropped rather than joined to their base; mouse reporting is kept as a mode,
  not sent; no reflow.
- **Drawing** (`TerminalRenderer`, `TerminalPalette`): only the visible rows, through `IEditorCanvas`: the ground, each
  row's background runs, its text runs (one string per change of colour or rendition, blanks inside a run kept, a wide
  character its own run at its cell), underline and strike lines, the selection, the cursor (block, underline or bar;
  hollow without the keyboard). A row's runs are kept on the row while its version and the palette are the same, and
  dropped when the row leaves the view (memory follows the screen, not the scrollback). The palette is the theme's
  tokens: ground the panel's surface, text the interface's text, cursor the caret, selection the theme's; red/green/yellow
  the error/success/warning tokens, blue the keyword's colour, magenta and cyan the design's hues; each basic colour moved
  to 4.5:1 on the ground (checked for both themes in the tests); bold basic colours drawn bright, faint half-way to the
  ground. The code font (`CRAFT_FONT`, its fallback) at `CRAFT_TERMINAL_LINE_SPACING` (1.3).
- **Sessions and keys** (`TerminalInstance`): the output read on the thread pool (64 KB reads) and parsed in 16 KB slices
  under the screen's lock, so a frame never waits long; one redraw request per burst through the host's dispatch; the
  replies written back; the exit shown on the screen ("[the shell ended with exit code N]") and in the tab. Scrolled back,
  the view keeps its text while output arrives; a key or typed text returns to the bottom. `TerminalKeys`: arrows,
  Home/End (application form after DECCKM), Insert, Delete, Page Up/Down, F1-F12 with xterm's modifier parameter;
  Enter, Tab, Shift+Tab, Backspace (Ctrl: BS), Escape; Ctrl+letter as its control character, Ctrl+Space NUL; Alt as ESC
  before the key (the platform's echo of the same character is dropped once); Ctrl+Alt+letter left to the text (AltGr).
  Paste: line breaks as CR, inside ESC[200~/ESC[201~ when the shell asked (2004), an ESC[201~ inside taken out.
- **Start folder, shell, environment** (`TerminalLaunch`, the owner's rule, pure and tested): the folder chosen (the
  explorer's "Open in terminal", a plug-in's request) first, then `CRAFT_TERMINAL_CWD`, then the open folder's root, else
  (only files open, or nothing) the home folder, the shell's own default; a folder that does not exist is passed over and
  logged. Shell: `CRAFT_TERMINAL_SHELL` with `CRAFT_TERMINAL_ARGS`, else the host's default (ConPTY: pwsh on the PATH with
  PATHEXT, else Windows PowerShell, else `%ComSpec%`, each with `-NoLogo` for the PowerShells; pipe: `%ComSpec%`, else
  `$SHELL`, else /bin/sh), `CRAFT_TERMINAL_ARGS` replacing the default's arguments. Environment: the app's, then
  `CRAFT_TERMINAL_ENV` (`NAME=value|...`, `${NAME}` the app's value, an empty value removes), with `TERM_PROGRAM=Craft` and
  `COLORTERM=truecolor`; names ignore case on Windows. Decision: a workspace's `.craft/settings.json` sets none of
  `CRAFT_TERMINAL_SHELL`, `_ARGS`, `_ENV`, `_HOST` (what runs is code), as with the plug-in folders; it may set `_CWD`.
- **Commands and keys** (`TerminalManager`, on K's contracts, taken back on dispose): `terminal.new` (Ctrl+Shift+`),
  `terminal.toggle` (Ctrl+`: shows the Terminal tab, opening a terminal if none; hides the panel when it shows),
  `terminal.next`, `terminal.previous`, `terminal.kill` (ends the shell, closes its tab), `terminal.clear` (scrollback and
  screen, the prompt's row kept at the top), `terminal.copy` (Ctrl+Shift+C), `terminal.paste` (Ctrl+Shift+V),
  `terminal.runSelection` (the selection, or the caret's line, sent with Enter to the active terminal, opening one if
  none); in the View and Run menus. The terminal view's keys go through `EditorHost.HandleKey` with the terminal as the
  "editor": the user's bindings first; the chords of the `terminal.*` commands and of `CRAFT_TERMINAL_EDITOR_COMMANDS`
  (default the palette, quick open, the panel, settings) run their commands; every other key goes to the shell (so Ctrl+R,
  Ctrl+W, Ctrl+N are the shell's while it has the keyboard).
- **Windows** (`src/Craft.Maui/Platforms/Windows/ConPtyTerminalHost.cs`, "conpty", `LibraryImport`, `AllowUnsafeBlocks`
  in Craft.Maui): two anonymous pipes, `CreatePseudoConsole` at the terminal's size, `InitializeProcThreadAttributeList`
  and `UpdateProcThreadAttribute(PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE)`, `CreateProcessW` with `EXTENDED_STARTUPINFO_PRESENT`
  and a sorted Unicode environment block, `STARTF_USESTDHANDLES` with no handles (the child takes the pseudo-console's, never
  one this process holds); the program looked up on the PATH with PATHEXT; the pseudo-console's ends of the pipes closed
  after the start; the exit waited on by the thread pool (`RegisterWaitForSingleObject`), then `ClosePseudoConsole` while
  the reader drains the output; dispose terminates the shell and closes the pseudo-console (which ends what is attached to
  it). `WindowsShellPlatform.TerminalHost` gives it; `IShellPlatform.TerminalHost` (a default member, null) is how the
  shell registers the platform's host while it runs. The Windows key map gained VK_OEM_3 as `Backquote`.
- **MAUI** (`Craft.Maui/TerminalView.cs`, built here on net10.0): `TerminalDrawable` (the font measured inside the first
  frame as the editor's, drawn under the screen's lock), `TerminalView` (a ScrollView over a spacer of the scrollback and
  the screen, so the platform's scroll bar and wheel scroll back, the GraphicsView kept over the viewport; the size in cells
  from the measured viewport, sent to the screen and the session; a drag selects; the keyboard through the platform's
  `ITextInputAdapter` into `TerminalManager.Input`; the cursor blinks at `CRAFT_CARET_BLINK_MS`), `TerminalPanelView` (the
  Terminal tab: the terminals' tabs, each its title, "(ended N)" once it ended, ✕ to kill; "+"; with none open, where the
  next one starts and why). D's placeholder in `BottomPanelView` gave way to `TerminalContent`; `ShellPage` makes the
  manager, sets `EditorHost.Terminals`, shows and focuses the tab on the commands; the explorer's rows have a context
  menu "Open in terminal" (a file's row opens its folder).
- **Settings added** (each a `CRAFT_*` variable, on the settings page): `CRAFT_TERMINAL_SHELL`, `CRAFT_TERMINAL_ARGS`,
  `CRAFT_TERMINAL_ENV`, `CRAFT_TERMINAL_CWD`, `CRAFT_TERMINAL_SCROLLBACK` (5,000), `CRAFT_TERMINAL_HOST` (auto),
  `CRAFT_TERMINAL_LINE_SPACING` (1.3), `CRAFT_TERMINAL_EDITOR_COMMANDS`. Each is read when a terminal opens.
- **Measured** (`dotnet run -c Release --project benches/Craft.Benches -- terminal`, the same 4-core container; a million
  lines of build-like output, 82.2 MB with colours, a progress line rewritten with CR and CJK names, fed in 64 KB reads to a
  120x30 screen with 5,000 lines of scrollback): first read (cold) 21 ms; warm 108.6 MB/s, 1.32 M lines/s; 6.4 MB allocated
  in all, every byte of it the scrollback's rows made once as the ring fills, 0 bytes after the first 100,000 lines; the
  screen's managed memory 7.36 MB after 100,000 lines and 7.38 MB after the million. Frames on a counting canvas, 30 rows:
  still frame median 0.9 us, p99 1.9 us, 0 strings, 0 bytes; a frame after one new line (feed and draw) 4.3 us, p99 12.5
  us, 3 run strings, 352 bytes; a full repaint (a new palette) 55 us, p99 143 us, 87 run strings. The editor bench
  (`-- editor`): keystroke to render median 6.8 us, p99 25.2 us, 1,032 bytes and 1 run string per keystroke (D: 6.6 us,
  24.2 us, 1,028 bytes, 1); blink 5.1 us, 0 bytes (D: 5.0); scroll 5.5 us (D: 5.3): unchanged within the container's noise
  (the editor's path is untouched; only an `EditorKey` value was added).
- **Tests** (group `Terminal`, 15): SGR colours and renditions in both forms; cursor moves and both saved cursors; erase,
  insert and delete of characters and lines, the erase's background; wide characters (CJK, emoji, the last column, a half
  written over, UTF-8 split byte by byte, bad bytes, a combining mark); the alternate screen (1049, 47); scroll regions, RI,
  SU, SD and the scrollback; wrap, tabs (HTS, CBT, TBC), DECAWM off and the selection's text across a wrap; titles, folders
  (OSC 7 incl. a Windows path, 9;9), modes, DECSTR, DSR/DA replies, BEL, skipped strings, DEC line drawing; the scrollback
  cap with 0 bytes over 100,000 lines once full, the oldest kept line, ED 3 and Clear; resize both ways and narrower; the
  renderer (only the visible rows, a still frame making no string, the selection's fill, a wide character's run, both
  palettes' contrast); the keys' bytes and paste; the start folder in its cases (a folder open, files only, the variable,
  the explorer's folder, a missing one passed over); the settings, the environment, the workspace's refusal, the hosts'
  registry, PATH and PATHEXT lookup; a session through "pipe" that reads typed input, answers and reports exit code 3; the
  manager (commands and chords, the start in the open folder, a plug-in opening and writing through `IEditorHost.Terminals`,
  previous, run selection, copy, kill, everything taken back). With `Shell`, `Editor`, `PluginKit`, `ApiGuard`, `Inventory`,
  `Design`: 79 passed here on the first run, and 81 after the rebase onto A2 and P (their new tests in those groups).
- **Not compiled here** (Windows only): `src/Craft.Maui/Platforms/Windows/ConPtyTerminalHost.cs` (compile-checked here in a
  scratch net10.0 project with `WINDOWS` defined, nullable analysis, warnings as errors and the LibraryImport generator;
  not run), the `TerminalHost` property in `WindowsShellPlatform.cs`, the `Backquote` line in `WindowsTextInputAdapter.cs`,
  and the `#if WINDOWS` block of `TerminalView.OnGraphicsHandlerChanged`. Craft.App is unchanged.
- **Not yet**: split terminals; reflow on resize; mouse reporting to full-screen programs; combining marks joined to their
  base; a hyperlink (OSC 8) or clipboard (OSC 52) from the shell; Windows' "win32-input-mode" (ConPTY translates VT keys);
  find in the terminal; the narrow (phone) layout has no terminal (phase M decides: a phone may report it has no shell
  through `ITerminalHost.Unavailable`).

## V as built (2026-10-31)

- **Craft.Voice** (`plugins/Craft.Voice`, in `Craft.slnx`; P7 is its first part): a pre-installed, removable plug-in
  ("voice", `plugins\Craft.Voice\` beside the program through P's generic `CraftPreinstalledPlugins` target, so
  Craft.App.csproj did not change; `CRAFT_PLUGINS_DISABLED=Craft.Voice` turns it off), built on `Craft.Abstractions` and
  Idrak (Idrak.Audio, Idrak.Data for Hugging Face names, Idrak.Gpu for the GPU devices, Idrak.Audio.Plugins as the speech
  families registered by the setting `idrakPlugins`: no family is named in Craft). It reaches the AI plug-in only through
  public contracts: `ModelProviders` for the chat model, the host's panels for the last reply, and reads the "ai" plug-in's
  `model` setting when its own `codeModel` is empty. plugin.json capabilities: microphone, input, model, network.
  Android/AOT: Idrak.Audio and Idrak.Audio.Plugins are AOT-compatible; the capture and playback load the system's audio
  library at run time ("aaudio" for phase M).
- **The Windows lesson** (a library in a plug-in's own folder registers no built-ins): `Craft.Abstractions.IdrakInPlugins`
  (new, public) holds what Craft.Ai's `IdrakLibraries` did, for any plug-in: `IncludeLibraries(assembly)` hands each
  Idrak library beside the plug-in, from its own load context, to `IdrakPlugins.Load`; `IncludePackages(assembly, names)`
  the packages a setting names. Craft.Ai's `IdrakLibraries` now calls it; the voice plug-in calls both at activation. The
  test runs the test program again with `--probe-plugin FOLDER` (a fresh process, nothing of Idrak.Audio touched before
  it) on a copy of the plug-in's output folder: Idrak.Audio lives in the `Craft.Voice` context; `AudioCaptures` holds
  aaudio, alsa, winmm, `AudioPlaybacks` the same, `SpeechFamilies` IdrakAudio and WhisperForConditionalGeneration,
  `SpeechSynthesisFamilies` IdrakAudio and VitsModel, `LiveCommitPolicies` agreement; the "audio" chat part was
  registered by the plug-in's own copy; Idrak.Gpu's Cuda, Vulkan and Hip providers are registered (so "auto" finds the
  GPU, the owner's Craft.Ai finding of 3b7f0c7); the commands and both keys are there.
- **Contracts added** (Craft.Abstractions, additive, contracts still 1.0): `IEditorHost.TypeText(document, text)` (the
  user's own input at the caret over the selection, one `EditKind.Command` undo step; for an input method such as
  dictation, never a model's text, which stays a diff through `ProposeAsync`: Decision, dictation is the user typing by
  voice, not a plug-in's edit), `IEditorHost.Select(document, selection)` (a caret move the editor shows; `EditorHost`
  raises `SelectionRequested`, which ShellPage answers on the tab's `EditorModel` with `EnsureCaretVisible`; `TypeText`
  raises it too, so the view's caret follows the host's and a replaced selection is not left selected),
  `IEditorHost.Panels`, `IEditorHost.KeyPressed` (`EditorKeyEventArgs`, raised by `HandleKey` before routing, nothing
  made while no one listens), `PluginCapabilities.Input` and `Microphone`, and the registries `SpokenSymbols`
  (`SpokenSymbol`: words, text, language or every language, spaces before and after; `For(language)` gives the
  language's own first; `Version` for the dictation's cached table) and `SpokenCommands` (`SpokenCommand`: a phrase that
  runs a command id, or a phrase with `#` that runs an action with the number said). Craft.CSharp registers C#'s symbols
  ("arrow" and "lambda" `=>`, "null coalescing" `??`, "nullable", "open/close generic") over the every-language table.
  `IEditorDocument.SemanticSession` (P's) gives the hover.
- **Capture and live transcription**: `voice.listen` (Ctrl+Shift+M, `key`) opens the microphone (`capture`: auto is
  `AudioCaptures.ForThisMachine`, "winmm" on Windows; `device`, `sampleRate`), `voice.code` (Ctrl+Shift+Alt+M, `codeKey`)
  opens it for spoken code; `mode` toggle (the key again closes it) or push (one phrase a press: the editor's keys have
  no key-up, so a held key is not used). The recognizer (`recognizer`, a name through Idrak's `ModelSource.Resolve`,
  `SpeechModels.Load` on `computeDevice`, auto by default) loads once off the UI thread with its stage and seconds in
  the status bar (`voice.loadRecognizer` loads it ahead), is shared while in use and let go `keepAliveMinutes` after.
  Idrak's `LiveTranscriber` runs over the input on the thread pool (`stepSeconds`, `windowSeconds`, `silenceSeconds`,
  `commitPolicy`, `agreement`, `language`); each block passes once through the listener for its level (the loudest in
  dBFS every `levelMs`, eight bars: "● Mic ▅" in the status bar while it is open, a click closes it) and, unless `codeAs`
  is text, into a fixed ring of the last window's samples. A2 has no push seam for ghost text (its ghost is a provider
  the editor asks after typing), so the live words are a status item: the committed words, then the open ones in
  brackets. Stopping ends the input (the phrase being said is finished first); the device opens, closes and the models
  are freed off the UI thread (the rule of e04652c), cancellations with `CancelAsync`.
- **Dictation**: each phrase (a segment the transcriber ended) in the order said: `codePrefix` ("code") sends the rest as
  spoken code; `dictatePrefix` ("type") types the rest; a phrase naming a command runs it (`commandPrefix`, empty: any);
  anything else is typed at the caret through `TypeText`, one undo step a phrase. The text: the longest spoken symbol
  first ("double equals" before "equals"), the symbol's spaces, number words as digits (`digits`: "forty two", "one
  hundred and five"; a unit after a unit ends a number), the recognizer's own punctuation dropped (`punctuation` spoken,
  or heard), a space before the first word when the caret follows a word.
- **Voice commands**: every registered command's title without its "Area: " part, or whole ("save" runs "File: Save",
  "format the document" "Edit: Format document"; "the", "a", "an", "please" left out), and the `SpokenCommands`: "go to
  line #" and "line #" (through `Select`), "next error"/"previous error" (the plug-in's `voice.nextProblem` and
  `voice.previousProblem`, "Go: Next problem" in the palette), "read selection", "read that", "read error", "read hover",
  "read reply", "stop listening", "stop reading".
- **Spoken code**: the request, the selection (or "at the caret"), the file's name and language and `codeContextLines`
  lines around the caret go to the chat model of `modelProvider` (local) and `codeModel` (empty: the AI plug-in's
  `model`); its first fenced block is proposed over the selection or at the caret through `ProposeAsync` (placement and
  origin "Voice · MODEL · heard the sound | the transcript" in the diff view). `codeAs` auto sends the phrase's sound
  itself (a WAV through Idrak's "wav" encoder as a `ChatAudio` part) to a model whose `PartKinds` hold "audio", else the
  transcript; sound refuses a text-only model by name; text never sends sound. `codeMaxTokens`.
- **Read back**: `voice.readSelection` (the caret's line when nothing is selected), `voice.readProblem` (the problem at the
  caret, else the next), `voice.readHover` (`IEditorDocument.SemanticSession.GetHoverAsync`), `voice.readReply` (the last
  assistant entry of a conversation panel); `synthesizer` through `ModelSource.Resolve`, `SpeechSynthesisModels.Load`,
  Idrak's `VoicedSynthesizer` (`voice`: a speaker or an installed voice), streamed block by block to `playback` (auto:
  `AudioPlaybacks.ForThisMachine`; `outputDevice`) off the UI thread; the status bar says "Speaking … · any key stops";
  any key (`KeyPressed`, subscribed only while speaking) or `voice.stopReading` cancels it within a block.
- **Measured** (group `Voice`, the 4-core container, a fake recognizer, 160 phrases through `PcmStreamInput` at 40 times
  real time, a synchronous dispatcher so the time is the pipeline's own): from the transcriber's end of a phrase to the
  text in the document first 0.33 ms, median 0.24-0.27 ms, p99 0.7-7.8 ms over two runs (the p99 is a GC or the thread
  pool); managed memory after 40 and after 160 phrases 11,485 and 11,500 KiB (133 bytes a phrase, the document's own
  text and undo steps included; an earlier run read -608 bytes a phrase): flat. 679 recognizer calls for 160 phrases (the
  open phrase read again every step). The first words on screen with a real recognizer are the recognizer's step plus its
  time per step: the owner's run.
- **Tests** (group `Voice`, headless): a generated WAV (a tone a phrase) through a test capture registered in
  `AudioCaptures` and Idrak's `PcmStreamInput`, paced, with a fake recognizer (the phrase by its tone, the share of its
  words heard while it sounds): three phrases dictate "Var total = 0;\nPrint(total);" as three undo steps, the open words
  shown in brackets, the microphone's mark with its level, gone when it closes; "Save." runs a command titled "File: Save",
  "Go to line two." moves the caret (the editor told), "Format the document." runs "Edit: Format document", "Type save."
  types it, "next error" with no problem says it cannot run; a spoken-code phrase arrives as a diff from a fake chat model,
  accepted as one undo step, the transcript to a text-only model and the phrase's own sound (its tone's frequency checked)
  to one that hears; read back plays block by block through a fake playback and any key stops it, the output closed,
  nothing played after; the symbols, numbers, C#'s own table and command matching; the measure above; the plug-in from a
  copied folder in a fresh process (above). With `Editor`, `PhaseP`, `Ai`, `AiPlugins`, `PluginKit`, `Plugins`, `ApiGuard`
  and `Inventory`: 88 ran, 87 passed on the first run; the one failure was the probe's check that the default context
  holds no Idrak.Audio, which the test program's own folder (unlike the app's) does put there through Idrak's
  LibraryDefaults: replaced by the check that the "audio" chat part came from the plug-in's own copy; `Voice`,
  `ApiGuard`, `Inventory` then 14 of 14. `api/Craft.Voice.txt` new; `api/Craft.Abstractions.txt`, `api/Craft.Editor.txt`
  and `plans/inventory.md` (SpokenCommands, SpokenSymbols) regenerated.
- **Not compiled here** (Windows only): nothing; ShellPage (`Craft.Maui`, net10.0) was built here with nullable
  analysis. No line under `Platforms/Windows` or in Craft.App changed.
- **Not yet / the owner's**: no real recognizer, synthesizer, microphone or speaker was run here (the owner's run: a
  Whisper checkpoint through `recognizer`, a VITS one through `synthesizer`); the first-words latency and the accuracy on
  code words with a real recognizer (a keyword-biasing contract in Idrak only if measured necessary, as the plan says);
  push mode holds one phrase a press, not a held key (no key-up in the editor's keys); the live words are in the status
  bar, not ghost text (A2 has no push seam); the casing of dictated words is the recognizer's ("Var"), no setting yet;
  the hover read needs a language with a semantic layer; P7's conformance suite (`SpeechRecognizerSuite`) is for a real
  recognizer and runs in Idrak, not on Craft's fake.
## R as built (2026-10-31)

No Idrak gap was met: R0's `PreferencePairSources`, `PreferencePairs.Build`, `PreferenceLosses`, `LanguageModelPolicy.UpdatePreferences`,
`ReinforcementTrainer`, `RolloutCollector` and Idrak.Reinforcement.Plugins' "csharp" environment (`CodingSuite`, `csharp-tests`,
`csharp-build`) are used as they are. Idrak is unchanged.

- **The plug-in** (`plugins/Craft.Plugins.Learning`, plug-in "learning", pre-installed by Craft.App's generic plug-in copy
  target, removable: delete the folder or `CRAFT_PLUGINS_DISABLED=Craft.Plugins.Learning`), on `Craft.Abstractions` and Idrak
  alone. It carries Idrak.Reinforcement, Idrak.Reinforcement.Plugins, Idrak.Text, Idrak.Text.Plugins, Idrak.Data and
  **Idrak.Gpu** (training is the heaviest device user: the "auto" device finds the GPU only with it), and hands every Idrak
  library of its folder to `IdrakPlugins.Load` when it registers (`LearningLibraries`, the pattern of Craft.Ai's
  `IdrakLibraries`; Craft's loader shares one copy of each library between plug-ins), then registers the "csharp" coding
  family (`CSharpCodingPlugin`, with "coding" and the C# coding language it requires), taken back on unregistering. Android
  (phase M): no reflection of its own (JSON written and read by hand); the C# environment needs the .NET SDK, which a phone
  lacks.
- **Contracts added** (Craft.Abstractions, additive, contracts still 1.0): `AiEvents` (a registry of `IAiEventSink`;
  `AiAnswer`, `AiAnswerKinds`, `AiVerdict`, `AiVerdicts`, `Listening`, `NewId`), `ModelAdapters` (a registry of
  `IModelAdapterSource`, `For(model)`, `Changed`), `ModelRequest.Adapter`, `IEditorHost.DataFolder` (the options' data
  folder) and `IEditorHost.OpenExternalAsync` (`EditorHost.OpenExternal`, set by `ShellPage` to MAUI's `Launcher`). Craft.Ai
  raises the events only while a sink listens: inline completion (`Answered` with the laid-out prompt, the shown ghost,
  the snapshot and caret, at the stream's end), chat's Apply as diff and the code actions' rewrites (`Answered` before the
  diff, `Judged` after it: accepted with where the edit lies, rejected with the original text as kept), the agent (one answer
  per run at its first review; accepted at the first accepted file, rejected on Discard all). `AiModels` asks
  `ModelAdapters.For` per lease (the adapter is part of the lease's key) and releases its leases on `Changed`; the "local"
  provider attaches `ModelRequest.Adapter` with `PretrainedModel.LoadAdapter` after the load (the base weights untouched).
- **Consent**, exactly as the plan's section. Two questions through `IEditorHost.AskAsync`, never bundled: the first
  ("Learn on this machine?") the first time an AI plug-in answers (the answer that asked is not recorded), with what is
  recorded, where (the folder; "Open the folder first" opens it through the host and asks again), how much (`capMb`), how
  long (`keepDays`), what training runs and when (idle or on demand, the compute limits), that a learned model only
  proposes, how to withdraw; answers Yes in all workspaces (the default scope offered first) / Yes in this workspace only /
  No / Open the folder first; a dismissed question decides nothing. The second ("Share what was learned?") only after a yes
  and only once `shareTo` names a destination (no default), naming exactly what would leave (`shareWhat`: scores, episodes,
  adapter), to where, the preview before each share, what is always left out, how to withdraw. Each answer is stored in
  `consent.json` (the learning folder) with its time, scope ("all" or the workspace) and the text's version (`learn-1-` or
  `share-1-` and a hash of every word shown): a changed text (folder, cap, what is recorded, destination, …) reads as
  "changed", records nothing and asks again on the next answer. `CRAFT_PLUGIN_LEARNING_CONSENT` and `_SHARING` take ask or off
  only ("on" is refused naming the variable: a setting can only turn consent off). Withdrawal is one command (the panel's
  first line, `learning.withdraw`, the Plug-ins menu): recording stops at once (the answers waiting are dropped), sharing too,
  and the user chooses Keep or Delete (the workspace's episodes, tasks and adapters; the bytes freed are said). The status
  bar shows "● Learning" while recording, "↑ Sharing" while a share runs, the training line while one runs.
- **Episodes** (`LearningSession`, `EpisodeStore`): only while the first consent holds and the kind is in `record`. A
  completion's verdict is read from the document: the ghost's text inserted at the caret (all, or a word at a time, or
  taken while it still streamed) is accepted, then followed through every change (edits inside it: edited, with the text
  after and how far, a normalized edit distance; an undo inside it: undone); text typed there instead is rejected, the typed
  text kept; a new completion in the same text passes over the old one. Chat, code actions and the agent are told. A verdict
  settles `settleSeconds` (30) after its last edit, or when its document closes; then one JSON line is appended to the
  workspace's `episodes.jsonl` under `<data folder>/learning/workspaces/<name>-<hash>/` (or `folder`), at most `capMb` (the
  oldest go first), `keepDays`; the file's path relative to the workspace, the context, the answer, the ids when given, the
  verdict, the kept text (`PreferenceInfoKeys` verdict, kept, kept_step 0 when replayed). The Learning panel (`learning.panel`,
  `learning.show`) lists the two switches with their scope, time and text version, the folder (opens it), the episodes'
  count, size and verdicts and the last ten, Export (JSON lines under `exports/`), Delete (asked first, bytes said), Train now
  or Stop, Make tasks from the failing tests, Mark the test at the caret, the adapter versions with their held-out scores,
  Roll back, Delete an adapter version. Every file read, write, delete, plan and training runs off the UI thread (the rule of
  e04652c) and comes back through `Dispatch`; the consents are read off it at activation (nothing is asked before).
- **Rewards**: "editor-verdict" registered in Idrak's `RewardFunctions` (options accepted 1, edited 0.5 scaled by how far,
  rejected −1, undone −1, none 0; `verdictWeights`), Idrak's reward suite passes; the tasks' reward is `taskReward`
  ("csharp-tests:1,csharp-build:0.5"), their builds and tests in the sandbox `sandbox` ("approve-each", P5's, by default).
- **Tasks from the workspace's tests** (`WorkspaceTasks`): `dotnet test --logger trx` through the sandbox, its TRX read
  (failed `UnitTestResult`s and their messages), plus the tests marked at the caret; each a `task.json` (prompt naming the
  test and why it fails, language csharp, `base` the workspace, no commands: the C# variant's restore, build and test, the
  test projects protected) under the workspace's `tasks/` and `tasks-held/` (the held-out share, by a hash of the name), so
  `idrak reinforce MODEL --env csharp --env-option suite=FOLDER` reads them too (the command shown in the panel's message).
- **Training** (`LearningTrainer`): on demand (`learning.train`) or idle (`schedule` idle, `idleMinutes` without typing or an
  answer, `minEpisodes` new ones), off the UI thread, the model `model` (else the AI plug-in's) loaded by Idrak's model sources
  on `device` under `threads`/`cpuMemoryMb`/`gpuMemoryMb` (Idrak's ComputeResources), the current adapter loaded first so
  training goes on from it. "preference": the episodes as one-step trajectories (a completion's prompt as the raw text the
  model saw, the others in the chat template; the answer's ids with the template's end of turn, as Idrak's pairs end a kept
  text; the editor-verdict reward), the held-out share set aside, pairs by `pairs` ("verdict"), `steps` updates of `loss`
  ("dpo", `lossOptions`, `reference`) through `LanguageModelPolicy.UpdatePreferences`, LoRA rank `rank`, `learningRate`,
  `batchTokens`. "tasks": Idrak's `ReinforcementTrainer` on "csharp" over the tasks (`algorithm` grpo, `estimator`; "both"
  adds the trainer's "group" preference rounds, then the verdict rounds). Live progress (stage, step of steps, loss, the
  device, time left) in the status bar and the host's progress; Stop ends it at the next step, nothing written.
- **Promotion** (`AdapterVersions`): every training writes `adapters/vN` (PEFT layout and `version.json`: base model, episodes,
  pairs, held-out pairs, steps, first and last loss, the held-out score before and after, promoted, why). The score is the mean
  log p(chosen) − log p(rejected) over the held-out pairs, measured before the first step (the current adapter, or the base
  model) and after the last; promoted only when after > before + `promotionMargin` (tasks: the held-out suite's mean return).
  `promotions.txt` holds the promotions in order: Roll back goes one step back (to the previous, then the base model);
  versions stay until deleted. A promotion or rollback raises `ModelAdapters.Changed`, so chat, completion and the agent load
  the model with the new adapter on their next request.
- **Sharing** (`ShareDestinations` in `Craft.Plugins.Learning.Abstractions`: "folder", "https", or a plug-in's; `shareTo`
  "kind:target" or an https address): both consents needed; the plan (`SharePlan`: every item's name, size, a preview, the
  workspace paths it touches; what was left out and why) is the confirming question's details; `.gitignore` and
  `.craft/learn-ignore` (git's rules: comments, negation, anchors, folders, `*`, `?`, `**`) and a secrets scanner (private
  keys, AWS, GitHub, Slack, OpenAI-style and Google keys, passwords and keys assigned in code, plus `secretPatterns`) leave
  items out; exactly the confirmed items are sent.
- **Settings** (plug-in "learning", `CRAFT_PLUGIN_LEARNING_*`, the K naming every plug-in uses; the plan's `CRAFT_LEARN_*`
  became these): consent, sharing, record, folder, capMb (64), keepDays (90), settleSeconds (30), verdictWeights, taskReward,
  sandbox (approve-each), schedule (idle), idleMinutes (10), minEpisodes (20), trainer (preference), model, device (auto),
  threads, cpuMemoryMb, gpuMemoryMb, pairs (verdict), loss (dpo), lossOptions, reference (previous), algorithm (grpo),
  estimator, steps (40), rank (8), learningRate (1e-4), batchTokens (4096), heldOutShare (0.2), promotionMargin (0), shareTo
  (none), shareWhat (scores), secretPatterns.
- **Tests** (group `Learning`, 10, headless): nothing written and no request made before the consent, the first question's
  facts, a dismissed one deciding nothing and not asked twice, the second asked apart and only after a yes, both stored with
  time, scope and version, the recording mark; the four verdicts from document edits (accepted, edited with the text and how
  far, rejected with the text typed instead, undone); no share without the sharing consent; a changed text asks again and
  records nothing, a variable cannot say yes; withdrawal stops recording on the next answer and deletes on request, saying
  what was freed; a share sends exactly the previewed items byte for byte, the `.gitignore`, learn-ignore and secret ones left
  out and listed, to a fake destination, a cancelled preview sending nothing; the git patterns and the destination parse;
  editor-verdict against Idrak's reward suite; 24 recorded completions replayed into 24 verdict pairs and a tiny random Qwen3
  adapter trained; promotion refuses a worse adapter, keeps every version, rolls back twice to the base model, deletes one;
  a failing test of a fixture solution (xunit, restored here) found from dotnet test's TRX, written as a task Idrak's
  `CodingSuite` reads, and a scripted policy's episodes in the "csharp" environment rewarded by csharp-tests (1 with the fix,
  0.5 without); the plug-in loaded from a copied folder sees "csharp" and "coding", csharp-tests, csharp-build, tests-passed,
  editor-verdict (its origin), the pair sources, the process sandbox, the preference losses through its own Idrak.Text, and
  leaves nothing when unregistered. With `Ai`, `AiPlugins`, `PhaseP`, `PluginKit`, `Plugins`, `ApiGuard`, `Inventory`: 58
  passed (two failed on the first run: the reward did not honour a cancelled token; a changed text was not asked again in
  the same session; fixed). `api/Craft.Plugins.Learning.txt` added, `api/Craft.Abstractions.txt`, `api/Craft.Editor.txt` and
  `plans/inventory.md` regenerated (`AiEvents`, `ModelAdapters`, `ShareDestinations`).
- **Measured** (CPU, 4 threads, the tests' tiny decoder: 2 layers of width 32, rank 4): 12 DPO steps on 18 recorded pairs,
  the loss 0.693 → 0.514, device memory 191,872 B after step 2 and after the last, managed 20.61 MB → 20.62 MB; held-out
  margin −5.66 (base) → −1.68, promoted. The "csharp" environment on the fixture: an episode 9.0 s with its setup (restore,
  the measured verification), 8.4 s for the next. The owner's real-model numbers (episodes per minute, reward before and
  after, the held-out score) are to be written here.
- **Not compiled here**: nothing; `ShellPage`'s one line compiled on Craft.Maui's net10.0 target (`Launcher` opening the
  folder is the owner's to see on Windows). **Not yet**: the plug-in under `idrak reinforce -P` (its output leaves out the
  host's Craft.Abstractions, so the idrak tool cannot load it alone; the tasks it writes are read by `idrak reinforce` as
  they are); the consent screens are the host's question card (a page of their own when phase U brings the activities
  contract); the agent's episodes carry the changed files, not their diffs; the panel's progress is the status line until
  phase U; training loads its own copy of the model beside the AI plug-in's.

## U as built (2026-10-31)

**Contracts (`Craft.Abstractions`).** `IEditorHost.Activities` (`IEditorActivities`: `Start(title, kind, cancellable,
token, keep)` gives an `IActivity` with `Report(fraction or null, text, ActivityRate)`, `Complete(outcome)`,
`Fail(reason, error, retry)`, its `CancellationToken`; `Running`, `Recent`, `Cancel(id)`, `Retry(id)`, `Changed`;
`ActivityKinds`: download, model-load, agent, generation, training, indexing, build, save, command, plugin, voice;
`NoActivities` for a host without them). `IEditorHost.Notify(title, text, severity, actions)` and
`IEditorHost.ShowStatus(title, text, severity, actions)` (`IEditorNotification` updates in place, disposed takes it
away; `NotificationSeverity`, `NotificationAction`). `IEditorHost.Dispatch(label, action)`. `LanguageOpenContext.Activities`
for a language's long work. All default members: a third-party host or plug-in built before U still runs.

**Craft.Editor (`Craft.Editor.Activities`, headless).** `ActivityCenter` (one short lock, no work under it; the UI hears
`Changed` through the dispatcher at most once per `CRAFT_ACTIVITY_FRAME_MS`, 16; an activity ending within
`CRAFT_ACTIVITY_MIN_MS`, 300, never shows nor is kept, a failure always is; the time left from the share and the time
so far; Cancel through `CancelAsync`, so the work's token callbacks never run on the UI thread). `NotificationCenter`
(running operations live, sticky status lines, ended operations with outcome and time taken, failures with Retry and
Open log, messages with their buttons; grouped by source; `CRAFT_NOTIFICATIONS_KEEP`, 200; the bell's count is what is
unseen, never a status line; `Toast` for a failure or a question only; `StatusOf(source)` and `ShowStatus` are what U2
moves the side panel into). `UiWorkLabels` (every host dispatch runs under a label, a plug-in's as "plug-in NAME" or
"NAME: label"; the editor view labels its keys, typed text, pointer and paint, a key's label made once per key) and
`UiWatchdog` (its own thread posts a tick every `CRAFT_UI_WATCHDOG_MS`, 100; while a tick waits past
`CRAFT_UI_STALL_MS`, 200, the running label is taken; the stall is kept with the full collections and the runtime's
pause during it) with `UiStallReports` (a `stall-*.txt` under `CRAFT_CRASH_FOLDER`, at most `CRAFT_UI_STALL_REPORTS`, 20,
a session). `ActivityText` words the bar and the centre. The host: `ActivityCenter`, `Notifications`, `Labels`;
`StartProgress` is an activity now; a save is an activity ("Saving NAME"); a plug-in's running activities end and its
status lines go when it is deactivated.

**The window (Craft.Maui).** The status bar: the newest running activity (a spinner, or a bar when its share is known;
its title, share, rate, time left, text, "+N"), a tap opens the centre; the bell with its count at the right.
`NotificationCenterView` (the centre as a card over the body's corner: groups with Dismiss, rows kept per entry and
updated in place so a progress report redraws a label, Cancel, Retry, Open log, the plug-ins' buttons, Clear all),
`ToastView` (failures and questions, `CRAFT_TOAST_MS`, 6000; Show opens the centre), `View: Show or hide the
notifications` (craft.notifications). A stall shows in the status bar ("The window was blocked 3.2 s by …"), Output,
the centre (watchdog, with Open report), and the telemetry tab (`ui.stall`). The plug-ins' load results (with their
load time) and activation failures, and settings that were not taken, are notifications and toasts now, not the
platform's message boxes (the owner's note 10, for those). All styled from the palette's tokens; no input of its own
(note 12 adds nothing).

**Every long operation reports.** A model's download and load (`AiModels` starts an activity per lease, fed by the
provider's stages: resolving, the 500 ms load ticker, ready; a failure with Retry; `LocalModelProvider` also turns every
stage into its `Status`, so the status bar's model text and the chip show the device in use once loaded, whoever asked
for the lease: the owner's note 6, the display half), the load command (cancellable), agent runs (turn, tokens, tok/s,
Cancel; the folder copy's files), chat replies (tokens, tok/s, Cancel), inline completion (shown while it runs, never
kept), code actions and voice (their progress), training rounds and the learning plug-in's test runs (R: Cancel stops
the round), the C# workspace's build and a long diagnostics pass (the first compilation, the source generators), saves,
the approve-each sandbox's commands (the last output line, Cancel; a status line of the last command). P6's indexing
and P9's reporter bridge are not on main (P batch 2 is on wip/p-batch-2); they report through the same contract when
they land (P9: an `IProgressReporterKind` whose reporter is an `IActivity`).

**The audit: every UI-thread path found, and its fix.**
1. `ModelHost` loaded under its lock and the Models panel read `Loaded` on the UI thread (Idrak d99c89c, before U).
2. The plug-ins' Idrak libraries (`IdrakInPlugins.IncludeLibraries`: every Idrak*.dll loaded and its built-ins
   registered) ran in the AI and voice plug-ins' activation on the UI thread: now at the plug-in's load at start-up,
   before the window (`CraftPlugins`), so activation returns at once.
3. `AiModels` called `EnsureIncluded` under its lock on the calling (UI) thread before a lease: now inside the lease's
   `Task.Run`.
4. The chat posted one dispatch per token, each rebuilding the reply's text (quadratic on the UI thread for a long
   reply): the pieces are gathered and posted once per UI turn, in order with the tool rows. Its file is written on the
   thread pool. The chat's failure was logged from the pool thread (the log is the UI thread's): now through the dispatcher.
5. The agent deleted its working copy (a recursive delete of the folder's copy) on the UI thread at start, Clear,
   Discard all and close; read the copy's files for each edited file's counts on the UI thread; read and wrote them in
   the review: all on the thread pool now.
6. The terminal wrote typed bytes, pastes and the screen's answers to the shell synchronously, the answers while the
   parser held the screen's lock: a shell not reading (a full pipe) blocked the UI thread or the paint. The writes go
   through a queue to a writer loop, in order.
7. The recent list wrote its file on every file or folder opened, on the UI thread: serialized there, written on the
   thread pool in order (`FlushAsync` for the tests and the close).
8. `RecoveryStore` waited on an async write (`GetAwaiter().GetResult()`) on the pool, and a tab's close deleted the copy
   on the UI thread, waiting for a copy being written: a synchronous atomic write (`TextDocument.WriteAtomically`) on
   the pool, and the close's delete off the closing thread (`WriteNowAsync` awaits it); the store's own dispose still
   deletes at once. (The store is not used by the app yet.)
9. `CSharpWorkspace`: a session's leave and a memory release waited on the mutation gate (`_mutate.Wait()`) on a pool
   thread for as long as a build: `LeaveAsync` and an awaited release; the sync `Leave` and `Release` stay for the
   bench and the tests.
10. The watchdog's own dispose joined its thread on the UI thread: it only signals now.
11. Read and kept as they are, with why: the editor's bounded parse wait (`WaitUntilClassified`, `CRAFT_PARSE_WAIT_MS`
    8, the frame's own budget); the continuations' `.Result` on completed tasks; the C# parser's worker
    (`_resume.Wait`, `Monitor.Wait`), off the UI thread; the learning trainer's sync waits, on its training thread.
The build check: `Ui.TheUiSideHasNoSynchronousWait` scans `src/Craft.Maui`, `src/Craft.App`, `src/Craft.Ai`,
`src/Craft.Editor` and `plugins/*` for `.Result`, `.Wait(`, `.WaitOne(`, `.WaitAll(`, `GetAwaiter().GetResult()`,
`Monitor.Wait(`, `Thread.Sleep(` and `SpinWait.SpinUntil(` outside an allow-list with reasons, failing with file:line
(and on an allowed line that is gone).

**Checks (headless, here).** `Activities` (6), `Notifications` (2), `Ui` (3): reports from four threads coalesced into
one or two UI updates; kept outcomes, Retry once, Cancel reaching the work's token, the minimum; the bar's words; the
host's progress and saves as activities; a scripted session of all eleven kinds listed newest first with "+10" and each
cancelled; the centre's running, status, finished and failed entries grouped by source, Open log and Retry, the bell's
count, toasts only for the failure and the question, the keep limit, a plug-in's message, status and operation under
its name and taken back on deactivation; the watchdog reporting a deliberate 700 ms block by its label with a report
file; the watchdog silent (threshold 250 ms on the shared test machine) through 600 typed keys, a fake 7B load's 200
reports, a fake agent run's 100 and a 4 MB terminal flood parsed under the screen's lock while the UI reads it.

**Windows-only, not compiled here.** Nothing under `Platforms/Windows` or `Craft.App` changed. `Craft.Maui` builds for
net10.0 here; its Windows target (the same sources) is the owner's build.

**Left.** The owner's run: a 7B download and load with the window answering, the agent beside it, the worst stall
from the telemetry tab (`ui.stall`) written here; a deliberate stall's report. P6's and P9's reporting when P batch 2
lands. The owner's notes 6 (the GPU actually used: Idrak.Gpu in each model-loading plug-in), 10 (a Craft dialog for the
remaining message boxes, questions and the About box) and 12 (the doubled inputs) beyond what U covers.

## U2 as built (2026-10-31)

The owner's five points from the Windows run, and the notes 5, 7 and 9 of "The owner's notes from the Windows run" that
sit in this area. Rebased onto U (`a950bbe`): the models panel's state line and the sandbox's last command are U's
notification centre status lines (`IEditorHost.ShowStatus`), kept.

- **1. Pages as tabs** (`Craft.Editor.Shell.PageTabs`, `ShellPageIds`): `ShellModel.Pages`; `craft.settings`,
  `craft.models` and `craft.plugins` open a page tab of the editor area (one each: opening an open one selects it), drawn
  after the documents' tabs with its own close button (`TabStripView` over both; the narrow switcher shows the page's
  title, back and close). A document chosen or opened shows again (the page tab stays); Ctrl+W on a page closes it.
  `SettingsPage`, `ModelsPage` and `PluginsPage` are `ContentView`s now (disposed when their tab closes), never pushed
  over the window, so the title bar's menus, the rail and the status bar answer while they are open. `IShellUi` lost
  `ShowSettingsAsync` (nothing shows a page through it any more); `ShellViewNames.Models` is gone. The diff view stays a
  modal page (a decision the user takes before anything else, A1's).
- **2. The tree follows the disk**: the contract `IFolderWatcher` and the registry `FolderWatchers` (Craft.Abstractions:
  "system", .NET's `FileSystemWatcher` over the folder and its subfolders, names only, a 64 KB buffer, an overflow
  reported as one `Rescan`; "none"), picked by `CRAFT_WORKSPACE_WATCHER` (an unknown name is an options problem, never a
  crash). `FolderTreeWatch` (Craft.Editor.Shell): the watcher's threads drop a change outside the workspace or inside an
  ignored folder (`CRAFT_WORKSPACE_IGNORE`, `Workspace.Contains`) at once, gather the touched folders for
  `CRAFT_WORKSPACE_WATCH_MS` (150), read each once on the thread pool and hand them to the UI thread in one
  `FolderTree.Apply` (only the folders the tree holds; a gone subfolder forgotten with everything below it; one rebuild of
  the rows and one `Changed` per batch, none when nothing it holds changed). A rescan asks the UI thread which folders
  are held and reads them off it. Every save through the host (`DocumentSaved`: the editor's save, save as, a new file
  saved) is read at once, without the wait, and gives the tab the language of its new name. The agent's accepted new
  file is created through the new `IEditorHost.SaveAsync(document, path)` (default member; the host's raises
  `DocumentSaved`): accepting "Agent: new file X" (its placement says "accepting it creates X") is the user's yes to
  writing it; an existing file's accepted change stays in the editor, unsaved, as before. Measured here (the `Layout`
  test, 40 ms batches): a file another process writes shows in a second (the check), an editor save at once, twenty
  files written into `src/bin` give no batch and no row, a rename and a folder's delete show; the agent's accepted file
  shows within a second (`AiPlugins`, 50 ms batches).
- **3. The right-hand side panel is gone.** No column of panels: `PanelsView`, `CRAFT_PANEL_WIDTH` and
  `ShellLayout.PanelsVisible` are removed. A1's "Models and devices" `ListPanel` is gone: what it listed is the provider's
  status (`ModelProviderStatus.Loaded`, new: the models loaded and whether in use; the local provider raises
  `StatusChanged` on each load stage), shown on the Models page ("Loaded now") and in U's status line "Chat and agent
  model". P5's "Terminal (sandbox)" panel is gone: the sandbox writes to the read-only terminal tab "Agent sandbox" of the
  bottom panel's Terminal tab, through the new `IEditorTerminals.OpenOutput(name)` (`IEditorTerminalOutput`: `Append`
  from any thread, VT colours, `Show` without the keyboard, `IsOpen`; one tab per name; Craft.Editor's
  `OutputTerminalSession` queues the bytes for the terminal's own reader, so the screen, scrollback, selection and copy
  are the terminal's; keys, text and pastes go nowhere; `TerminalViewRequest.Reveal`). The tab opens on the first
  command, not at start; Ctrl+` and "Run the selection" still open the user's shell when only output tabs are open. The
  plug-in keeps the last `panelLines` lines for a host without terminals. **Decision (docs/plugins.md, "Where panels
  appear"): K's `AddPanel` stays; a conversation panel is a tab of the chat column; any other panel is a side view of the
  rail** (the recommended of the two: it is where P6's search view goes too): the shell gives it a rail item (its title,
  `RailIcons.Panel`) and a command `craft.sideView.<id>` ("View: <title>"); a click shows it in the explorer's place, a
  second click folds the side, the Explorer item brings the tree back, `ShowPanel(id)` shows it; a plug-in's own
  `RailItem` with the panel's id replaces the shell's item. The Learning panel (R) is such a side view now.
- **4 and 5. Every panel collapses and resizes through one control**: `Craft.Maui.SplitPanel` (the content beside a
  one-unit line in a 6-unit grip; MAUI's pan gesture drags it live within `PanelLayout.Clamp`, the pointer over it shows
  the accent; a drag's end raises `Resized` once; `Fill` for the narrow layout, no splitter). The side view (the
  explorer or a plug-in panel), the chat and agent column (its new close button, Ctrl+Alt+I) and the bottom panel (its
  new chevron, Ctrl+J, a click on its active tab) each sit in one. `PanelLayout` (Craft.Editor.Shell, `ShellModel.Panels`)
  keeps each panel's open state and size: read from `CRAFT_PANEL_EXPLORER_WIDTH` (260), `CRAFT_PANEL_CHAT_WIDTH` (360),
  `CRAFT_PANEL_BOTTOM_HEIGHT` (132), `CRAFT_PANEL_EXPLORER_OPEN`, `CRAFT_PANEL_CHAT_OPEN`, `CRAFT_PANEL_BOTTOM_OPEN`
  (true), `CRAFT_PANEL_MIN_SIZE` (120: the least a drag leaves a panel and the editor), written when a drag or a toggle
  ends to the workspace's `.craft/settings.json` (the user's settings with no folder) through `CraftSettings.StoreAsync`
  (checked and held at once, the file written on the thread pool by `SettingsFile.SaveAsync`, a later copy never
  overwritten by an earlier one). Renamed (nothing was released): `CRAFT_TREE_WIDTH`, `CRAFT_CHAT_WIDTH`,
  `CRAFT_BOTTOM_PANEL_HEIGHT` are the `CRAFT_PANEL_*` names above. The narrow layout is unchanged (the tree in the
  editor's place, the chat as a sheet, no rail, side views or bottom panel).
- **The owner's note 7** (the rail's Explorer button did nothing): the rail buttons' icon and accent line are
  input-transparent, so the click reaches the button's tap; the Explorer item (and Ctrl+B) toggles the side view in the
  wide layout (shows the tree when another view shows), the tree in the narrow one. **Note 9** (the tree re-rendered whole
  when a file opened): `FolderTreeView` keeps one `ObservableCollection` and replaces only the run of rows that differs
  (`RowChange.Between`, one walk from each end; within it, equal rows are left as they are): opening a file moves two
  fills, a folder opened inserts its children, a file the disk added inserts one row; the scroll and the open folders
  stay; the active file's row is scrolled into view. Rows are found by path, not by index. Keyboard navigation and
  type-ahead in the tree are X's. **Note 5** (the chat and the agent never scrolled while streaming): the column follows
  the newest text once it is laid out (its content's size changed), unless the user scrolled up (more than a line from
  the end); switching panels follows again.
- **Tests**: group `Layout` (7: pages as tabs with the menus and commands in reach; the tree within a second for a
  process's file and at once for an editor save, ignored churn, rename, delete, one inserted row; `RowChange`; every
  panel's state and size round-tripping through the workspace's file and back after a restart, the clamp; the side column
  gone and a plug-in panel as a rail side view, a conversation panel not; the read-only output tab), `AiPlugins` (the
  agent's accepted new file created and in the tree within a second), with `Ui`, `Shell`, `Design`, `Terminal`, `Ai`,
  `PhaseP` (the sandbox's output in its "Agent sandbox" tab), `PluginKit`, `ApiGuard`, `Inventory`: 96 passed here.
  The first run failed my own new test: the tree showed the fixture's data folder written inside the workspace (right
  behaviour; the fixture now keeps it outside).
- **Not compiled here**: nothing. No file under `Platforms/` or in `Craft.App` changed; every changed MAUI file compiled
  with nullable analysis on Craft.Maui's net10.0 target. Not run here: the window (the splitters' drag with a mouse on
  WinUI, the grip's hover, the page tabs, the bottom panel's chevron).
- **Not yet**: the splitter shows no resize cursor (that needs platform code; the hover colour says it); the tree's
  keyboard navigation and type-ahead (X); a page tab cannot be dragged (X); the owner's note 2 (Ctrl+S on the agent's new
  file outside the workspace) is helped only for an accepted new file, which is now created at once.

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

## Where the work stopped (2026-10-31, evening)

The three agents running (U, U2, P batch 2) stopped when the account's usage limit was reached. The owner then said:
continue the UI ones (U and U2, resumed from their own worktrees), and keep only the plug-ins' unfinished work, off
`main`. So P batch 2 (P6 code search, P9 progress and telemetry, P10 project model names, P8 screenshot to code;
not built, not tested; base `e04652c`; 51 files, +4,791 −25) is saved on the branch `wip/p-batch-2`, the one branch the
owner allowed for it. Left there: the tests' checks of progress items and messages, P6 in the rail's Search view (not
the side panel U2 removes), one build and the targeted run, rebased onto `main`, "P as built (second batch)". Its Idrak
part (`AgentOptions.ExtraTools`, `d449824`) is on Idrak `main`; nothing of Idrak is left unmerged.

## The owner's notes from the Windows run (2026-10-31): first, before every phase left

The owner ran the whole app on Windows (the test project, the 7B model) and wrote these down. They come before U, U2,
P batch 2 and every held phase: the next session starts here, one agent per group, at most three. Each is checked on
the owner's machine, not only headless.

**Broken**
1. **Roslyn stopped working.** The status bar says "Plain text — no language for .cs (the C# plug-in is not installed or
   is disabled)": the C# plug-in no longer loads in the app (likely since P's shared loading of Idrak libraries across
   plug-in folders, or the plug-in copy target); the Plug-ins page and `CRAFT_CRASH_FOLDER` show why. A test loads
   every pre-installed plug-in from the app's real output folder layout.
2. **Ctrl+S and File › Save do nothing** on a document the agent proposed for a new file (an untitled tab whose
   breadcrumb shows a path outside the workspace, `C:\Users\…\Example.cs`): save must write it, or ask where with the
   picker, and say so if it fails.
3. **The agent and the chat lose context, trim their answers and stop mid-sentence** ("I understand that you wan",
   "I'm here to h"): each agent message starts a new run on a new folder copy with no memory of the run before, and the
   replies end after a few tokens; the conversation carries over, the token limit and the stop are settings with sane
   defaults, and a reply ends only at the model's end or the user's Stop.
4. **Text in the chat and the agent cannot be selected or copied.**
5. **The chat and the agent never scroll** while an answer streams: they follow the newest text unless the user
   scrolled up.
6. **CUDA never shows in the status bar** (`local` only): the device in use is shown in the status bar and the model
   chip, and the GPU is used (`Idrak.Gpu` in every plug-in that loads a model).
   **Fixed (2026-10-31).** Cause: Craft.Ai, Craft.Voice and the learning plug-in already carried Idrak.Gpu and included
   it at start-up, but Idrak's `Device.Default` was chosen once and kept (`field ??= Best()`): a read before a plug-in's
   Idrak.Gpu registered its providers fixed the CPU for the process, so "auto" never took the GPU; and the device was
   shown only after a load. Fix: Idrak 5b5854d, the default is kept with the providers' list it was chosen among and
   chosen again when that list changes (one reference comparison a read; a host's set default still wins; test "device
   contracts: the default device is chosen again …"); `LocalModelProvider` lists this machine's devices and what "auto"
   takes once on the thread pool (`ModelProviderStatus.Devices`, `AutoDevice`, `DeviceChoice`, `IsLoading`), so the
   composer's chip and the status bar show "cuda:0 (auto)" before the first load and the device in use after it. Test
   `LookAndFeel.EveryPluginThatLoadsAModelSeesTheGpuDevices`: Craft.Ai, the learning and the voice plug-in, each loaded
   from a copied folder in a fresh process, list cuda, vulkan and hip. P2's StarCoder2 family loads through Craft.Ai.
   The owner checks the GPU actually used on Windows.
7. **The explorer button on the rail does nothing** when clicked: it toggles the explorer.
8. **The folder and file pickers start with "Select folder" disabled**: the picker opens on a folder it can select
   (the workspace, else the user's), with the button enabled.
9. **The tree re-renders whole** whenever a file opens: only the rows that changed update, the scroll and the
   expanded folders stay, and navigation is quick (keyboard, type-ahead, reveal the active file).

**Fixed (2026-10-31, the first five)**: items 1, 2, 3, 4 and 8 above, each where it lived, with a headless test.
- **1, Roslyn: fixed.** Cause: Idrak.Reinforcement.Plugins' C# coding environment (phase R's learning plug-in registers
  it) was an `IdrakPlugin` named "csharp", Craft.CSharp's name too; Idrak's plug-in registry keeps one plug-in per name
  and unregisters the earlier, so loading `plugins\Craft.Plugins.Learning\` (after Craft.CSharp, in name order) took C#
  away. Reproduced here by running Craft.App's own copy targets into a scratch `plugins\` and loading it with
  `CraftPlugins.LoadInstalled` in a host holding only the app's assemblies: Craft.CSharp loaded, then `.cs` had no
  language after Learning. Fix in Idrak (`3614c6a`): that plug-in is "csharp-coding" (beside "csharp-language"); in Craft,
  `LoadInstalled` marks a plug-in whose own plug-ins a later folder displaced as not loaded, naming who took its place (the
  Plug-ins page and the startup alert say it). Test `Plugins: EveryPreinstalledPluginLoadsFromTheAppsLayout`: every
  pre-installed plug-in laid out as the app's targets do (its whole output folder, the host's contracts left out), loaded
  together in a fresh process (`--probe-installed`), each loaded and started, `.cs`/`.csx` C#, the registries present.
- **2, Save on the agent's new file: fixed.** Causes: a new file the agent proposed opened as a bare untitled tab (the
  path dropped), so Save went to the save picker; the picker (and any save error outside the caught kinds, such as the
  encoding one) threw into the command runner, which only logs. Fix: `DocumentTab.SuggestedPath` (`ShellModel.NewFile(path)`,
  `DocumentTabs.AddFor`/`FindSuggested`): titled by its name, in its language, Save writes it there creating its folders,
  without asking; a second proposal for it goes to the same tab; Save as starts in its nearest existing folder; a dialog
  that fails to open, and any failed write, is said in a message; `EditorHost.SaveAsync` makes a given path's folders, so U2's
  "accepted whole, the file is created" works for a new file in a new folder too. Test `Shell: SaveWritesANewFileWhereItWasMadeFor…`.
- **3, context and cut replies: fixed.** Causes: the agent panel started a new run, copy and conversation for every
  message (Idrak's `CodingAgent` could only start one: `CodingAgent.ContinueAsync` added on Idrak `d85b6ab`); the chat
  kept its history already. The cut text ("you wan", "to h") was mid-word where the model's tokens are whole words: the
  conversation view made a new label for every streamed piece and swapped it into a stack in a scroll view, which on
  WinUI kept a view's first size; the generator's own limits (2048/1024 tokens, the model's full context) were not it.
  Fix: a follow-up continues the run (same copy, its changes and log, the conversation through `ContinueAsync`); Clear,
  Discard all or another folder start afresh; the view grows the streamed message's text in place; `chatMaxTokens` 2048
  (was 1024), `chatStops`/`agentStops` new settings (`|` between stops; the template's end of turn always applies); a
  reply that ends at its limit says so, naming the setting and the context. Tests `AiPlugins: ALongChatReplyArrivesWhole…`
  (a fake model's 3,001-piece reply arrives whole, the next message carries it) and `AFollowUpContinuesTheAgentsRun…`.
  To check on the owner's machine with the 7B model: if a reply still stops short, the panel now says whether it was the
  limit; nothing else ends it but the model's end of turn or Stop.
- **4, select and copy: fixed.** Every message's text is a `SelectableLabel` (on Windows its TextBlock takes text
  selection, `Platforms/Windows/SelectableText.cs`), each model message has a Copy button, every code block has Copy.
- **8, pickers: fixed.** Cause: the Windows App SDK pickers opened on `PickerLocationId.DocumentsLibrary`, a virtual
  location where "Select folder" stays disabled, and the save picker cannot start in a folder. Fix: the Shell's own
  `IFileOpenDialog`/`IFileSaveDialog` (`Platforms/Windows/ShellFileDialog.cs`, source-generated COM, trim-safe), opened on
  a real folder: the workspace (the active file's folder for Open file, the file's own for Save as), else Documents;
  `IFileDialogs.PickFolderAsync`/`PickFileAsync` take that start folder. They also run in an elevated process.

**Look and feel**
10. **Custom dialogs**: the platform's message boxes look out of place; one Craft dialog (title, text, actions, the
    design's tokens) for questions, confirmations, consents and the About box.
    **Fixed (2026-10-31).** Cause: the presenter, the shell's prompts, the command palette and narrow quick open called
    MAUI's `DisplayAlertAsync`/`DisplayActionSheetAsync`/`DisplayPromptAsync` (WinUI's ContentDialog). Fix: one
    `CraftDialog` (Craft.Maui) over `DialogQueue` (Craft.Editor, headless): an overlay in the window (dimmed backdrop
    that takes the pointer, a card on the raised fill with the strong border, the title in Inter SemiBold, the text,
    the details in the code font, an optional field, the actions: primary in the accent, a losing one in the error's
    colour, a picker's rows as a list), one at a time and the rest waiting, a cancelled request taken away; Escape
    answers the way out, Enter the focused or default action, Tab and Shift+Tab (the arrows in a list) move and wrap,
    every other key is the dialog's; the focus goes to the default action and back to what had it. Used everywhere
    through `MauiEditorPresenter` (messages, the plug-ins' questions: P5's approval, the learning consents) and
    `MauiShellUi` (the unsaved-changes question, the About box, pickers), the command palette and narrow quick open.
    The window's keys go through `DialogQueue.Before` (the Windows key route now forwards every unhandled key; the
    filter keeps the old chords-and-function-keys rule when no dialog is open); on Windows the card cycles Tab inside
    it. Test `LookAndFeel.NoPlatformMessageBoxIsLeft` scans `src` and `plugins` (allowed: the crash box in
    `CrashDialog.cs`, Win32's own because the window may be what failed) and three behaviour tests.
11. **The model chip belongs with the composer**: the model choice sits at the chat and agent input (pick a model there),
    not alone in the title bar.
    **Fixed (2026-10-31).** Cause: D put the chip in the title bar. Fix: the title bar keeps the logo, the menus and the
    search; the composer of the chat and the agent has a `ModelChip` under its input (the model, its device, a dot for
    ready, loading or failed, hover states), whose tap opens a Craft picker of the provider's models (the one set, the
    loaded, those used this session; the one in use marked), the devices ("Run on auto (the fastest found: cuda:0)",
    cpu, each GPU) and "Another model… (Models and devices)". A pick writes the provider's model or device setting
    (the model then loads as an activity); a setting fixed by the environment (`CRAFT_PLUGIN_AI_MODEL`) is said so, not
    written. Test `LookAndFeel.TheModelChipShowsTheDeviceAndThePickerItsModelsAndDevices`.
12. **The inputs look doubled** (a box inside a box: the search, the composer, the settings fields): one border, the
    design's input style.
    **Fixed (2026-10-31).** Cause: WinUI's TextBox (MAUI's Entry and Editor) draws its own border, fill and focus
    underline inside the frame Craft drew around some of them. Fix: one `CraftInput` (a single border, radius 6, 8 for
    the composer, the raised fill, the strong border, the accent as the focus ring) around every text input (the search
    box, the find bar, the composer, the settings page, the Plug-ins and Models pages' fields, the dialog's prompt), and
    `WindowsControlStyles` (Platforms/Windows) takes the TextBox's border, fill and underline away in every state.
13. **The settings page looks bad**: sections (Editor, C# and Roslyn, Models, Completion, Agent and sandbox, Plug-ins,
    Appearance, Keyboard), proper controls (switches, numbers, drop-downs, a font picker) instead of a text box and Set
    for each, search, and the variable shown small; the composer of the chat and the agent made as good as the design.
    **Done (2026-10-31).** The settings page is made from metadata declared next to each option (`SettingShape` on every
    `CraftOptions.Variables` entry and on `PluginSetting`): sections in a left list, a search across them, a control per
    kind, live apply, the file per change, modified marks and resets, the environment read-only with why, plug-ins under
    their names. The composer is notes 11 and 12 (the model chip, `CraftInput`). See "Notes 13 and 16 as built".
14. **The editor's font**: the default code font looks wrong; JetBrains Mono must actually draw (the bundled font
    reaching the drawn surface), else a well-chosen monospaced fallback, with the line height and weight tuned.
    **Fixed (2026-10-31).** Cause: the editor's canvas is Win2D through MAUI Graphics, whose text format takes a family
    by the system's name; the MAUI font alias "JetBrains Mono" exists only for XAML controls, so the canvas drew its
    default and the fallback check swapped in Cascadia Mono (or worse). Fix: Craft.App copies the JetBrains Mono files to
    `fonts\` beside the program; `WindowsDrawnFonts` names each face by its file (`file:///…#JetBrains Mono`, then
    `ms-appx:///fonts/…`), the bold and italic from their own files (no synthetic styles); `CodeFontPicker` (the editor
    and the terminal) measures each candidate on the frame's canvas and takes the first whose cell is the file's own
    advance (0.6 em, read from the font's `head`/`hhea`/`post` tables, `FontFileMetrics`) within 2%, else the family
    by name, else `CRAFT_FONT_FALLBACK` (now a list, "Cascadia Mono, Consolas"), each only if monospaced; a face the
    canvas refuses counts as not taken. Type as the design: 13 on 22-unit lines, regular weight. Test
    `LookAndFeel.TheBundledCodeFontIsCheckedByItsGlyphWidths`. The owner checks the drawn glyphs on Windows (Output
    names a fallback when one is taken).
15. **More interactive and friendly** overall: hover states, focus rings, tooltips, empty states that say what to do,
    smooth scrolling, context menus in the editor and the tree.
    **Built (2026-10-31)** with phase X's first slice: states on every control, tooltips with the keys in effect, empty
    states, context menus (editor, tree, tabs) on the command registry, motion and smooth scrolling. See "Note 15 and
    phase X's first slice as built".

**Missing**
16. **Keyboard shortcuts**: every shortcut a C# developer expects (Visual Studio's and VS Code's common ones: go to
    line, go to file, comment, duplicate line, move line, multi-caret, rename, find in files, quick fix, and so on),
    and a page to see and remap them (searchable, conflicts shown, reset), saved in the settings.
    **Done (2026-10-31)** but multi-caret (phase X) and the shortcuts of commands that do not exist yet (rename, peek,
    find in files: no contract or P6 not merged). Two-key chords, a keymap choice (VS Code, Visual Studio), the editor's
    line commands, and the Keyboard section of the settings. See "Notes 13 and 16 as built".
17. **Languages of a C# project in the C# plug-in**: syntax colours (and bracket matching, folding later) for `.csproj`,
    `.props`, `.targets`, `.sln`/`.slnx`, `.xml`, `.xaml`, `.config`, `.resx`, `.razor`/`.cshtml` (markup and C#),
    `.editorconfig`, `.json` (`appsettings.json`, `launchSettings.json`, `global.json`); each removable with the plug-in.
18. **Format document for those files** (XML and JSON formatting beside C#'s), on Shift+Alt+F and on save as a setting.

**Built (2026-10-31): 17 and 18**, in the C# plug-in, through the public contracts only (a third party can do the same).
- **17, the C# project's languages: built.** `Craft.CSharp`'s `ProjectLanguages` registers seven languages through
  `EditorLanguages`, one plug-in part each, so each comes with the plug-in and goes when it is removed or disabled:
  `msbuild` (.csproj, .props, .targets: `$(Property)`, `@(Item)`, `%(Metadata)` coloured too), `xml` (.xml, .config,
  .resx, .slnx), `xaml` (.xaml, namespace prefixes coloured), `razor` (.razor, .cshtml: HTML, with `@code { }`,
  `@functions`, `@{ }`, `@if`/`@foreach`/... blocks and `} else {`, `@( )`, `@expr`, directives, `@*  *@` and directive
  attributes coloured, the C# by a small lexer), `json` (comments and trailing commas, a property's name told from a
  value, escapes), `editorconfig` (sections, keys, values, severities) and `sln`. Colours use the theme's existing
  classes (no new kind). Brackets: `<>`, `()`, `[]`, `{}` for markup (matching skips strings and comments, as before),
  `{}` `[]` for JSON. Hot path: a `LineStateSession` keeps one state number per line and classifies only the lines asked
  for; after a change the states below it are kept, shifted, and trusted again as soon as a line ends as it did (typing
  in a name of a 2,000-line project redoes that line only, test-checked); spans over the line's chars, keywords by span
  in frozen sets, `SearchValues` for the markup's specials, no regex, nothing allocated per token. Proven unchanged:
  after random edits, the spans equal a fresh session's (MSBuild, Razor, JSON). No contract was missing for the colours.
- **18, Format document for XML and JSON: built.** New contract `IFormattingLanguageSession` (`Craft.Abstractions`):
  a session that formats with or without a semantic layer; `ISemanticLanguageSession` now extends it (C# unchanged).
  `EditorModel.FormatDocumentAsync` (Shift+Alt+F and Ctrl+K Ctrl+D, the same key path and the Edit menu's
  `craft.formatDocument`) asks any formatting session, off the UI thread, and applies the edits as one undo step when
  the text is still the one formatted; it shows as an activity, and a malformed file (the formatter's `FormatException`:
  "line 3, column 9: expected ',' or '}'") is left unchanged with that message as a failed activity (a toast) and
  `LastFormatMessage`. XML (MSBuild, XML, XAML): one element per line, the editor's tab size and spaces, text-only
  elements on one line, mixed content and `xml:space="preserve"` untouched, empty elements joined, attribute lines one
  step past their tag, a trailing comment kept, one blank line kept. JSON: members and elements one per line, `{}`/`[]`
  kept, comments where they were, trailing commas kept, one blank line kept. Only white space between tokens changes, so
  the caret keeps its place. **Format on save**: `CRAFT_FORMAT_ON_SAVE` (`CraftOptions.FormatOnSave`, `.craft/settings.json`,
  listed in `CraftOptions.Variables` for the settings page), off by default (C# had no format on save before); Save
  formats through the tab's editor first (`ShellModel.EditorOf`, set by the shell page), one undo step, and a malformed
  file is saved as it is. Tests: group `ProjectLanguages` (each extension with and without the languages, colours of
  each, a `.razor` with `@code { }` and `@expr`, incremental and fresh-equal spans, XML and JSON round trips idempotent
  with comments kept and malformed left alone, one undo step, Shift+Alt+F, format on save on and off) and `Plugins`
  (every extension from the plug-in's own folder and context, and gone when it is removed or disabled).
- **Left**: folding (needs an editor contract for regions); Razor's markup inside a C# block is taken a line at a time
  (a line that starts with a tag, or `@:`, is markup; a multi-line tag or plain text lines inside `@if { }` read as C#);
  Razor's C# is lexed, not parsed by Roslyn (no semantic colours or diagnostics in .razor); no Razor, .sln or
  .editorconfig formatting; XML attribute values and a multi-line tag's closing `>` line are kept as written; HTML's
  `<script>`/`<style>` contents are not coloured as JS/CSS. Checked headless only; the owner's Windows check: open
  `test-output\CarftTestPOC`'s `.csproj`, an `appsettings.json` with a comment, a `.razor` if there is one, and
  Shift+Alt+F on each.

**Second Windows run (2026-10-31, late), queued for the next free slot, broken first**
19. **The Output panel's text cannot be copied**: selectable lines, Ctrl+C, a Copy all, as the chat's messages (note 4).
    **Built (2026-10-31)**, with Problems and Idrak telemetry: see "Notes 19-21 as built".
20. **Copying in the terminal crashed the app with no crash report**: a native (WinUI/COM) fail-fast skips every managed
    handler, so the crash dialog never ran. Likely the clipboard touched off the UI thread, or the terminal's Ctrl+C;
    found and fixed where it lives, with a test where it can be headless. **Built (2026-10-31)**: the cause could not be
    proven on Linux (no Windows record of that crash); every path of the terminal's copy is hardened, see "Notes 19-21 as
    built". The next such crash leaves its record (note 21).
21. **A global crash reporter that needs no hook ("an audit or a support ticket")**: a run marker written at start and
    removed on a clean exit, so the next start knows the last run died whatever killed it; a breadcrumb trail flushed to
    disk as it happens (the last UI work, commands, key names never typed text, first-chance exceptions, output lines);
    on Windows, the next start reads the Application event log's entry for that process (faulting module and code, or
    the .NET Runtime stack); and a "Report a problem" ticket (offered at the next start and in Help) bundling all of it
    with the app and system details into `CRAFT_CRASH_FOLDER`, shown for the owner to review and paste. Nothing is sent
    anywhere. Every option per the option rule. **Built (2026-10-31)**: see "Notes 19-21 as built".

**From the run with a C# error (2026-10-31, on ebe717f and 8dcc3de)**
22. **"C# worked but when I introduced a C# error it crashed"**, with the log's `AI: Fix CS1002 failed` and `The agent run
    failed`: the RTX 5070 Ti (16 GB) out of memory, 13.55 GiB in use, 1.02 GiB offloaded, for one 7B int8 model (about 8 GB).
    **Built (2026-10-31).** Causes found:
    - *Two copies of the model.* Craft's `ModelHost` had no limit, and a loaded model is kept under everything its load
      depends on (name, device, context, weights, adapter); `AiModels` kept its lease under the name only, and the load
      command (the composer's chip) went to the provider directly. A model loaded again under other settings (a device or
      weights pick, an adapter) loaded a second copy while the first stayed, held by the earlier lease or for its 10-minute
      keep-alive: 13.55 + 1.02 GiB fits two copies of the int8 weights (one is about 7-8 GiB), not one with its cache (inferred from the numbers; the owner's next run shows the device memory in use after a load). Fix: `AiModels` keeps each role's lease (chat,
      completion) under all of those settings and gives the old one back when a role's settings change; the load command
      goes through `AiModels` (one copy, one key); the provider unloads every idle model before it loads another
      (`LocalModelProvider.LetIdleGo`, never one in use). The KV cache was not it: Idrak sizes it per request (the prompt
      and the reply's limit, `DecodingContext(promptLength + limit)`), never the model's whole context up front.
    - *"AI: Fix CS1002" ran from the Problems row's Fix button* (nothing binds or runs it otherwise; code actions run only
      when picked). Its failure was a modal message: it is a notification now, and every out-of-memory failure (chat,
      agent, code action, a load, completion) says what to do (`AiErrors.Describe`: a smaller model at the chip, smaller
      weights, other GPU programs closed; IDRAK_OFFLOAD named, never set).
    - *Headless, the editor's own path does not throw* (test `Diagnostics.TypingACSharpErrorKeystrokeByKeystrokeNeverThrowsAndStaysOnTheUiThread`:
      the test project opened through ShellModel with the C# language, on a UI thread of its own; a missing `;`, an unknown
      name, an unclosed string and brace typed and deleted a key at a time, the passes landing between keys; squiggles
      painted, Problems rows, the status count, hover, the code actions at each error and at the caret; every view event
      checked to arrive on the UI thread). Hardened where it could throw: a hover whose text was deleted before its pause
      ended asked Roslyn for a position past the end (`ArgumentOutOfRangeException` out of a dispatched action ends a
      WinUI app): it is dropped, and any edit cancels a waiting hover; the diagnostics shown are clamped to the text (to
      their own snapshot before they are mapped, to the current one after: `EditorModel.ClampTo`); a Problems row's Fix
      or Explain takes the problem as the editor has it now (`ShellText.CurrentProblem`, "gone" said), its Go to clamps;
      `EditorView` hops the model's DiagnosticsChanged, Invalidated and CaretMoved to the UI thread when a buffer changed
      on a worker; the Problems list keeps its rows (and the strip its tabs) when nothing it shows changed, instead of
      replacing the WinUI list's items on every keystroke. Tests in `Diagnostics`. The owner's crash report still names
      the exact frame: send it (`CRAFT_CRASH_FOLDER`).
23. **The model loads only when asked** (the owner: "an action after the model is selected in the agent composer, not
    while the editor starts nor while it runs"; then "a smaller one for inline completion, the big one for agents, with
    settings"). **Built (2026-10-31).** Two models, each a setting: `model` (the chat, the agent, the code actions; the
    composer's chip picks it) and `completionModel` (default `Qwen/Qwen2.5-Coder-1.5B`, a setting's default: its
    fill-in-the-middle format is Idrak.Text.Plugins' as for any model; the settings page picks it). Nothing loads at
    start-up (`loadOnStart`, default off, new) or while editing: the chat model loads when it is picked or confirmed at the
    chip, on the first message of the chat or the agent (the composer says "Loading …, the message is sent once it is
    loaded"; the load is an activity with its stage and Cancel), or by 'AI: Load the local model'; completion loads its own
    small model on its first request (`completionLoad`, default on; off: only a model already loaded), never the chat
    model, which it uses only while loaded; with no model it does nothing and says so once in the status bar
    ("Completion: off — no model loaded", the reason as its tooltip). A failed completion load or an out-of-memory
    generation is not tried again on every pause: only when its settings change or the model is loaded on purpose.
    `CRAFT_PLUGIN_AI_MODEL` names the model; it no longer means load it. The voice and learning plug-ins' models already
    loaded only on use (the recognizer on its command or on listening, the trainer on a training round). Tests `ModelLoading`.
    For the owner on a 16 GB card: the 7B at int8 beside the 1.5B leaves room for the agent's cache; a 7B at float32 does not.
24. **Two UI stalls at start-up** (309 and 214 ms, "unlabelled UI work"; before them: notifications, the watchdog, the
    platform's hook, the learning plug-in's posts). **Built (2026-10-31).** The page's `OnAppearing` is a window event, so
    everything it ran was unlabelled: the plug-ins' start, the rail and menus, and after the folder's open its handlers.
    Found on the UI thread and moved off: the folder's open read `.craft/settings.json`, the git branch and checked the
    folder there (now on the thread pool, `ShellModel.OpenFolderAsync`); the chat read and parsed the kept chat
    (`.craft/chat.json`) there when the first document opened (now on the pool, shown through the dispatcher,
    `AiChatPanel.KeptRead`); the voice plug-in loaded its families' packages in its activation (now on the pool; what needs
    them waits, `VoicePlugin.EnsurePackages`). Redundant work dropped: a folder with no settings of its own reloaded equal
    options, which re-applied the whole window's theme and rebuilt every open editor's model and language session; equal
    options now change nothing. Labelled: each part of `OnAppearing`, each plug-in's activation ("plug-in start: NAME"),
    the window's resize, the folder's handlers, the code font's pick and measure (editor and terminal), the terminal's
    paint, and every view's dispatch (`UiDispatch.DispatchLabelled`): the next report names the work. The plug-ins'
    Idrak libraries were already registered before the window (phase U). The owner's next run on Windows names what is left.

25. **Every model has its own settings** (the owner, 2026-11-01: "all models should have settings: inline completion
    (enable / disable) and device to run on and each respective settings; chat, agent model as well"). Queued for the
    next free slot, ahead of the rest of X. Three roles, each with the same set: enabled, model, device (auto, cpu,
    cuda:N...), weights, context, keep-alive, temperature, max reply tokens.
    - Completion: enabled and model exist (the 1.5B default); device, weights, context and keep-alive are new (today it
      shares the chat's).
    - Chat: today's `model`, `device`, `weights`, `context`, `keepAliveMinutes` become the chat's (the variables kept);
      enabled and temperature are new.
    - Agent: all new; an empty model or device means the chat's, so one copy is shared on the card by default.
    - The settings page shows the Completion, Chat and Agent sections; each composer's chip writes its own panel's
      model and device; every one a `CRAFT_PLUGIN_AI_*` variable and a `.craft/settings.json` key; the lease per role
      (d260816) keys on the role's own settings, so two roles on one model and settings share one copy.
    **Built (2026-11-01).** `AiSettings` holds the three sets, resolved by `ChatRole`, `AgentRole` and `CompletionRole`
    (`AiRoleSettings`: enabled, model, device, context, weights, keep-alive, temperature, most reply tokens):
    - Chat: `chatEnabled` and `chatTemperature` are new (default the library's `GenerationOptions.Temperature`, as
      before); `model`, `device`, `weights`, `context`, `keepAliveMinutes` are the chat's, their variables unchanged.
    - Completion: `completionDevice` (empty: auto), `completionWeights`, `completionContext`,
      `completionKeepAliveMinutes`. An empty weights, context or keep-alive is the chat's, as it was before (so the
      1.5B keeps the weights the owner runs it with). Turned off, completion asks for nothing, loads nothing, and gives
      back the copy it held.
    - Agent: `agentEnabled`, `agentModel`, `agentDevice`, `agentWeights`, `agentContext`, `agentKeepAliveMinutes`,
      `agentTemperature`. Every empty one is the chat's, so by default the agent's lease *is* the chat's (one copy).
    - Leases: `AiModels` keeps a lease per role (`ChatAsync`, `AgentAsync`, `CompletionAsync`, `RoleAsync`), keyed on
      the provider, the name, the adapter, the device, the context, the weights and the keep-alive. Two roles with equal
      keys share one lease. A role whose key changes gives back its old lease unless another role holds it. A role
      turned off throws "turned off" (`AiModels.Off`, naming the variable) and loads nothing.
    - The provider: the request's new `KeepAlive` (`ModelRequest.KeepAlive`) is the role's keep-alive on Idrak's
      `ModelHost` lease. The provider's status lists Chat, Agent and Inline completion (`ModelRole` gained `Key`,
      `Enabled`, `Device` in use, `DeviceChoice`, the role's model and device settings, its load and settings
      commands, `Inherits`/`InheritsModel`/`InheritsDevice` and `IsLoading`). It reads the host's settings live
      (`LocalModelProvider.SettingsSource`), so the chip and the status bar follow the settings page.
    - Settings page: new Chat section (`SettingSections.Chat`). Completion, Chat and Agent and sandbox each show their
      set: the device as a choice from the "devices" source (others typed), the weights as a choice, and the context
      (tokens), keep-alive (min), temperature (0-2, step 0.05), reply tokens (tokens) and the delay (ms) with ranges and
      steps. The shell registers a "Settings: SECTION" command per section (`SettingSections.CommandOf`, "craft.settings.chat").
    - Panels: a disabled chat or agent says so with an "Open its settings" button. Its empty state says so with the
      same link (`ShellEmptyStates.Conversation` reads the panel's `IConversationPanel.ModelRoleKey`).
    - Chips: the composer's chip shows the shown panel's role (`ShellText.ModelChip`/`ModelPicks`/`WriteOf` take a
      role). The chat's chip writes `model`/`device` and runs `ai.loadModel`. The agent's chip writes
      `agentModel`/`agentDevice` and runs the new `ai.loadAgentModel`, with "Same as chat" (clears the setting) first,
      and "same as chat: …" on the chip while it inherits. A plug-in setting written anywhere refreshes the chip
      (`ShellModel.SettingApplied`).
    - Status bar: the model item's tooltip lists each role's model, the device it runs on once loaded (else the one it
      would take) and its state (`ShellText.ModelRolesTooltip`); the notification centre's "Models" line names all three.
    - Tests: `ModelLoading.EachRoleReadsItsOwnSettingsAndTheChatKeepsItsOldNames`,
      `ModelLoading.TheAgentSharesTheChatsCopyUntilItsOwnSettingsDiffer`, `ModelLoading.ARoleTurnedOffLoadsNothing`,
      `ModelLoading.ATurnedOffPanelSaysSoWithALinkToItsSettings`, `Settings.EachRolesSectionShowsItsSetWithShapes`,
      `Ui.EachChipWritesItsOwnRoleAndTheTooltipShowsEveryRole`, and `Ai.TheSettingsReadEveryVariable` extended.
    - Left: the Models page (DesignPages) still edits only the chat's model and device fields; a role's device in use
      shows once a load ran in this session (a copy loaded before is matched by its settings). The code actions follow
      the chat's role (and `chatEnabled`). The owner's Windows run should confirm the 7B and the 1.5B as one copy each
      and the agent sharing the chat's copy.

26. **The bottom panel's text is one read-only text surface, not a list with checkboxes** (the owner, 2026-11-01: "the
    checkbox is so gross, why didn't you add a multi-line editor and append to it"; "I can only select one line, and
    when I need more I need to check: is that the friendly, usable design I asked for?"). Note 19 made Output and
    telemetry a `CollectionView` in `SelectionMode.Multiple`, which on Windows draws a checkbox per row, and its rows
    `SelectableLabel`s, which select within one line only. Next free slot, before note 25. **Built (2026-11-01)**: see
    "Note 26 as built".
    - Output and Idrak telemetry are Craft's own editor surface in a read-only mode (a new `IsReadOnly` on the editor
      model: typing, paste, cut, delete and the AI never change it; the host appends through its own path, outside undo):
      drag-select across lines, Shift+click, Ctrl+A, Ctrl+C and Copy in the context menu, Ctrl+F, only visible lines
      laid out and drawn, new lines appended at the end and followed unless the user scrolled up, the code font and the
      theme, error and warning lines coloured (a small log language), bounded by `CRAFT_HOST_LOG_LINES` (oldest lines
      trimmed in one step).
    - The owner, on the same panels: "this isn't modern design, this is an ancient design". So each panel follows what
      current editors do, from the design's tokens (hover states, no box inside a box, no row chrome):
    - Idrak telemetry is not a log but measurements: a live metrics table (source, measure, last, mean, p95, count,
      unit), sortable by a header click, a filter box, a sparkline of each measure's recent values, stalls and slow
      values marked; plain rows with no checkboxes, a range selected by drag or Shift+click, Ctrl+C copying it as a
      table (tab-separated, pastes into a sheet).
    - Output also gets a filter box and a "follow the end" toggle in its header.
    - Problems is plain rows grouped by file (a collapsible file header with its count), a severity icon per row, a
      click jumps to its place, Ctrl+click and Shift+click pick several, Ctrl+C copies the picked rows, Copy all stays.
    - Tests headless: read-only refuses every edit path, append keeps the selection and the follow rule, the trim keeps
      the view's place; and a scan that no `SelectionMode.Multiple` list is left in the shell.

27. **The agent wrote its tool calls as text and stopped** (the owner's run, 2026-11-01, Qwen2.5-Coder-7B-Instruct,
    "Create C# class For House with float 3 props and + float price": the reply described `write_file`, `read_file`
    and `run_command` in markdown json fences, ended "You created a new House.cs…", and no file was written or offered;
    "why the agent didn't continue and why the file never written or asked to be written?"). Causes:
    - The model wrote the calls as fenced ```json blocks instead of its template's `<tool_call>…</tool_call>`, which
      is the only place Idrak's "json" parser reads for this template; the first call also had one `}` too many.
    - A turn with no call is the run's final answer, so the run ended after one turn with nothing done and the model's
      claim unchallenged.
    Fix in Idrak (on its `main`, by its rules: `IDRAK_DEVICES=cpu`, targeted filters), then used from Craft. Next free
    slot, before note 25:
    - A tolerant read (an option, on for the coding agent): when a reply has no call in the template's markers, a fenced
      or bare JSON object or list shaped `{"name", "arguments"}` whose name is one of the request's tools is taken as a
      call, the first complete JSON value read (trailing junk such as the extra brace ignored); a JSON example naming no
      offered tool stays text.
    - The coding agent's nudge: a turn that ends without a call but holds call-like text gets a correction message
      (the format the template wants, with an example) and the run goes on; the number of nudges an option
      (`CodingAgent`'s options and Craft's `agentNudges`), so a run never ends silently on a malformed call.
    - Craft's agent panel says so when a run ends with no tool calls and no changes ("the model wrote no tool calls;
      nothing was changed"), never letting the model's claim stand alone.
    - Tests: the owner's reply verbatim parses into the three calls (the extra brace tolerated); a JSON example with
      an unknown name stays text; a scripted model that answers in fences first, then properly after the nudge, writes
      the file through the diff; the nudge limit ends the run with the message.

**Already planned and still owed** (from the earlier notes, in U and U2 above): the notification centre, every
operation in the status bar, the window never freezing, the settings and Models pages not covering the menus, new
files showing in the tree, the side panel gone, every panel collapsible and resizable, and the open question of
whether voice dictation goes through the diff view (not answered yet).

The held phases (M Android, L more languages, A3 remote models) stay held for the owner's word.

## Note 26 as built (2026-11-01)

- **Read-only editor** (`EditorModel.IsReadOnly`, carried by `EditorView.IsReadOnly`): every edit is refused where it
  enters (slice 2's `CommitEdits`, which typing, paste, delete, Enter, Tab, the line commands, find's replace,
  formatting, the completions and the multi-caret edits go through; undo and redo); typed text, Ctrl+X and Ctrl+V do nothing (nothing is
  copied away or read from the clipboard), Ctrl+S goes on to the window, inline completion never asks;
  `RefusedEdits` counts them. Selecting (drag across lines, Shift+click, Ctrl+A), Ctrl+C, Ctrl+F, F3 and scrolling work.
  `ShowLineNumbers` (off for Output: the text a cell from the left), `ViewportChanged`, `ScrollToOffsets(x, y, instant)`
  (a request: the offsets stay what the host showed).
- **Output** (`Craft.Editor.Shell.LogSurface`, shown in `BottomPanelView` by Craft's own `EditorView`): one read-only
  document over a piece-table buffer (its `Edit` throws); `Post` from any thread queues a line and says when a flush must
  be dispatched, so one UI dispatch takes every line that came before it runs ("output: the log's new lines"), appended
  in one buffer change outside any undo history, the oldest past `CRAFT_HOST_LOG_LINES` trimmed in the same change. The
  editor attached keeps its selection (the append moves nothing, the trim shifts it with the text) and its place: while
  following it scrolls to the end at once (no easing), a scroll up stops following, the end starts it again, and the
  header's Follow toggle sets it; not following, a trim moves the view up with the text so the same lines stay in view.
  The filter box shows only the lines holding its text (ignoring case) as a view over the same text
  (`LineFilterBuffer`: an entry per shown line read through to the log's characters, arrays that only grow at their end
  so every snapshot stays valid, trimmed entries skipped, a full array copied once with the live entries only). The
  log's language (`LogSurface.Language`, `craft-log`, in no registry: no file, no tool and no picker sees it) colours an
  error's line, a warning's line, a debug line and an information line's time; `LogClasses.Theme` draws them in the
  theme's error, warning and secondary colours on the panel's surface (no current line's band). Code font, theme, only
  the visible lines laid out and drawn (the editor's renderer). Its context menu is `EditorContextMenus.ReadOnlyText`
  (Copy, Select all, Find), the Edit commands acting on the menu's target editor and the editing ones unable to run on a
  read-only one; Ctrl+F opens slice 3's find overlay on Output's editor (`ShellPage.ShowFind` with its target; replace closed, and a
  read-only text replaces nothing: `EditorFind` reports 0). Keys go to the
  read-only editor first, then to the window's keys with no editor (`ReadOnlySurfaceKeys`), so Ctrl+J, Ctrl+` and the
  palette work from it.
- **Idrak telemetry** (`MetricsTable`, drawn by `Craft.Maui.PanelListView`): one row per source and metric with last,
  mean, p95 (nearest rank of the last `CRAFT_TELEMETRY_SAMPLES`, default 60), count and unit, a sparkline of the recent
  values; a stall (its name) or a value in ms at least `CRAFT_TELEMETRY_SLOW_MS` (default 100) marked in the warning
  colour (the row's edge, the Last cell, the sparkline's segment). A header's click sorts (numbers from the largest
  first, again the other way, ties in arrival order); the filter matches the source or the measure. Rows picked by click,
  Shift+click, Ctrl+click, Up and Down (Shift extends), Home, End, Ctrl+A, Escape; Ctrl+C copies the picked rows (none:
  every row) as a tab-separated table under its header line, Copy all every row shown. Measures come from any thread
  under a short lock; one refresh per dispatch takes them; a frame allocates nothing (tested).
- **Problems** (`ProblemsList`, drawn by `PanelListView`): every open editor's problems (the tabs with a semantic
  layer), grouped by file in the tabs' order, a header per file with its count that folds and stays folded, errors
  first then by place, a severity mark per row, the place at the right; a click opens the file and moves the caret there,
  Ctrl+click and Shift+click pick several (no jump), Up, Down and Enter, Ctrl+C copies the picked rows as a build tool
  writes them, Copy all every row; Fix and Explain on the hovered or keyboard row. An update showing the same rows
  replaces nothing and redraws nothing (d260816's rule); the picks follow their problems to new rows. At most
  `CRAFT_PROBLEMS_LIMIT` rows, the tab's count all of them.
- **No checkbox list is left in the shell**: `Ui.TheShellHasNoCheckboxList` scans `src/Craft.Maui` for
  `SelectionMode.Multiple` and the bottom panel for a CollectionView, a CheckBox or a `SelectableLabel`. `TelemetryLog`
  and `TelemetryRow` are gone (`MetricsTable` replaces them).
- **Tests**: groups `ReadOnly` (2), `OutputLog` (4), `Metrics` (4), `ProblemsPanel` (2) and `Ui.TheShellHasNoCheckboxList`,
  with `Ui`, `ApiGuard`, `Inventory`, `Copy`, `Design`, `Shell`, `Editor`, `Experience`.
- **Left**: the find overlay sits over the editor area, not over the panel, when it searches Output; a log message's
  continuation lines are not coloured; the telemetry and Problems lists have no context menu (Ctrl+C and Copy all copy);
  drag-to-select a range in the drawn lists is not there (click, Shift+click, Ctrl+click and the keys are); the owner's
  Windows run checks the look, the follow and the keys.

## Notes 19-21 as built (2026-10-31)

- **19, copy from the bottom panel** (`Craft.Maui.BottomPanelView`, `Craft.Editor.Shell.PanelCopy`): Output's and the
  telemetry's rows are the chat's `SelectableLabel` (Windows: `Platforms/Windows/SelectableText.cs`, the TextBlock takes
  text selection: a part of a line copies with Ctrl+C or the context menu); the lists choose rows (`SelectionMode.Multiple`)
  and Ctrl+C that no control took copies the chosen rows in the list's order (`ShellPage.PanelCopyKeys`, before the key
  bindings, only with no editor focused); "Copy all" in the panel's header copies the tab's whole list. Problems: the
  message selectable, a Copy per row and Ctrl+C on the row chosen last, as a build tool writes it
  (`Program.cs(12,5): error CS1002: ; expected`); Copy all copies every row. The lists stay virtualized (CollectionView)
  and bounded (`CRAFT_HOST_LOG_LINES`, `CRAFT_PROBLEMS_LIMIT`).
- **20, the terminal's copy crash.** Read end to end: the drag selection (`TerminalView`, GraphicsView pointer events),
  `TerminalScreen.TextOf` (its ranges are clamped: a selection past the buffer cannot throw), Ctrl+C (it sent ^C to the
  shell even with a selection; Ctrl+Shift+C was the copy), no context menu, and the clipboard (`MauiEditorClipboard`,
  called synchronously inside the WinUI `PreviewKeyDown` that ran the command; the chat's Copy called
  `Clipboard.Default` inside an `async void` click). Not provable here: no Windows record of the owner's crash exists, and
  every managed exception on that path was already caught by `ExecuteCommandAsync`, which points at a native failure of
  the clipboard inside the input callback or an exception escaping a WinUI callback (both fail-fast with 0xC000027B and
  skip every managed handler). Hardened where it lives: (1) `Craft.Editor.DispatchedClipboard`: every clipboard call is
  queued on the UI dispatcher (never inside the key or pointer handler that asked, never off the UI thread) and any
  throw, at once or later, becomes the task's failure, logged; `MauiEditorClipboard.Shared` is the one every view uses
  (editor, terminal, chat, panel, report). (2) Ctrl+C in a terminal copies the selection when there is one and ends it,
  else ^C goes to the shell (`TerminalManager.TryCopyOnCtrlC`, `CRAFT_TERMINAL_CTRL_C_COPIES`); a failed copy is said in
  Output. (3) No exception leaves a WinRT callback: `Craft.Core.Diagnostics.CallbackGuard` catches in the keyboard
  adapter's PreviewKeyDown, CharacterReceived and PointerPressed, the window's KeyDown and closing, the composer's keys,
  the terminal view's pointer handlers and every host dispatch (`EditorHost.Dispatch`); each failure is noted in the
  trail, written once per place and type as `callback-<time>.txt`, and logged. (4) ConPTY: `ClosePseudoConsole` (which
  can wait for its output to drain) runs off the UI thread, after waiting for a running exit callback, so the exit
  callback never reads a freed process handle; the exit callback is guarded. Audit: the other clipboard users (the
  editor's copy, cut and paste, the chat's Copy) go through the same dispatched clipboard.
- **21, the crash reporter that needs no hook** (`Craft.Core.Diagnostics`): `CrashWatch.Begin` (Craft.App's MauiProgram,
  before anything else) writes `run-<pid>.json` (pid, process, start, version, trail) to `CRAFT_CRASH_FOLDER`; the page's
  dispose (the clean exit) removes it and the trail. The next start finds a marker whose process is gone (same id, name
  and start time) and keeps it as `run-<pid>-<start>.died.json` with its trail renamed beside it (`CRAFT_CRASH_RUNS_KEPT`
  of them). `CrashTrail`: the breadcrumbs, a struct put on a bounded channel (nothing allocated per key: tested, 20,000
  keys), one writer off the UI thread appending to a ring of two files (`CRAFT_CRASH_TRAIL_KB`), flushed to the system
  after each batch; it holds the UI work labels (`UiWorkLabels.Trail`), the commands, the key names (never typed text),
  the first-chance exceptions (type, message, top frames; `CRAFT_CRASH_TRAIL_EXCEPTIONS` a second, cancellations left
  out), the Output lines and the crash dialog's crash (flushed before its message box). Windows' record:
  `Craft.Core.Abstractions.ICrashRecordSource` in the `CrashRecordSources` registry (library default `none`;
  `CRAFT_CRASH_RECORDS`, auto the platform's); Windows registers `windows-eventlog`
  (`Platforms/Windows/WindowsEventLogCrashRecords.cs`, EvtQuery/EvtNext/EvtRender through LibraryImport, read only), whose
  XML `EventLogCrashXml` reads (Application Error 1000 by the pid: module, code with its meaning, offset; Application
  Hang 1002; .NET Runtime 1023/1025/1026 by the app's name and the run's time, with the managed stack). The ticket:
  `Craft.Editor.Shell.ProblemReport` offers it at the start after an unclean exit (`CRAFT_CRASH_TICKET_OFFER`) and from
  Help › Report a problem (the run that died, or this one); it gathers off the UI thread as an activity
  (`CrashTicket.Gather`: the marker, the record, the run's crash, stall and callback reports, the trail's last
  `CRAFT_CRASH_TICKET_TRAIL_LINES`, the app, .NET and OS, the plug-ins with versions), writes `ticket-<time>.txt`,
  and shows it for review with Copy, Open folder and Open the report. The profile folder, the user and machine names and
  any e-mail address are replaced; nothing is sent. Every option declares its shape (Files › Crash reports; Terminal).
- **Tests**: groups `CrashWatch` (9: the trail's lines and repeats, its ring, no allocation per key, a died run found once
  and kept, the event XML, the ticket and its scrubbing, the offer, review and Copy, the registry, the callback guard)
  and `Copy` (4: the dispatched clipboard, the panels' text, Ctrl+C in a terminal, a failed copy logged), with
  `CrashReports`, `Terminal`, `Settings`, `Keys`, `Shell`, `ApiGuard`, `Inventory`.
- **Not compiled here** (Windows only): `src/Craft.App` (MauiProgram's `CrashWatch.Begin` and `Crashes`,
  CrashDialog's trail note and flush), `src/Craft.Maui/Platforms/Windows/WindowsEventLogCrashRecords.cs` (new),
  `WindowsTextInputAdapter.cs` (the guarded handlers), `WindowsShellPlatform.cs` (`CrashRecords`, the guarded KeyDown and
  closing), `ComposerKeys.cs` (the guarded PreviewKeyDown), `ConPtyTerminalHost.cs` (the dispose off the UI thread, the
  guarded exit).

## Notes 13 and 16 as built (2026-10-31)

- **Settings metadata** (`Craft.Abstractions/Settings.cs`): `SettingKind` (text, toggle, whole, number, choice, font,
  path, list, key bindings), `SettingSections` (Editor, Appearance, Files and workspace, C# and Roslyn, Models and AI,
  Completion, Agent and sandbox, Terminal, Voice, Learning, Plug-ins, Keyboard, in that order), `SettingShape` (section,
  title, group, range, step, unit, listed choices, a choice source, other values allowed), declared on every
  `CraftVariable` (`Shape`) and on `PluginSetting` (`Shape`). `SettingChoiceSources` (a registry, library defaults:
  themes, accents, folder watchers, memory monitors, inline completion providers, terminal hosts, keymaps, the bundled
  fonts; a platform or a plug-in registers over them). `PluginSettingShapes.Apply` places a plug-in's settings in a
  section and reads a switch or a number box off a default it does not shape; the AI plug-in (`AiSettingShapes`:
  Models and AI, Completion, Agent and sandbox), Learning, Voice, Sandbox and Tools name their sections; a plug-in that
  names none sits under Plug-ins. A test checks every option's declared range against its parser (min and max taken,
  one past refused) and every listed choice against it.
- **The page** (`Craft.Editor.Shell.SettingsPageModel`, headless; `Craft.Maui.SettingsPage`): the left list with the
  count set in each section, a search across every section (title, variable, words, section, group, plug-in), the
  scope (your settings or this workspace's) per change, a switch, a number box with − and + steppers, its range in the
  tooltip and its unit, a drop-down (with a box for another value where one is allowed), a font picker with a preview,
  text boxes in `CraftInput`; each change checked against its shape (a plug-in's too) and applied at once through
  `ShellModel.ApplySetting`; "modified · user / workspace / environment" marks, a reset per setting and per section;
  the environment's value and a file that may not set the variable read-only with why; the variable small in the code
  font; plug-ins' settings under the plug-in's name; the Models and AI section links the Models page, whose own left
  list now opens these sections. Narrow width: the list folds into a drop-down.
- **Keys** (`Craft.Abstractions`): `EditorKey` gained `Period`, `Slash`, `Minus`, `Plus` (the = key), `BracketLeft`,
  `BracketRight` (at the end); `KeyChord.DisplayText` ("Ctrl+/"; `ToString` keeps the names, the registry's keys);
  `KeyBinding` gained `Then` (a two-key chord, "Ctrl+K Ctrl+C") and `When` (`KeyContext.TextEditor`: only while the text
  editor has the keyboard, before its own keys; a terminal never runs it); `ParseKeys`, `FormatList` (round trip);
  `EditorKeyBindings.Find(first, then)`, `IsPrefix` (a set kept on registration: no scan per keystroke); `IKeyMap`,
  `KeyMap`, `EditorKeyMaps` ("vscode" the library's default, "visualstudio" registered by Craft.Editor). Options
  `CRAFT_KEYMAP`, `CRAFT_ZOOM_STEP`; `CRAFT_KEY_BINDINGS` applies live now (out of `AppliedAtStart`).
- **The router** (`EditorHost.HandleKey`): the user's binding; the first key of a two-key chord waits for the second
  (`PendingChord`, the status bar says "(Ctrl+K) was pressed"); the keymap's binding over the registered one; an
  overriding or a text-editor binding; the editor's own keys; then the rest. `SetKeyBindings` takes new user bindings
  and keymap at once; `KeyCapture` hands the keyboard page every chord while it records. A user's binding of a
  text-editor command stays text-editor only.
- **The shortcuts** (`StandardShortcuts`, registered by `ShellEditCommands` and `ShellModel`): new editor commands on
  `EditorModel` (`EditorModel.Lines.cs`, each one undo step): toggle line comment Ctrl+/, comment Ctrl+K Ctrl+C,
  uncomment Ctrl+K Ctrl+U, duplicate lines (Visual Studio: Ctrl+D; VS Code leaves Ctrl+D to X's multi-caret), delete
  lines Ctrl+Shift+K (VS: Ctrl+Shift+L), move lines Alt+Up/Down, copy lines Shift+Alt+Up/Down, indent and outdent lines
  Ctrl+] and Ctrl+[, select line Ctrl+L (again: the next line), expand and shrink the selection Shift+Alt+Right/Left (VS:
  Shift+Alt+= / -; word, the brackets' inside, the brackets, the lines, everything), go to line Ctrl+G (quick open's
  ':42:7'). Shell: save all Ctrl+K S (VS: Ctrl+Shift+S), reopen the closed tab Ctrl+Shift+T, close tab Ctrl+F4 too,
  next and previous tab Ctrl+PageDown/PageUp (VS: Ctrl+Alt+PageDown/PageUp), zoom in, out, reset Ctrl+=, Ctrl+-, Ctrl+0
  (the code font's size in your settings, `CRAFT_ZOOM_STEP`; VS: Ctrl+Shift+. and ,), the palette F1 too (VS: Ctrl+Q),
  quick open Ctrl+P (VS: Ctrl+T), the explorer Ctrl+B (VS: Ctrl+Alt+L), quick fix Ctrl+. (VS: Alt+Enter too; the code
  actions the plug-ins offer at the caret), format Ctrl+K Ctrl+D beside Shift+Alt+F, keyboard shortcuts Ctrl+K Ctrl+S.
  As commands for the palette and the page: find next and previous (F3, Shift+F3), trigger completion (Ctrl+Space),
  parameter hints (Ctrl+Shift+Space). Already there and listed: the file commands, Ctrl+F/H, F12, Ctrl+J, Ctrl+`,
  Ctrl+Alt+I, Ctrl+Shift+P. Not registered (no command yet): rename F2, peek Alt+F12, find in files Ctrl+Shift+F (P6),
  go to symbol, multi-caret (X).
- **The composer's keys** (the owner's run: the composer sent on focus loss, 04de0b7 took that away): `KeyContext.Composer`
  and `Craft.Editor.Shell.ComposerInput` (the composer as the keys' target; `EditorHost.KeyTarget` tells a command where
  the key came from): Enter sends (`craft.composer.send`), Shift+Enter a new line, Escape leaves (the editor takes the
  keyboard), each remappable on the Keyboard page; a key no binding takes stays the box's; Enter in the editor or
  anywhere else is never a send. `Craft.Maui.ComposerEditor.RouteKey`; on Windows `Platforms/Windows/ComposerKeys.cs`
  (an EditorHandler mapping: the TextBox's PreviewKeyDown hands Enter, Escape and Ctrl or Alt chords to the route, and
  a key it takes never reaches the box), configured in `WindowsShellPlatform`'s one constructor.
- **The Keyboard section** (`KeyboardModel` headless, `Craft.Maui.KeyboardSection`): every command with its keys and
  where each comes from (yours, keymap, default, built in: the editor's own), search by words or by pressing the keys,
  conflicts marked (keys over another command's, a key that is the first of a two-key chord, the editor's key taken),
  + records a chord by pressing it (two for Ctrl+K Ctrl+C) or typing it, the conflict said before Save, × removes (a
  default becomes "Keys=" in your bindings; the editor's own key is given to another command, not removed), ↺ resets a
  command, Reset all, the keymap drop-down; saved in `CRAFT_KEY_BINDINGS` (your settings file), applied at once.
- **Tests**: groups `Settings` (5: every option's shape against its parser; the sections and controls from the
  metadata; a change live in the chosen file, refused outside its range or choices, resets; the environment read-only;
  a plug-in's settings under it, checked and read live) and `Keys` (7: chords and lists round trip; the composer's Enter, Shift+Enter, Escape and a remap to Ctrl+Enter; the standard
  shortcuts registered as declared with no conflict; two-key chords, the text-editor context, a terminal keeping its
  keys, the Visual Studio keymap live, an unregistered keymap said; recording, remapping, removing and resetting through
  the settings file and back; every line command; go to line, zoom, reopen, the Keyboard section opened), with
  `Shell`, `Design`, `Layout`, `Editor`, `PluginKit`, `ApiGuard`, `Inventory`.
- **Not compiled here**: `src/Craft.Maui/Platforms/Windows/WindowsTextInputAdapter.cs` (the new keys' VirtualKey arms:
  0xBE, 0xBF, 0xBD, Subtract, 0xBB, Add, 0xDB, 0xDD), `Platforms/Windows/ComposerKeys.cs` (new) and the one line added
  to `WindowsShellPlatform`'s constructor. Every other file compiled (Craft.Maui on net10.0 with the tests).
- **Not yet**: a platform's installed font families as a choice source (the font picker offers the bundled ones and
  takes any typed); a second chord without Ctrl or Alt (Ctrl+K S) is caught by the window only when the editor has the
  keyboard; the Keyboard section draws every command's row at once (about a hundred).

## Note 15 and phase X's first slice as built (2026-10-31)

- **States on every control** (one mechanism): `Craft.Editor.Shell.InteractionStyle` makes each kind's look (flat,
  secondary and primary buttons, rows, menu items, rail items, tabs, chips) in each state (rest, hover, pressed, keyboard
  focus with the accent's ring, disabled) from the theme's tokens only; `Craft.Maui.InteractiveStates` applies it (pointer
  enter, exit, press and release, focus, `IsEnabled`), a focus the pointer gave shows no ring. Every button
  `ShellPalette.Flat`/`SecondaryButton`/`Primary` makes has it; the rail, the editor tabs, the bottom panel's tabs, the
  tree rows, quick open's rows, the dialog's list rows, the menus' rows, the bell and the status bar's position use it
  too; `CraftInput` adds hover (the secondary text's border) and disabled; a theme change re-applies every control's
  states in one walk. A losing action keeps the error's colour in every state.
- **Tooltips from the bindings**: `EditorHost.KeysOf(command)` is the keys in effect (the user's, else the keymap's,
  else the registered binding's, else the editor's own key, each only while no layer above took those keys), and
  `EditorHost.KeyBindingsChanged` says when they change; `ShellTooltips.For` writes "Explorer (Ctrl+B)". The rail, the
  narrow title bar's buttons, the search box's hint, the tabs' close, the bottom panel's collapse, the chat's close, the
  notification centre's close, the bell and every status bar item have one; menus show the same keys
  (`ShellMenus.Rows`). A remap on the Keyboard page shows at once.
- **Empty states** (`ShellEmptyStates`, drawn by `Craft.Maui.EmptyStateView`): the explorer with no folder (Open
  folder, Open a file, the recent folders), Problems with none (no file, a file no language checks, not checked yet,
  checking, none found), the chat and the agent before the first message (`IConversationPanel.EmptyText`, the AI
  plug-in's words for each, and which model answers, or Models and devices when none is set), the notification centre,
  and quick open with no match (a line, a command, no folder, listing, none).
- **Context menus** on `EditorMenus` under `EditorContextMenus.Editor`, `.Tree` and `.Tab` (never title-bar menus); a
  plug-in adds items the same way. `MenuModel` (headless) holds the open menu, the title bar's too: rows with the keys in
  effect and whether each can run on the target, Up and Down (and Home, End, a letter) over the rows that can run,
  Enter runs, Escape closes, a chord closes and goes on; a picked row runs its command with `EditorContextMenus.Target`
  set while it starts; nothing runs until a row is picked. Drawn by `MenuView` (the raised card, the groups' lines, the
  keyboard's row on the selection) in the popup layer, kept inside the window. The editor's: Cut, Copy, Paste, Go to
  definition, Find references (new: `ISemanticLanguageSession.FindReferencesAsync`, Roslyn's in the C# plug-in, Shift+F12,
  `CRAFT_REFERENCES_LIMIT`, as an activity, then a list), Format document, Quick fix, and the AI plug-in's selection and
  problem actions (registered by the plug-in, run only when picked); a right-click inside the selection keeps it;
  Shift+F10 opens it at the caret with the first row focused. The tree's: New file, New folder, Reveal in File Explorer,
  Open in terminal, Copy path, Copy relative path, Rename, Delete (confirmed through `CraftDialog`, to the Recycle Bin on
  Windows, said "for good" where there is no trash; a dirty tab stays open). The tab's: Close, Close others, Close to
  the right, Copy path, Copy relative path, Reveal in the explorer, Reveal in File Explorer. New commands:
  `craft.explorer.newFile`, `.newFolder`, `.rename`, `.delete`, `craft.revealInFileManager` (Shift+Alt+R),
  `craft.openInTerminal`, `craft.copyPath` (Shift+Alt+C), `craft.copyRelativePath`, `craft.closeOtherTabs`,
  `craft.closeTabsToTheRight`, `craft.revealInTree`, `craft.findReferences`, `craft.contextMenu`; the disk's work runs off
  the UI thread. New contracts: `IShellFiles` (the system's file manager and trash; `WindowsShellFiles`),
  `IShellUi.PromptAsync`/`ConfirmAsync` (Craft's dialog), `DialogRequest.Confirm`.
- **Motion**: `Motion.Duration` (`CRAFT_MOTION_MS`, 160) and `CRAFT_REDUCED_MOTION` (off; on, every transition is a
  jump), both declared in `CraftOptions.Variables` (Appearance, Motion). The side view, the chat column and the bottom
  panel grow open and shrink closed (`SplitPanel.SetVisible`); the notification centre, toasts and menus fade and slide
  in and out (`ShellMotion`); the completion list unfolds from its first row (`EditorModel.CompletionShownHeight`, the
  hit test keeps the whole box).
- **Smooth scrolling**: `SmoothScroll` (pixel offsets eased to a target, a wheel turn adding to it, clamped, nothing
  allocated per frame: test-checked) over `CRAFT_SCROLL_MS` (120; 0 or reduced motion jumps), `CRAFT_SCROLL_WHEEL_LINES`
  (3). The editor draws the moving offsets (only the visible lines laid out, as before) and the scroll view follows at
  the end; jumps of more than a line (Page Down, Go to line, a search hit) glide, the line typing pushes in view does
  not. The mouse wheel reaches it through `IEditorScrollSink` (Windows' adapter); a precision touchpad keeps WinUI's own
  panning.
- **Tests**: group `Experience` (9: every kind's states differ by state on both themes; tooltips and menu keys follow a
  user remap, a keymap and an unbinding; the menus' keys and that only the picked command runs, on its target; the
  tree's and tabs' commands on disk and on the tabs; the empty states; motion off when reduced and the options
  declared; the completion list unfolding; the smooth scroll's offsets and no allocation; a right-click keeping the
  selection), `Semantic.FindReferencesListsTheDeclarationAndEveryUseAcrossFiles`, with `Editor`, `Shell`, `Keys`,
  `Settings`, `LookAndFeel`, `Layout`, `Design`, `AiPlugins`, `ApiGuard`, `Inventory`.
- **Not compiled here** (Windows only): `Platforms/Windows/WindowsTextInputAdapter.cs` (the wheel handler: one field,
  its add and remove, `OnWheel`) and `Platforms/Windows/WindowsShellPlatform.cs` (the `ShellFiles` property and the
  new `WindowsShellFiles` class; the one constructor untouched).
- **Left of X**: following the system's own reduced-motion setting (only `CRAFT_REDUCED_MOTION` now); tabs moving and
  the menus' motion when they close; direct manipulation (drag tabs, files, drops); rename in place and delete with Undo
  (a dialog now); context menus in Problems, the chat and agent messages, the terminal and Output (the other slots');
  pin a tab; run the selection in the terminal from the editor's menu; the inline overlays (find, palette with fuzzy
  highlights); the living editor (minimap, sticky scroll, folding, guides, multi-caret, column selection); the welcome
  page; plain-word errors; the conversation items; and the measured input-to-response numbers.

## Usability before features (the owner's order, 2026-10-31)

The owner: the editor must be dynamic, friendly, usable and interactive before any more features. So the order from
here is: U and U2 (running), the owner's notes above (broken first, then look and feel, then shortcuts and the C#
project's languages), then phase X below; only then features again (P batch 2 on `wip/p-batch-2`, more plug-ins, M, L,
A3). No agent starts a feature while an item here is open.

### Phase X: the experience pass

Each item is checked by the owner on Windows, against the design (`docs/design/`), and measured where it can be:

- **Every action answers at once.** A click, a key or a drag shows its effect within one frame (or a pressed state
  and a spinner when the work takes longer, through the activities); nothing looks dead. Hover, pressed, focus and
  disabled states on every control, from the design's tokens; a visible focus ring for the keyboard.
  **States and focus ring: built (2026-10-31)**; the measured frame for each interaction is still owed.
- **Motion that explains.** Short transitions (panels opening and closing, tabs moving, the notification centre, the
  completion list, toasts), 120-200 ms, off when the system asks for reduced motion (`CRAFT_REDUCED_MOTION`).
  **Built (2026-10-31)** but tabs moving and the system's own setting.
- **Direct manipulation.** Drag tabs to reorder and to split the editor; drag files in the tree to move them and onto
  the editor to open; drag the panels' splitters; drop a file or an image onto the chat; double-click a tab strip's empty
  space for a new file.
- **Context menus everywhere**: the editor (cut, copy, paste, go to definition, find references, rename, format, AI
  actions on the selection, run selection in terminal), the tree (new file, new folder, rename inline, delete with undo,
  reveal in Explorer, open in terminal, copy path), the tabs (close, close others, close to the right, pin, reveal),
  the problems, the chat and agent messages (copy, apply, retry).
  **Editor, tree and tabs: built (2026-10-31)** but rename in place, delete with Undo, pin and run selection in terminal;
  the problems', the messages' (and the terminal's, Output's) come with the other slots' copy work.
- **Inline, not modal.** Rename in place (tree and symbols), find and replace as an overlay in the editor, the command
  palette and quick-open as one overlay with fuzzy highlights, recent items and keyboard navigation (arrows, Enter,
  Escape), confirmations as an inline bar with Undo rather than a dialog where possible.
- **The editor itself feels alive**: smooth scrolling (built 2026-10-31), a minimap or an overview ruler with errors and search hits,
  sticky scroll of the current type and member, code folding, indentation guides, matching-bracket and current-word
  highlights, multi-caret (Alt+click, Ctrl+D), column selection, line and selection moves (Alt+Up/Down), auto-indent
  and auto-close that never fight the user, hover cards that stay while the pointer moves into them.
  **Slice 2 built (2026-10-31): multi-caret, column selection, code folding, indentation guides, the matching-bracket
  and current-word highlights, auto-indent and auto-close** (see "X slice 2 as built" below). Left of this bullet:
  smooth scrolling (slice 1), the minimap or overview ruler, sticky scroll, hover cards that stay.
- **Friendly when empty or wrong**: a welcome page (open a folder, recent folders, start from a template, the shortcuts
  that matter, the model state), empty states that say what to do (no folder, no problems, no model, no terminal),
  errors in plain words with the action that fixes them (a model that fails to load, a plug-in that failed, a save
  refused), never a raw exception in front of the user.
- **The chat and the agent feel like a conversation**: streaming that scrolls, markdown rendered (code blocks with
  colours, copy and apply), the model and its device chosen at the composer, a stop button while it writes, retry and
  edit-and-resend, the agent's steps as a live list (thinking, a tool call, a command waiting for approval) that the user
  can open, the context shown as removable chips.
- **Settings that read like an app's**, not a list of variables: sections, real controls, search, the effect shown
  live, the variable name small for those who want it, and a shortcuts page with remapping (the owner's note 16).
- **Measured**: the time from input to visible response for each interaction above (the UI watchdog's numbers and a
  bench of the shell's interactions), written here; no interaction over one frame without a pressed state and a spinner.

Checks: the owner uses Craft on the test project for a session (open, edit, search, refactor, chat, run the agent, the
terminal) and every item above holds; the measured numbers are written here.

### X slice 2 as built (2026-10-31): the editing surface

- **Multi-caret** (`Craft.Editor/EditorModel.Carets.cs`). The primary selection is `Selection`; `Selections` holds all,
  in order, merged where they overlap; `SetSelections`, `AddCaret`, `AddSelection`, `CollapseCarets`. Alt+click adds a
  caret (on one of several, takes it away; a drag from it selects), Ctrl+D (`AddNextOccurrence`: the word at the caret
  first, then its next whole-word place, wrapping; a selection the user made matches anywhere; case matched),
  Ctrl+Shift+L (`SelectAllOccurrences`), Ctrl+Alt+Up/Down (a caret above or below), Escape back to one caret (before the
  find highlight and the selection). Typing (auto-close and step-over per caret), Backspace, Delete, Enter (its
  indent), Tab, paste (as many lines as carets: one at each, else the whole text at each; never auto-closed), cut, copy
  (one line per selection), the movement keys (each caret keeps its own column on Up/Down), and the line commands
  (comment, uncomment, toggle, move, copy, duplicate, delete, indent, outdent: every caret's lines merged into blocks)
  act on every caret. Each is one change and one undo step: a per-caret plan into pooled buffers (`EditCarets`,
  `ApplyParts`), then one place commits every edit of the editor to the document (`CommitEdits`, where note 26's
  read-only check sits). New contract `IMultiSelectionDocument` (`Craft.Abstractions`): Craft.Core's `TextDocument` keeps
  every caret of a step, restores them on undo and redo, and joins multi-caret typing into one step the way single-caret
  typing joins; a document without it restores the primary. The completion list and ghost text stay single-caret (they
  close while there are several). The caret limit is `CRAFT_MAX_CARETS` (10,000), said in `LastCaretMessage`.
- **Column selection**: Shift+Alt+drag (the box from the press to the pointer, per shown line, clamped to short lines)
  and, by keys, Ctrl+Shift+Alt+arrows in the VS Code keymap (VS Code's own keys: its Shift+Alt+Up/Down copy lines and
  Shift+Alt+Left/Right expand and shrink, so they stay so) and Shift+Alt+arrows in the Visual Studio keymap (Visual
  Studio's box selection, over its copy-lines and its unbound Shift+Alt+Left/Right). The box's selections are carets.
- **Keys** (`StandardShortcuts`, so the Keyboard page shows and remaps them): VS Code's Ctrl+D, Ctrl+Shift+L,
  Ctrl+Alt+Up/Down, Ctrl+Shift+Alt+arrows, Ctrl+Shift+[ and ] (fold, unfold), Ctrl+K Ctrl+0 and Ctrl+K Ctrl+J (fold all,
  unfold all); the Visual Studio keymap keeps Ctrl+D for duplicate and Ctrl+Shift+L for delete lines, takes Shift+Alt+.
  for the next occurrence, Shift+Alt+arrows for the box, and Ctrl+M Ctrl+M (toggle), Ctrl+M Ctrl+O (fold all), Ctrl+M
  Ctrl+L and Ctrl+M Ctrl+P (unfold all). New palette and Edit-menu commands: add the next occurrence, select all
  occurrences, add a caret above/below, the four column moves, back to one caret, fold, unfold, toggle, fold all, unfold all.
- **Folding**. Contract (`Craft.Abstractions/Folding.cs`): `FoldingRange` (header line, last hidden line, kind: region,
  comment, imports, marked), `FoldingRequest`, `IFoldingRangeProvider` (its languages; asked on the thread pool) and the
  `FoldingRangeProviders` registry (a third party folds any language; the editor joins every provider of a language,
  one range per header, the longest). Providers in the C# plug-in, one part each, gone with it: `CSharpFolding` (Roslyn's
  tree: namespaces, types from the line after their attributes, members and accessors with bodies, statements' blocks,
  local functions, lambdas, switch, initializers, collection expressions, using runs, `#region`, comments over several
  lines; the session's last parse reused when it is of the asked version, else parsed there), `MarkupFolding` (MSBuild,
  XML, XAML: elements, long start tags, comments) and `JsonFolding` (objects, arrays, block comments), both one pass over
  the lines with a state carried across them. The editor (`EditorFolding`) asks `CRAFT_FOLDING_DELAY_MS` (300) after
  the last change, off the UI thread, and applies the answer on the UI thread when the text is still that version; in
  between the ranges and the folds follow the edits (`MapRanges`: one pass over prefix sums of the edits' line deltas).
  `FoldMap` holds the folds (nested ones kept inside an outer one) as merged hidden intervals with the count hidden
  before each: a line's row, a row's line, the next shown line and whether a line is hidden are binary searches over the
  intervals, never a walk over the folds. Folded lines are not laid out, classified or drawn: the renderer steps rows,
  and classifies each run of shown lines on screen apart. The gutter has a chevron column (a click folds or unfolds);
  a folded header shows "..." after its text; the line numbers stay the lines' own (they jump). Up/Down and PageUp/Down
  step over folds; any caret a search, go to line, Ctrl+D, a click or an undo puts on a hidden line unfolds what hides
  it; a fold that hides a caret moves it to the header's end; an edit that reaches hidden lines unfolds them (an edit
  never happens out of sight). `CRAFT_FOLDING` switches it off.
- **Indentation guides, the matching bracket, the current word** (each an option on the settings page, Editor ›
  Display: `CRAFT_INDENT_GUIDES`, `CRAFT_BRACKET_HIGHLIGHT`, `CRAFT_WORD_HIGHLIGHT`). New theme tokens, each derived
  from the theme's own colours so a plug-in's theme draws them in its colours: `IndentGuide`, `WordHighlight`,
  `FoldChevron`, `FoldMarker`. A guide at each indent level of the leading white space; a blank line takes the smaller
  level around it, so the guides run through it. The current word: the whole-word places of the word at the caret on
  the lines on screen (one caret, nothing selected), found once per caret and version.
- **Auto-indent and auto-close, checked and fixed where they fought**: Backspace in an empty pair it inserted deletes
  both (a pair the user typed is left alone); the auto-inserted closes now follow deletions too (before, a Backspace
  inside "(ab" forgot the ")" and the next ")" was typed twice); a '}' or ']' typed where only white space precedes it
  takes the indent of the line that opened it (after Enter in a block the close went at the inner indent); pasting
  never auto-closes (was already so, now tested). Enter's indent and the step-over were right and are unchanged.
- **The document**: a held Backspace or Delete grouped into one undo step concatenated the whole removed run again on
  every key (quadratic, one caret too); the removed text now gathers in a buffer with room at both ends
  (`TextDocument.HistoryEdit.Grow`), each key copying its own characters only.
- **Measured** (headless, Linux container, Release, the tests' fixed metrics; tests `MultiCaret.AKeystrokeAtAHundredCaretsIsMeasured`
  and `Folding.APaintOfAFolded100000LineFileDrawsAndClassifiesOnlyTheRowsOnScreen`):
  - A keystroke at 100 carets (400 typed, 400 Backspaces): median 191 us, p99 3.3 ms, 314 KB allocated; of it the
    document's change alone (the same changes replayed into a `TextDocument`) 136 us and 312 KB; the editor's own work
    about 1.7 KB (0.5%). Its paint (21 lines, 21 carets) median 384 us, p99 1.5 ms.
  - A folded 100,000-line file (10,000 folds, every block folded to its header): fold all 5.3 ms; a paint of 21 rows
    median 9.7 us, p99 27.8 us, 273 characters classified a frame (only the rows on screen).
- **Left of X's editing surface**: the document's multi-edit change copies a path of the persistent piece tree per
  edit and fragments it a piece per caret per keystroke (the 312 KB above, and a tree that grows while typing at many
  carets): a batched build in `PieceTableBuffer` is the next step there, measured before and after. Completion and
  ghost text at several carets; a fold's own hover preview; folding providers registered after a document opened apply
  when it opens again; Razor, .sln and .editorconfig have no folding provider. The owner's Windows check below.
- **Not compiled here**: nothing. No Windows-only line changed: Alt+click and Shift+Alt+drag read the modifiers the
  Windows input adapter already reports with the press (`WindowsTextInputAdapter.Modifiers()` through
  `IKeyModifierSource`), and Ctrl+Shift+[ and ] are the bracket keys it already maps.
- **Tests**: groups `MultiCaret` (7), `Folding` (6), `Surface` (2), with `Editor`, `Keys`, `Settings`, `Document`,
  `ProjectLanguages`, `Shell`, `Plugins`, `CSharp`, `Diagnostics`, `AiPlugins`, `Semantic`, `LookAndFeel`, `Design`,
  `Ui`, `Layout`, `ApiGuard`, `Inventory`: 191 passed, 0 failed (after the rebase over slice 1 and notes 19-21).
- **The owner's check** on `test-output\CarftTestPOC`: Alt+click three carets and type, Backspace, Ctrl+Z once; Ctrl+D
  on a name a few times, then type; Ctrl+Shift+L; Shift+Alt+drag a box and type; paste three copied lines at three carets;
  Alt+Down with two carets; the gutter chevrons on a class and a method, Ctrl+Shift+[ and ], Ctrl+K Ctrl+0 and Ctrl+K
  Ctrl+J; Ctrl+G or a search into a fold; fold in the `.csproj` and an `appsettings.json`; the guides and the word
  highlight on and off in Settings › Editor › Display; with auto-close on, "(" then Backspace, and Enter in a block then "}";
  the Visual Studio keymap's Shift+Alt+. and Ctrl+M Ctrl+M.
### X slice 3 as built (2026-10-31): inline, not modal; the welcome page; direct manipulation

- **One overlay for quick open and the command palette** (`Craft.Editor.Shell.QuickOpen`, `OverlaySelection`;
  `Craft.Maui.QuickOpenOverlay`), replacing the `CraftDialog` list (the old `CommandPalette` and the narrow layout's prompt
  and pick are gone): Ctrl+P (files), Ctrl+Shift+P and F1 ('>' commands), Ctrl+G (':' a line, ':42:7'); the title bar's
  search box opens it under itself (across the top when narrow or when the title bar is the window's caption) with what was
  typed. Fuzzy matching with the matched characters drawn in the accent (`FuzzyMatch.Score(query, text, positions)` and
  `FuzzyMatch.Runs`: the name's runs, or the folder's and the name's when the path matched); recent first
  (`CRAFT_QUICK_OPEN_RECENT`, 20: every file opened, from anywhere, and every command picked; the recent list's files seed
  it at start; an empty query lists them in the order used, a query ranks them a little ahead; marked "recent"); each
  command's keys read from the bindings in effect (slice 1's `EditorHost.KeysOf`: the user's, then the keymap's, then the registered,
  then the editor's own; a remap or an unbinding shows at once); file icons by language (`Craft.Abstractions.FileIcons`, a
  registry by extension with the library's defaults for the files a C# developer opens, a language plug-in registers its
  own, `Plain` for the rest); Up and Down (wrapping), Page Up and Down, Ctrl+Home and Ctrl+End, Enter, Escape (the focus goes
  back to the editor). The list is a virtualized `CollectionView` (`SelectionMode.None`, the selected row drawn by the
  overlay, no checkbox), holding ten pages of `CRAFT_QUICK_OPEN_ROWS` and showing one. A file list longer than
  `CRAFT_QUICK_OPEN_BACKGROUND_FILES` (2,000) is matched on the thread pool (`QueryInBackgroundAsync`), the rows made on
  the UI thread; a newer keystroke cancels the older match, whose rows never land. The input is a `RoutedEntry`: its keys
  go to the overlay before the text box (on Windows a PreviewKeyDown, `Platforms/Windows/EntryKeys.cs`).
  **`@` (the current file's symbols) is left out**: no language session gives document symbols (`ILanguageSession` has
  `ScopeAt` only, and `FindSymbolsAsync` is the workspace's); '@' is part of a file name until a document-symbols contract
  exists.
- **Measured** (`Inline.QuickOpenMatchesTenThousandFilesOffTheUiThread`, Linux container, Release, a 10,000-file list,
  8 queries × 5 rounds, the background rows proven the same as the calling thread's, in order): cold (first query, the JIT)
  8.6 to 24.5 ms; warm median 2.3 to 2.9 ms; warm worst 5.8 to 24.6 ms (one GC). Written to
  `test-output/benches/quick-open-10k.txt`. The owner's Windows number is still to be taken.
- **Find and replace as an overlay** (`Craft.Maui.FindOverlay` in the editor's top-right corner, over `EditorFind`; the
  old find bar in `ShellPage` and its `_query` are gone): Ctrl+F, Ctrl+H (with the selection as the query); match case,
  whole word and regular expression toggles (Alt+C, Alt+W, Alt+R; on in the accent's fill); "3 of 17" (`FindCount.Current`,
  `FindCount.Text`; "No results"; "Invalid" with the parser's reason in the tooltip); previous and next (Shift+Enter and
  Enter, F3); Replace (Enter in its box) and Replace all (Ctrl+Alt+Enter), one undo step, said in the status bar; typing
  selects the first match from the caret; Escape closes, the highlights go and the editor takes the keyboard. A regular
  expression (`FindPattern`, immutable, any thread) runs one line at a time on the line's span (`EnumerateMatches`, no
  string), a match of no characters is never one, `$1`/`${name}` go into a replacement, and a line over
  `CRAFT_FIND_REGEX_TIMEOUT_MS` (250) is stopped and said. The matches of a snapshot are listed once (up to
  `CRAFT_FIND_COUNT_LIMIT`) and kept while the version and the pattern stay, so the count and next and previous read the
  list (a binary search); a document over `CRAFT_FIND_BACKGROUND_CHARS` (200,000) is listed, searched and replaced off the
  UI thread (`CountInBackgroundAsync`, `FindAsync`, `ReplaceAllInBackgroundAsync`), the result taken only if the snapshot's
  version did not change. Every match is drawn by its own layer (`FindHighlightLayer`, one call per visible line from the
  renderer), apart from slice 2's layers.
- **The welcome page** (`WelcomePageModel`, `Craft.Maui.WelcomeView`, a page tab `ShellPageIds.Welcome`): Open folder…
  and New file; the recent folders (the user's recent list; a click opens one, a gone one is taken off and said; × removes
  it with Undo); the main shortcuts read from the bindings (a remap shows); "Show the welcome page at start"
  (`CRAFT_WELCOME_AT_START`, declared with its toggle shape, written to the user's settings, read-only with why when the
  environment sets it). It opens at start when nothing was asked to open and no folder is open (else a new file, as
  before), and from Help › Welcome (`craft.welcome`). Two columns that wrap into one at narrow widths.
- **Direct manipulation**: a document's tab dragged onto another moves there (`DocumentTabs.Move`, `MoveOnto`: after it
  from the left, before it from the right; the active tab stays); a file's row dragged from the tree onto the window opens
  it; a double click (double tap) on the tab strip's empty space makes a new file; files and a folder dropped from the
  system's file manager open (`ShellModel.OpenDroppedAsync`: files in tabs, the first folder as the workspace, the rest
  said in the status bar; the disk checks off the UI thread). The drop's paths come from
  `IShellPlatform.DroppedPathsAsync` (Windows: the drag's storage items, under a deferral; the rest of the logic is in
  Craft.Editor).
- **Inline confirmations with Undo** (`UndoBar`, `Craft.Maui.UndoBarView` at the bottom of the editor area,
  `CRAFT_UNDO_BAR_MS` 5,000): the notification centre's Clear all (`ShellModel.ClearNotifications`, also the command
  `craft.clearNotifications`; `NotificationCenter.TakeAll`/`Restore`) and a recent folder's removal
  (`ShellModel.RemoveRecent`, `RecentList.Restore` at its place) act at once, say so with Undo, and keep the change when
  the time is up or × is pressed. Closing an unsaved tab keeps the `CraftDialog` question.
- **Options added** (each with its shape, on the settings page): `CRAFT_WELCOME_AT_START`, `CRAFT_UNDO_BAR_MS`,
  `CRAFT_FIND_BACKGROUND_CHARS`, `CRAFT_FIND_REGEX_TIMEOUT_MS`, `CRAFT_QUICK_OPEN_BACKGROUND_FILES`,
  `CRAFT_QUICK_OPEN_RECENT`.
- **Tests**: group `Inline` (6: fuzzy ranks and highlight runs, prefixes, recent first, keys from the bindings and a remap,
  the list's keys; 10,000 files off the thread with the same rows; find's current match, regular expressions, groups in a
  replacement, replace all as one undo, a large document listed, stepped and replaced off the thread; the welcome page's
  recent folders, Undo, the shortcuts and the switch, Help › Welcome, Clear all with Undo; tab reorder and drops; the undo
  bar's time), with `ApiGuard`, `Inventory`, `Editor`, `Design`, `Keys`, `Settings`, `Shell`, `Notifications`,
  `LookAndFeel`, `Layout`.
- **Not compiled here** (Windows only): `src/Craft.Maui/Platforms/Windows/EntryKeys.cs` (new), the `EntryKeys.Configure()`
  line in `WindowsShellPlatform`'s one constructor, and `WindowsShellPlatform.DroppedPathsAsync`.
- **Left of X after this slice**: `@` symbols (a document-symbols contract first); dragging a tab to split the editor;
  dragging files in the tree to move them; a drop onto the chat; rename in place of symbols; the overview ruler's search
  hits; the measured input-to-response numbers of each interaction on the owner's machine (the 10,000-file number on
  Windows among them); the rest of X's items belong to slices 1 and 2 or are not started (sticky scroll, the minimap, the
  chat's markdown and steps).

## The X restart (planned 2026-11-01): note 27, parts 4 and 5, the rest

Planned by a read-only agent against Craft `main` and Idrak `main` (2026-11-01); "(seen)" was read in the code, "(guess)"
is for the slot to confirm. Starts after B1-B7 merge. Every new drawing layer draws only through `IEditorCanvas`, so it
takes B1's line-layout cache and caret layer; no layer asks for a full redraw on the blink; each layer is its own class
the renderer calls, as `FindHighlightLayer` is.

**Decisions (the owner: "go with the recommendations, noted"):** the tolerant read is a helper after the turn, used by
`CodingAgent` and the CLI; the reinforcement environment is unchanged (it trains the template's format); sticky scroll
reads the folding ranges; the minimap is off by default with the overview ruler on, until the owner's Windows frame
number is in; peek is an overlay under the line; a rename's unopened files are opened in the background, shown in the
diff and saved on accept; split right and down, up to 4 groups; one contract step to the next minor version for the
whole restart, taken by the first slot that merges an addition (after B3's step); a tree drag moves with an Undo bar,
Ctrl copies; the chat's `File.ReadAllText` on the UI thread (found) is fixed in 1b; completion at several carets applies
where the text before the caret matches the primary's, the rest said.

### Note 27: tool calls written as text (Idrak first, then Craft)
- Cause (seen): the JSON parser reads calls only between the template's markers (`ToolCallParsers.cs` ~219-224), a bare
  answer only when it starts with `{` or `[`; `CodingAgent.LoopAsync` ends the run on the first reply with no call; the
  CLI's `agent` has its own loop (`AgentCommand.cs`).
- Idrak: `ToolCallText.CallsInText(text, ToolCallContext)` in `Idrak.Abstraction/Generation/ToolCallParsers.cs`: fenced
  blocks first, then bare text; the first complete JSON value from the first `{` or `[` (the existing `JsonValueEnd`),
  trailing junk ignored; each value `{name, arguments}` or a list; every name an offered tool, else the value stays text;
  sync span parsing once per turn (rules 8, 17, 74). `AgentOptions.CallsInText` (true), `AgentOptions.Nudges` (2): a turn
  with no call but call-like text gets a correction in the template's own format and the run goes on; out of nudges, the
  new `CodingAgentEnd.CallsNotRead` with a note. `CodingAgentRun.Nudges` an init property. CLI: `--nudges N`,
  `--strict-calls`. Tests: the owner's reply verbatim gives three calls; an example naming no tool stays text; fenced
  calls run; a nudged model writes the file; the limit ends with `CallsNotRead`; public API, inventory.
- Craft: `agentNudges` (2, 0-20) and `agentCallsInText` (true) in `AiSettings` (Agent section,
  `CRAFT_PLUGIN_AI_AGENTNUDGES`, `CRAFT_PLUGIN_AI_AGENTCALLSINTEXT`), read in `AiAgentPanel.RunAgentAsync`; the end note
  says "The model wrote no tool calls; nothing was changed." or how many nudges were given. Tests (`AiPlugins`): fenced
  calls end in a diff of one undo step; a run with no calls says nothing changed; the setting reaches the agent.

### Note 27, Idrak part, as built (2026-11-01)
- Cause (confirmed in the code): Idrak's "json" tool-call parser reads a call only between the template's markers
  (`<tool_call>…</tool_call>`), and a bare answer only when it starts with `{` or `[`; the reply's markdown ```json fences
  stayed answer text, and `CodingAgent.LoopAsync` (and the CLI's own loop in `ChatResponder`) took the first reply with no
  call as the final answer, so the run ended after one turn with nothing written.
- Idrak `4f394d5` on `main`: `ToolCallText.CallsInText(text, ToolCallContext)` and `ToolCallText.LooksLikeCall` (with a
  span `JsonValueEnd`) in `Idrak.Abstraction/Generation/ToolCallParsers.cs`; `AgentOptions.CallsInText` (true),
  `AgentOptions.Nudges` (2), `AgentOptions.Template`; `CodingAgentEnd.CallsNotRead`; `CodingAgentRun.Nudges`; the
  after-turn helpers `CodingAgent.CallContext`, `CodingAgent.ReadCallsInText`, `CodingAgent.Correction`, shared by the
  agent's loop and the CLI's; CLI `agent --nudges N` (2) and `--strict-calls`, its JSON output counting `nudges` and
  `calls_not_read`. API dumps and the inventory regenerated; no version or CHANGELOG change.
- Measured: text with no call (prose, a C# fence, a JSON example naming no offered tool) reads in 0 bytes once warm
  (100 runs, in the test); only the calls taken are built (values checked as UTF-8 in a pooled buffer first).
- Tests (CPU): Idrak `chat templates|coding agent|coding tools|cli run: tools|public API|inventory` 27 passed, 0 failed
  (after the rebase onto s2's fine-tuning commit); Craft built against it, `CRAFT_FILTER="AiPlugins|Ai"` 49 passed.
- Decisions (the recommended option each time, the owner asleep):
  - The template comes from `AgentOptions.Template`, else the model's own when it is a `ChatGenerator`, else none (a
    generic example); Craft wraps the model (`CountingChatModel`), so piece 1b passes the lease's template there.
  - Nudges count in a row and start again after a reply whose calls were read; `CodingAgentRun.Nudges` is the total.
  - Call-like means a ```json or bare fence opening with `{` or `[`, or `"name"` followed by an offered tool's name;
    a C# fence or plain prose ends the run as answered, as before.
  - Fences of any language are read when their body opens as a call (`{"` or `[{`); the offered names are the gate.
  - Bare text: every call-shaped value in order, not only the first; a list not taken stays text whole; an object not
    taken is searched inside (a call nested in a wrapper is found).
  - Trailing commas and comments are allowed; arguments under the template's key, "arguments" or "parameters", or as a
    JSON string.
  - The reply keeps its text as the model wrote it, with the calls attached (the transcript stays truthful).
  - A reply cut at the token limit gets no correction (out of budget, as before).
  - The correction's example: in the markers for the "json" format, the bare JSON when the template's answer is the call,
    a generic line for other formats or an unknown template; `write_file` when offered (its required arguments as
    "..."), with the list of offered tools and how to say the task is done.
  - `CallsNotRead` is the enum's value 3 (the others keep their numbers); the transcript's figures add `nudges` only when
    there were any, so reinforcement transcripts are unchanged; the reinforcement environment is unchanged.
  - The CLI's `chat` and `run` are unchanged (no text calls, no corrections); `agent` prints one line on stderr when a
    task ends with calls not read.
- Left for piece 1b (Craft): `agentNudges` and `agentCallsInText` into `AgentOptions` (with `Template` from the lease),
  the end note for `CallsNotRead` and for a run with no tool calls, and its `AiPlugins` tests.
- The owner's commands (the House run asks before `dotnet build`; MODEL is note 27's coder model, its id or folder):
  ```powershell
  cd D:\Projects\Idrak; git checkout main; git pull origin main
  dotnet build -c Release tests\Idrak.Tests
  $env:IDRAK_DEVICES="cpu"; $env:IDRAK_FILTER="chat templates|coding agent|coding tools|cli run: tools|public API|inventory"; dotnet run -c Release --no-build --project tests\Idrak.Tests -- --progress --failures-only --failures-file test-output\fails.txt
  New-Item -ItemType Directory -Force test-output\agent-house | Out-Null; dotnet run -c Release --no-build --project src\Idrak.Cli -- agent MODEL "Create C# class For House with float 3 props and + float price" --workspace test-output\agent-house --nudges 2
  ```

### X part 4
- **Document symbols**: a default member `ILanguageSession.GetDocumentSymbolsAsync(snapshot, ct)` (none by default) and
  `DocumentSymbol(Name, Kind, Start, Length, NameStart, NameLength, Detail, Children)`; `ScopeKind` gains Property, Field,
  Event, EnumMember. C#: `CSharpLanguageSession.Symbols.cs` walks the last parse once per version on the pool, cached.
- **`@` in quick open**: `QuickOpenMode.Symbols`, Ctrl+Shift+O; fuzzy over a name array made once per version (rule 15);
  `@:` groups by kind; Up/Down preview, Escape goes back; off the UI thread over the background threshold.
- **Breadcrumb**: the path in the workspace, the file, the scopes; middle parts fold to "…" by measured width; each part
  clickable (a folder lists its entries in the overlay, a symbol opens `@` on its siblings).
- **Sticky scroll**: `StickyScrollLayer` over `EditorFolding`'s sorted ranges, a binary search a frame, no allocation;
  `CRAFT_STICKY_SCROLL` (on), `CRAFT_STICKY_SCROLL_LINES` (5); a click moves to the header.
- **Overview ruler**: `OverviewRuler` at the viewport's right edge, `CRAFT_OVERVIEW_RULER` (on),
  `CRAFT_OVERVIEW_RULER_WIDTH` (14); errors, warnings, search hits, carets bucketed in one pass into a pooled array,
  rebuilt only when they change (rules 73, 75); tokens `RulerError`, `RulerWarning`, `RulerFind`, `RulerCaret`; click
  and drag scroll. (guess) beside or under WinUI's scroll bar: the owner's check.
- **F2 rename**: default `ISemanticLanguageSession.PrepareRenameAsync` / `RenameAsync` returning a `WorkspaceEdit`; C#
  through Roslyn's `Renamer`; `RenameOverlay` over the name; the edit computed off the UI thread as the activity
  "Rename"; a new default `IEditorHost.ProposeWorkspaceAsync(WorkspaceEdit, title)`: the diff lists every file, Accept
  all, each open document one undo step, the undo bar "Renamed in N files"; F2 (and Ctrl+R Ctrl+R in the Visual Studio
  keymap).
- **Hover cards that stay**: the pointer inside the card keeps it; leaving closes after `CRAFT_HOVER_HIDE_MS` (300);
  select, copy, wheel inside; "Go to definition"; the fold's preview of `CRAFT_FOLD_PREVIEW_LINES` (12) lines.
- **Faster typing at many carets**: `PieceTableBuffer` splits once before the first and after the last edit, flattens
  the middle into a pooled array, appends all inserted text in one run, joins continuing pieces, coalesces small adds
  under `CRAFT_PIECE_COALESCE_CHARS` (64), rebuilds the middle balanced; from `CRAFT_PIECE_BATCH_MIN_EDITS` (8) edits.
  Proven by random multi-edit equivalence (rule 71); measured against slice 2's 191 us / 314 KB.
- Tests: `DocSymbols`, `Inline` (extended), `Breadcrumb`, `StickyScroll`, `OverviewRuler`, `SymbolRename`,
  `HoverCards`, `Buffer` and `MultiCaret` (extended), `ApiGuard`, `Inventory`.

### X part 5
- **Split editor**: `EditorGroups` (each its own `DocumentTabs`, one focused); find, breadcrumb, quick open, status bar
  and the agent's active document follow the focus; a document in two groups keeps one text and one language session
  (shared, counted); drop zones from the drag position; Split right (Ctrl+\), Split down, Move to next group, Close
  group, Ctrl+1/2/3; `CRAFT_EDITOR_GROUPS_MAX` (4); only the focused view blinks.
- **Tree move by drag**: folder rows are drop targets; the move off the UI thread; clean tabs follow; Undo bar; Ctrl
  copies; a cross-volume folder move refused and said.
- **Drop onto the chat**: default `IConversationPanel.AcceptDropAsync(paths, ct)`; text files become `path:` chips,
  images `image:` chips read off the UI thread up to `chatImageMaxMb`; a model without images greys the chip with the
  reason; the agent takes dropped files as `@path`. Fix found: `AskAsync` reads extra files on the UI thread.
- **Models page per role**: each role's card edits its own enabled, model, device, "Same as chat", state, settings link;
  `ModelRole.EnabledSetting` added.
- **More folding**: `RazorFolding`, `SolutionFolding`, `EditorConfigFolding` in `Craft.CSharp/Project/ProjectFolding.cs`;
  `FoldingRangeProviders.Changed` so a provider registered later applies without reopening.
- Tests: `EditorGroups`, `TreeMove`, `ChatDrop`, `ModelsPage`, `MoreFolding`.

### X rest
- **Minimap** (`MinimapRenderer`, `MinimapView`): `CRAFT_MINIMAP` (off), `CRAFT_MINIMAP_SCALE` (2), `CRAFT_MINIMAP_MAX_COLUMNS`
  (120); a rect per coloured run from a bounded per-line cache; redrawn on scroll, edit or theme only.
- **Completion and ghost text at several carets**: one request at the primary; accept applies at every caret whose text
  before it matches, the rest said; one undo step; `CRAFT_COMPLETION_ALL_CARETS` (on).
- **Peek definition** (Alt+F12): an overlay under the line holding a read-only editor; a closed file read off the UI
  thread with syntactic colours only (no workspace built).
- **Input-to-response times**: `InteractionClock` stamps inputs and closes them at the next painted frame into the
  telemetry table; `CRAFT_INTERACTION_LOG` writes `test-output\timings\interactions.txt`; the command "Craft: Measure
  interactions" replays a fixed script and writes p50, p95, max per kind.
- Tests: `Minimap`, `CaretCompletion`, `PeekDefinition`, `InteractionTimes`.

### X part 6: the Search view and the Run view (the owner, 2026-11-01)
The owner: "the search icon on the left side bar only opens the search command; it should have a search tool" and
"there is no run page that configures and runs based on the current workspace's available runners and builders".
- **Search view** (seen: the rail's Search runs `ShellCommandIds.QuickOpen`, `ShellModel.cs:1263`). The rail's Search
  opens a real Search side view (U2's side views), Ctrl+Shift+F: a query box with match case, whole word and regular
  expression (the find overlay's `FindPattern`), include and exclude globs, `CRAFT_WORKSPACE_IGNORE` kept (and
  `.gitignore` read), results grouped by file (file icon, count, collapsible) with each line's match highlighted, a
  click opens the place; Replace and Replace all in files as one diff the user accepts (open documents one undo step
  each), the Undo bar after. The search runs off the UI thread over the files in parallel (rule 79), reads bytes and
  searches with `SearchValues`/`IndexOf` spans (rules 17, 74), streams results in batches as they come, is cancelled by
  the next keystroke (`CRAFT_SEARCH_DELAY_MS`), shows as an activity, and caps results (`CRAFT_SEARCH_MAX_RESULTS`,
  `CRAFT_SEARCH_MAX_FILE_MB`). Open documents are searched in their current text, not the disk's. Contract
  `IWorkspaceSearchProvider : IKind` and `WorkspaceSearchProviders` ("text" built in), so P6's code search later adds its
  results to the same view instead of a second one. Measured: a cold and a warm search over the test project and over
  Craft's own source size, with the time to the first result.
- **Run view** (none exists). A rail item "Run" opens a Run side view listing what this workspace can run, found off the
  UI thread and kept up to date by the folder watcher, each with Run, Build (where it applies) and Stop, and a picked
  default for F5 / Ctrl+F5 (run) and Ctrl+Shift+B (build). Contract `IRunProvider : IKind` and `RunProviders` in
  `Craft.Abstractions`, so every plug-in contributes its own:
  - the C# plug-in: every `.csproj` (by `OutputType`: run Exe projects, build and test the rest), `.sln`/`.slnx` (build,
    test), `launchSettings.json` profiles (command, arguments, environment, URL), `dotnet run/build/test/publish`;
  - W's npm plug-in: `package.json` scripts (and `ng serve` through them);
  - plain scripts: `.ps1`, `.cmd`/`.bat`, `.sh` at the workspace root and in a `scripts` folder.
  Configurations (name, provider, target, arguments, environment, working folder, before-run build) are edited in the
  view and saved in `.craft/run.json`; every run goes through a registered sandbox with approval (no silent run),
  streams to its own terminal tab, is an activity with Stop that ends the process tree, and its errors (MSBuild's and
  the compiler's `file(line,col): error CODE:` lines, npm's) land in Problems. Debugging is not part of this step (not
  asked).
- Decisions (recommended, noted): one Search view for text now and code search later; Run without debugging;
  configurations in `.craft/run.json`; the same keys as Visual Studio and VS Code (F5, Ctrl+F5, Ctrl+Shift+B) in both
  keymaps.
- Tests: `WorkspaceSearch` (results equal a plain scan, regex and globs, ignore files, open documents' text, cancel,
  replace in files as one diff and Undo, 0 bytes per line searched warm), `RunView` (providers found for a fixture with
  a .sln, two .csproj, launchSettings, package.json and scripts; a run through a fake sandbox; Stop; errors to Problems;
  `.craft/run.json` round trip).
- Slot: X part 6 runs in slot 3 right after the split editor (3a), before 3b.

### X part 7: rich context menus in the explorer and on the open tabs (the owner, 2026-11-01)
The owner: "I want a rich context menu both in the file explorer and the opened file item". Today (X slice 1) the tree's
menu has New file, New folder, Reveal in File Explorer, Open in terminal, Copy path, Copy relative path, Rename, Delete,
and the tab's has Close, Close others, Close to the right, Copy path, Copy relative path, Reveal in the explorer, Reveal
in File Explorer: plain text rows.
- **The menu itself**: each row with its icon (the icon theme's file and folder icons, and command icons drawn in the
  design's style), its keys in effect, a disabled row with the reason as its tooltip, groups with separators,
  submenus (hover or Right opens, Left closes), type to jump, and the modern look of the design (raised card, motion,
  no box inside a box). `EditorMenus` items gain an optional icon, a submenu and a "when" over the target (a file, a
  folder, a language, a project kind), so plug-ins add rich items the same way.
- **Explorer (file)**: Open, Open to the side (the split editor), Open with (a language picked), Reveal in File
  Explorer, Open in terminal; Find in folder (the Search view scoped there); Run and Build (the Run view's providers for
  a `.csproj`, `.sln`, `package.json` script, `.ps1`); Format file; Add to chat context, Ask the agent about it; Cut,
  Copy, Paste (files, off the UI thread, with Undo), Duplicate; Compare with selected (a diff of two files); Copy
  name, Copy path, Copy relative path; Rename in place (inline in the tree), Delete with Undo (Recycle Bin on Windows).
- **Explorer (folder)**: New file, New folder (inline names), Find in folder, Open in terminal, Collapse all inside,
  Run the folder's runners, Paste, Rename in place, Delete with Undo, Copy path.
- **Explorer (empty area and the workspace root)**: New file, New folder, Paste, Collapse all, Refresh, Open folder.
- **Open tab**: Close, Close others, Close to the right, Close saved, Close all, Reopen closed (Ctrl+Shift+T); Pin and
  Unpin (a pinned tab stays at the left and is not closed by Close others); Split right, Split down, Move to the other
  group; Copy name, Copy path, Copy relative path; Reveal in the explorer, Reveal in File Explorer, Open in terminal;
  Format file; Compare with saved (a dirty file's diff); Revert file (with Undo); Run and Build when the file has a
  runner; Add to chat context.
- Every action that touches the disk runs off the UI thread and shows as an activity when it lasts; every destructive
  one is undoable (the Undo bar) instead of a dialog where it can be.
- Decisions (recommended, noted): the git items come with X part 8's git plug-in; Compare uses the existing diff view; Reopen
  closed keeps the last `CRAFT_TABS_REOPEN_LIMIT` (20) closed tabs with their carets.
- Tests: `ContextMenus` (each target's rows and their enabled state, submenus and keys, a plug-in's rich item with its
  "when", paste, duplicate and delete with Undo, inline rename, pin and close-others, reopen closed, compare with
  saved).
- Slot: slot 3 after X part 6.

### X part 8: the Source Control view (the owner, 2026-11-01: "no source control side bar tool")
- A pre-installed, removable plug-in `Craft.Plugins.Git` on public contracts: `ISourceControlProvider : IKind` and
  `SourceControlProviders` in `Craft.Abstractions` (another system can register its own), a rail item "Source
  Control" with the count of changes as a badge, its side view, and the status bar's branch item (exists) clickable.
- It runs the user's own `git` (found on PATH or `CRAFT_PLUGIN_GIT_PATH`, MachineOnly; nothing native, no libgit2),
  every call off the UI thread, `--porcelain=v2 -z` output parsed as bytes (rule 74), refreshed by the folder watcher
  and on save, debounced (`CRAFT_PLUGIN_GIT_REFRESH_MS`, 500); the user's own actions in this view run without the
  sandbox's question (they are the user's, not a model's), each as an activity; the agent's git stays through its
  sandbox as now. No git: the view says so with what to do.
- **The view**: Changes and Staged changes grouped, each file with its icon, its state letter and colour (modified,
  added, deleted, renamed, untracked, conflict), a click opens the diff (the existing diff view, side by side, working
  copy against the index or HEAD), Stage, Unstage, Discard (with Undo where it can: the discarded text kept for the
  Undo bar's time), stage all, a commit message box (Ctrl+Enter commits, amend as a toggle), Pull, Push, Fetch, Sync,
  branches (switch, create, the current one with ahead and behind), stash and pop; merge conflicts listed with Accept
  current, Accept incoming, Accept both inline in the editor.
- **Generate the commit message** (the owner, 2026-11-01): a sparkle icon button in the commit message box (tooltip
  "Generate commit message", its keys; the palette's "Source Control: Generate commit message"). It reads the staged
  diff (else every change, said so) off the UI thread, trimmed to the commit role's context budget by file
  (`CRAFT_PLUGIN_GIT_COMMITDIFFTOKENS`, the largest files summarised by name and counts), and asks the chat model through
  the AI plug-in's public contract (a new `ICommitMessageProvider : IKind` registry in `Craft.Abstractions`, the AI
  plug-in registers "ai"; the git plug-in never names a model). The message streams into the box as an editable draft
  (never committed by itself), in the repository's own style (its recent `git log` subjects as examples,
  `_COMMITSTYLE`: auto, conventional, plain), with Stop while it writes. **It uses a model already loaded, with that
  model's own settings** (the owner, 2026-11-01: "using the current loaded models with settings"): by default
  (`_COMMITMODEL` = loaded) the first loaded of the chat, the agent, then the completion role's model, through its
  existing lease (never a second copy) and that role's temperature, context and reply limit; the button's menu lists
  the loaded models to pick one, and shows which it will use in its tooltip. Only when no model is loaded does it say
  so, offering "Load the chat model" (note 23's rule: a load only when asked). Settings (Source Control section):
  `_COMMITMODEL` (loaded, chat, agent, completion, or a model name), `_COMMITPROMPT`, `_COMMITSTYLE`,
  `_COMMITMAXTOKENS`. Tests in
  `SourceControl`: a fake model's message streams into the box from a fixture diff, the budget trims by file, Stop,
  no model said, nothing commits by itself.
- **In the editor**: gutter marks for added, changed and deleted lines against the index (from `git diff` of the open
  file, recomputed off the UI thread after a pause), a click on a mark shows the change inline with Revert and Stage;
  the overview ruler (X part 4) shows them too.
- **In the menus** (X part 7): the tree's and the tab's Stage, Unstage, Discard, Open changes, Copy remote link
  (later), Reveal in Source Control; the tree's file names coloured by their state.
- Options: `CRAFT_PLUGIN_GIT_PATH`, `_REFRESH_MS`, `_AUTOFETCH_MINUTES` (0: off), `_GUTTER` (on), `_CONFIRMSYNC` (on).
- Decisions (recommended, noted): the git CLI, not a native library; the user's own git actions without the sandbox's
  question; blame and history views later (not asked); GitHub and pull requests not part of this step.
- Tests: `SourceControl` (porcelain v2 fixtures parsed, including renames, conflicts, untracked and spaces in names;
  a real git repository made in a temp folder when git is on the machine, skipped with its reason when not: stage,
  unstage, commit, discard with Undo, branch switch; gutter marks from a fixture diff; the rail badge count).
- Slot: slot 1 after 1b (before 1c).

### X part 9: the modern-editor gaps (the owner, 2026-11-01: "any modern design gaps and missing helpful editor
features would be a huge support")
Each checked absent in the code and the plan (2026-11-01; "partly" where a piece exists). Present already, not added
again: zoom, expand selection, the quick-fix command, find references, hover, signature help, format, multi-caret,
folding, smooth scroll, the palette, the shortcuts page, the welcome page. Each is an option with its `CRAFT_*`
variable, drawn through B1's cached layouts, and a contract where a plug-in may add to it.
- **Editing helpers (first)**: 1 word wrap (Alt+Z, `CRAFT_WORD_WRAP`, wrapped rows laid out only on screen); 2
  bracket pair colours (`CRAFT_BRACKET_COLORS`, by depth, theme tokens); 3 code snippets (a `SnippetProviders`
  registry, tab stops and placeholders, C#'s `prop`, `ctor`, `for`, `foreach`, `if`, `try`, ... in completion, the
  user's own in `.craft/snippets` and the user folder); 4 inlay hints (parameter names, inferred types, from Roslyn,
  `ISemanticLanguageSession.InlayHintsAsync` for the visible lines only); 5 code lens (references count, Run and Debug
  test from the Run view's providers, `ICodeLensProvider`); 6 diagnostics at the line end (`CRAFT_INLINE_DIAGNOSTICS`);
  7 a lightbulb in the gutter where code actions exist (partly: the command exists); 8 render whitespace, vertical
  rulers (`CRAFT_RULERS` "80,120"), trim trailing white space and insert a final newline on save; 9 `.editorconfig`
  applied (indent style and size, line ending, charset, trim, final newline) per file (partly: coloured only); 10 linked
  tag rename in HTML and XML.
- **Navigation**: 11 an Outline view (X part 4's document symbols as a tree, following the caret); 12 call hierarchy
  and type hierarchy (Roslyn); 13 bookmarks (toggle, next, list, kept per workspace); 14 preview tabs (a single click in
  the tree previews in an italic tab the next preview replaces; an edit or a double click keeps it); 15 restore the last
  session (folder, tabs, carets, folds, layout, split groups; `CRAFT_RESTORE_SESSION`); 16 local history (a snapshot per
  save in the user's data folder, bounded, a timeline per file with compare and restore); 17 `file(line,col)` and
  `file:line` links clickable in the terminal and Output.
- **Tools**: 18 a Test explorer (tests found by Roslyn and by the Run view's providers, run one or all, results in the
  tree and as gutter marks, failures to Problems); 19 Markdown preview (side by side, following the scroll); 20 an image
  preview tab (png, jpg, gif, svg, ico; zoom); 21 a TODO/FIXME list from comments; 22 spell check in comments and strings
  (a word list, no native library; user words kept); 23 inline AI chat (Ctrl+I at the caret or on a selection, the
  answer as a diff, through the chat role's loaded model).
- **Look**: 24 a theme picker with live preview, more built-in themes and a high-contrast pair; 25 Zen and full-screen
  modes; 26 status bar items that change what they show (encoding, line ending, indentation, language) on click
  (partly: to verify).
- Decisions (recommended, noted): all 26, in the order above, after X part 8 and before W; debugging stays out (not
  asked). Tests: a group per piece (`WordWrap`, `BracketColors`, `Snippets`, `InlayHints`, `CodeLens`, `InlineDiagnostics`,
  `EditorConfigApplied`, `LinkedTags`, `Outline`, `Hierarchy`, `Bookmarks`, `PreviewTabs`, `SessionRestore`,
  `LocalHistory`, `OutputLinks`, `TestExplorer`, `MarkdownPreview`, `ImagePreview`, `TodoList`, `SpellCheck`,
  `InlineChat`, `ThemePicker`, `ZenMode`, `StatusPickers`). Slots: split by area over the three slots as each frees
  (editing helpers, navigation, tools and look).

### X part 10: the chat and the agent redesigned (the owner, 2026-11-01: "make both chat and agent windows modern; it's
good now but still basic; I can only copy one message, which is hard if I want several; redesign the chat in both; allow
it to expand to cover the full editor space, not just the right side, without overflowing the left side bar")
It takes in Phase X's bullet "The chat and the agent feel like a conversation". Both panels share one conversation view.
- **One selectable transcript**: the conversation is drawn as one read-only text surface (note 26's read-only editor
  surface, B1's cached layouts: only the visible messages laid out), so a drag selects across messages, Shift+click
  extends, Ctrl+A selects the whole conversation, Ctrl+C copies the selection as Markdown (code blocks fenced, the
  speakers named) or as plain text (Ctrl+Shift+C); each message keeps Copy on hover; "Copy conversation" and "Export
  conversation…" (Markdown file) in the panel's menu; a message's context menu: Copy, Copy as Markdown, Quote in reply,
  Retry from here, Edit and resend (the user's), Delete from here. No checkboxes.
- **Messages, modern**: the user's message as a soft bubble on the right, the model's as full-width text with its model
  and device small above it; Markdown rendered (headings, lists, tables, links, inline code), code blocks with the
  language's colours (the language plug-ins' tokenizers), a header with the language, Copy, Insert at caret, Apply as
  diff, and Run in terminal for shell blocks; long blocks folded with "Show all"; streaming with a caret and the
  newest text followed unless the user scrolled up (a "Jump to latest" chip then); Stop while it writes; tokens per
  second and the time small under a finished reply.
- **The agent's steps as a live timeline**: thinking, each tool call (its name, a one-line summary, open to see the
  arguments and output), a command waiting for approval with Approve and Deny inline, the files changed with their diff
  counts and Review, the run's end in plain words (note 27's "nothing was changed" when so); collapsible per step.
- **The composer**: a multi-line input that grows to `CRAFT_COMPOSER_MAX_LINES` (12), the context as removable chips
  (the open file, the selection, dropped files and images, `@` mentions of files and symbols with a picker), the model
  chip (note 11), Send and Stop, the history of sent messages with Up and Down when the input is empty, slash commands
  (`/explain`, `/fix`, `/tests`, `/clear`) listed as you type.
- **Expand to the editor area**: a button in the panel's header and the command "Chat: Expand" (Ctrl+Shift+Alt+M, in
  both keymaps) make the chat or agent take the whole editor area (the editor groups hidden, the tab strip showing the
  conversation as a tab-like title), between the rail and side view on the left and the window's right edge, never over
  the activity rail or the side view (it follows their widths as they resize, B8); the bottom panel stays below unless
  collapsed. Escape or the button restores the right-hand column at its kept width; the state is kept per workspace.
  On a narrow window the expanded view is the default (the layout follows the measured width).
- **Conversations**: a list of past conversations per workspace (title from the first message, time), New, Rename,
  Delete with Undo, search; kept in `.craft/chat.json` as now, read off the UI thread.
- Options: `CRAFT_CHAT_EXPANDED` (off), `CRAFT_COMPOSER_MAX_LINES` (12), `CRAFT_CHAT_COPY_FORMAT` (markdown),
  `CRAFT_CHAT_FOLD_CODE_LINES` (30), `CRAFT_CHAT_SHOW_METRICS` (on).
- Decisions (recommended, noted): one shared conversation view for chat and agent; the transcript as one selectable
  surface rather than a list of labels; copy as Markdown by default; expand covers the editor groups only.
- Tests: `Conversation` (selection across messages and Ctrl+A, copy as Markdown and plain, the message menu's actions,
  retry and edit-and-resend, streaming follows unless scrolled up, the agent's timeline items from a scripted run,
  approval inline, the composer's chips, history and slash commands, expand and restore keep the rail and side view
  and the widths, conversations list with Undo; only visible messages laid out, 0 bytes per warm frame).
- Slot: slot s4 right after piece 1b (it changes the same chat files), before X part 8.

### Slots and merge order
- Slot 1, AI: 1a note 27 in Idrak; 1b Craft's note 27, chat drop, Models page, more folding; then X part 10 (the chat
  and the agent redesigned); then X part 8 (Source Control); 1c (last) the interaction times.
- Slot 2, the surface: 2a batched build, hover cards and fold preview, ruler, sticky scroll; 2b minimap, completion at
  several carets.
- Slot 3, language and shell: 3a split editor first (the owner asked for it directly, 2026-11-01: "I need to split the
  editor screen"), then tree move and peek; then X part 6 (the Search view, the Run view), then X part 7 (rich context menus); 3b symbols, `@`, breadcrumb,
  F2 rename.
- Merge: 1a, then 2a, 3a (the split editor), 1b as they finish, then 2b and 3b, then 1c; 3a's `EditorModel.cs` change
  (a shared language session) rebases on 2a's. Shared files (`CraftOptions.cs`, the API dumps,
  `StandardShortcuts.cs`, `MenuModel.cs`): a dump that conflicts is regenerated, built once, `ApiGuard|Inventory` run.
- Windows-only (not compiled here): the wheel inside a hover card (guess), `IInputTimestamps`, Ctrl+\ if the adapter
  lacks the key (guess), Ctrl+1/2/3 and Alt+F12 checked in the adapter.

## Phase W: the web languages (planned 2026-11-01, starts when X is done)

Planned by a read-only agent against Craft `main` c3cfaed and Idrak `main` 5b5854d. "(confirm)" marks a version fact to
check against the release notes on day one; "(guess)" one for the slot to confirm.

**Decisions (recommended, noted):** one pre-installed, removable plug-in per language (`Craft.Plugins.Html`, `.Css`,
`.JavaScript`, `.TypeScript`, `.Angular`) plus `.Npm` and `.Lint`, the lexers in a plain shared library `src/Craft.Web`
copied into each (so HTML's `<script>` still colours with the JavaScript plug-in removed); Angular templates chosen by
project (`angular.json`) plus the `*.component.*` names; JSON moves out of the C# plug-in into its own pre-installed
`Craft.Plugins.Json`; automatic runs (lint on save) asked once per workspace and program, remembered in the user's data
folder (never the workspace), revocable on the settings page; the question's wording through Idrak's
`CommandLimits.Requester`; CSS formatted built in, Prettier where the project has it, through `DocumentFormatters`; a
project with no ESLint gets "Set up ESLint" through the sandbox; version precedence: the setting, then the project's
files, then latest; `node_modules` shown dimmed and collapsed, never indexed or searched (`CRAFT_WORKSPACE_SHOW_IGNORED`);
scripts that read input get the output tab plus "Open in terminal" after approval; the semantic layer is a later step
W-semantic, held for the owner's word (Idrak's process contract, an LSP client, `tsgo` where TS 7 is installed, else
`typescript-language-server`, then Angular's); npm now, yarn, pnpm and bun detected and said "not yet".

**What the code lacks today (seen):** `EditorLanguages.ForFile` and `FileIcons` work by extension only; folders have no
icons; `LineTokenizer`, `LineStateSession`, `ITextFormatter`, `FormatStyle` are internal to Craft.CSharp; a plug-in
cannot publish diagnostics, and Problems holds open tabs only; toggle comment does nothing without a line comment;
Idrak's "approve-each" words every question for "the agent"; no host event says the workspace changed.

**Modern design, rich icons, format and syntax everywhere (the owner, 2026-11-01: "I need a modern design with rich file
icons and format, syntax support").** A standing requirement for W and every later slot:
- **Modern look**: every new surface (tree, tabs, quick open, breadcrumb, Problems, npm and lint views, settings) follows
  the design's tokens (`docs/design/`) and what current editors do: no box inside a box, hover and focus states, motion,
  icons beside every file and folder, nothing that looks like a list of checkboxes.
- **Rich file icons**: today an icon is a 1-3 character text badge in one colour, by extension only (`FileIcon(Extension,
  Badge, Argb, Language)`). It becomes a drawn vector icon: `FileIcon` gains an optional shape (SVG path data, one or
  more layers each with its colour, drawn through MAUI Graphics' own path parser: rule 0, no package), kept beside the
  badge as the fallback; folder icons by name with open and closed shapes (`src`, `test`/`tests`, `docs`, `.git`,
  `.github`, `.vscode`, `.craft`, `node_modules`, `dist`, `bin`, `obj`, `assets`, `public`, `components`, `services`,
  ...); icons by exact name, compound suffix, glob and extension (contract 5 below); an icon theme as a registry
  (`FileIconThemes : IKind`, a built-in "craft" set, a plug-in can add another, a setting `CRAFT_ICON_THEME` picks
  one); light and dark variants from the theme. The built-in set is Craft's own drawing (no copied icon set, no vendor
  logo taken as-is), made for the design and checked by the owner on Windows. Shapes are parsed once and cached per
  size (rule 75); drawing an icon allocates nothing on a warm frame (rule 63). The icons show in the tree, the tabs,
  quick open, the breadcrumb, the welcome page, Problems and search results. The C# set (.cs, .csproj, .sln, .slnx,
  .razor, .xaml, .json, ...) is redrawn the same way.
- **Format support for every language**: Format Document (Shift+Alt+F) and Format Selection work for every language
  W adds, not only where a project has Prettier: built-in formatters (only white space between tokens changes, as XML,
  JSON and CSS do, so the caret keeps its place; a malformed file left unchanged with the reason) for JavaScript,
  TypeScript, JSX/TSX, HTML and Angular templates, CSS/SCSS/LESS; Prettier (the project's own install) used instead
  when the project has a Prettier config, said so; format on save per language (`CRAFT_FORMAT_ON_SAVE`); options for
  the indent, the tab size from the editor, and the line length where a formatter breaks lines. This replaces the
  earlier decision "CSS built in, Prettier elsewhere".
- **Syntax**: the latest syntax of each language (above), with brackets, folding, toggle comment, auto-close and
  indentation guides working in every one.

**Contracts first** (`Craft.Abstractions`, additions only, one minor contract step; B3's rule: no constructor removed):
1. The line-state kit public (`LineTokenizer`, `LineStateSession`, `ITextFormatter`, `FormatStyle`), the C# plug-in moved
   onto it; `LineStates` interns states over 64 bits, bounded (rule 77); a session can change dialect and raises
   `Reclassified`.
2. `ILanguageSession.RulesAt(snapshot, position)` (`<script>` toggles `//`, `<style>` `/* */`); toggle comment falls back
   to the block comment.
3. `ILanguage.FilePatterns` (exact names, `*` globs); `ForFile` ranks exact name, longest pattern, extension; span
   matching with alternate lookups, no allocation (rules 15, 17).
4. `ILanguageSelector : IKind` and `LanguageSelectors`, answering from a cache (no I/O on the call);
   `ForFile(path, workspaceRoot)`.
5. `FileIcons` by name, compound suffix, glob, extension, and `FileIcons.ForFolder(name)`; folder badges in the tree.
6. `IEditorHost.PublishDiagnostics(path, source, snapshot or null, items)`, merged per source; Problems for closed files.
7. `ILinter : IKind` and `Linters` (languages, `Unavailable` reason, detection off the UI thread, `LintAsync`); a fix is
   only a diff the user accepts.
8. `IDocumentFormatter : IKind` and `DocumentFormatters`, asked before the session's own.
9. `PluginSetting.MachineOnly` (not settable by a workspace: node and npm paths, global tools).
10. `IEditorHost.WorkspaceChanged`.
11. Idrak, on its `main`: `CommandLimits.Requester` (default "the agent"), used in "approve-each"'s question.

**The lexers** (`src/Craft.Web`: Script/, Style/, Markup/; nothing native, no regex, nothing allocated per token; one
state per line; keywords in per-dialect `FrozenSet` alternate lookups, specials in `SearchValues`, no boxing; rules 15,
17, 75, 88, 89):
- **JavaScript (ES2025 and later)**: regex or division by the previous significant token carried in the state;
  template literals with `${}` nested to any depth across lines; regex `v`, `d`, modifiers, duplicate named groups;
  `using`/`await using`, `accessor`, `#x in obj`, hashbang, import attributes, standard decorators, numeric separators,
  BigInt; `import defer`/`source` (confirm).
- **JSX/TSX**: elements as frames; `<T,>` generics in .tsx; plain .ts never lexes JSX; JSX in .js by the `jsx` setting
  (auto: react or preact in package.json).
- **TypeScript**: all of JS plus `satisfies`, `accessor`, const and `in`/`out` type parameters, `declare`, `namespace`,
  `abstract`, `override`, `keyof`, `infer`, `asserts`, `import type`, template literal types, .d.ts, .mts, .cts.
- **CSS** (CSS Syntax 3 tokens, known names from data lists updated on day one): nesting, `@layer`, `@container`,
  `@scope`, `@starting-style`, `@property`, `:has()`, `color-mix()`, `oklch()`, relative colours, anchor positioning,
  `if()` and `@function` (confirm); SCSS (`$var`, `#{}`, `@use`, `@mixin`, ...) and LESS.
- **HTML (living standard)**: raw text `<script>`/`<style>`, RCDATA; script types (module, importmap and JSON types as
  JSON, templates as HTML); the embedded lexer's state rides in HTML's; `style="…"` as CSS and `on*="…"` as JS.
- **Angular**: `{{ }}`, `[x]`, `(x)`, `[(x)]`, `#ref`, `*ngIf`/`*ngFor` microsyntax, `@if`/`@else`, `@for`/`@empty`,
  `@switch`, `@defer` with its blocks, `@let`; expressions in an Angular JS dialect (pipes, `?.`, `!`); inline
  `template:`/`styles:` in an Angular project's .ts as "angular-typescript".
- Colours through the theme's existing classes (no new class); folding by braces, `#region` markers, comment and import
  runs, JSX and HTML elements, Angular blocks.
- **Proven** (rule 71): thousands of random edits per fixture equal a fresh session; embedded spans equal standalone;
  the C# plug-in's ProjectLanguages tests pass unchanged on the public kit. **Measured** (rules 63, 76, 78): a warm
  screen of 60 lines at 0 bytes, lines re-lexed per keystroke, a backtick at the top of a 100,000-line TS file, folding,
  the paint through B1's cache; target (guess) a warm screen under 0.5 ms; results in `test-output/benches/web-*.txt`.

**Versions**: per language `version` (latest) and `projectVersion` (true); read from `tsconfig.json`/`jsconfig.json`
(`extends` followed, bounded), `node_modules/typescript/package.json` or `devDependencies`, `@angular/core`,
`angular.json`; as UTF-8 bytes with `Utf8JsonReader` (rules 74, 49), off the UI thread, once per folder; a version
changes colours only where an older one reads a newer word as plain. Day one confirms ES2026, TS 6/7, Angular 21+,
ESLint 9/10, typescript-eslint 8, CSS Baseline 2026.

**npm** (`Craft.Plugins.Npm`): Node from PATH or `CRAFT_PLUGIN_NPM_NODE` (MachineOnly), its version in a status item;
npm run as `node …/npm-cli.js` (guess on the layout), never `npm.cmd`; scripts from every `package.json` become
commands ("npm: run build (src/app)") in the palette and the tree's menu; every run through the sandbox (approve-each,
`Requester` "npm"), the question showing the script's text, output streamed to a read-only terminal tab, an activity
with Cancel that stops the process tree, `longRunning` scripts without a time limit; install (`install` or `ci`) as an
activity; `.npmrc` never logged.

**The linter** (`Craft.Plugins.Lint`): ESLint from the project's own install (flat config first, typescript-eslint and
angular-eslint as configured; `.eslintrc*` only with `ESLINT_USE_FLAT_CONFIG=false`, said), `--format json
--output-file` parsed as bytes; TSLint only with `tslint.json` and no ESLint config, said deprecated once; `linter` =
auto|eslint|tslint|off, `runOn` = save|manual, `delayMs` 500; "Lint workspace"; fixes as one diff; lint as you type
waits for W-semantic.

**Options** (`PluginSetting`s, `.craft/settings.json` except MachineOnly): `CRAFT_PLUGIN_{HTML,CSS,JAVASCRIPT,TYPESCRIPT,ANGULAR}_VERSION`,
`…_PROJECTVERSION`; `CRAFT_PLUGIN_HTML_EMBEDDED`, `…_ATTRIBUTESASCODE`; `CRAFT_PLUGIN_JAVASCRIPT_JSX`;
`CRAFT_PLUGIN_ANGULAR_TEMPLATES`, `…_INLINETEMPLATES`; `CRAFT_PLUGIN_CSS_FORMAT`; `CRAFT_PLUGIN_NPM_NODE`, `…_NPM`,
`…_SANDBOX`, `…_INSTALL`, `…_LONGRUNNING`, `…_TIMELIMITMINUTES`; `CRAFT_PLUGIN_LINT_LINTER`, `…_RUNON`, `…_DELAYMS`,
`…_ALLOWGLOBAL`, `…_TIMELIMITSECONDS`, `…_SANDBOX`; `CRAFT_PLUGIN_PRETTIER_*`; `CRAFT_WORKSPACE_SHOW_IGNORED`.

**Android and AOT**: `Craft.Web` and the language plug-ins are pure C# with no reflection (`IsAotCompatible`); Npm and
Lint use `Utf8JsonReader` only and say Unavailable on Android (no Node).

**Test groups** (all start "Web"): `WebKit`, `WebFiles`, `WebScript`, `WebStyle`, `WebMarkup`, `WebAngular`, `WebNpm`,
`WebLint`, `WebPlugins`; with `ProjectLanguages`, `Plugins`, `Folding`, `Editor`, `Settings`, `Shell`, `Inline`,
`ApiGuard`, `Inventory` as touched; Idrak's part with `IDRAK_DEVICES=cpu`.

**Slots** (at most 3; merge order W0, W-files, W-npm, then W-style and W-script, then W-tools, then W-markup):
- W0: the kit (1), `RulesAt` and the comment fallback (2), JSON to `Craft.Plugins.Json`, the empty `src/Craft.Web`.
- W-files: contracts 3, 4, 5; the rich icon theme (vector shapes, folders by name, `FileIconThemes`, the C# set redrawn);
  `node_modules` shown dimmed.
- W-npm: Idrak's `Requester` (11) first, then contracts 9 and 10 and the npm plug-in.
- W-script (after W0, W-files): JS, TS, JSX, TSX lexers and folding; the JavaScript and TypeScript plug-ins.
- W-style (after W0): CSS, SCSS, LESS lexers, formatter, folding; the CSS plug-in. Each lexer slot also writes its
  language's built-in formatter (W-script for JS, TS, JSX, TSX; W-markup for HTML and Angular).
- W-tools (after W-npm): contracts 6, 7, 8; the Lint plug-in; Prettier.
- W-markup (after W-script, W-style): HTML and Angular lexers, the Angular dialect, inline templates; the Html and
  Angular plug-ins with the selector.

**The owner's check** (Windows, Node installed): a new Angular project made under `test-output\WebTestPOC` (`npx -y
@angular/cli@latest new WebTestPOC --defaults --skip-git --style=scss`), opened in the app: icons in the tree and quick
open, colours of app.ts, app.html (with `@if`/`@for`), app.scss, index.html, main.ts, tsconfig.json, package.json;
brackets, folding, toggle comment; "npm: run build" and "npm install"; "Set up ESLint", a squiggle and its fix as a
diff, "Lint workspace"; Shift+Alt+F on the .scss; the Angular plug-in disabled turns .html to plain HTML; typing at the
top of a long .ts with the paint and keystroke numbers written here.

## P batch 2 resumed (planned 2026-11-01; last, once W is done)

Planned by a read-only agent from `wip/p-batch-2` (`74a5c21` on `e04652c`; `main` 47 commits ahead). The WIP holds P6
code search, P9 progress and telemetry, P10 workspace model names, P8 screenshot to code: 28 new files, 23 edited, never
built, no tests, no dumps. About 20 of its 23 edited files conflict with `main`, most of them duplicates `main` now has
(`IdrakInPlugins`, `IEditorHost.DataFolder`, `ModelRoleKeys`, `AiModels.AgentAsync`, U2's side views).

**Decisions (recommended, noted):** re-apply plug-in by plug-in on fresh `main` (copy each plug-in's folder from
`74a5c21`, rewrite the host side from its diff), never a rebase; P batch 2 starts once W is done; P10's role defaults
come from `.craft/settings.json` (no second mechanism; `ModelRoleSources` dropped); P9's figures go to a status item and
`host.Record` (no new static); the chat model's pull reports through the lease's `ModelLoadStatus` (one activity); P6
watches files through `IFolderWatcher` with content changes added as a default member; the rail's Search shows the first
query side view, else quick open; P6 chunks other languages by line windows with W's extensions now, folding ranges
later; `wip/p-batch-2` is deleted after the last slot merges; P8 is checked on a Hugging Face vision-language folder of a
family Idrak.Vision.Plugins has; P11 (Idrak's text shaper drawing the editor's lines) starts only if, after B1, a warm
full screen of C# has p95 above `CRAFT_P11_FRAME_MS` (8 ms) with line layout at least half of it, or the platform draws
a text wrong that Idrak's shaper draws right.

- **Shared**: `Craft.Abstractions` gains only `EditorImage`, `EditorImageSource`, `IEditorHost.ReadImageAsync`,
  `IConversationPanel.AttachImage`, `IQueryPanel : IEditorPanel` (default bodies): one minor contract step; P6 and P8
  state it, P9 and P10 the earlier one. Each slot adds its `api/Craft.Plugins.*.txt`; the last regenerates the inventory.
- **P10** (slot A): the "craft" `ModelSources` source from `.craft/model.json` (read on the pool at open and on the
  watcher's change, kept; no stat on the UI thread); the Models page's "Workspace names"; `craft:NAME` at the chip; the
  resolved target in the lease key. `CRAFT_PLUGIN_WORKSPACEMODELS_FILE`, `_WORKSPACE`. Group `WorkspaceModels`.
- **P9** (slot B, first): the "editor" `ProgressReporters` kind as an `IActivity` (counts only with no host); model
  measures (`model.tokensPerSecond`, `model.firstTokenMs`, `model.deviceMemory`) into the metrics table.
  `ProgressReporterSuite`. Group `ProgressBridge` (also the activities' items and messages).
- **P8** (slot B, second): pictures from the clipboard, a picker or a drop on the chat (on X's drop); `Idrak.Vision.Plugins`
  registered when the plug-in loads, not on the UI thread at activation; a text-only model refuses and keeps the picture.
  `ChatPartKindSuite("image")`, `VisionEncoderSuite`. Group `VisionChat`.
- **P6** (slot C): Roslyn chunks, camelCase terms, "code-bm25" keyword index (`KeywordIndexSuite` with `bm25` as the
  reference on prose, rule 71), optional embeddings fused by reciprocal rank, the index on disk updated per changed file;
  the Search view as an `IQueryPanel`; indexing as a cancellable activity; `FileIcons` on rows; Ctrl+Shift+F;
  `search_code` for the chat and the agent; the 21 `CRAFT_PLUGIN_SEARCH_*` settings in a Search section; bench `search`
  (cold and warm apart, p50/p99, memory over 1,000 updates). Group `CodeSearch`.
- **Merge order**: A, then B, then C. No Idrak change planned (`ExtraTools`, `ModelImages`, `ProgressReporters` are on
  Idrak `main`).


## The owner's third Windows run (2026-11-01): bugs first, then X, W and P batch 2

The owner's run on `c3cfaed` (the trail `trail-45356-20261101-022524`, its ticket and 34 stall reports, from
`CRAFT_CRASH_FOLDER`) and the owner's messages, in their order: phase W after X; the sample plug-in that did not load;
learning's failed training ("why does learning need Idrak.Audio or Vision?"); a long C# file that scrolls with lag, inline
completion off too; then resume P; and Craft's new build-and-test rule (in `CLAUDE.md`, Craft only). Analysed with no
code changed; every fix below follows Idrak's `docs/optimization.md` (the rule numbers named).

**Decisions (the owner, 2026-11-01: "1a 2a 3a 4a")**: this plan approved, and three planning agents (no code) write
the X restart, W and P batch 2 in detail below; ESLint 9 as W's linter, TSLint only where a project has `tslint.json`;
the bugs before the X restart; the full paint fix (a cached layout per line, the caret drawn alone). Every language
supports its latest syntax (the owner: "in every language of course use latest support").

### The bugs (B1-B5)

**B1, paint and scroll lag (found).** The trail holds 501 frames, each 53-68 ms ("editor: paint" to "editor: painted"),
one every 0.5 s while idle (the caret's blink) and one per step while scrolling, so a scroll runs at 15-18 frames a
second whatever completion does; the watchdog's 0.2-0.4 s "editor: paint" stalls are runs of them. Cause:
`EditorRenderer` calls `IEditorCanvas.DrawText` once per coloured run of every visible line, and `MauiEditorCanvas`
draws each with MAUI Graphics' `DrawString` (a box, alignment and overflow), which on Windows makes a new text format
and layout per call; every frame draws every visible line again, and the blink invalidates the whole view. Not the
cause: the code font (`CodeFontPicker.Ensure` returns at once after the first pick; it does build a key string every
frame, rule 75, removed too). Fix (the owner's 4a):
- The Windows canvas (`Platforms/Windows`, behind `IEditorCanvas`, nothing outside `Platforms/` changes platform) draws
  straight on Win2D's drawing session: one layout per shown line with its runs' colours and styles set on ranges, kept
  in a cache keyed by the line's text version, the theme and the font (rule 75), bounded to the lines on screen and a
  margin and dropped on memory pressure (rule 77); a scroll lays out only the lines it brings in. Check first that Win2D
  comes with MAUI already (rule 0: no package added).
- The caret, and the blink, drawn on a layer of their own: a blink invalidates the caret's cell only.
- Proven unchanged (rule 71): the tests' fixed-metric canvas records the same runs, places and colours before and after;
  on Windows a frame's pixels hashed before and after on a fixed file. Measured (rules 76, 78): cold and warm frames apart,
  through the telemetry table; a warm frame allocates nothing (rule 63). Target: a warm frame of a full screen of C#
  well under one frame (16 ms), written here with the owner's number.
- The same for the gutter's numbers and `PanelListView` (its "panel list: paint" stalls of 0.2-0.3 s, Output and
  telemetry) and the terminal, which draw through the same canvas.

**B2, the crash (0xc000027b, a stowed exception in `Microsoft.UI.Xaml.dll`, 02:28:51; not proven).** The trail's last
line is a paint that never reached "painted". Fix: a frame that throws is caught, logged with its type and step
(rule 90) and skipped, never let out into WinUI; the trail writes the inner exception. The next run's ticket names
the frame if it is not that.

**B3, the sample plug-in did not load (found).** `TypeInitializationException` from `SampleRegistration.Binding`:
`KeyBinding` gained two parameters (`Then`, `When`; notes 16 and the composer keys) while the plug-in contract stayed
"1.0", and the sample's DLL in `test-output\plugins` was built before (it is built outside the solution), so its call to
the old three-parameter constructor fails. Fix: the old constructor kept beside the new (no binary break); the API guard
marks a changed or removed public member of `Craft.Abstractions` as needing a contract version step; the loader shows
the inner exception ("MissingMethodException: KeyBinding..ctor(KeyChord, string, bool)") instead of the type
initializer's; a test loads a plug-in compiled against the older contract.

**B4, learning: "No architecture 'Qwen2ForCausalLM' is registered" (found).** `LearningLibraries.EnsureIncluded`
loads the plain Idrak libraries and registers only `csharp-coding`; it skips the plug-in packages, so
`Idrak.Text.Plugins` (Qwen2's family) is registered only by Craft.Ai, at its first model load. Since note 23 nothing
loads at start, so a training round before any chat load finds no Qwen2. Fix: learning registers the model families
itself before its first load, through the same `idrakPlugins` setting as Craft.Ai (one registration a process, rule 75);
test: a training round in a fresh process with no chat load finds the family. Also checked:
`LearningTrainer.Rewards`'s `GetAwaiter().GetResult()` never runs on the UI thread (rule 50).

**B5, "why does learning need Idrak.Audio or Vision?" (found: it does not).** Three things made it look so: Idrak's
message for a missing family suggests `Idrak.Vision.Plugins` to every caller; `IdrakAudioForConditionalGeneration` in
the list is the voice plug-in's; and Idrak's `LibraryDefaults.Find` probes every Idrak library by name at start (13
first-chance `FileNotFoundException`s in the trail: Audio, Vision, Diffusion, Cluster, Network, Onnx.Runtime, Mcp, ...).
Fix in Idrak (on its `main`, by its rules, `IDRAK_DEVICES=cpu` and an `IDRAK_FILTER`): probe only the libraries present
beside the program, once; the missing-family message names the package that holds the family asked for.

**B6, the agent made the file but never wrote it to the workspace (the owner, 2026-11-01, several prompts, the agent
over MCP).** Not yet traced. Likely the same root as the open item "the AI test that fails only on Windows (the agent's
working copy goes missing)": the agent works on a copy of the folder, and what it wrote there must come back to the
workspace as a diff the user accepts (no silent edit). The slot traces the whole path on the copy: tool write, the run's
end, the proposal, the diff view, accept, the file on disk; and the same through the app's MCP tools. A headless test
drives a scripted model that writes a new file and a changed file, accepts both, and finds them in the workspace, on a
workspace path with spaces and a Windows-style drive path where the code allows.

**B7, a learning run always fails (the owner, 2026-11-01: "whenever I try to run it there is an error, out of memory
or any").** Beyond B4's missing family: a training round must run end to end on the owner's 16 GB card beside the
loaded models. The slot runs a round headless on a tiny model (fresh process, no chat load) and fixes every failure on
the way, then plans memory: the trainer takes the role's lease (one copy of a model already loaded, never a second,
note 22's rule), lets idle models go before it loads, sizes its batch and sequence from the device's free memory
measured at run time (card-agnostic; options for each, `CRAFT_PLUGIN_LEARNING_*`), and on an out-of-memory error steps
the batch down and tries again, said in the activity, before it fails with `AiErrors.Describe`'s plain words and what to
change. Every failure names its stage (load, episodes, pairs, step, evaluate, promote). Device memory measured before
and after a round does not grow over several rounds (rule 77).

**B8, dragging a splitter to resize is hard (the owner, 2026-11-01: "the drag to resize is a pain and hard, make it
easy").** Causes (seen in `src/Craft.Maui/SplitPanel.cs`): the grip is 6 units wide and takes layout room; the pointer
never shows a resize cursor (MAUI has no cursor API); the drag is MAUI's `PanGestureRecognizer`, which on Windows starts
after a threshold and loses the drag when the pointer moves fast over the editor's canvas (no pointer capture). Fix:
- The grab area is wider (`CRAFT_SPLITTER_GRIP`, 10 units) and overlaps both neighbours instead of taking room; the
  visible line stays one unit and turns the accent on hover (after `CRAFT_SPLITTER_HOVER_MS`, 100) and while dragged.
- The resize cursor (west-east, north-south) on hover and through the drag, through a small contract
  (`IPointerCursor`, Windows: `ProtectedCursor` with `InputSystemCursor`, under `Platforms/Windows`).
- The drag by pointer press, move and release with pointer capture (Windows, under `Platforms/Windows` behind the same
  contract), so it starts at once and never drops; the pan gesture stays the fallback elsewhere.
- Double-click on a splitter restores the panel's default size; the size steps by keyboard too (focus the splitter,
  arrows, Shift for bigger steps); the drag snaps closed below the panel's minimum and open again past it.
- The editor beneath keeps painting smoothly while dragging (B1's cache): a resize lays out only the lines that change.
- Tests (`Layout`, headless on `SplitPanel`'s logic): clamping, snap, double-click reset, keyboard steps, sizes kept.
  Windows-only (not compiled here): the cursor and the capture. The owner's check: drag each splitter fast across the
  editor, double-click to reset, the cursor shows.
B8 goes first in the next free slot, before the X restart.

**B9, the terminal: one shell only, and an ugly design (the owner, 2026-11-01, with screenshots).** Seen: the shell is
`CRAFT_TERMINAL_SHELL` (a machine setting) else the host's default, which is Windows PowerShell; the tab shows the
program's full path (`C:\Windows\system32\WindowsPowerShell\v1.0\powershell.exe`); the text starts at the panel's very
edge; the empty state is a long sentence with a bare "+". Fix:
- **Shell profiles**: a contract `ITerminalProfileSource : IKind` and `TerminalProfiles`; on Windows the found shells
  (PowerShell 7 `pwsh`, Windows PowerShell, Command Prompt, Git Bash, each WSL distribution, Developer PowerShell and
  Command Prompt for Visual Studio where installed), found off the UI thread and cached; a profile has a name, a program,
  arguments, an icon, a start folder and environment. The user's own profiles in the settings
  (`terminal.profiles`). `CRAFT_TERMINAL_DEFAULT_PROFILE` picks the default, on the settings page (Terminal section) as
  a drop-down of the found profiles, and per workspace; `CRAFT_TERMINAL_SHELL` keeps working as a profile named by its
  path.
- **New terminal**: "+" opens the default; its chevron opens a menu of the profiles ("New PowerShell 7", "New Command
  Prompt", "New Git Bash", ...) and "Set default profile…"; the palette has "Terminal: New (profile)" and "Terminal:
  Select default profile".
- **Design** (the design's tokens, like current editors): tabs show the profile's icon and a short name ("pwsh", "cmd",
  the running program's name when it changes; rename by double-click), a close on hover, a busy dot while a command
  runs; with three or more terminals, a list at the right of the panel instead of tabs (`CRAFT_TERMINAL_TABS` auto, tabs,
  list); padding around the text (`CRAFT_TERMINAL_PADDING`, 8); the cursor style and blink as settings; a thin scroll bar
  that shows on hover; the empty state as an empty-state card (icon, "No terminal open", a primary "New terminal" button
  with its keys, the profiles under it); the terminal's context menu: Copy, Paste, Select all, Clear, Split, Rename,
  Kill. Split terminals side by side in the panel.
- Tests: `Terminal` (profiles found from a fake machine, the default chosen and kept, `CRAFT_TERMINAL_SHELL` as a
  profile, the menu's rows, a terminal started per profile through the "pipe" host, rename, the list layout from three
  terminals). Windows-only (not compiled here): the shell discovery's registry and path reads under `Platforms/Windows`.

**B10, "Roslyn ready" belongs in the notification centre, and a global loader while a workspace loads (the owner,
2026-11-01).** Seen: the status bar's text "Roslyn ready" (`ShellChrome.cs:92`) and the explorer's Roslyn card
("Workspace Ready, 1 project, 6 MB") stay on screen. Fix:
- When a language's workspace finishes loading, it posts one notification to the notification centre ("C# workspace
  ready: 1 project, 6 MB, 2.3 s", from the language's own status; never a toast unless it failed); a failure is a
  notification with its plain words and the action that fixes it. The status bar shows the language's state only while
  it loads or when it failed; the explorer's Roslyn card goes (its facts move into the notification and the language
  item's tooltip).
- **The global loader**: a thin progress line under the title bar across the window (indeterminate, or determinate
  when the work reports a fraction), with the work's name and percent in its tooltip and a click opening the
  notification centre's activities; shown for any activity of a new kind `ActivityKinds.Workspace` (opening a folder,
  reading the projects, a language building its workspace, indexing) and for any activity a plug-in marks
  `Global = true` (an additive member). Shown only after `CRAFT_LOADER_DELAY_MS` (300), so quick work does not flash;
  motion off with reduced motion. One loader whatever runs: it shows the most advanced of what runs and their count.
- Tests: `Notifications` and `Activities` (the ready notification posted once per load with its facts, the failure
  notification, the status text only while loading, the loader shown after the delay, hidden when all end, the count
  and the fraction).

**B11, the Learning view is a bare list that explains nothing (the owner, 2026-11-01, with a screenshot: "not
informative nor illustrative for what it does; it should help the users").** Seen: rows of text ("Learn on this
machine: off", "Share: off", "Folder", "Episodes: 0 (0 B of 64 MiB)", "Export the episodes", "Delete the episodes",
"Train now · preference · no model set", "Make tasks from the failing tests", "Mark the test at the caret", "Adapter:
none") with no controls, no state, no explanation. Redesign (the design's tokens, modern, no box inside a box):
- **What it is, first**: a header card that says in two plain sentences what learning does ("Craft can learn from how
  you accept, edit or reject the AI's suggestions, and train a small adapter so the model writes code more like you.
  Everything stays on this machine unless you choose to share."), with a three-step illustration (Record → Train →
  Better suggestions), each step drawn with its icon and showing where you are.
- **Guided start when off**: the empty state is a short checklist (1 turn on recording, 2 code with the AI and accept,
  edit or reject, 3 train when there are enough episodes: `CRAFT_PLUGIN_LEARNING_MINEPISODES`), each with its action
  button; "What is recorded?" opens a sample episode so the user sees exactly what is kept.
- **Controls, not text rows**: Record on this machine as a switch with its consent text under it; Share as a switch
  that asks for the destination inline; the folder with Open and Change; storage as a bar (used of the limit) with
  Export and Delete (Delete with Undo).
- **Training card**: the model it trains (a picker of the loaded models with their settings, B7's lease rule), the
  method (preference, with a one-line explanation of each), Train now as the primary button with its state (idle,
  waiting for enough episodes with the count, training with progress, time left and Cancel), "also when idle N min" as
  a switch with the minutes; after a round: the loss and the preference margin as small charts over the rounds, and
  whether the new adapter was better (promoted) or kept back, with Roll back.
- **Adapter card**: which adapter answers now (none: the base model), its age, how much better it scored, Use base
  model / Use adapter, Roll back.
- **Tasks from tests**: explained in one line ("turn failing tests into practice tasks the model is rewarded for
  solving"), a count of failing tests found, Make tasks (through the sandbox, said), and "Mark the test at the caret"
  shown only when the caret is in a test.
- Every row has a tooltip and the variable shown small (as the settings page); errors in plain words with the fix
  (B7's stage names).
- Tests in `Learning`: the view model's states (off, recording with too few episodes, ready, training, after a round
  promoted and not), the checklist's actions, Delete with Undo, the picker lists the loaded models.
- Slot: the next free slot after s2 merges (s2 changes the learning plug-in's training now), with B8-B10 if that slot
  is the same.

B8, B9 and B10 go together in the next free slot (the shell and its panels), before the X restart.

Also in the trail, for the slots: `WindowsShellPlatform.RouteKeys` threw "The window has no WinUI content yet" at start
(hook the keys once the page appeared), a WinRT `InvalidOperationException` at start, and three 0.2-0.3 s "unlabelled UI
work" stalls at start-up (the plug-ins' rail, menus and models; the window's resize) still to label and move off.

### B5 as built (2026-11-01)

Learning never needed Idrak.Audio or Vision. Two causes were Idrak's and are fixed in Idrak (`ed1a94f` on Idrak's
`main`). The third (the voice plug-in's `IdrakAudioForConditionalGeneration` in the list of registered names) is
correct and is left as it is.
- **The 13 first-chance `FileNotFoundException`s.** `LibraryDefaults.Find` loaded all 13 library names with
  `Assembly.Load`. Idrak's new internal `LibraryFiles` now reads the program's files once (rule 75): the host's
  trusted assemblies (the deps list) and the files beside Idrak.Abstraction. `Find` loads a name only when the program
  has it as a file or has already loaded it, so a missing library throws nothing. A host that gives no files
  (single-file, a phone's assembly store) loads every name as before. This works the same on every platform (rule 70).
  Rule 71: a test shows that a host with every library finds exactly what loading each name finds.
- **The message that suggested Vision to everyone.** Each plug-in package now declares the architectures it registers
  with `[assembly: IdrakPluginNames(nameof(PretrainedArchitectures), …)]` (Text, Vision and Audio; the constants come
  from their own family classes). A new contract, `IPluginPackageCatalog`, has a `SlotTable` registry,
  `PluginPackageCatalogs`. Its library default, "present", reads those declarations from the loaded assemblies and from
  the program's files (metadata only, without loading them, once). An app can register a catalog of packages it does
  not ship. The refusal now names the package that declares the name ("Idrak.Text.Plugins declares it: call that
  package's RegisterAll() …"). When no package declares it, the refusal names no package.
- Decision: presence is read from the host's deps list plus the folder beside Idrak.Abstraction, not by trying a load
  (the recommended option; no exception, platform-neutral, behaviour unchanged where nothing can be read).
- Decision: package names come from attributes the packages declare and the library reads, never from a table of
  families in the library (Idrak's "no family-specific code in the library").
- Decision: the catalog contract lives in `Idrak.Abstraction.Plugins` and is listed in Idrak's `shared-contracts.txt`,
  because any package's refusals can use it; today only `PretrainedArchitectures` does.
- Decision: Idrak's `-P` test said Idrak.Mcp declares nothing, but Idrak.Mcp now declares library registrations, so
  the test failed before this change. It now uses Idrak.AspNetCore, which declares neither.
- Craft: no source change was needed. `PluginProbe.RunInstalled` now counts first-chance exceptions whose stack passes
  through `LibraryDefaults`, and `EveryPreinstalledPluginLoadsFromTheAppsLayout` checks there are none. The test
  runner lacks 5 of the 13 libraries (Vision, Diffusion, Cluster, Network, Onnx.Runtime), so the old code would have
  thrown 5 there.
- Results: Idrak `IDRAK_DEVICES=cpu`, filter `plugins: packages|override loop|kind table|public API|abstraction
  inventory`: 26 passed. Craft `CRAFT_FILTER=Plugins|PluginKit|PhaseP|Learning`: 44 passed.

The owner's list (Idrak first, then Craft):
```powershell
cd D:\Projects\Idrak; git checkout main; git pull origin main
dotnet build -c Release tests\Idrak.Tests
$env:IDRAK_DEVICES="cpu"; $env:IDRAK_FILTER="plugins: packages|override loop|kind table|public API|abstraction inventory"; dotnet run -c Release --no-build --project tests\Idrak.Tests -- --progress --failures-only --failures-file test-output\fails.txt
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="Plugins|PluginKit|PhaseP|Learning"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
```
In the app's trail (`test-output\crashes`), check that there is no `FileNotFoundException` from `LibraryDefaults.Find`.
A training round before any chat load still needs B4 (the slot handling the plug-ins). Until then, its message now
names Idrak.Text.Plugins.

### The order and the slots

1. B1-B7, three slots: B1 and B2 (the canvas), B3, B4, B6 and B7 (the plug-ins, the agent and learning), B5
   (Idrak, then Craft's use of it).
2. The X restart: note 27, X part 4, X part 5, then the rest of X (planned below by its agent).
3. W, the moment X is done (planned below by its agent).
4. P batch 2, last: only once W is done (the owner, 2026-11-01: "make Idrak's plug-ins in the editor a later
   priority"); planned below by its agent.

The target is Windows; Android (phase M) is tested once the Windows version is done (the owner, 2026-11-01), so M
follows this list; until then no project carries the Android target, and the code stays Android-ready.

The owner, going to sleep (2026-11-01): every decision takes the recommended option, written here in one line; a free
slot starts the next ready work at once; the owner tests later. Except M (Android), A3 (remote models) and whether voice
dictation goes through the diff view: those wait for the owner's clear decision, never a recommendation, and no agent
starts them (the owner, 2026-11-01).

Craft is a stress test of Idrak (the owner, 2026-11-01: "all these apps are here to stress test the lib; test and fix
weak points"): a slot that finds the cause in Idrak (an error, a missing contract, memory that grows, a slow path, a
message that misleads) fixes it in Idrak on its `main` by Idrak's rules (`IDRAK_DEVICES=cpu`, an `IDRAK_FILTER` for the
code changed, its API dump), pushes it first, then uses it from Craft; never a workaround in Craft.

For the session of 2026-11-01 only, the owner allowed a fourth agent in parallel ("for this session only, if you can
run a fourth agent in parallel to speed things up, then start"); `CLAUDE.md`'s limit of three stands for every other
session.

Every slot follows Craft's new build-and-test rule (`CLAUDE.md`): all the work, one build, all the tests, one run,
fixes as a batch, the full filter once, then push.

## B3, B4, B6 and B7 as built (2026-11-01)

Slot s2. Idrak `main` first (three library changes, by Idrak's rules), then Craft. Each cause below was found in the code
and reproduced headless where it can be on Linux; the owner's Windows run confirms the rest.

**Idrak** (`IDRAK_DEVICES=cpu`, filter `fine-tuning: a model tuned in place|public API`; API dumps `Idrak.txt` and
`Idrak.Text.txt` regenerated):
- `PretrainedModel.LoadAdapter` freezes the base when the model had no adapter, as `AddAdapters` does. Cause found
  (B7): loaded weights come with `requiresGrad: true` and `LoadAdapter` added adapters with the base unfrozen, so a
  `FineTuningSession` over a loaded adapter (which adds none when the model has some) trained every weight of the model:
  learning's second round (going on from the promoted adapter) held the whole model's gradients and optimizer state, an
  out-of-memory on any real model.
- `ModuleExtensions.RemoveAdapters` (new): removes every adapter and frees its tensors and gradients, so a model tuned in
  place goes back to its base without keeping the adapters' memory (`DisableAdapters` only lifts them for a while).
- `FineTuner.BatchTokensThatFit(model, options, longest)` (new): the options' batch, halved while the activation
  estimate (the rung the options start from, the gradients still to make) is above what the device reports it can give
  (free memory within a limit), never below one sequence; the options' own when the device reports neither.
- Test `fine-tuning: a model tuned in place goes back as found …` (CPU): after `LoadAdapter` only the adapter's tensors
  train (before, every weight), a session over it trains the adapter alone, `RemoveAdapters` frees the adapters and
  gradients (bytes in use fall) and the model scores as a fresh base load (rule 71, 1e-5), the batch fits a room of the
  1,024-token estimate at exactly 1,024 tokens, one sequence with no room, the options' 4,096 with room.

**B3, the sample plug-in did not load.** Causes found: two binary breaks of contract 1.0 (K, `49fad59`), found by
diffing every API dump since K: `KeyBinding`'s three-parameter constructor and `Deconstruct` (note 16 added `Then` and
`When`), and `EditorKey`'s values (E5 put `Space` before `A`, moving every letter, digit and F-key one up, and `Comma` from
63 to 64: the old sample's Ctrl+Shift+W would have bound Ctrl+Shift+U). Fixed: the 1.0 constructor and `Deconstruct`
kept beside the record's own; `Space` moved after `Comma`, so every 1.0 value is back (`W` 41, `A` 16, `F12` 62, `Comma`
63; the keys added since keep theirs; `Space` is 64). `ISemanticLanguageSession.FormatAsync` moved to its base interface
in notes 17-18 (a caller built before would miss it; an implementer is unaffected); no plug-in outside Craft uses it, so
it stays. The loader (`CraftPlugins.Describe`, public) reports the cause under every wrapper (a reflection call's, a type
initializer's, a one-error aggregate's): "MissingMethodException: Method not found: … (in the type initializer of
Old.OldRegistration); the plug-in calls something this Craft (contract 1.0) does not have: rebuild it against this
Craft's Craft.Abstractions". The API guard keeps the contract's list (`api/Craft.Abstractions.contract.txt`, "contract
1.0" then every type, base and member of Craft.Abstractions): a removed or changed entry fails unless
`CraftContracts.Version`'s major stepped; additions join the list with `CRAFT_UPDATE=1` under the same version.
- Decision (recommended): no contract step now, since every 1.0 member is back (checked: of the 564 entries of K's dump,
  all are in the list; only the constructor's printed default `= false` differs, the same binary signature). The X
  restart's minor step stays theirs.
- Tests: `PluginKit.APluginBuiltAgainstContract10LoadsAndBindsTheKeysItMeant` (a plug-in compiled with Roslyn in the
  test against a small Craft.Abstractions of the 1.0 shapes loads, and binds Ctrl+Shift+W), `PluginKit.ALoadFailureNamesItsCauseNotTheTypeInitializer`,
  `ApiGuard.RemovedOrChangedMembersOfTheContractNeedAContractStep`, `ApiGuard.TheContractCheckNamesTheMembersARemovalBreaks`.

**B4, "No architecture 'Qwen2ForCausalLM' is registered".** Cause as analysed: learning registered its Idrak libraries
and the C# environment, never a family. Fixed: before its first load the trainer registers the families' packages
(`LearningLibraries.EnsureFamilies`) through `IdrakInPlugins.IncludePackages`, the path Craft.Ai's own load now takes
too (`LocalModelProvider.RegisterIdrakPlugins` moved onto it), once a process (rule 75). The list: learning's new
`idrakPlugins` setting, empty by default meaning the AI plug-in's `idrakPlugins`, or Idrak.Text.Plugins when no AI
plug-in is installed. `LearningTrainer.Rewards`' and `TrainTasks`' `GetAwaiter().GetResult()` run only inside the
training's `Task.Run` (checked: no caller on the UI thread). Test `Learning.ATrainingRoundInAFreshProcessWithNoChatFindsTheModelFamily`:
the test program run again with `--probe-learning` (a fresh process, no family registered, no chat load) records 24
verdicts and trains a tiny random Qwen2; the round writes v1 and registers the family.

**B7, a learning run always failed.** Causes found, beyond B4: the trainer loaded its own copy of the model, in float32
(it never read the weights setting), beside the AI plug-in's copy (a 7B in float32 alone is about 28 GB); and a round
going on from a promoted adapter trained every weight (Idrak's `LoadAdapter`, above). Fixed:
- One copy (note 22): `ModelLease` lends the provider's loaded model (`Underlying`) and an exclusive hold (`Exclusive`).
  The "local" provider gates each copy (`CopyGate`): replies pass side by side as before; a hold waits for the running
  replies to end, and replies asked for meanwhile wait until it is given back. The trainer asks the AI plug-in's
  provider for the model with the chat's own key (the device and weights empty mean the AI plug-in's, the adapter
  `ModelAdapters.For`), so it gets the copy the chat holds (or loads the one copy, the provider letting idle copies go
  first); only with no provider that lends its copy does it load one of its own, now with `weights`.
- The copy comes back as found: the round lifts the copy's adapters off, trains its own, and at its end removes and frees
  them (`RemoveAdapters`), puts the lifted ones back and returns the device's cached blocks.
- Sized at run time (card-agnostic): `maxLength` auto is the longest pair (at most the model's positions); `batchTokens`
  auto is the most of `batchTokensMax` (4,096) that `BatchTokensThatFit` says the free memory holds, measured with the
  round's adapters on. Out of memory (after Idrak's own activation ladder), a stage halves the batch, rebuilds the session
  and runs again, said in the activity ("out of memory at 1024 tokens a batch: 512 now"), down to one sequence
  (`stepDown`, on); then the round fails.
- Every failure names its stage (`LearningStages`: load, episodes, pairs, step, evaluate, promote) in words, an
  out-of-memory one with what to change (`batchTokens`, `maxLength`, the rank, smaller `weights`, other GPU programs;
  `IDRAK_OFFLOAD` named, never set): "Training failed at its step stage: the device ran out of memory for … at 512 tokens
  a batch. Lower CRAFT_PLUGIN_LEARNING_BATCHTOKENS …". `ModelErrors.IsOutOfMemory` (Craft.Abstractions) is the one test
  both plug-ins use.
- New settings (plug-in "learning"): `weights`, `idrakPlugins`, `batchTokens` (now "auto" by default), `batchTokensMax`,
  `maxLength`, `stepDown`.
- Tests: `Learning.RoundsTrainTheAIPluginsCopyInPlaceAndGiveItBackAsFound` (three rounds on the "local" provider's copy
  the chat holds: one copy loaded throughout, the chat's greedy answer the same after them; **measured** device memory in
  use 203,140 B before and 203,140 B after each of the three rounds, rule 77),
  `Learning.TheCopysGateGivesATrainingRoundTheModelAlone`, `Learning.OutOfMemoryStepsTheBatchDownAndAFailureNamesItsStage`
  (CPU limits doubled from 32 KiB above what is in use: below the first that fits, the round fails at its step stage in
  words; at it, the batch stepped down; an automatic batch is fitted below 4,096; a model that does not load fails at the
  load stage).
- Not done here: the tasks trainer (`trainer` tasks or both) keeps a fixed batch (its rollouts' lengths are not known
  before they run); its stages are named. The owner's real-model numbers (the 7B's batch, seconds a step, memory before
  and after a round) are to be written here.

**B6, the agent made the file but never wrote it to the workspace.** Traced through the panel, the copy, the review and
the app's MCP tools. Causes found:
- The panel listed only what `edit_file` and `write_file` changed, and read their path itself without the tools' rule:
  Idrak's `CodingTools` trims the path and takes a full path inside the copy, the panel did neither, so such a file was
  written in the copy and never listed; and a file a command made (`dotnet new class`, a formatter) was never listed at
  all. Unlisted, it was never reviewed, so never written to the workspace.
- An accepted change to an existing file stayed in the editor unsaved, so the workspace's file did not change until the
  user saved.
- Over MCP, `propose_edit` changed only a document already open: an agent could create no file and change no file the
  user had not opened.
Fixed: the copy remembers each copied file's length and time; after every command and at the run's end the files written
since are listed (build output left out: `agentChangesIgnore`, bin;obj;.git;.vs;node_modules;test-output); a tool's path
is read as the tools read it (`AgentWorkspace.ToolRelative`). An existing file's accepted change is saved at once
(`agentSaveOnAccept`, on), never when the editor held the user's own unsaved edits before the diff (said so); a new file
accepted whole is created as before. `propose_edit` opens a file of the open folder that is not open, takes a path with
no file yet (an empty `old_text`) as a new file there, and saves what is accepted (`saveOnAccept` of plug-in "tools", on;
a path outside the folder is refused). Still a diff the user accepts, one undo step (no silent edit).
- Tests: `AiPlugins.EveryFileTheAgentWritesReachesTheWorkspaceOnAccept` (a workspace path with spaces; a padded `./`
  path with spaces, an edit, a command that writes a file and build output, a write by the copy's full path: four listed,
  the build output not, nothing in the workspace before the review, all four there after it, the edited file saved),
  `Ai.ProposeEditOverMcpCreatesANewFileAndSavesAFolderFileOnAccept`.
- Not reproducible here: a Windows drive path (`C:\…`) and backslashes, which Linux reads as a file name; the code reads
  paths with .NET's `Path`, which treats both on Windows. If the owner's run was the model writing its calls as text
  (note 27, Qwen2.5-Coder writing fenced JSON), no file was ever written to the copy: that is the X restart's note 27,
  not this fix; the owner's next run tells which (the agent's column then lists the files, or says it made no call).
- **Found: the AI test that failed only on Windows ("the agent's working copy goes missing", the "Now" table's item 8).**
  Since phase U the panel deletes a working copy on the thread pool, fire-and-forget (Clear, Discard all, a new run, the
  panel's close); the agent tests then deleted the copies' folder in their `finally` at once, so two recursive deletes ran
  over one tree and one met a file the other had removed (`DirectoryNotFoundException` on a path of the copy). Here it
  showed in 2 of 3 runs of `AiPlugins` once a test with more files was added; on Windows, where a delete is slower, before.
  Fixed: `AiAgentPanel.DiscardsDone()` is done when every copy being deleted is gone, and the tests wait for it before
  removing the folders (3 of 3 runs pass). The app never deletes or reuses a copy's folder while its delete runs (each run's
  copy has a name of its own), and `AgentWorkspace.Discard` already leaves a held file for the next start.

**Tests, all at once, then the full filter once** (CPU, here): the 11 new tests passed on their first run;
the full filter (`PluginKit`, `Plugins`, `PhaseP`, `Learning`, `ModelLoading`, `AiPlugins`, `Ai`, `ApiGuard`, `Inventory`,
`Keys`, `Settings`, `Terminal`, `Voice`) found two: `Ai.TheSettingsReadEveryVariable` (the two new agent settings, its
expectation updated) and the copy-delete race above; after the fixes, 109 passed. Idrak: the new test, `public API`, and
every test that loads an adapter and trains (DoRA, PEFT checks, the vision tuning ones, the agent fine-tuning): 12 passed.
API dumps regenerated: `Craft.Abstractions`, `Craft.Ai`, `Craft.Core`, `Craft.Plugins.Learning`, `Craft.Plugins.Tools`;
the contract list `api/Craft.Abstractions.contract.txt` added.

**Decisions (each the recommended option, the owner asleep):**
- B3: the 1.0 members restored instead of a major contract step (no plug-in needs rebuilding).
- B3: the guard refuses removals only; additions join the list under the same version (the X restart's minor step stays theirs).
- B4: learning takes the AI plug-in's `idrakPlugins`, with a setting of its own to override it.
- B7: the trainer trains the AI plug-in's copy in place under an exclusive hold, rather than unloading it for the round.
- B7: `batchTokens` is automatic by default (fitted to the free memory), a number keeps it fixed; step-down on by default.
- B7: the tasks trainer keeps a fixed batch for now (its rollouts' lengths are not known up front).
- B6: an accepted agent change is saved at once (`agentSaveOnAccept`, `saveOnAccept`), never over the user's unsaved edits.
- B6: `propose_edit` may open or create a file inside the open folder only; outside it is refused.

**Commands for the owner** (Idrak changed, so its pull first; then Craft's):
```powershell
cd D:\Projects\Idrak; git checkout main; git pull origin main
dotnet build -c Release tests\Idrak.Tests
$env:IDRAK_FILTER="fine-tuning: a model tuned in place|public API"; dotnet run -c Release --no-build --project tests\Idrak.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:IDRAK_FILTER
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="PluginKit.|Plugins.|PhaseP.|Learning.|ModelLoading.|AiPlugins.|Ai.|ApiGuard.|Inventory.|Keys.|Settings.|Terminal.|Voice."; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
```
In the app: the old sample DLL in `test-output\plugins` (not rebuilt) loads and Ctrl+Shift+W counts words (B3); Learning:
Train now before any chat trains the 7B's adapter on the chat's copy, its line naming the batch, and the status bar's
device memory is the same after the round as before (B4, B7: the numbers to write here); the agent asked to create a
class shows the new file and any edited file under the changed files, and Review diff writes them into the folder (B6).

## B1 and B2 as built (2026-11-01)

- **One kept layout per shown line (B1).** New contract in `Craft.Abstractions` (`Surface.cs`): `StyledRun`, `StyledLine`
  (a line's runs; `Text` joins them at their columns once; `Layout`/`LayoutKey` hold the canvas's layout, disposed when the
  line changes or is dropped) and `IStyledLineCanvas` with `StyledLines.Draw` (a canvas without it gets one `DrawText` per
  run, exactly as before). `EditorRenderer` keeps each line's runs as a `StyledLine` (its run cache, keyed as before by the
  line's characters and spans, cleared on a new theme or model) and draws a line with one call; the cache keeps the lines on
  screen and `CRAFT_PAINT_CACHE_LINES` (64) above and below, trimmed once it holds half as many again, and releases a dropped
  line's layout (`ReleaseLayouts` for memory pressure). The platform-neutral decision of what to lay out and keep is
  `Craft.Editor.TextLayoutCache` (a line's layout kept on the line for the canvas's font-and-measures key; a lone run, such
  as a line number, a panel's row, the terminal's text or a popup's, kept by its characters and style while drawn in every
  frame and dropped at the end of a frame that drew text without it; a line whose layout would not keep its columns, a wide
  or fallback glyph, is drawn run by run from the runs' own layouts). The platform's half is `ITextLayoutSurface`
  (`Craft.Editor`), reached in Craft.Maui through `IDrawnTextLayouts`/`IDrawnTextSurface` and `IShellPlatform.TextLayouts`;
  `MauiEditorCanvas` implements `IStyledLineCanvas` and sends every `DrawText` and every line through the cache when the
  platform has a surface, else draws with MAUI's `DrawString` as before. So the editor's text, its gutter numbers, the panel
  lists (Problems, telemetry) and the terminal all take the same path. Windows: `Platforms/Windows/Win2DTextLayouts.cs`, a
  Win2D `CanvasTextLayout` per line (each run's colour, and family, weight and slant for bold or italic, on its range) on the
  drawing session of MAUI's Windows canvas (`PlatformCanvas.Session`), its text formats made as MAUI's internal
  `FontExtensions.ToCanvasTextFormat` makes them (read from its IL: name, size, weight clamped 1-999, style). Win2D 1.4.0
  comes with MAUI on Windows (a dependency of Microsoft.Maui.Graphics): rule 0, no package added.
- **The caret on its own layer (B1).** `EditorView` has a second, transparent, input-transparent `GraphicsView` over the
  text (`CaretDrawable`, `EditorRenderer.DrawCarets`: the carets read from the model, so the layer is right whichever view
  draws first); the blink invalidates that layer only. While ghost text, the completion list or a tooltip shows
  (`EditorRenderer.CaretsOnTextLayer`), the text frame draws the carets under them as before and the layer draws none.
- **`CodeFontPicker.Ensure`** compares the family, fallback and size instead of building a key string every frame (rule 75),
  and disposes the canvas it replaces (its kept layouts with it).
- **The C# classifier allocated in every frame with a documentation comment on screen** (found by B1's allocation test:
  3,256 B a frame on `EditorModel.cs`, the old run path too): `DescendantTokens(range)` over a documentation comment, a
  directive or skipped tokens made iterators each frame. `CSharpClassifier` now walks a structured trivia's tokens from
  `FindToken` with `GetNextToken`, applying Roslyn's span filter and stopping at the first token whose parents do not reach
  the structure (in broken code `GetNextToken` goes on to the token holding the trivia, a missing token as wide as the
  trivia: without that check the walk recursed until the stack overflowed, caught by `CSharp.IncrementalParsesEqualFreshParses`);
  `ClassifyByDescendants` keeps Roslyn's walk as the reference, and `CSharp.TheTokenWalkEqualsDescendantTokens` (12,000
  random ranges over edited texts with comments, docs and directives) proves the spans equal (rule 71). 0 B a frame after.
- **A frame that throws (B2).** `Craft.Core.Diagnostics.PaintGuard`: the editor's frame, its caret layer's, the panel
  lists' and the terminal's catch what their body throws, report it through `CallbackGuard` (trail, a callback report once
  per place and type, Output) with the surface and the step (`editor: paint (at: render: draw the lines; the frame was
  skipped)`; `EditorRenderer.Step` names the renderer's step, rule 90) and skip the frame; nothing leaves the platform's draw
  callback into WinUI. The one exception let through is a lost graphics device (DXGI's removed, hung, reset, internal error;
  Direct2D's recreate-target, by HRESULT number, no platform API), which Win2D's CanvasControl catches itself to make its
  device again; the surface drops its layouts first and the trail notes it. The trail writes inner exceptions
  (`CrashTrail.AppendWithInner`: "| inner: Type: message", three deep, an aggregate's first) for first-chance exceptions and
  for the callback guard's notes.
- **`RouteKeys` at start (B2's trail).** `IShellPlatform.IsReady(window)` (Windows: the native window has its content);
  `ShellPage.HookPlatform` asks it first and tries again later, so "The window has no WinUI content yet" is no longer thrown
  (and no longer a first-chance exception in the trail).
- **Telemetry (rules 76, 78).** Each editor frame is timed (`EditorView.LastFrameMilliseconds`) and recorded in the telemetry
  table as `editor.paint.cold` (it laid text out or built runs) or `editor.paint.warm`.
- **Options added** (each with its shape on the settings page, Editor › Advanced): `CRAFT_PAINT_LINE_LAYOUTS` (true; off draws
  each run with `DrawString` every frame, as before), `CRAFT_PAINT_CACHE_LINES` (64).
- **Proven unchanged (rule 71).** Once, before any change, the old renderer's draws on a fixed C# file (a copy of
  `EditorRenderer.cs`, six views: scrolled down and right, a selection, the caret) were dumped (1,488 draws, 1,107 texts);
  after the change the same dump is byte-identical. Kept as tests: `Paint.ALineDrawnWholeHoldsTheSameRunsPlacesAndColoursAsRunByRun`
  (the line path through a real `TextLayoutCache` draws the same kinds, places, colours and texts as the run path on
  `EditorModel.cs`, six views), `Paint.TheCaretLayerDrawsTheCaretsTheTextFrameDrew...` (the layer's fills equal the text
  frame's, overwrite too), and the classifier's walk above.
- **Measured** (headless, Linux container, Release, the tests' fixed metrics, 1200 × 800, 41 lines shown; written to
  `test-output/benches/paint.txt`): before, the run path on the fixed file: warm blink frame median 210 µs, p99 370 µs,
  0 B (the renderer itself was never the 53-68 ms: that is Windows' `DrawString` per run). After, the line path on
  `EditorModel.cs`: cold first frame 82.9 ms (the first C# parse waited for, the run strings, 30 layouts, the JIT), warm frame
  median 151.7 µs, p99 263.2 µs, **0 B a frame**, no layout made; a 3-line scroll 1.13 ms (2 layouts: the lines it brought in
  that hold text); scrolling back lays out nothing (the margin). In the full filter's run (the parse and the JIT already
  warm in the process): cold 1.6-5.6 ms, warm median 111.7-117.1 µs, p99 175.7-295.8 µs, 0 B, a 3-line scroll 273-365 µs. Under other slots'
  builds the p99 rose to 4-8 ms (the machine, not the frame: the median stayed 0.11-0.36 ms). The Windows numbers are the
  owner's (below).
- **Rule 90 on the way**: in a long test run (the JIT compiling in the background) the warm frame showed 88 then 176 bytes
  over 1,000 frames, and 0 with `DOTNET_TieredCompilation=0`: unoptimized code boxing a value type (rule 89) in the lone
  runs' lookup (`HashCode.Combine` over `TextStyle`, and a `Dictionary` keyed by a struct of Craft's: its `key == null`
  test). The lone runs are now keyed by their string (the dictionary's shared precompiled code) with a short chain of
  styles, each style compared field by field. Then 88 B once in a long run with the JIT off too: not a frame's work but a
  garbage collection in the window. Shown by the test itself: the first frame after a forced collection allocates 984 B and
  the next ones 0 (Roslyn keeps some red nodes only weakly, large member lists and `GetStructure`'s cache, to save memory,
  and rebuilds them when next walked). `Paint.AWarmFrame...` now asserts 0 B for every frame with no collection in or just
  before it, 0 B for the frames after the first one past a collection, and under 4 KB for that first one.
- **Compiled here**: the whole Windows target of Craft.Maui (with a local, uncommitted `TargetFrameworks` change and
  `EnableWindowsTargeting`: the Windows App SDK and Win2D restored from NuGet; `Craft.Maui.dll` for
  `net10.0-windows10.0.19041.0` built with no error or warning; only the PRI resource step after it, `MakePri.exe`, cannot run
  on Linux). **Not run here**: the Windows drawing itself (Win2D's layouts on screen, their pixels against `DrawString`'s, the
  device-lost recovery) and the caret layer's transparency over the text: the owner's run.
- **Decisions on the recommendation (one line each)**: the full fix, one layout per line (the owner's 4a); the cache logic in
  `Craft.Editor` (headless-tested), only the Win2D drawing in `Platforms/Windows`; the platform's layouts reached like its
  drawn fonts (an `IShellPlatform` member), not a new registry; lone runs keyed by their characters, so a string made again
  each frame still finds its layout; a line whose layout would not keep its columns falls back to its runs (never drawn off
  its cells); the lone runs keyed by string, never by a struct of Craft's (rule 89); the caret layer as a second GraphicsView,
  the carets back on the text frame while a popup or ghost text shows;
  a lost device rethrown to Win2D (its documented recovery), every other failure swallowed; `PaintGuard` in `Craft.Core`
  beside `CallbackGuard` (no MAUI type in it); the classifier's allocation fixed in Craft.CSharp, where it lives.
- **Left**: the fills (`FillRect`, `DrawLine`) still go through MAUI's canvas state (a brush per colour change), not measured
  on Windows yet; the terminal's paint still holds the screen's lock while it draws (as before); the "WinRT
  InvalidOperationException at start" and the three start-up stalls of the trail are not traced in this slot.
- **Tests**: group `Paint` (9) with `CSharp`, `Editor`, `Surface`, `Folding`, `MultiCaret`, `Experience`, `Layout`, `Ui`,
  `Design`, `LookAndFeel`, `CrashWatch`, `Settings`, `Shell`, `ApiGuard`, `Inventory`: 174 passed, 0 failed (after the rebase
  over B3-B7, against Idrak `main` 7a20d5f, which B5's `Plugins.EveryPreinstalledPluginLoadsFromTheAppsLayout` needs). The
  API dumps of Craft.Abstractions, Craft.Core and Craft.Editor and B3's contract list regenerated (lines added only).
- **The owner's commands** (the app on the test project, a long C# file: scroll with the wheel and by dragging the bar,
  leave it idle a few seconds, open Problems and the terminal; then View › Idrak telemetry: `editor.paint.warm` and
  `editor.paint.cold`, and the trail in `test-output\crashes` should show no 0.2-0.4 s "editor: paint" stalls; the text
  should look exactly as before; `$env:CRAFT_PAINT_LINE_LAYOUTS="false"` gives the old path to compare):
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt --filter "Paint|CSharp|Editor|Surface|Folding|MultiCaret|Experience|Layout|Ui|Design|LookAndFeel|CrashWatch|Settings|Shell|ApiGuard|Inventory"
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```

## B8, B9 and B10 as built (2026-11-01)

**B8, the splitters made easy.** `Craft.Maui.SplitPanel` keeps its API and gains the rest: the grab area is
`CRAFT_SPLITTER_GRIP` (10) wide, centred on the one-unit line and over both neighbours (a transparent view translated over
the edge; the panels sit above their neighbours, `ZIndex`), so it takes no room; the line takes the accent after the
pointer rests `CRAFT_SPLITTER_HOVER_MS` (100), while dragged and while the splitter has the keyboard. The behaviour is
headless in `Craft.Editor.Shell.SplitterModel`: a drag measured in the window's coordinates from where it started (the
grip moves with the panel), kept within `PanelLayout.Clamp`; below the panel's smallest size (`CRAFT_PANEL_MIN_SIZE`) it
shows closed and opens again past it (`CRAFT_SPLITTER_SNAP`, on); a drag that ends closed closes the panel through
`PanelLayout.SetOpenAsync` with its size kept; a double-click restores the option's default (`PanelLayout.DefaultSizeOf`:
260, 360, 132); a focused splitter steps by the arrows of its axis (`CRAFT_SPLITTER_KEY_STEP`, 10; Shift five steps), Home
and End go to the smallest and largest, Enter restores, Escape during a drag puts it back. The resize cursor and the
captured drag come through a new contract, `Craft.Maui.IPointerCursor` (`Attach(view, shape, IPointerDrag)`), reached as
`IShellPlatform.PointerCursor` (a default member, null): on Windows `WindowsPointerCursor` sets the element's
`ProtectedCursor` to `InputSystemCursor` SizeWestEast or SizeNorthSouth and drives the drag with `CapturePointer`
(press, move, release, capture lost, double-tap; handlers that see handled events too); elsewhere MAUI's pan and a
double-tap stay the fallback. The keys reach the splitter through the platform's `ITextInputAdapter` (the grip is a
`GraphicsView`, a focusable control on Windows), as the editor's and the terminal's do.

**B9, the terminal's profiles and design.**
- Contracts (`Craft.Abstractions/TerminalProfiles.cs`, additive): `TerminalProfile` (id, name, shell, icon, short name,
  folder, environment, source), `ITerminalProfileSource : IKind`, `TerminalProfiles` (a `KindTable`; `DiscoverAsync` asks
  every source on the thread pool, first of an id wins, a source that throws is named and passed over; `LastFound` feeds the
  settings' drop-down), `TerminalIcons`, `TerminalProgram` (file names, the PATH lookup `TerminalLaunch.FindOnPath` now
  calls, a command line split with a quoted program, slugs), `TerminalMachine` (what discovery reads: variables, files,
  folders, and what only the platform tells: WSL distributions, Git for Windows' folder) and the library's source
  `MachineTerminalProfiles` ("machine"): on Windows PowerShell 7 (PATH, else Program Files), Windows PowerShell, the Command
  Prompt, Git Bash (`--login -i`), each WSL distribution (`wsl.exe -d`), Developer PowerShell (`Launch-VsDevShell.ps1`) and
  Developer Command Prompt (`VsDevCmd.bat`) for each Visual Studio edition found under Program Files; elsewhere the login
  shell, bash, zsh, fish, pwsh and sh. File checks only, no process started; platform-neutral (rule 70), tested on a fake
  machine. Windows registers `WindowsTerminalProfiles` ("machine" over the library's) with the registry's two reads, as
  `IShellPlatform.TerminalProfiles`. `SettingChoiceSources.TerminalProfiles`,
  `EditorContextMenus.Terminal` and `.TerminalProfiles` added.
- Settings: `CRAFT_TERMINAL_DEFAULT_PROFILE` (a drop-down of the profiles on the settings page, Terminal; a workspace may set
  it), `CRAFT_TERMINAL_PROFILES` (`Name=command line|…`; a workspace cannot set it, what runs is code), `CRAFT_TERMINAL_TABS`
  (auto, tabs, list), `CRAFT_TERMINAL_LIST_FROM` (3), `CRAFT_TERMINAL_PADDING` (8), `CRAFT_TERMINAL_CURSOR` (block,
  underline, bar), `CRAFT_TERMINAL_CURSOR_BLINK` (true), `CRAFT_TERMINAL_BUSY_AFTER_MS` (500), `CRAFT_TERMINAL_BUSY_QUIET_MS`
  (400). `CRAFT_TERMINAL_SHELL` keeps working as a profile named by its path (id "setting", with `CRAFT_TERMINAL_ARGS`).
- `TerminalLaunch`: `UserProfiles`, `SettingProfile`, `AllProfiles` (found, the user's, the setting's), `DefaultProfile`
  (the setting's name or id, else `CRAFT_TERMINAL_SHELL`'s, else the host's default program's profile with exactly phase T's
  shell), `ShortTitle` ("C:\…\cmd.exe - ping localhost" gives "ping", a program's path its file name), `IconOf`,
  `CursorStyle`, `ListsTerminals`. `TerminalScreen.DefaultCursorStyle` (DECSCUSR 0 and a reset return to it; a shell's choice
  wins) and `CommandRunning` (OSC 133 C, D, A). `TerminalInstance`: `Profile`, `Group` (split groups), `Rename`/`UserName`,
  `ShortTitle`, `SelectAll`, `IsBusy` (the shell's OSC 133 mark when it gives one, else a line sent and output still coming
  after `BUSY_AFTER_MS`, gone after `BUSY_QUIET_MS` of quiet), `Clock`.
- `TerminalManager`: profiles found off the UI thread at its start (`ProfilesReady`) and their commands registered on the
  UI thread: one palette command per profile ("Terminal: New PowerShell 7", `terminal.new.<id>`) with its row in the "+"
  chevron's menu, then "Select the default profile…" (`terminal.selectDefaultProfile`, written per workspace through
  `StoreDefaultProfile`), `terminal.newWithProfile`, `terminal.split` (the same profile in the same folder, beside it),
  `terminal.rename`, `terminal.selectAll`; Copy, Paste, Clear, Kill, Rename act on the context menu's terminal
  (`EditorContextMenus.Target`), else the active one; `Rows()` (one per split group: icon, short names joined, tooltip,
  active, busy, ended); the "terminalProfiles" choices with the user's profiles; taken back on dispose.
- The view (`Craft.Maui/TerminalPanelView.cs`, new): a 32-unit header of tabs (profile icon, short name, a busy dot in
  the accent, a close button shown on hover and on the active tab; a double-click renames, a right-click opens its menu)
  or, from three groups, a 200-unit list at the panel's right; "+" (the default profile, its keys in the tooltip), its
  chevron (the profiles' menu through `MenuModel`), split and kill; the active group's terminals side by side with a
  one-unit line, the views reused so focus and input stay; the empty state an `EmptyStateView` card (icon, "No terminal
  open", where it starts, a primary "New terminal (Ctrl+Shift+`)", the profiles under "PROFILES"). `TerminalView`
  (outside the paint): the padding as the view's inset (the paint unchanged), the blink setting, `Activated` (a split pane
  clicked becomes the active terminal), `ContextRequested` (Copy, Paste, Select all, Clear, Split, Rename, Kill).
  `EmptyState.Icon` and `.ListTitle` added.

**B10, the workspace's notification and the global loader.**
- `ActivityKinds.Workspace` and `IActivity.Global` (a default member; `ActivitySnapshot.Global`), additive. Opening a
  folder (`ShellModel.OpenFolderAsync`) and the C# workspace's build run as `Workspace` activities, not kept: the build's
  outcome is the language's one notification. `SemanticTelemetry.Failure` (the C# workspace sets it from the build's error
  and clears it on the next success).
- `WorkspaceNotices` (`Craft.Editor.Shell`): one notification per workspace and load, "C# workspace ready" with "1 project,
  6 MB, 2.3 s (Craft)" (success, never a toast); a rebuild of the same workspace replaces it in place; a failure is "C#
  workspace failed" in plain words with its fix (a missing SDK, a file it may not read, a missing project file) and a
  primary Try again (the session releases its workspace and builds again); an error, so it toasts, with Open log. The page
  observes the active editor's layer when diagnostics change and when the last workspace activity ends.
- `SemanticCard.StatusBarText`: "Roslyn loading…" or "Roslyn failed" (the error's colour, a click opens the centre), nothing
  once ready or while a large file waits; `Tooltip` holds the facts ("Roslyn: 1 project, 6 MB, built in 2.3 s"), on the
  status bar's language item. The explorer's Roslyn card is gone.
- The loader: `GlobalLoader` (headless: the loader's kinds, `CRAFT_LOADER_KINDS`, "workspace", and anything marked global,
  past `CRAFT_LOADER_DELAY_MS`, 300; follows the most advanced, the count; `DueIn` for the view's next look) and
  `Craft.Maui.LoaderView`: a 2-unit accent line in a 6-unit band at the top of the body, across the window, determinate
  when a fraction is reported (it grows), else a third-wide segment crossing every `CRAFT_LOADER_CYCLE_MS` (1,400); with
  reduced motion the segment stands still at half strength; the tooltip "C#: building the workspace of Craft · 40% · and 1
  more"; a click opens the notification centre.

**Decisions on the recommendation (one line each).**
- The splitter's logic headless in `Craft.Editor.Shell` (tested without a window), only the cursor and the capture under `Platforms/Windows`.
- `IPointerCursor` in Craft.Maui's own namespace beside `IShellPlatform` (only the MAUI views use it), reached as an `IShellPlatform` member like the platform's other pieces, not a new registry.
- The grip overlaps both neighbours by a translation over the edge, the split panels above their neighbours, so the line keeps its one unit of room.
- Snap: below the smallest size the panel shows closed during the drag, and a drag ending there closes it with its size kept.
- Keys on a focused splitter: the arrows of its axis, Shift five steps, Home, End, Enter restores, Escape cancels a drag.
- Profiles found by file checks only (no `vswhere` or other process), the rules platform-neutral over `TerminalMachine`; the registry's two reads are the only Windows code.
- The default profile matches by name or id; with none set, `CRAFT_TERMINAL_SHELL`, else the host's default program, so phase T's start is unchanged.
- The user's profiles carry a name and a command line only (icon from the program; folder and environment follow the terminal's rules); a workspace may pick the default but not add profiles.
- The busy dot: the shell's OSC 133 marks when it gives them, else output still coming 500 ms after a line was sent (a silent long command shows no dot).
- Splits as groups of terminals in the manager (a tab per group), shown side by side; the views reused so a split keeps the keyboard.
- The padding as the terminal view's inset, so s1's paint loop is untouched; the scroll bar stays WinUI's own (thin, wider on hover).
- The C# build's activity not kept: the language's notification is the one place for its outcome, so nothing is said twice.
- The loader shows `workspace` work by default (`CRAFT_LOADER_KINDS` adds others, such as indexing) and any activity marked global.
- `CraftOptions.TerminalEditorCommands` and the new `LoaderKinds` default to one shared list, so two default options compare equal (before, the terminal's list made every options object differ, and the shell's "options unchanged" shortcut never fired).

**Compiled here.** Every project for net10.0 (the test build), and all of Craft.Maui's sources for
`net10.0-windows10.0.19041.0` in a scratch project outside the repository (the Windows App SDK 1.8 and MAUI 10.0.110 from the
NuGet cache, `WINDOWS` defined, nullable, warnings as errors, `AnalysisModePerformance` All; the PRI step off, it needs
`MakePri.exe`): no error, no warning. **Not run here** (the owner's run): `Platforms/Windows/WindowsPointerCursor.cs` (the
cursor through reflection on `ProtectedCursor`, the capture), `Platforms/Windows/WindowsTerminalProfiles.cs` (the registry's
reads), the two `WindowsShellPlatform` properties, and the `#if WINDOWS` key adapter of `SplitPanel`.

**Tests.** New: `Layout` (`SplitterTests`, 4: the drag clamped and measured from its start, snap closed and open, double-click,
keys and Escape, the options and the default sizes), `Terminal` (`TerminalProfileTests`, 6: profiles on a fake Windows and a
fake Unix machine, discovery past a broken source, the user's profiles, `CRAFT_TERMINAL_SHELL` as a profile and the default,
short names, the list from three, the cursor's default shape and OSC 133, the manager opening a profile through "pipe",
split, rename, the menus' rows, set default, select all, kill, the busy dot over a scripted session), `Notifications`
(`WorkspaceNoticeTests`, 2), `Activities` (`GlobalLoaderTests`, 1). Updated: `Inventory.OptionsReadEveryVariable` (the new
variables, two default options equal), `Settings.ThePageMakesSectionsAndControlsFromTheMetadata` ("blink" finds the
terminal cursor's blink too). After the rebase onto B1-B7 (one build: the merged API dumps and B3's contract list against
the merged code, Idrak `main` 4f394d5): `Layout|Terminal|Notifications|Activities|Settings|Shell|Ui|Design|LookAndFeel|
Experience|ApiGuard|Inventory` 129 passed (the two above failed first: the contract list regenerated, lines added only, and
the expectation), `Semantic|CSharp` 35 passed. API dumps of Craft.Abstractions, Craft.Core, Craft.Editor, B3's contract list
and the inventory regenerated (lines added only).

**Left.** A tab cannot be dragged to reorder or to join a split; reflow and the other phase T gaps stand; the busy dot for a
silent long command needs the shell's OSC 133 marks (a prompt script for PowerShell); profiles' own icons beyond the six.

**The owner's commands** (Idrak first: Craft's `main` needs Idrak `main` 4f394d5). In the app: drag each splitter fast across
the editor (the resize arrows show, the drag never drops), drag one below its smallest size (it closes) and back, double-click
one (its default size), Tab to a splitter and use the arrows; in the Terminal tab: the chevron's profiles (PowerShell 7,
Command Prompt, Git Bash, WSL, the developer shells found), Set default profile, a split, a rename by double-click, the
right-click menu, three terminals (the list), the empty state; open the test project (the loader line under the title bar
while the C# workspace builds, then one "C# workspace ready" in the bell's centre and no "Roslyn ready" in the status bar):
```powershell
cd D:\Projects\Idrak; git checkout main; git pull origin main
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="Layout|Terminal|Notifications|Activities|Settings|Shell|Ui|Design|LookAndFeel|Experience|ApiGuard|Inventory|Semantic|CSharp"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
```
