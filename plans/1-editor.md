# Plan 1: Craft, a code editor (C# first) with AI plug-ins on Idrak's contracts

**Status (2026-11-01, Craft `main` c3cfaed, Idrak `main` 5b5854d):** E0-E5, S0, K, A0-A2, V, R0, R, D, T, U, U2, the
owner's notes 1-26 and X slices 1-3 are built (see "Now" below). Next, in this order: the bugs B1-B5 of the third
Windows run, then the X restart (note 27, X parts 4 and 5, the rest of X), then phase W (the web languages), then P
batch 2 last, once W is done (the owner: the Idrak plug-ins in the editor are a later priority). See "The owner's third
Windows run (2026-11-01)" at the end.

## Now (2026-11-01, late)

| # | Phase or item | Status |
|---|---|---|
| 1 | E0-E5, S0, K, A0-A2, V, R0, R, D, T, U, U2, notes 1-26, X slices 1-3 | Built earlier |
| 2 | B1-B11 (paint and scroll, crash guard, sample plug-in and contract guard, learning's families, the start-up probe, the agent's files, learning's memory, splitters, terminal profiles, Roslyn's notification and the loader, the Learning view) | Built 2026-11-01 |
| 3 | Note 27 (Idrak and Craft), X restart pieces 1b, 2a, 2b, 3a, 3b, X parts 6, 6b, 7, 8, 10, speculative decoding (part 11 item 1) | Built 2026-11-01 |
| 4 | X part 9: editing helpers 1, 2, 6-9; navigation 13-17 | Built 2026-11-01 |
| 5 | X part 9: tools 18-22 (Test explorer, Markdown and image preview, TODO list, spell check) | Stopped for W (the owner, 2026-11-01: "stop all"), 14 tests passing, not pushed; for later, after W |
| 6 | X part 9 item 23 and part 11 items 4-5 (inline AI chat, AI review, fix the build) | Stopped for W, half written, not pushed; for later, after W |
| 7 | X part 9: language 3-5, 10-12 (snippets, inlay hints, code lens, linked tags, Outline, hierarchy) and two C# session speed fixes | Stopped for W, tests being fixed (one real bug found: a mirrored `$1` must repeat its placeholder's text), not pushed; for later, after W |
| 8 | Main's health (the Lightbulb wait, a timing-dependent terminal test) | Built (46d6e17) |
| 8b | The recovery prompt at start, X part 9's look group 24-26 | Stopped for W, just begun; for later, after W |
| 9 | Phase W, the web languages | Running: W0, W-files and W-npm first (the owner, 2026-11-01: "start Web now", "stop all") |
| 10 | Left of X, for later (after W): rows 5, 6, 7 and 8b above, part 11 items 2-3 (next-edit suggestions, repo-aware completion), 1c (measured input-to-response times); W-script's "Left, for later" line (re-reading a project on WorkspaceChanged, a contextual word split across lines, generics in a JSX tag, Prettier-style layout) | Planned; starts only on the owner's word after W; the stopped slots' unpushed work was saved as patches outside the repository (the session's scratchpad, gone with the container) and is redone from this plan |

| 10 | Left of X, for later (after W): rows 5, 6, 7 and 8b above, part 11 items 2-3 (next-edit suggestions, repo-aware completion), 1c (measured input-to-response times); left of W-tools: a closed file's Fix in Problems, ESLint's multi-pass fix, ESLint and Angular output in the Run view's problems (lint as you type waits for W-semantic) | Planned; starts only on the owner's word after W; the stopped slots' unpushed work was saved as patches outside the repository (the session's scratchpad, gone with the container) and is redone from this plan |
| 11 | P batch 2 (P10, P9 and P8, P6; P11 if measured) | Planned; starts only on the owner's word |
| 12 | The owner's Windows runs of everything above | The owner's (each "as built" section lists its checks and commands) |
| 13 | M (Android), A3 (remote models), voice dictation through the diff view | Only on the owner's clear decision |

**The owner's order (2026-11-01): W first, no endless leftovers.** "The web languages should have been finished far
before the above enhancements." From here W runs to its end before any other work: no slot takes an X item, a
leftover or a new enhancement while a W piece is ready. What a W slot leaves undone goes into one "Left, for later"
line in its "as built" section and into row 10 above, never into a follow-up slot of its own; a failing test on
`main` is the only thing that may interrupt W. New asks from the owner are written into the plan and queued after W
unless the owner says otherwise. **When W is done, the work stops** (the owner, 2026-11-01: "finish W and don't start new
phases"): the "later" items of row 10, P batch 2 and every held phase wait for the owner's word; no agent starts them.

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

### X restart piece 1b as built (2026-11-01)

Slot s4: Craft's side of note 27, the drop onto the chat, the Models page per role and more folding (X part 5's three
bullets). Craft only; nothing was needed in Idrak (its note 27 part, `4f394d5`, had every hook).
- **Note 27 in Craft.** New settings in the Agent section: `agentNudges` (2, 0-20, `CRAFT_PLUGIN_AI_AGENTNUDGES`) and
  `agentCallsInText` (on, `CRAFT_PLUGIN_AI_AGENTCALLSINTEXT`), each with its shape; `AiAgentPanel.RunAgentAsync` passes
  them to `AgentOptions.Nudges`/`CallsInText`, and `AgentOptions.Template` from the lease: new `ModelLease.ChatTemplate`
  (additive), filled by `LocalModelProvider` with the model's own template, so the correction is worded in its markers
  although the panel wraps the model (`CountingChatModel`). The end note (`AiAgentPanel.EndNote`): "The model wrote no tool
  calls; nothing was changed." for a run with no call and no file written, whatever the model claimed; `CallsNotRead`
  says "…after N corrections (agentNudges M); nothing from its last reply was run." (and "Nothing was changed." when so);
  any run that was corrected says how many times.
- **Drop onto the chat.** `IConversationPanel.AcceptDropAsync(paths, ct)` (default: takes none) and
  `ContextChip.Problem` (greyed with the reason) in `Craft.Abstractions`; `ConversationDrops` (Craft.Editor) hands the
  paths over and words what the host says ("Notes takes no files: nothing was added.", or "took 2 of the 5"). The chat
  column (`ConversationsView`) is a drop target: the tree's drag key (`FolderTreeView.DragPathKey`), else the system's
  drop (`IShellPlatform.DroppedPathsAsync`); the composer takes the selection colour while a drag is over it; the window's
  own drop leaves a drop on the column to it (`ConversationsView.Holds`). `AiChatPanel`: a text file becomes a `path:`
  chip, an image an `image:` chip (.png .jpg .jpeg .gif .bmp .webp .tif .tiff, its bytes read on the thread pool up to
  the new `chatImageMaxMb`, 20, Chat section); a folder, a missing file, a binary one (a NUL in its first 512 bytes) or a
  larger image is not taken, said in one note. An image for a model whose part kinds have no "image" is greyed with the
  reason (at the drop when the model's kinds are known from its last load, else when the message goes: the chips come
  back greyed and nothing reaches the model); nothing is sent while a greyed chip is there (the composer keeps the text
  and says why). `AiAgentPanel` takes files and folders of the open folder as `@path` chips, sent with the next task or
  note ("The files this is about (paths in the folder): @a, @b."); one outside the folder is not taken, said.
- **Fix found (the restart's decision line):** `AskAsync` → `BuildContext` → `ExtraFiles` → `File.ReadAllText` ran on the
  UI thread. Now the context is gathered on the UI thread (the active document's text, an open file's immutable
  snapshot, the images read at the drop) and a file that is not open is read on the thread pool when the message goes,
  only its first `chatFileLines` lines (`File.ReadLines(…).Take`).
- **Models page per role.** `ModelRole.EnabledSetting` (additive), filled by `LocalModelProvider` (`chatEnabled`,
  `agentEnabled`, `completion`). `ModelRoleCards` (Craft.Editor, headless) says what each card shows and what each
  control writes; `ModelsPage` draws a card per role: title and switch, the model (shown, "same as chat: NAME" while
  inherited; a field whose Enter or "Use and load" writes the role's own setting and runs its own load command), the
  device as a segmented choice of the measured devices, "Same as chat" for the agent (clears `agentModel` and
  `agentDevice`), the state, "Open its settings". One box per card, plain rows inside it, the palette's buttons (hover and
  focus states); an off role's card dimmed.
- **More folding** (`Craft.CSharp/Project/ProjectFolding.cs`, registered by the C# plug-in and `ProjectLanguages`, one
  part each): `RazorFolding` (elements matched by name, a hash of the name's span, HTML's void elements never waiting;
  long start tags; `@code`, `@{ }`, `@if (…) { }` and every brace in them, C# strings, chars and comments skipped there;
  script and style bodies' braces; `@* *@`, `<!-- -->`, `/* */`), `SolutionFolding` (Project, ProjectSection, Global,
  GlobalSection, comment runs), `EditorConfigFolding` (sections, comment runs): each one pass over the lines with its
  state carried, no copy of the text. `FoldingRangeProviders.Changed` (raised on register and unregister):
  `EditorFolding` takes its language's providers again on the UI thread and asks at once (not after the typing delay);
  with none left, its ranges and folds go (`EditorFolding.ProvidersChanged`).
- **Contract:** `CraftContracts.Version` 1.1, the restart's one minor step (the first addition to merge; B8-B11's
  additions had joined the list under 1.0). The guard writes the running version as the list's header on a minor step
  (`contract 1.1`); Craft.Ai's `plugin.json` says 1.1 (it uses 1.1's members); the C# plug-in and the samples stay 1.0
  (an older minor is current). API dumps regenerated: `Craft.Abstractions`, `Craft.Ai`, `Craft.CSharp`, `Craft.Editor`,
  and the contract list.
- **Decisions (the recommended option each time, the owner asleep):**
  - The lease carries the template (`ModelLease.ChatTemplate`) rather than the panel unwrapping the model: a remote
    provider can give one too.
  - "Nothing was changed" means no tool call in the run and no file written in the copy.
  - An image's bytes are read at the drop (the size checked first); a text file's text when the message goes.
  - Whether the model takes images is known from its last load; before that the check is at send, and the chips come back greyed.
  - A binary file other than an image is not taken (a NUL byte in its first 512 bytes).
  - The agent takes files and folders inside the open folder only (it works on a copy of it).
  - The enabled switch writes "true" or "false" explicitly into the user's settings, so a workspace value cannot hide it.
  - Razor: a brace opens C# after `@code`, `@functions`, `@{`, `@if`, `@for(each)`, `@while`, `@do`, `@switch`, `@try`,
    `@lock` (not `@using`, `@section`); in C# a `<` after a name, `)` or `]` is a comparison or a generic, not a tag.
  - .editorconfig: comments right above a header belong to the next section.
  - The chat column's drop is kept apart from the window's by the drop's position over the column.
- **Tests** (groups `AiPlugins`, `ChatDrop`, `ModelsPage`, `MoreFolding`; 11 new):
  `AiPlugins.FencedCallsEndInADiffOfOneUndoStep`, `AiPlugins.ARunWithNoToolCallsSaysNothingChangedAndCallsNotReadCountTheNudges`,
  `AiPlugins.TheSettingsReachTheAgent`, `AiPlugins.TheLeasesTemplateReachesTheCorrection` (through Idrak's `FakeChatModel`);
  `ChatDrop.FilesBecomePathChipsAndImagesImageChipsAndTheAgentTakesThemAsMentions`,
  `ChatDrop.AnImageForAModelWithoutImagesIsGreyedAndNothingIsSentUntilRemoved`,
  `ChatDrop.DroppedFilesAndAChipsFileAreReadOffTheUiThread` (the read held 5 s by a test hook: the drop's call and
  `AskAsync` return at once); `ModelsPage.EachCardWritesItsOwnRolesSettingsAndTheAgentInherits`;
  `MoreFolding.RazorSolutionAndEditorConfigFoldTheirBlocks`, `MoreFolding.RandomEditsEqualAFreshFold` (60 random edits
  each, the ranges equal a fresh editor's), `MoreFolding.AProviderRegisteredAfterADocumentOpenedFoldsItWithoutReopening`;
  `Ai.TheSettingsReadEveryVariable` extended. First run: 14 of 17 passed (the dumps, and one thread-id check that proved
  nothing after an await, replaced by the 5 s hold); after the rebase onto B1-B11 the full filter (`AiPlugins`, `Ai`,
  `ChatDrop`, `ModelsPage`, `MoreFolding`, `Folding`, `ModelLoading`, `Settings`, `ProjectLanguages`, `Plugins`,
  `ApiGuard`, `Inventory`, `Ui.TheUiSideHasNoSynchronousWait`): 87 passed, 0 failed.
- **Windows only (not run here; compiled here for net10.0):** the drop on the chat column (`ConversationsView`'s
  `DropGestureRecognizer`, the system's paths through `WindowsShellPlatform.DroppedPathsAsync`, whether the window's own
  drop also fires: kept apart by position), the greyed chip's look, the Models page's cards. No file under
  `Platforms/Windows` changed.
- **Left:** X part 5's split editor and tree move (other slots); X part 10 redraws the chat column, so the drop target
  and chips move with it.
- **The owner's commands** (Idrak's pull first: note 27's Idrak part is new since the last list):
  ```powershell
  cd D:\Projects\Idrak; git checkout main; git pull origin main
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  $env:CRAFT_FILTER="AiPlugins.|Ai.|ChatDrop.|ModelsPage.|MoreFolding.|Folding.|ModelLoading.|Settings.|ProjectLanguages.|ApiGuard.|Inventory."; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```
  In the app: ask the agent "Create C# class For House with float 3 props and + float price" (the House run): it
  writes the file through the diff, or its end note says it wrote no tool calls and nothing changed; drag `Program.cs`
  from the tree onto the chat (a `Program.cs` chip), drop a PNG from File Explorer (an image chip, greyed with the reason
  for the 7B coder, which takes no images; the message is not sent until it is removed); drop a file on the Agent tab (an
  `@Program.cs` chip); Settings › Models and devices: turn the agent off and on, set its model, "Same as chat", each
  card's "Open its settings"; open a `.razor`, a `.sln` and the `.editorconfig` and fold them.

### X restart piece 3a as built (2026-11-01)

Slot s1: the split editor (the owner asked for it directly), moving files by drag in the tree, peek definition. Craft
only; nothing was needed in Idrak.
- **Split editor** (`Craft.Editor/Shell/EditorGroups.cs`): `EditorGroups` holds the groups, each its own `DocumentTabs` and
  active tab, one focused, laid out as a tree of rows and columns (`EditorGroupNode`, each child's share in `Weights`).
  `ShellModel.Tabs` is the focused group's tabs, so every command, quick open, Ctrl+O, find, the breadcrumb, the status
  bar and the title follow the focus; only the focused group's active tab is the host's active document (the agent's and
  the plug-ins'). A document in two groups is one `ITextDocument` and one host document in two tabs: closed in the host
  and disposed with the last tab (`DocumentTabs.SharedElsewhere`), its unsaved changes asked about only then; untitled
  numbers are counted across the groups. Its views share one language session: `LanguageSessionPool` (`Craft.Editor`,
  `ShellModel.Sessions`) gives every view of a document in one language and settings the same session, counted, disposed
  with the last view; `EditorModel` has a new constructor taking a session it does not own (`OwnsLanguageSession`,
  `EditorModel.OpenContext`), so carets, scroll and folds stay per view. Commands (palette, both keymaps, the View menu):
  Split right Ctrl+\ (`EditorKey.Backslash`, new), Split down Ctrl+K Ctrl+\, Move the editor to the next group
  Ctrl+Alt+Right, Close the editor group, Focus the next group, Focus group 1-3 Ctrl+1/2/3. A tab (or a file from the tree)
  dragged over a group's editor shows its drop zone (the centre, the right or the bottom half: the accent's tint in a
  rounded line, fading in) and goes there (`EditorGroups.Drop`, `ZoneAt`); a tab dropped on another group's tab lands at
  its place. Only the focused view's caret blinks (`EditorView.IsFocusedView`); an unfocused group's active tab keeps a
  strong-border line instead of the accent; a press or a key in a view focuses its group. Each view lays out only its own
  visible lines (B1's cache per view). Options: `CRAFT_EDITOR_GROUPS_MAX` (4, 1-16), `CRAFT_EDITOR_GROUPS_CLOSE_EMPTY` (on).
  The layout (groups, their files, active files, shares, focus) is written per workspace to `.craft/editor-layout.json`
  (`EditorLayoutFile`, on the thread pool, latest wins) and read back by `ShellModel.RestoreEditorLayoutAsync`.
- **Tree move by drag** (`ShellModel.Move.cs`, `FolderTreeView`): folders' rows can be dragged too; every folder's row takes
  a dropped row (the row under the drag shows the accent's tint); the move (`ShellModel.MoveEntryAsync`) checks and moves
  off the UI thread as an activity ("Moving X"), the moved file's clean tabs open again under the new path in every group
  at their places (`FollowMovedAsync`, which rename now uses too), and the undo bar says "Moved X to Y" with Undo (the move
  back). Ctrl at the drop copies (`IShellPlatform.DropCopies`; "Copied X to Y", Undo deletes the copy; a copy onto its own
  folder is "X copy"). Refused and said in the status bar: a folder to another drive (`SameVolumeOf`), a folder into
  itself, a name the folder has already, a file with unsaved changes. `CRAFT_TREE_DRAG_MOVE` (on).
- **Peek definition** (Alt+F12, `ShellModel.Peek.cs`, `Craft.Maui.PeekView`): `EditorModel.PeekDefinition` asks the
  language off the UI thread and raises `PeekRequested` (a metadata symbol shows its description instead);
  `ShellModel.PeekAsync` resolves it: the source document, or an open tab's document (shared: the same text, its views'
  session through the pool), or a closed file read off the UI thread with its language's syntactic colours only
  (`SyntacticLanguageSession`: opened with no workspace, semantics deferred and semantic colours off, wrapped so nothing
  semantic is reachable). The overlay is a raised card under the caret's line: a header (the file and line, "colours only"
  for a closed file, Open, ×) and a read-only editor of `CRAFT_PEEK_LINES` (12) lines with the name selected. Escape closes
  it (from the source editor too), Enter, a double click or Open opens the definition in the focused group with its name
  selected (`OpenPeekAsync`). `CRAFT_PEEK_MAX_MB` (16): a larger closed file is not read, said.
- **Decisions on the recommendation (one line each)**:
  - Groups nest as rows and columns (split right then down gives a column inside the row), VS Code's grid, not one row.
  - The pages (settings, Models, Plug-ins, Welcome) stay in the first group's strip and area.
  - A file opened anywhere opens in the focused group, sharing the document when another group shows it.
  - Closing one of two tabs of an edited document asks nothing (the other keeps the changes); the last tab asks.
  - A group left without tabs closes while another is open (VS Code's default), switchable.
  - Ctrl+N one past the last group makes a group to the right showing the focused tab (VS Code's); past that, said.
  - Move to the next group from the last group splits right, unless it is the group's only tab (said).
  - Split down is Ctrl+K Ctrl+\, Move to the next group Ctrl+Alt+Right (VS Code's); Close group has no key (VS Code has none).
  - The layout file is written only once the area was split (a single group never adds a file to the user's folder).
  - Reopening the kept layout at start waits for session restore (later): `RestoreEditorLayoutAsync` is its call, tested.
  - The splitter between groups: s3's `SplitterModel` and `IPointerCursor` (B8, merged before this slot rebased) in a
    `GroupSplitter` of the groups' own (no snapping closed: a group closes with its tabs; a double-click shares in halves).
  - `EditorKey.Backslash` joins the restart's contract 1.1 step, which piece 1b took first.
  - A file's row takes a drop for its folder, so the workspace's root is reachable without an empty-space target.
  - A move of a file with unsaved changes is refused, as rename refuses (its text would be read again from the disk).
  - A name the target folder has is refused, never overwritten.
  - The move back by Undo is not itself offered for undo; Undo of a copy deletes the copy for good (Craft made it).
  - The peek floats over the lines under the caret's (an overlay), not pushing them down.
  - An open document's peek shares its views' session; a closed file's peek gets a syntactic session of its own.
  - The peek closes when its editor stops being shown (another tab, the group closed).
  - The Paint tests paint a fixed copy of `EditorModel.cs` (`tests/Craft.Tests/Editor/Samples/EditorModel.cs.txt`, the text
    B1 measured): this slot's constructor put more documentation comments on screen, whose structured trivia Roslyn keeps
    only weakly (10,448 B on the first frame after a forced collection, against the test's 4 KB), so the measure must
    not move with edits to the source file (diagnosed by rule 90: 0 B a frame with the old text, the same failure with
    `DOTNET_TieredCompilation=0`).
- **Measured** (headless, Linux container, Release, the tests' fixed metrics; `EditorGroups.TwoGroupsShowingA100000Line...`,
  written to `test-output/benches/split-editor.txt`): one 100,000-line document (11 MB of text) in two groups: the split
  allocated 5.4 KB, the second view 8.4 KB more (14.6 KB on every thread), the heap grew 52 KB (in the filter's run; 19 KB
  alone): one copy of the text and one language session. A warm frame per group (600 × 800, each at its own place, lines
  1,000 and 80,000): median 5.6 and 5.7 µs, p99 10.2 and 16.5 µs, 41 lines drawn each (alone: 5.2 µs). The test's language
  colours nothing, so these are the frame's own costs; a C# file's warm frame is B1's (113-144 µs here). The Windows
  numbers are the owner's (below).
- **Windows-only lines** (compiled here: Craft.Maui's Windows target with a local, uncommitted `TargetFrameworks` change and
  `EnableWindowsTargeting`, no error; only `MakePri.exe` cannot run on Linux): `WindowsTextInputAdapter.Map` maps VK_OEM_5
  to `EditorKey.Backslash`; `WindowsShellPlatform.DropCopies` reads `DragDropModifiers.Control` at the drop. **Not run
  here**: the window (the panes, drop zones, the splitters' drag with WinUI's pointer, the peek card, the tree's drags).
- **Tests**: groups `EditorGroups` (6), `TreeMove` (3), `PeekDefinition` (3); `Inventory` (the five options); the API dumps
  of Craft.Abstractions, Craft.Core and Craft.Editor and B3's contract list (lines added only). The full filter
  (`EditorGroups|TreeMove|PeekDefinition|Shell|Inline|Editor|Surface|Paint|Keys|Settings|Layout|Ui|Experience|ApiGuard|Inventory`,
  160 tests) after the rebase onto B8-B11 and piece 1b: 159 passed, `Paint.AWarmFrame...` failed (the decision above);
  after the fix the Paint group: 11 passed.
- **Left**: the kept layout is not reopened at start yet (session restore); dragging a tab out of the window; the groups'
  splitters take no keys yet (s3's arrows on its panels' splitters); the peek cannot be edited in place.
- **The owner's commands** (the app on the test project: Ctrl+\ on `Program.cs`, type in one group and see the other; Ctrl+K
  Ctrl+\; Ctrl+1/2/3; drag a tab onto another group's right half and centre; drag the splitter between groups; Ctrl+Alt+Right;
  View › Close the editor group with an edited file; drag a file onto a folder in the explorer, then Undo; Ctrl+drag to
  copy; Alt+F12 on a type (Escape, Enter, a double click in the peek); only the focused editor's caret blinks):
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  $env:CRAFT_FILTER="EditorGroups|TreeMove|PeekDefinition|Shell|Inline|Editor|Surface|Paint|Keys|Settings|Layout|Ui|Experience|ApiGuard|Inventory"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```

### Main's health and X restart piece 3b as built (2026-11-01)

Slot s3. Craft only: nothing was missing in Idrak (the outline, the index and the rename are Craft's and Roslyn's).
- **Main's health (pushed first, two commits).** The two `PhaseP` tests read each plug-in's `plugin.json` contract version
  from its source (`PhasePTests.StatedContractVersion`, checked to be one this Craft loads) instead of a fixed "1.0": the
  Sandbox plug-in is at 1.1 by right (X part 10). `Ui.TheUiSideHasNoSynchronousWait`: `CopyGate.TryEnter`'s
  `_door.Wait(0)` (speculative decoding) is a zero-timeout try-enter that never blocks: on the scan's allowed list with
  that reason. `main` built once and `ApiGuard|Inventory|PhaseP|PluginKit|Plugins|Ai.|DraftModel|SourceControl` ran
  once: 76 passed (the dumps after X part 8 and speculative decoding were current; nothing to regenerate).
- **Document symbols** (contract 1.1, additions only): `ILanguageSession.GetDocumentSymbolsAsync(snapshot, ct)` (none by
  default), `DocumentSymbol(Name, Kind, Start, Length, NameStart, NameLength, Detail, Children)` with `End` and
  `Contains`; `ScopeKind` gained `Property`, `Field`, `Event`, `EnumMember`. C# (`CSharpLanguageSession.Symbols.cs`): one
  walk of the last parse's declarations on the thread pool, kept with the parse (the same version gives the same list; an
  older snapshot is parsed for its answer only): namespaces, types (their keyword as the detail, generics with their
  parameters), fields and constants (a symbol per variable), properties, indexers, events, constructors, destructors,
  operators, conversions, methods (parameters and type as the detail), enum members (their values), delegates, top-level
  local functions. `SyntacticLanguageSession` forwards it. `Craft.Editor.SymbolIndex`: the tree flattened once per version
  into arrays (names, parents, depths, lines, columns), `IndexOf(ReadOnlySpan<char>)` through an alternate lookup (rule
  15), `PathAt`, `ChildrenOf`, `SiblingsParentAt`; `SymbolKinds` (words, plurals, badges, the groups' order).
  `EditorModel.DocumentSymbolsAsync` (one request a version, shared by every caller; the newest index kept in `Symbols`)
  and `RequestSymbols(landed)`.
- **`@` in quick open**: `QuickOpenMode.Symbols` ('@'), Go to symbol `craft.goToSymbol` (Ctrl+Shift+O; Visual Studio's
  keymap also Alt+\); fuzzy over the index's names, the exact name first; "@:" orders the best rows by kind, the group's
  title with its count ("methods (7)") on its first row; `SymbolsUnder` lists one level (the breadcrumb's siblings); over
  `CRAFT_QUICK_OPEN_BACKGROUND_FILES` symbols the match runs on the thread pool (the rows proven the same as the calling
  thread's). Rows carry the kind's badge, the detail and the container. `SymbolPreview`: Up and Down select each symbol's
  name in the editor and scroll to it, Escape puts the selection and the scroll back, a pick keeps it. The chat's
  composer "@" symbols (X part 10) now come from this contract (`ConversationComposer.SymbolsOf(model, landed)`: the last
  version's at once, the current one asked off the UI thread, the picker relisted when it lands).
- **Breadcrumb** (`Craft.Editor.Shell.BreadcrumbLayout`, `BreadcrumbPart`; `Craft.Maui.BreadcrumbView`): every folder from
  the workspace's root to the file (the whole folder for a file outside it), the file, the scopes at the caret; each part
  clickable: a folder (and the file) lists its entries in the overlay (`QuickOpen.BrowseAsync`, folders first, a picked
  folder lists its own), a symbol lists its siblings with '@'. When the parts are wider than the bar, the fewest middle
  parts fold into "…" (`BreadcrumbLayout.Fold`, by the labels' laid-out widths kept per text): never the first, the file
  or the last symbol; "…" acts as its last folded part, its tooltip names them. It follows the focused group (3a's).
- **F2 rename**: `ISemanticLanguageSession.PrepareRenameAsync` (`RenameTarget`: the name's span and text, or a `Refusal`)
  and `RenameAsync` (a `WorkspaceEdit`: per file `WorkspaceFileEdit(Path, Edits, Version)`, the open documents' versions,
  `Conflicts`, `Refusal`, `EditCount`; defaults: none, and a refusal). C# (`CSharpLanguageSession.Rename.cs`): the name
  checked (`SyntaxFacts.IsValidIdentifier`; a keyword asks for '@'), a metadata, keyword or compiler-declared symbol
  refused with why, Roslyn's `Renamer.RenameSymbolAsync` over the workspace's solution (open documents at their unsaved
  text), each changed file's text changes as edits, and the conflicts: the errors a changed file has that it did not
  have. `IEditorHost.ProposeWorkspaceAsync(edit, title)` (default false; the plug-ins' scope forwards it):
  `EditorHost.ProposeWorkspaceEditAsync` refuses before asking when an open document's version moved, a file the edit
  made against unsaved text was closed, or unsaved text was not in the edit, then X part 6's review across files (every
  file's diff together, Accept all; each open document one undo step; a closed file read off the UI thread, edited and
  saved). `ShellModel.Rename.cs`: `craft.renameSymbol` (F2; Visual Studio: Ctrl+R Ctrl+R) asks the target off the UI
  thread and raises `RenameRequested`; `Craft.Maui.RenameOverlay` (a raised box over the name, the name selected; Enter
  commits, Escape or leaving cancels); `CommitRenameAsync` computes the edit as the activity "Rename" (cancellable),
  conflicts in the diff's title and a warning notification, then the Undo bar "Renamed in N files" takes all of it back.
- **Decisions (the recommended option each time, the owner asleep):**
  - '@' first in quick open now means symbols (VS Code's); a file whose name starts with '@' is found without typing it.
  - Ctrl+Shift+O goes to a symbol (VS Code's); Open folder moves to Ctrl+K Ctrl+O (VS Code's); the hints, the welcome page and the empty state say so.
  - Visual Studio keymap: Ctrl+R Ctrl+R renames and Alt+\ goes to a symbol; Ctrl+R in the editor becomes that chord's first key there (Open recent stays in the File menu and the palette).
  - The outline is syntactic (the last parse), no workspace needed: it works before the workspace builds and in a closed file's peek.
  - Namespaces are in the outline (the breadcrumb leaves them to the path); local functions inside members are not (VS Code's C# outline).
  - A symbol's span starts at its attributes and modifiers, not its documentation comment; a field of several variables gives a symbol each.
  - `ScopeAt` keeps `Member` for the breadcrumb's scopes (its callers unchanged); the new kinds are the outline's.
  - '@' goes off the UI thread over the files' threshold (`CRAFT_QUICK_OPEN_BACKGROUND_FILES`): no new option.
  - "@:" groups the best rows by kind (then score), the group's title on the row's right where "recent" sits.
  - The preview keeps the primary selection and the scroll for Escape (extra carets are not kept); a text change meanwhile leaves the editor where it is.
  - Breadcrumb: every folder from the workspace's root (VS Code's), not only the last; "…" stands for its last folded part.
  - The breadcrumb learns its parts' widths from the first layout of a new set (kept per text, cleared with the theme or font), then folds.
  - A symbol part lists its siblings (its parent's children); the file part lists its folder.
  - Conflicts are the new errors in the renamed files (Roslyn's public `Renamer` reports none); said, and the user may still accept.
  - A stale document refuses the whole rename before the user is asked ("run it again"), never a partial one.
  - The rename box cancels when it loses the keyboard.
  - `FileChange.Origin` (added) names the review's source, so a rename's diff says "Rename A to B: the open document".
  - `ConversationComposer.SymbolsOf` takes the `landed` callback (Craft.Editor's dump: that one line changed; not a plug-in contract).
  - Only the rename's own status line is kept (`ShowStatus` lines otherwise pile up; others' calls left as they are).
- **Measured** (Linux container, Release, `test-output/benches/piece-3b.txt`): C# symbols of a 5,123-line file (2,481
  symbols, after its parse landed): the call returned in 0.03-0.04 ms, the walk 2.0-2.4 ms cold, a new version 2.25-2.82
  ms median (2.92-3.54 worst; two runs), the same version again under 0.01 ms. '@' over those 2,481 symbols per key (15 keys × 6 rounds, 120 rows kept):
  median 0.12-0.13 ms, worst 0.20-0.21 ms, 38 KB a key (the rows made), off the UI thread with the same rows. A rename of a symbol
  used 600 times in 31 files (one open), on a test UI thread with the watchdog (20 ms ticks, 250 ms stall): 1.9 s from
  Enter to applied in the first run, 0.8 s in the full filter's (the workspace built, Roslyn's rename, the review, 30
  files saved), worst tick 3-5 ms, 0 stalls.
- **Tests**: groups `DocSymbols` (3), `Inline` (+3: '@' with groups, a level and the preview; 2,481 symbols off the thread;
  a folder's entries), `Breadcrumb` (3), `SymbolRename` (4: F2 across two files with one review, one undo step and Undo of
  all; names, refusals, versions and a conflict; a stale document refused and the contract's undo bar; the watchdog
  measure), `Conversation` (+1: the composer's '@' from the contract). Updated: `Inline.QuickOpenRanks…` ('@' is now
  symbols), `Shell` (Ctrl+Shift+O, F2, Ctrl+K Ctrl+O). API dumps of Craft.Abstractions, its contract list, Craft.CSharp and
  Craft.Editor regenerated. After the rebase onto X part 6b (and Idrak's interactive sandbox start), the full filter
  `DocSymbols|Inline|Breadcrumb|SymbolRename|Conversation|Semantic|CSharp|EditorGroups|Shell|Keys|Settings|Ui|Experience|ApiGuard|Inventory|PhaseP`:
  168 passed, 0 failed.
- **Windows-only (not compiled here)**: nothing new under `Platforms/`; the rename box's Enter and Escape go through
  `RoutedEntry`'s Windows hook (`EntryKeys`). **Not run here**: the window (the box's place over the name, the
  breadcrumb's folding and clicks, the '@' preview).
- **Left**: renaming the file with its type (Roslyn's `RenameFile`), in comments and strings (off); the breadcrumb takes no
  keyboard focus yet (VS Code's Ctrl+Shift+.); other languages give no outline until they implement the member.
- **The owner's commands** (nothing of this piece is in Idrak, but Craft's `main` now builds against Idrak's interactive
  sandbox start, 9e63a92, so Idrak is pulled first):
  ```powershell
  cd D:\Projects\Idrak; git checkout main; git pull origin main
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  $env:CRAFT_FILTER="DocSymbols|Inline|Breadcrumb|SymbolRename|Conversation|Semantic|CSharp|EditorGroups|Shell|Keys|Settings|Ui|Experience|ApiGuard|Inventory|PhaseP"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```
  In the app, on `Program.cs`: Ctrl+Shift+O, type a name, Up and Down (the editor follows), Escape (back), "@:" (by
  kind), Enter on one; click the breadcrumb's folder (its entries), its member (the siblings), narrow the window (the
  middle folds to "…"); F2 on a method used in two files, type a new name, Enter (one diff of both files, Accept all),
  then Undo on the bar; Ctrl+K Ctrl+O opens a folder; in the chat's box type "@" (the file's symbols).

### X restart piece 2a as built (2026-11-01)

Slot s3: X part 4's **Faster typing at many carets**, **Hover cards that stay**, **Overview ruler** and **Sticky scroll**.
Craft only; nothing in Idrak (no cause there).
- **The batched document build** (`Craft.Core/Text/PieceTableBuffer.cs`, `PieceTree.cs`). A change of at least
  `CRAFT_PIECE_BATCH_MIN_EDITS` (8) edits splits the tree once before its first edit and once after its last, flattens
  the middle into a pooled array (`PieceNode.Flatten`), copies the kept pieces and skips the removed text in one walk,
  appends every inserted text to the add buffer in one run, joins pieces that continue each other in their block, copies an
  insertion together with the short add-buffer piece before it into one piece while the two stay within
  `CRAFT_PIECE_COALESCE_CHARS` (64; 0 never), and rebuilds the middle perfectly balanced (`PieceNode.FromSorted`); the two
  seams join through `Concat`. The middle starts up to the coalescing limit before the first edit, so the first caret's
  typing joins too (found by the piece-count test: without it the first caret kept a piece a keystroke). A middle of more
  than `CRAFT_PIECE_BATCH_PIECES_PER_EDIT` (32) pieces an edit (carets far apart in a much-edited file) takes the per-edit
  path, whose cost does not grow with the middle. Pooled arrays are cleared on return (they hold chunk references).
- **Hover cards that stay** (`Craft.Editor/HoverCard.cs`, `EditorModel.Layers.cs`). `EditorTooltip.Kind` (Plain, Hover,
  FoldPreview); `EditorModel.HoverCard` lays the card out from the model's state whenever asked (box, wrapped rows, scroll,
  selection), so the paint and the pointer agree. The pointer inside the card keeps it (no new hover, no hide); leaving it
  and its word (the gutter, the ruler, the sticky rows, another word, out of the editor) closes it once
  `CRAFT_HOVER_HIDE_MS` (300) has passed, counted from leaving (moving on does not postpone it); back inside in time keeps
  it. A drag in the card selects its text, Ctrl+C copies it (`Copy` takes the card's selection first), the wheel over the
  card scrolls it (`EditorModel.WheelOverPopup`, called by `EditorView`'s wheel first) when it is taller than half the
  editor, with its own thin scroll bar. A hover on a symbol (a semantic session, an answer) ends with a "Go to definition
  F12" row: a click puts the caret on the symbol and runs go to definition (`GoToDefinitionAt`). Signature help and a
  definition's description stay plain tooltips, as before.
- **The fold's preview.** The pointer resting on a folded header's "..." or its gutter chevron shows up to
  `CRAFT_FOLD_PREVIEW_LINES` (12) of the hidden lines under the header, in their colours: each line's runs from the
  renderer's run cache (`EditorRenderer.LineRunsFor`: the line classified alone, kept like a shown line and pinned against
  the cache's trim while shown), the lines' common indent taken off. A click in the preview unfolds the block.
- **The overview ruler** (`Craft.Editor/OverviewRuler.cs`, owned by the renderer). `CRAFT_OVERVIEW_RULER` (on),
  `CRAFT_OVERVIEW_RULER_WIDTH` (14) at the viewport's right edge: the whole content mapped onto the viewport's height;
  errors and warnings (right half, an error wins), the find overlay's listed hits (left half; the ruler never lists them
  itself), carets (across, thin), and the band the viewport shows. One pass over each sorted list (diagnostics, hits,
  selections) into a pooled byte array of one bucket per unit of height, then runs lane by lane; rebuilt only when the text
  version, the diagnostics array, the find list, the selections (`SelectionStamp`), the folds (`FoldsStamp`), the rows or
  the height changed (rules 73, 75). New theme tokens `RulerError`, `RulerWarning`, `RulerFind`, `RulerCaret`, each the
  theme's error, warning, find and caret colour unless a theme sets it. A press on the ruler centres the view there, a drag
  follows (instant scroll requests); the caret is kept out from under it (`EnsureCaretVisible`'s text width).
- **Sticky scroll** (`Craft.Editor/StickyScrollLayer.cs`, `EditorModel.StickyScroll`). `CRAFT_STICKY_SCROLL` (on),
  `CRAFT_STICKY_SCROLL_LINES` (5), over `EditorFolding.Ranges`: each range's enclosing range is found once when the ranges
  change (`EditorFolding.RangesStamp`, one pass with a stack); a frame finds the top line's innermost range by a binary
  search and walks up, into arrays it keeps, and is reused while the scroll, the text, the ranges and the folds stay. A
  header stays while it is above the row it covers and its block goes on below that row (outermost first). Drawn over the
  text and the gutter with its own line numbers and a border and soft shadow under it; carets under the headers are not
  drawn (both layers); a click on a header puts the caret at its first non-blank and scrolls the header to where its row
  was; a caret moved up under the headers scrolls out from under them.
- **Measured** (headless, Linux container, Release, the tests' fixed metrics; `MultiCaret.AKeystrokeAtAHundredCaretsIsMeasured`,
  400 keys typed then 400 Backspaces at 100 carets; the document's change replayed with the batch off, then on):
  - Before (edit by edit): median 115.7-132.7 us, 312,061 bytes a change, **40,101 pieces after 400 keys**, 40,000
    characters added to the add buffer.
  - After (batched): median 30.8-34.6 us, p99 80-94 us, 44,284 bytes a change, **807 pieces after 400 keys**, 1,263,131
    characters added (coalescing copies a caret's run while it grows to 64: about 31 times the typed text, 2.5 MB here,
    against the 40,101 tree nodes, about 3.5 MB, it saves).
  - The whole keystroke at 100 carets: median 66.4-79.4 us (p99 176-217 us), 45,984 bytes (slice 2: 191 us, 314 KB); the editor's own
    about 1.7 KB; its paint (ruler included) median 97.6-120.1 us.
  - Warm frames: the ruler's and the sticky headers' frames allocate nothing (200 frames each under 1 KB in all, 0 seen);
    B1's C# paint test still 0 B a frame (warm median 173 us in the long run). Once, in the first long run, that test saw
    88 B in one frame; alone, with `DOTNET_TieredCompilation=0` and in the next long run it was 0 (rule 90: the long run's
    background tier-up, as B1 recorded).
- **Decisions on the recommendation (one line each)**:
  - The batch starts at 8 edits and is taken while the middle holds at most 32 pieces an edit (a new option, so it is not fixed).
  - Coalescing copies only pieces of the add buffer, never the original text (copying the text between carets would grow the add buffer by the whole middle a keystroke).
  - The middle starts up to the coalescing limit before the first edit, so the first caret coalesces like the others.
  - "Before" and "after" are measured in one run (the batch switched off by its option), not against slice 2's machine.
  - `InternalsVisibleTo Craft.Tests` on Craft.Core for the tree's structural checks, not a public diagnostic API.
  - The card's layout lives in the model (`HoverCard`), so the hit tests and the paint use one box.
  - A hover card stays while the pointer is on its word too; leaving starts one hide timer that moving on does not postpone.
  - The wheel over a card is the card's (the text under it does not scroll); a card shows at most half the editor's height.
  - The definition row's link takes the theme's `Info` colour (blue, as the design's links); no new token.
  - A click in a fold's preview unfolds the block (its text is not selectable; a hover card's is).
  - The ruler is drawn by the renderer over the text's right edge (not a view of its own) and maps the content's height, as VS Code's.
  - Ruler marks: errors and warnings right, find left, carets across; at least two units high.
  - The ruler keeps `ContentWidth` as it was; the scroll margin already leaves room for the line ends under it.
  - Sticky scroll follows the folding ranges, so it is off where a document does not fold (and with `CRAFT_FOLDING` off).
  - The sticky headers scroll sideways with the text and draw their own line numbers.
  - Pointer hit tests for the card, the ruler and the sticky rows run before the editor's own press (after the completion list).
- **Found on the way**: since piece 1b every editor left open re-asks a provider when one registers, so the Folding tests'
  `provider.Calls` counted other groups' undisposed editors in a long run; they now count their own editor's
  `Folding.Requests`.
- **Windows-only lines**: none. The wheel's route to the card is in `EditorView`'s shared code; not compiled for the
  Windows target here (no `#if WINDOWS` line changed). Not run here: the drawing on Windows, and whether WinUI's own scroll
  bar sits over the ruler (the plan's "(guess)"): the owner's check.
- **Left**: an `IEditorCanvas` has no clip, so a preview line wider than its card draws past the card's right edge; the
  sticky headers have no push-up animation (a header leaves when its block's end reaches its row); the ruler shows the find
  overlay's hits only once the find box has listed them; a long-run allocation diagnosis tool (Idrak's `AllocatedTypes`)
  is not in Craft's tests.
- **Tests**: new groups `HoverCards` (3), `OverviewRuler` (3), `StickyScroll` (4); `Buffer` (+3: batched equivalence
  against a string for five option sets with tree height, balance, length and line-break checks after every step and old
  snapshots; a piece a caret at 100 carets; the per-edit path for a large middle) and `MultiCaret` (the measure, before and
  after, the piece bound); `Editor` (the draw order with the ruler; the old hover test leaves after the hide delay),
  `Folding` (own requests), `Inventory` (the nine options). The full filter
  (`Buffer|Document|MultiCaret|HoverCards|OverviewRuler|StickyScroll|Surface|Editor|Paint|Folding|Semantic|Settings|ApiGuard|Inventory`):
  156 passed, 0 failed (after the rebases over pieces 1b and 3a). The API dumps of Craft.Abstractions, Craft.Core and Craft.Editor
  and the contract list (1.1, piece 1b's step) regenerated: lines added only.
- **Options added** (each with its shape on the settings page): `CRAFT_PIECE_COALESCE_CHARS` (64),
  `CRAFT_PIECE_BATCH_MIN_EDITS` (8), `CRAFT_PIECE_BATCH_PIECES_PER_EDIT` (32) in Editor › Advanced; `CRAFT_HOVER_HIDE_MS`
  (300) in Editor › Hover; `CRAFT_FOLD_PREVIEW_LINES` (12), `CRAFT_OVERVIEW_RULER` (on), `CRAFT_OVERVIEW_RULER_WIDTH` (14),
  `CRAFT_STICKY_SCROLL` (on), `CRAFT_STICKY_SCROLL_LINES` (5) in Editor › Display.
- **The owner's check** on `test-output\CarftTestPOC`: Alt+click ten carets (or Ctrl+Shift+L on a common word) and hold a
  key; hover a symbol, move into its card, select and Ctrl+C, scroll a long card with the wheel, click "Go to definition",
  move out and see it close after a moment; fold a method and rest the pointer on its "..." and on its chevron, click the
  preview; open Problems with an error and a warning and look at the right edge, Ctrl+F a word, click and drag on the ruler
  (is WinUI's scroll bar over it?); scroll inside a method of a class and see the namespace, class and method headers stay,
  click one; Settings › Editor › Display turns the ruler and sticky scroll off.
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt --filter "Buffer|Document|MultiCaret|HoverCards|OverviewRuler|StickyScroll|Surface|Editor|Paint|Folding|Semantic|Settings|ApiGuard|Inventory"
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```

### X restart piece 2b as built (2026-11-01)

Slot s1: X rest's **Minimap** and **Completion and ghost text at several carets**. Craft only; nothing was missing in Idrak
(Craft builds against Idrak `main` 9e63a92, which s4's Run view input needs).
- **The minimap** (`Craft.Editor/MinimapRenderer.cs`, `Craft.Maui/MinimapView.cs`). Its own `GraphicsView` beside the text's
  scroll view in each `EditorView` (a grid column), so the text's frames and the caret's blink never draw it; one renderer
  per view. It shows while `CRAFT_MINIMAP` is on (off by default) and the view is at least `CRAFT_MINIMAP_MIN_WIDTH` (600)
  wide and more than twice the minimap's width (`MinimapRenderer.Shows`); only the documents' views may show it
  (`EditorView.ShowsMinimap`: not the peek, the output or the chat's transcript). A row is `CRAFT_MINIMAP_SCALE` (2) units
  tall and a column half as wide; `CRAFT_MINIMAP_MAX_COLUMNS` (120) columns, a 4-unit margin, and the overview ruler at its
  right edge (`EditorModel.MinimapShown`: the text's viewport then keeps no room for the ruler; `OverviewRuler.Draw` takes a
  left edge and a width). Each line of the window (the rows the height shows, scrolled with the text once the file does
  not fit, VS Code's geometry, `MinimapRenderer.Geometry`) is one rect per piece of a coloured run, cut at white space
  (pieces of one colour across a single space join), in the run's colour a little see-through. The lines come from a
  per-line cache keyed by the text's version, the theme and the colours' stamp (`EditorModel.ClassificationStamp`, bumped by
  `ApplyReclassification`), bounded to the window and as many lines again around it, its entries reused (no allocation once
  warm). Colours are the syntactic layer's only: new default member `ILanguageSession.ClassifySyntactic` (contract 1.1,
  additive; the default classifies as `Classify`; C#'s session gives the parse's spans without Roslyn's semantic colours and
  never asks for a semantic pass; `SyntacticLanguageSession` forwards it); never waiting for a parse (a landed one redraws).
  **Incremental**: the model keeps a line-change log (`NoteLineChange` on each buffer change, merged change by change: the
  touched lines in the old version's lines and the line delta after them); a frame moves the kept lines through it, builds
  the touched (and never-built) lines at once, and checks the other kept lines' colours again at most
  `CRAFT_MINIMAP_LINES_PER_FRAME` (64) a frame; `Pending` makes the view draw the next frame until the cache equals a fresh
  build (a typed quote or `/*` colours the lines below). A translucent slider (new tokens `MinimapSlider`,
  `MinimapSliderActive`: the theme's text colour, faint, unless a theme sets them) covers the lines the viewport shows; a
  press away from it centres the line there at once and a drag goes on from there; a press on it drags it (the text scrolls
  at once, `ScrollPerSlider` text units a slider unit); a press on the ruler at its edge is the ruler's. Problems (the
  error or warning colour across the row) and the find box's hits behind the lines, `CRAFT_MINIMAP_DIAGNOSTICS`,
  `CRAFT_MINIMAP_FIND` (on). The view redraws only when `NeedsRedraw` says: the scroll, the text, the colours, the theme,
  the rows and folds, the marks it shows, the size, a drag, or pending colours; with the ruler at its edge the carets and
  the repository's marks too (the ruler's marks); never the blink. A frame that throws is reported through `PaintGuard`
  ("editor: minimap") and skipped.
- **Completion and ghost text at several carets** (`EditorModel.CaretCompletion.cs`). `CRAFT_COMPLETION_ALL_CARETS` (on):
  typing and Ctrl+Space at several carets ask once, at the primary caret; the list filters by the primary's word. A
  committed item goes to every caret with nothing selected whose text around it equals what the item replaces at the
  primary (the characters before the caret and after it), a word start staying a word start ("xCon" is not "Con"), its
  range overlapping neither the primary's nor the one taken before it; one change and one undo step through the
  multi-caret path (`ApplyEdits` with every caret's selection after it); the carets left stay, moved by the edits before
  them. Ghost text is asked at the primary and drawn there with a "+N" pill after its first line (the accent on the popup
  ground; `InlineCompletionController.OtherCarets`, counted once per change of the carets, the text or its place); Tab and
  Ctrl+Right insert at every caret whose line before it equals the primary's, its leading white space aside, the ghost's
  later lines taking each caret's indent; typing through it works at several carets. When some carets are left the status
  says so: "Completion applied at 2 of 4 carets: the text before the others differs." (`EditorModel.StatusRequested`, the
  page's `IEditorHost.ShowStatus` line, replaced by the next). Off, both close while there are several carets (X slice 2).
- **Decisions on the recommendation (one line each)**:
  - The minimap is its own view beside the text (a grid column), not a layer of the text's frame, so no text frame or blink repaints it.
  - With the minimap shown, the overview ruler moves to the minimap's right edge (the X restart's decision), drawn by the minimap through the same `OverviewRuler`.
  - A row is the scale in units and a column half of it: the default 2 is VS Code's default minimap (1 × 2 units a character).
  - One rect per piece of a coloured run, cut at white space, pieces of one colour joined across a single space (a comment's sentence is a few rects, not one bar).
  - Syntactic colours through a new default interface member, not a new registry (a language without semantic colours changes nothing).
  - An edit builds only its lines and re-checks the rest at a budget a frame (a new option, `CRAFT_MINIMAP_LINES_PER_FRAME`): the whole window of C# costs 2.2-4.3 ms to classify here, a keystroke's frame now 0.41-0.71 ms.
  - The re-check is proven against a fresh build after typed text, a line break, a quote, two undos and a theme (rule 71), not assumed for lines away from the edit.
  - The cache's bound follows the window (twice the window, trimmed at three times), so no new option for it.
  - Narrow editors show the ruler alone: a new option `CRAFT_MINIMAP_MIN_WIDTH` per view (a split pane is narrow too), and never more than half the view.
  - Only document views show it; the peek, the output and the chat's transcript never do.
  - A click away from the slider jumps (instant) and centres the line, as VS Code's default; then the same press drags.
  - A caret that moves redraws the minimap only for the ruler's caret mark (ruler on); with the ruler off it does not.
  - Completion's match is the replaced text on both sides of the caret, as VS Code checks it, plus the word start.
  - Ghost text's match is the line before the caret, leading white space aside (the model wrote it for that line), and its later lines are re-indented per caret.
  - A caret with a selection never takes a completion (the primary must be empty to complete at all).
  - The status says only when some carets were left; nothing when every caret took it.
  - Escape with ghost text at several carets dismisses the ghost first; the next Escape goes back to one caret.
- **Measured** (headless, Linux container, Release, the tests' fixed metrics; `Minimap.APaintOfA100000LineFileWithTheMinimapOnAndOffIsMeasured`,
  written to `test-output/benches/minimap.txt`; a 100,000-line C# file at line 50,000, 1200 × 800, the minimap 138 wide, a
  window of 402 lines; first alone, then in the full filter's run under other slots' builds):
  - Minimap off: cold frame 756.7 / 568.1 ms (the first parse of the 100,000 lines waited for); warm median 269.7 / 209.6 µs,
    p99 954.3 / 361.4 µs, 0 B a frame.
  - Minimap on: the text's cold frame 0.54 / 0.33 ms and the minimap's 9.72 / 8.78 ms (402 lines classified and built, and
    the JIT); the text and the minimap together warm median 229.8 / 223.2 µs, p99 307.2 / 338.8 µs, 0 B a frame; the
    minimap's own warm frame median 13.2 / 17.8 µs, 0 B a frame.
  - The minimap after a 3-line scroll: median 46.4 / 51.5 µs. After a keystroke: median 408.2 / 714.2 µs (the touched line
    and 64 lines' colours), then 6 frames of at most 427.3 / 844.3 µs to check the rest; the whole window built afresh:
    median 2,239.8 / 4,265.2 µs (classifying is 2.65 ms of 2.84 ms, building the rects 0.19 ms, measured once with a probe).
  - Before the incremental cache (the first version, rebuilding the window on every version): a keystroke's minimap frame
    median 2,051-2,695 µs.
  - The Windows numbers (Win2D's fills: a frame of the window draws about 2,000 rects) are the owner's: View › Idrak telemetry.
- **Windows-only lines**: none. `MinimapView` and `EditorView` are shared MAUI code (GraphicsView interactions); not compiled
  for the Windows target here. **Not run here**: the window (the minimap's look and drag with WinUI's pointer, the status line).
- **Left**: the minimap draws no selections or caret lines; no "slider on hover only" mode and no characters mode; the
  minimap's frame is not in the telemetry table yet (`editor.paint.*` is the text's); a fold's "..." is not drawn on it;
  `CRAFT_MINIMAP` stays off by default until the owner's Windows frame number is in (the X restart's decision); the flaky
  `Paint.AWarmFrame…` 88 B (s4's) was not seen in this slot's runs.
- **Tests**: new groups `Minimap` (7: a rect per coloured run from the cache keyed by version and theme, the syntactic layer
  only, an edit's touched lines and the budget, equal to a fresh build after five kinds of change; the slider at the top,
  the end and in a file that fits, the ruler at its edge; click, drag, the slider's drag, the ruler's press, narrow and off;
  no redraw on a blink or a caret move with the ruler off, a redraw on a scroll, an edit, a landed parse, a theme and a size;
  0 B over 200 warm frames and the cache bounded through a whole file; one minimap per view; the measure) and
  `CaretCompletion` (3: an item at the matching carets, left ones said, one undo, typing asks once at the primary, off;
  an item replacing text after the caret; ghost text's "+2", Tab with re-indented later lines, the status, one undo, off);
  `Inventory` (the eight options). The full filter
  (`Minimap|CaretCompletion|MultiCaret|Editor|Surface|OverviewRuler|StickyScroll|EditorGroups|AiPlugins|Settings|Ui|ApiGuard|Inventory`,
  146 tests) after the rebase onto X parts 6b and 7 and main's health: 146 passed, 0 failed. The API dumps of
  Craft.Abstractions, Craft.Core and Craft.Editor and the contract list (1.1) regenerated: lines added only.
- **Options added** (each with its shape on the settings page): `CRAFT_MINIMAP` (off), `CRAFT_MINIMAP_SCALE` (2),
  `CRAFT_MINIMAP_MAX_COLUMNS` (120), `CRAFT_MINIMAP_MIN_WIDTH` (600), `CRAFT_MINIMAP_DIAGNOSTICS` (on), `CRAFT_MINIMAP_FIND`
  (on) in Editor › Display; `CRAFT_MINIMAP_LINES_PER_FRAME` (64) in Editor › Advanced; `CRAFT_COMPLETION_ALL_CARETS` (on) in
  Completion › Completion list.
- **The owner's check** on `test-output\CarftTestPOC` (the minimap on through its variable, or Settings › Editor › Display ›
  Minimap): a long C# file: the minimap beside the text with the ruler at its right edge; drag its slider, click below it,
  type and watch its line change, Ctrl+F a word (its hits on it); Settings › Appearance to another theme; split right
  (Ctrl+\) and see one minimap a view, and none when the window is narrow. Then Alt+click three carets after `Con` on
  three lines and one after `xCon`, Ctrl+Space, Enter: `Console` at three, the status says 3 of 4, Ctrl+Z once; with the
  AI plug-in's completion, ghost text at several carets shows "+N" and Tab inserts it at each.
  ```powershell
  cd D:\Projects\Idrak; git checkout main; git pull origin main
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  $env:CRAFT_FILTER="Minimap|CaretCompletion|MultiCaret|Editor|Surface|OverviewRuler|StickyScroll|EditorGroups|AiPlugins|Settings|Ui|ApiGuard|Inventory"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_MINIMAP="true"; $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```

### X part 6 as built (2026-11-01): the Search view and the Run view

Slot s2. Craft only; Idrak unchanged (its process sandbox was checked for what the Run view needs: an infinite time
limit, `Timeout.InfiniteTimeSpan`, is taken, and a cancel kills the whole process tree; no cause was in the library).
- **Contracts** (`Craft.Abstractions`, additive, contract 1.1 already stepped by piece 1b): `TextPattern` (the find
  overlay's matcher, moved from `FindPattern`, which now wraps it with the same API, so a find in files finds what the
  find overlay finds); `WorkspaceSearch.cs`: `WorkspaceSearchQuery` (text, case, whole word, regex, include and exclude
  globs, ignore files), `WorkspaceSearchRequest`, `WorkspaceSearchHit`, `WorkspaceSearchFile`, `WorkspaceSearchSummary`,
  `IWorkspaceSearchProvider : IKind` and `WorkspaceSearchProviders` ("text" built in), `WorkspaceGlob`, `GitIgnore`,
  `WorkspaceFiles` (the walk) and `WorkspaceTextScan` (the matching any text provider shares); `Run.cs`: `RunActions`,
  `RunTarget`, `RunCommand`, `RunConfiguration`, `RunWorkspace`, `RunProblem`, `IRunProvider : IKind` and `RunProviders`
  ("scripts" built in, newest first), `RunCommandLine`, `RunProblems` (MSBuild's and the compilers'
  `file(line,col): error CODE:` lines); the blocks `PanelQuery` (a live search field with toggles and icon buttons),
  `PanelResults`/`PanelResultGroup`/`PanelResultRow`/`PanelSpan` (a grouped, virtualized results list) and
  `PanelButton.Icon`; `RailIcons.Run`, `Build`, `Stop`, `Test`, `Replace`, `Close`, `Edit`, `Add`, `Collapse`, `Refresh`;
  `ActivityKinds.Search`.
- **The "text" provider**: the workspace's files walked folder by folder (the ignored folders and links never entered,
  each folder's `.gitignore` read as it is reached, an excluded folder not entered), searched in parallel (one file per
  processor, `CRAFT_SEARCH_THREADS`), each read as bytes into its worker's pooled buffer, passed over when binary (a zero
  byte in its first 8,000) or larger than `CRAFT_SEARCH_MAX_FILE_MB`, decoded once (UTF-8 with or without its mark,
  UTF-16 by its mark) into a pooled character buffer; plain text whose case matters is first looked for in the UTF-8 bytes
  (a file without it is never decoded); plain text is found over the whole text at once (vectorized `IndexOf`) with the
  lines counted as it goes, a regular expression (compiled for a workspace search) one line span at a time; a line
  without a match allocates nothing. Open documents are searched in their current text, their files not read. Each
  file's matches are reported as soon as found; the search stops at `CRAFT_SEARCH_MAX_RESULTS` and says so.
- **The Search view** (`Craft.Editor.Search.WorkspaceSearchView`, the shell's own side view drawn from the blocks): the
  rail's Search opens it (it ran quick open before; a second click folds the side), Ctrl+Shift+F (the one-line selection
  as the query, the keyboard in the field), Ctrl+Shift+H (with the replace field); the query with Aa, ab and .* inside the
  field (Alt+C, Alt+W, Alt+R in the tooltips), the replace field with Replace all, the files to include and exclude with
  the `.gitignore` toggle; a status line (results in files, searching, capped, files passed over, the expression's
  problem, with the time and the first result's time in its tooltip); Collapse all, Refresh, Clear; results grouped by
  file in path order (the file's icon from `FileIcons`, its folder, its count; a click folds it; Replace in this file and
  Dismiss on hover), each line with its number and its match highlighted in the find colour (with a replacement typed:
  the match struck through and the replacement after it); a click opens the place with the match selected. The search
  waits `CRAFT_SEARCH_DELAY_MS` after a keystroke, the next keystroke cancels it, it runs as an activity (Cancel in the
  status bar), and its files arrive in batches (one UI turn takes every file found since the last).
- **Replace in files as one review** (`EditorHost.ProposeFilesAsync`, `FileChange`, `FilesEditResult`;
  `IEditorPresenter.ChooseFilesAsync`, a default member that asks each file in turn; `ProposalDiffPage` over several
  files, each hunk under its file's name): the matches are found again in the text the host reads (the open document's
  current text, or the file read off the UI thread into a document of its own, never a tab), so no position is stale;
  accepted, each open document takes its hunks as one undo step and each closed file is saved with them (its encoding,
  mark and line breaks kept); a file that changed while the user was asked is left and said; the shell's Undo bar takes
  everything back (each open document's step while it is its last, each file written back while the disk still holds
  what was written). Replace one match and Replace in this file go through the same review.
- **The Run view** (`Craft.Editor.Run.RunView`, `RunFile`): a rail item "Run"; what the workspace runs, found by every
  registered provider off the UI thread and found again when its folder watcher (`CRAFT_WORKSPACE_WATCHER`, a burst
  gathered for `CRAFT_WORKSPACE_WATCH_MS`) sees a file a provider watches change, an editor saves one, or a provider is
  registered; Run, Build, Test and Stop per row (on hover), "running" and "default" badges, a click makes the row the
  default; the header's Run (F5), Build (Ctrl+Shift+B), Stop (Shift+F5), Add configuration, Refresh; the configuration
  editor in the view (name, target, action, arguments, variables `A=1|B=2`, folder, build first; Save, Cancel, Delete),
  kept in `.craft/run.json` (`default`, `configurations`). Every run: the provider's command, the user's approval
  (`EditorQuestion` with the command line, folder, variables, sandbox and time limit; `CRAFT_RUN_APPROVAL` once a
  session for the same command line, or each run), the registered sandbox `CRAFT_RUN_SANDBOX` (Idrak's
  `CommandSandboxes`; "not registered" said, nothing runs), its output in a read-only terminal tab of its own ("Run:
  App", "Build: App", the command first, error lines in red, the end with exit code and time), an activity whose Cancel
  (and Stop) ends the process tree, its time limit `CRAFT_RUN_TIME_LIMIT_MINUTES` (0: none), and its problems (each once)
  in Problems as "File.cs (build)" groups, a click opening the file at the place.
- **The C# plug-in's provider** (`Craft.CSharp.Run.DotnetRunProvider`, "dotnet", registered in `CSharpPlugin`): every
  `.csproj` read as an XML stream (an Exe, WinExe, a web or worker SDK, or a MAUI app runs and publishes; a test project
  by `IsTestProject` or a test framework's package tests; every project builds; kind and frameworks in its detail),
  `.sln` and `.slnx` (build, test), `Properties/launchSettings.json` profiles ("Project": `dotnet run --launch-profile`
  with its arguments, variables and address; "Executable": its program; IIS Express and Docker left out); `dotnet run
  --project` (a multi-targeted project with this system's framework), `dotnet build/test/publish -nologo`, a
  configuration's arguments after `--` for a run. The "scripts" provider: `.ps1` (pwsh, else Windows PowerShell),
  `.cmd`/`.bat` (cmd.exe, Windows only), `.sh` (bash, else sh) at the root and in `scripts/`; a script without its
  program here says so instead of running.
- **Options** (each a `CRAFT_*` variable on the settings page): `CRAFT_SEARCH_MAX_RESULTS` (20,000),
  `CRAFT_SEARCH_MAX_FILE_MB` (20), `CRAFT_SEARCH_THREADS` (0: every processor), `CRAFT_SEARCH_IGNORE_FILES` (true),
  `CRAFT_SEARCH_PROVIDER` (text) under Files › Search; `CRAFT_RUN_SANDBOX` (process), `CRAFT_RUN_TIME_LIMIT_MINUTES` (0),
  `CRAFT_RUN_APPROVAL` (once) under Terminal › Run; the debounce is the existing `CRAFT_SEARCH_DELAY_MS`.

**Decisions on the recommendation (the owner asleep; one line each).**
- Both views are the shell's own side views drawn from the public block contract (added through `IEditorHost.AddPanel`), so they look and act as a plug-in's view; what they search and run comes from the registries.
- Two new general blocks (a search field, a grouped results list) in `Craft.Abstractions` rather than shell-only views: P6's code search and X part 8's Source Control draw with them too.
- `FindPattern`'s matcher moved to `Craft.Abstractions` as `TextPattern` (`FindPattern` wraps it, its API unchanged), so every provider matches as the find overlay does.
- The "text" provider is built into `Craft.Abstractions`, as `FolderWatchers`' "system" is.
- Plain text over the whole text, a regular expression line by line (exact line semantics: `^`, `$`, a CRLF's `\r` left out), a case-sensitive query looked for in the bytes first.
- Results sorted by path as batches arrive (the same order every search); results are as of the search (Refresh, a replace or the next keystroke searches again), not edited live.
- Replace re-finds the matches in the current text the host reads; a dismissed match is not replaced; a file changed meanwhile is left and said.
- `.gitignore`: each folder's read as reached, the last matching rule wins, a deeper file's after its parents'; on by default (`CRAFT_SEARCH_IGNORE_FILES`); the ignored folders always.
- Globs ignore case; a glob without '/' matches a name anywhere (a folder's name: everything in it), one with '/' is taken from the root; commas inside braces belong to the braces.
- The Run view asks itself, once a session for each exact command line (command, folder, variables), with no setting that runs without asking; a workspace cannot set `CRAFT_RUN_SANDBOX` or `CRAFT_RUN_APPROVAL` (what runs is code).
- The default sandbox is "process" (Idrak.Text's), not "approve-each" (whose words address the agent and which would ask a second time); any registered one through `CRAFT_RUN_SANDBOX`.
- No time limit by default (Stop ends a run); `CRAFT_RUN_TIME_LIMIT_MINUTES` sets one.
- One read-only terminal tab per configuration and action, its output batched per UI turn; a running program gets no input (the process sandbox closes standard input).
- F5 and Ctrl+F5 both run the default (there is no debugger); Ctrl+Shift+B builds the default's target; Shift+F5 stops every run; registered bindings, so both keymaps have them (Visual Studio's keys are the same).
- No default set: the first configuration, else the first target that runs; providers newest first, so a project's runner comes before the library's scripts.
- `.craft/run.json` read and written with the runtime's JSON reader and writer (no reflection, AOT-safe), a temporary file moved over it.
- The last run's problems replace the previous run's, shown beside the language's as "File.cs (build)"; a tool's own line (`CSC : error CS2001`) is not one.
- The C# plug-in's `plugin.json` declares contract 1.1 (it registers a run provider); the restart's one contract step was already taken.
- A failing expectation outside this work, met in the filter, was fixed: PluginKit's "newer contract" message now reads the running version (it said 1.0 after the step to 1.1).

**Measured** (`WorkspaceSearch.ColdAndWarmSearchesOverCraftsSourceSize`, the 4-core Linux container, Release; a fixture
of Craft's source size: 300 files, 105,000 lines, 4.6 MB; warm the median of 5; written to
`test-output/benches/workspace-search.txt`; every search found the same):
| Query | Matches | Cold | First result (cold) | Warm | First result (warm) |
|---|---|---|---|---|---|
| `search` (case ignored) | 104,976 | 37.6 ms | 11.6 ms | 18.7 ms | 0.6 ms |
| `Search` (case, whole word) | 35,061 | 16.5 ms | 0.4 ms | 16.3 ms | 0.3 ms |
| `x\d+ = ` (regex) | 105,000 | 76.6 ms | 3.9 ms | 39.9 ms | 3.2 ms |
The scan allocates 0 bytes for 100,000 lines without a match (text, whole word, compiled regex; warm). The owner's
Windows numbers (the test project and Craft's own source in the app) are still to be taken.

**Tests**: `WorkspaceSearch` (7: the provider equals a plain scan for six queries over generated files with a BOM, UTF-16,
CRLF, a binary and a 5,000-character line, the cap and the size limit; globs, the ignored folders and `.gitignore`;
open documents in their current text, the view's groups, highlights and a click opening the place, folding, match case,
an expression that does not parse; the next keystroke cancelling a running search; Replace all as one review of both
files, the open document one undo step, the closed file saved with its mark, Undo, a dismissed match left, a rejected
review; 0 bytes per line; the cold and warm numbers), `RunView` (5: the fixture's solution, two projects and a test
project, launch profiles and scripts with their actions and commands; a run asked first, through a fake registered
sandbox, its output tab, its errors in Problems, asked once a session or each time, a sandbox not registered; Stop
ending a real script's process tree through the process sandbox; `.craft/run.json` round trip, the configuration edited
in the view, the default, the watcher finding a new project; the keys in both keymaps, the rail's Search and Run, the
workspace's refusal). First run: 8 of 13 passed; the five failures were two bugs (an `XmlReader` loop skipped the element
after each value it read, losing a project's frameworks; a Windows folder taken on Linux threw in the problem parser)
and three wrong expectations of the tests; one more (the glob list split inside braces) after. The full filter after the
rebase onto pieces 1b, 3a and 2a: 195 of 199 passed; the four (the Edit menu now holds Find and Replace in files, the C#
plug-in's contract 1.1, an allowed wait that moved to `TextPattern`, PluginKit's 1.0 message) fixed, then all passed.
API dumps of Craft.Abstractions, Craft.Core, Craft.Editor, Craft.CSharp, the contract list and the inventory regenerated
(lines added only).

**Not compiled here**: nothing; no file under `Platforms/` changed and Craft.Maui compiled on its net10.0 target. **Not
run here** (the owner's run): both views on WinUI (the icon buttons' hover, the toggles, the virtualized results list
and its scroll as batches arrive, the field keeping the keyboard while the view redraws), the diff page across files, a
real `dotnet run` and `dotnet build` of the test project with its errors in Problems, cmd and PowerShell scripts.

**Left**: input to a running program (the process sandbox closes standard input; a run through a terminal host would
need a sandbox that keeps one, an Idrak contract to add if asked); debugging (not asked); npm scripts (W's plug-in);
P6's code search through the same view; the results do not follow edits made after the search.

**The owner's commands** (Craft only; Idrak unchanged):
```powershell
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="WorkspaceSearch.|RunView.|Shell.|Inline.|Keys.|Settings.|Terminal.|Plugins.|CSharp.|Semantic.|Ui.|Experience.|ApiGuard.|Inventory.|Design.|Layout.|Editor.|PluginKit.|PhaseP."; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
```
In the app: the rail's Search (or Ctrl+Shift+F) and a query typed (results as you type, Aa, ab, .*, the files to
include and exclude), a click on a result, Replace all (one review of every file, then Undo at the window's bottom);
the rail's Run: CarftTestPOC's project, F5 (asked once, its output in "Run: CarftTestPOC"), a typo in Program.cs then
Ctrl+Shift+B (the error in Problems as "Program.cs (build)", a click opens it), Shift+F5 on a long run, Add
configuration with arguments, saved in `.craft\run.json`.

### X part 7 as built (2026-11-01): rich context menus in the explorer and on the open tabs

Slot s2. Craft only; Idrak unchanged (nothing in the library was needed).
- **The menu** (`MenuModel`, `ShellMenus.Rows`, `Craft.Maui.MenuView`): every row has its icon (the design's stroke icons,
  `MenuIcons`, 1.5-wide like the rail's; a file type's badge from `FileIcons` through "file:Name"; the folder's), its keys in
  effect (`EditorHost.KeysOf`), a chevron for a submenu; groups with a line; a row that cannot run is faded and says why in
  its tooltip (`IEditorCommand.DisabledReason`, a default member; `EditorCommand.Reason`); the raised card with the strong
  border and a soft shadow, rows with no box of their own, the motion of the menus (`ShellMotion`). Submenus: the pointer
  resting `CRAFT_MENU_SUBMENU_DELAY_MS` (200) on a row, Right, Enter or a click opens one beside it (`PopupLayer.ShowBeside`,
  flipped left at the window's edge), Left or Escape closes it, its rows take the keys. Typing jumps: letters typed within
  `CRAFT_MENU_TYPE_AHEAD_MS` (700) form a prefix, the same letter again goes to its next row.
- **Contracts** (`Craft.Abstractions`, additions, contract 1.1): `MenuItem` gains `Icon`, `Submenu`, `When`, `Argument`;
  `MenuWhen` (target kinds `MenuTargetKinds` File, Folder, Root; file endings; languages; project kinds; a plug-in's test)
  with `Files`, `Folders`, `Root`, `Entries`, `AnyFolder`; `ContextMenuTarget` gains `IsRoot`, `Language`, `ProjectKinds`,
  `Argument`, `Kind`; `MenuItemSource` and its registry `EditorMenuSources` (rows made as a menu opens, from memory);
  `EditorContextMenus.OpenWith` and `.RunTargets` (the two submenus); `MenuIcons`. `ShellModel.DescribeMenuTarget` fills a
  target in (the page sets it as `MenuModel.Describe`): the workspace's folder for the tree's empty space and its header,
  the file's language, the run providers it belongs to (its own targets, else its nearest folder's; a folder's within it).
- **The explorer's menus**. A file: Open, Open to the side (the next group, else a split right), Open with ▸ (each
  registered language by its file icon, then plain text), Reveal in File Explorer (Shift+Alt+R), Open in terminal, Find in
  folder… (the Search view, its files to include set to the folder), Run ▸ (its own run targets' Run, Build and Test through
  the Run view: approved, sandboxed), Format file (opened first when not open, one undo step, left unsaved), the AI
  plug-in's Add to chat context and Ask the agent about it, Cut, Copy, Paste, Duplicate, Select for compare, Compare with
  selected, Copy name, Copy path (Shift+Alt+C), Copy relative path, Rename…, Delete. A folder: New file…, New folder…,
  Reveal, Open in terminal, Find in folder…, Run ▸ (its runners), Collapse all inside, Cut, Copy, Paste, Duplicate, Copy
  name, Copy path, Copy relative path, Rename…, Delete. The workspace (its header, the empty space under the rows): New
  file…, New folder…, Reveal, Open in terminal, Find in folder…, Collapse all, Refresh (read off the UI thread now), Paste,
  Copy path, Open folder….
- **The tab's menu**: Close (Ctrl+W), Close others, Close to the right, Close saved, Close all, Reopen closed tab
  (Ctrl+Shift+T), Pin or Unpin, Split right (Ctrl+\), Split down, Move to the other group (Ctrl+Alt+Right), Copy name, Copy
  path, Copy relative path, Reveal in the explorer, Reveal in File Explorer, Open in terminal, Format file, Compare with
  saved, Revert file, Run ▸ (when the file has a runner), Add to chat context. Pinned tabs (`DocumentTab.IsPinned`,
  `DocumentTabs.SetPinned`, `PinnedCount`) stay at the left (a drag keeps a pinned tab among them and others after them), show
  a pin where the close button is (a click unpins), and Close others, Close to the right and Close saved leave them.
- **Undoable instead of asked**: Paste (copies get "Name copy.cs" on a name in use; a cut moves once, its clean tabs
  following; a folder never into itself), Duplicate, Delete (the entry kept aside under the system's temporary folder up to
  `CRAFT_DELETE_UNDO_MAX_MB` (64), then to the Recycle Bin, or for good where there is none; Undo copies it back and opens
  its closed tabs again; a larger one is asked about first, as before), Rename ("Renamed A.cs to B.cs", Undo the old name),
  Revert (the saved text as one undo step, the tab clean: `TextDocument.MarkUnchanged`; Undo, or Ctrl+Z), Compare with
  selected (accepted hunks, Undo). Every disk access runs off the UI thread, the longer ones as activities ("Copying",
  "Deleting", "Keeping X for Undo", "Restoring", "Reverting").
- **Names typed in place** (`ShellModel.TreeEdit`, `TreeEditKind`, `BeginTreeEdit`, `TreeEditProblem`,
  `CommitTreeEditAsync`, `CancelTreeEdit`; `FolderTree.BeginNew`/`EndNew`, `FolderTreeRow.IsNew`; the tree view's field):
  New file and New folder add an empty row at the top of the folder, Rename edits the row's name (the stem selected); the
  problem shows as it is typed (the error's colour, the reason in the tooltip: a name that cannot be, one the folder has),
  Enter or leaving the field takes it, Escape ends it. The narrow layout keeps the dialog.
- **Compare** (the existing diff view): Compare with saved shows the tab's changes since its save as hunks; a hunk accepted
  goes back to the saved text (one undo step), all of them make the tab clean. Compare with selected is a review across files
  (`ProposeFilesAsync`) of the selected file against the other's lines. `LineEdits.Between` makes one edit a changed run of
  lines (proven on the result; a diff that does not map back gives one edit of the whole text).
- **Reopen closed**: up to `CRAFT_TABS_REOPEN_LIMIT` (20) closed tabs, each with its group, place, selection and pin, the
  disk asked off the UI thread.
- **Options** (each with its `CRAFT_*` variable and shape on the settings page): `CRAFT_TABS_REOPEN_LIMIT` (20),
  `CRAFT_DELETE_UNDO_MAX_MB` (64), `CRAFT_MENU_TYPE_AHEAD_MS` (700), `CRAFT_MENU_SUBMENU_DELAY_MS` (200).
- **Found and fixed**: a move or paste into the workspace's own folder was refused ("must both be in the open folder"):
  `Workspace.Contains` says no for the root itself, so a tree drag onto a root file's row never moved (piece 3a).

**Decisions on the recommendation (the owner asleep; one line each).**
- One menu per place (tree, tab) whose rows show by their `When`, not a menu per kind of target: a plug-in adds a row once and says where it shows.
- A "when" is declarative (kinds, endings, languages, project kinds) with a plug-in's test as the escape hatch; tests run on the UI thread from memory only.
- A project kind is a run provider's name ("dotnet", "scripts", W's "npm"), so it follows what the Run view finds; no second project model.
- Rows made as a menu opens come from a registry of sources (`EditorMenuSources`), and a row's `Argument` reaches its command through the target: one command serves many rows.
- A disabled row stays visible with its reason as its tooltip (a disabled control shows no tooltip on WinUI, so the row is faded, not disabled).
- One level of submenu is drawn; the model counts deeper rows (a submenu with no row that can run is disabled with its reason).
- Keys are shown only where a binding runs the command (Cut, Copy and Paste in the tree have no tree-focused key yet, so they show none).
- Run ▸ on a file lists its own targets only (a .csproj, .sln, script); a source file's project kind still says its project, for a plug-in's "when".
- Delete keeps a copy aside for Undo (VS Code's way) rather than restoring from the Recycle Bin: the same on every system, the Recycle Bin still gets the file.
- Over `CRAFT_DELETE_UNDO_MAX_MB` the old question is asked (copying a large folder aside would be slow).
- Cut, Copy and Paste of files are Craft's own (the paths are held in the shell; the system clipboard keeps its text).
- A pasted name in use takes "Name copy.cs" (as Duplicate and a Ctrl+drag do); a cut's move refuses a name in use, as a drag's move does.
- Close all closes pinned tabs too ("all"); Close others, Close to the right and Close saved spare them.
- Pins are kept for the session only (not in the layout file yet).
- Compare uses the existing review: Compare with saved's accepted hunks go back to the saved text; Compare with selected's take the other file's lines into the selected one (Reject or close leaves both).
- Revert is done at once with Undo (no dialog) and makes the tab clean; Undo makes it unsaved again.
- Format file from the explorer opens the file first and leaves it unsaved (the user sees the change and saves).
- Names typed in place commit when the field loses the keyboard (VS Code's), keep the field on a name that cannot be, and end on an empty new name.
- The AI plug-in registers Add to chat context and Ask the agent about it (plug-ins first), through `IConversationPanel.AcceptDropAsync`.
- The git plug-in's items (X part 8) keep their group 60; they can take icons and a `When` the same way.

**Tests**: `ContextMenus` (6: a file's, a folder's and the workspace's rows, their icons, groups, keys, disabled reasons,
the described target, Open with and Run submenus by keys, typing to jump, a tab's rows; a plug-in's rich items by ending,
language and project kind, its submenu rows from a source run with their argument, a throwing test or source hides only its
own; paste twice with a free name and Undo, a folder into itself refused, cut and paste at the root and Undo, duplicate and
Undo, delete of a file and a folder with Undo (text and tab back), over the limit asked; rename in place with its problems
and Undo, new file and folder rows, Escape, Collapse all inside and all, the dialog without the field; pins, close others,
close saved, reopen closed with its place, pin and selection, the reopen limit, close all; compare with saved by hunks,
revert with Undo, compare with selected, `LineEdits` over CRLF, a lone CR, no last break and an empty text), with
`Experience` (the tree's and tab's menus' expectations, delete with Undo) and `Inventory` (the four options). First run: 19
of 22 passed; two wrong expectations of the tests (a source file belongs to its nearest folder's scripts; a test item took a
plug-in row's name) and the root-move bug above; then all passed. The full filter after the rebase onto X parts 8, 10 and 11
(`ContextMenus|Experience|Shell|EditorGroups|TreeMove|WorkspaceSearch|RunView|Inline|Keys|Settings|Ui|Layout|ApiGuard|Inventory`,
138 tests): 137 passed; `Ui.TheUiSideHasNoSynchronousWait` fails on `src/Craft.Ai/CopyGate.cs:40` (`_door.Wait(0)`, X part
11's non-blocking try, not this work; left to its slot). API dumps of Craft.Abstractions, Craft.Core, Craft.Editor, Craft.Ai,
the contract list and the inventory regenerated (lines added only).

**Not compiled here**: nothing under `Platforms/` changed; Craft.Maui compiled on its net10.0 target. **Not run here** (the
owner's run): the menus on WinUI (the icons' look, the submenu card beside its row and its hover delay, tooltips of faded
rows, typing), the tree's field (its focus, the stem selected, Enter, Escape, leaving it), the pin on a tab, the Recycle Bin.

**Left**: tree-focused keys (F2, Delete, Ctrl+C/X/V in the explorer); several rows selected at once in the tree; pins kept in
the layout file; cut rows drawn faded; files cut or copied to and from the system's file manager (CF_HDROP on Windows).

**The owner's commands** (Craft only; Idrak unchanged by this work):
```powershell
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="ContextMenus|Experience|Shell|EditorGroups|TreeMove|WorkspaceSearch|RunView|Inline|Keys|Settings|Ui|Layout|ApiGuard|Inventory"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
```
In the app: right-click a file, a folder, the explorer's header and the space under the rows; rest on Open with and on Run
(on the .csproj); type letters in an open menu; Right and Left; hover a faded row for its reason; New file (type a name,
Enter), Rename (a name in use shows red), Escape; Copy then Paste on a folder twice; Cut and Paste; Duplicate; Delete then
Undo at the window's bottom; right-click a tab: Pin, Close others, Close saved, Reopen closed (Ctrl+Shift+T) at its caret;
edit Program.cs, then Compare with saved and Revert file (and Undo); Select for compare on one file, Compare with selected on
another.

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

### X part 6b: input to a running program (found 2026-11-01 by X part 6)
Idrak's "process" sandbox closes standard input, so a console program run from the Run view (the owner's test project is
a console app) cannot read the keyboard. Idrak gap, fixed in Idrak on its `main` by its rules: the sandbox contract gains
an interactive start (`ICommandSandbox.Start` returning the process's input and output streams, additive, refused with
the reason by a sandbox that cannot), the "process" sandbox implements it, and "approve-each" asks once as before; the
same contract serves W's npm scripts that read input and W-semantic's language servers later. Craft: a run whose target
reads input (a console project, or `CRAFT_RUN_INTERACTIVE` per configuration, default auto) runs in a terminal tab
that takes the keyboard (B9's terminal over the streams), Ctrl+C sent to the program, Stop as now. Tests: Idrak
`sandbox` (a child echoing its input through the streams, the tree killed on stop, memory flat); Craft `RunView` (a
fixture console program reads two lines typed into its tab). Decision (recommended, noted): the "process" sandbox
first; "container" later. Slot: the next free slot after part 7 starts (Idrak part first).

### X part 6b and the paint allocation as built (2026-11-01)

Slot s4. Idrak `main` 9e63a92 first (Idrak's gap fixed in Idrak), then Craft.

**(A) Input to a running program.**
- **Idrak** (`Idrak.Abstraction/CommandSandboxes.cs`, additive): `ICommandSandbox.CheckStart(limits)` and
  `ICommandSandbox.StartAsync(command, directory, limits, ct)` (default members: a sandbox without an interactive start
  says "cannot start a command interactively", naming process, and `StartAsync` throws `NotSupportedException` with it);
  `CommandStart` (`Session` or `Refusal`, `Started`); `ICommandSession` (`ProcessId`, `Input`, `Output`, `Error`,
  `Completion` with the `CommandResult`, `Stop` ending the tree; `DisposeAsync` stops a running command);
  `CommandResult.Stopped`. The "process" sandbox (`ProcessCommandSandbox.StartAsync`, `ProcessCommandSession`) starts it
  as `RunAsync` does (the programs allowed, found on the PATH, the variables; the shared preparation is one method now),
  keeps standard input open and hands the process's own three pipes over (.NET's `Process` on every platform, rule 70; the
  input flushed on every write), watches the time and memory limits and `Stop` (the whole tree killed). "container" keeps
  the refusal. Test (`command sandboxes: interactive start`, CPU): a child of the test program (`--sandbox-child echo`)
  echoes two lines (one with non-Latin text) sent through the streams, an error line on its error stream, its variable
  set, and exits with 3 at the end of its input; **5,000 lines through the streams grew the managed memory by 176 bytes**;
  Stop kills a child and the grandchild it started; the time limit; a dispose stops; a program not allowed or not found is
  a refused start; network off throws naming container; a cancelled start throws; a sandbox without it (an app's, container)
  refuses with the reason. Passed on the first run; `public API` and the inventory regenerated (lines added only); the
  filter `command sandboxes|public API|abstraction inventory|plugins: coding: CodingPlugin` 10 of 10.
- **approve-each** (`plugins/Craft.Plugins.Sandbox`): `CheckStart` is the inner sandbox's; `StartAsync` asks the same
  question once (the same per-run allowance), then passes the inner sandbox's start through as it is; the "Agent sandbox"
  tab shows the command ("interactive: its input and output go to the caller") and its end. Run and start share one
  approval method now.
- **Craft contracts** (`Craft.Abstractions`, additive, contract 1.1): `IEditorTerminals.OpenProgram(EditorProgramRequest)`
  (a default member, null without terminals) → `IEditorTerminalProgram : IEditorTerminalOutput` (`OutputEnded`,
  `End(text, exitCode)`); `EditorProgramRequest(Name, Input, Output)` with `Error`, `Stop`, `Folder`, `Header`;
  `RunTarget.ReadsInput`; `RunInteractive` (Auto, On, Off) and `RunConfiguration.Interactive` (in its equality).
- **The program tab** (`Craft.Editor.Terminal.ProgramTerminalSession`, `TerminalInstance.Program`/`IsProgram`,
  `TerminalManager.OpenProgram`, the plug-in scope's `ScopedProgram`): the program's output and error read in pooled 16 KB
  chunks (at most 64 waiting, then its reads wait), a lone LF drawn as CR LF, parsed by B9's VT parser, the header and the
  plug-in's notes in order between them. What is typed goes through a terminal's line discipline, since the program reads
  a pipe and has no terminal: printable text echoed and kept, Backspace (a UTF-8 character whole), Ctrl+U, Enter sends the
  line with "\n", Ctrl+D sends a started line as it is and at a line's start closes the input, key sequences dropped,
  Ctrl+C (with nothing selected; with a selection it copies, as before) calls the run's Stop. Closing the tab of a running
  program stops it. A tab of the same name that ended (or an output tab) is replaced in its place; the new tab is the
  active one and `Show` gives it the keyboard.
- **The Run view**: `RunView.TakesKeyboard` (the configuration's on or off, else `CRAFT_RUN_INTERACTIVE`'s on or off, else
  auto: the target reads input); only the Run action (a build first, a test, a publish show their output as before). The
  approval lists "Input: its terminal tab takes the keyboard"; asked once a session per command line as before. The run
  goes through `StartAsync` on the thread pool, its tab "Run: NAME", its output also read on the way (`LineTap`) for
  Problems (so `dotnet run`'s compile errors still land there) and the activity's last line; its end said in the tab
  (exit code and time, stopped, the time or memory limit). Stop, the activity's Cancel and Ctrl+C end the tree. A sandbox
  that cannot start interactively: the run shows its output only (logged); a host without program tabs: the input closed,
  the output as lines in the old output tab. The configuration editor has "Takes the keyboard" (Auto, On, Off) for a run;
  `.craft/run.json` keeps `"interactive": "on"|"off"` (auto not written; another value passed over and said). Targets that
  read input: a console project (`OutputType` Exe of the plain SDK; not web, worker, MAUI, WinExe or a test project) and
  its "Project" launch profiles (`ProjectFacts.Console`), and every script; an "Executable" profile does not (On sets it).
- **Option**: `CRAFT_RUN_INTERACTIVE` (auto; auto, on, off) under Terminal › Run.

**(B) The flaky allocation in `Paint.AWarmFrame…`** (88 B in a few frames after Shell, Ui and Experience). Rule 90 in order:
1. The type: the allocation ticks named nothing of it (88 B is too little for a tick; the ticks that came were other
   threads'); 88 B is a `TaskCompletionSource` (24) with its `Task` (64). The test's failure message now names the ticks
   (`Craft.Tests.Runner.AllocatedTypes`, Idrak's helper in Craft's tests), with that caveat.
2. The step: the renderer's steps measured apart (a temporary counter on `EditorRenderer.Step`, removed): every byte was in
   "classify the lines on screen" (352 B = 4 × 88), every other step 0.
3. The JIT: with every method held at its unoptimized tier (`DOTNET_TC_CallCounting=0`) the test alone allocated 0: not a
   rule 88/89 box. No collection ran during those frames (the background and gen-2 counts unchanged): not Roslyn's weak
   caches either (B1's other cause).
4. The site: `CSharpLanguageSession.WantColors` (the C# session's semantic colours) made a new `_colorsIdle`
   `TaskCompletionSource` on the UI thread each time a frame asked for colours after the last pass had gone idle. The
   fixture has no workspace document, so each pass ends at once and the next frame asks again: one source every
   `SemanticColorsDelayMilliseconds`. Alone, the first pass (Roslyn's host still loading) does not end within the 1,000
   frames; after Shell, Ui and Experience it is warm and cycles (the earlier pass with `DOTNET_TieredCompilation=0` is
   consistent with that: a slower first pass, not a box).
   **Fix, at its site** (Craft.CSharp; nothing in Idrak): the idle source is made only when someone waits
   (`WhenColorsIdle`); a frame's want sets a flag. The test is unchanged (not loosened, not skipped). The reproduction
   (`Shell|Ui|Experience|Paint.AWarmFrame`) failed 3 times in 4 before and passed 5 times in 5 after; warm frame median
   172.2 us, p99 320.6 us, 0 B (this container, under the other slots' builds).

**Decisions on the recommendation (the owner asleep; one line each).**
- The interactive start is two default members on `ICommandSandbox` plus `ICommandSession` and `CommandStart` in `Idrak.Abstraction` (additive; every existing sandbox compiles unchanged and refuses with the reason).
- A limit the sandbox cannot keep throws `NotSupportedException` (as `RunAsync`); a program not allowed or not found is a refused `CommandStart` (as `RunAsync`'s refused result).
- The streams are the process's own pipes (nothing kept between them and the caller), the input flushed on each write.
- The time and memory limits apply to an interactive start; the output cap and `CommandLimits.Output` do not (the caller reads the output).
- No interrupt in the contract: a pipe carries no signal, so Ctrl+C stops the tree (as an unhandled Ctrl+C ends a program); a container's signal can come with "container"'s start later.
- `StartAsync`'s token cancels the start only (approve-each's question), never the started command.
- No telemetry added: the existing runs record none either (the run is an activity in Craft, as before).
- "process" first; "container" keeps the default refusal (the plan's "container later").
- approve-each asks once with the same question and allowance, then passes the start through; it shows the command and its end, not the program's streams.
- A program tab of its own (`OpenProgram`) on B9's screen and VT parser, not a shell host: the run's process is the sandbox's, never the terminal host's.
- The tab gives a line discipline (echo and line editing) because the program reads a pipe; programs that need a real console (`Console.ReadKey`, cursor moves) are not served (a pseudo-console sandbox would be).
- `CRAFT_RUN_INTERACTIVE` defaults to auto; a configuration's own value wins; only a Run takes the keyboard.
- A workspace may set `CRAFT_RUN_INTERACTIVE` (it changes how a run shows, not what runs; the approval still asks).
- A sandbox without an interactive start makes the run output-only (logged), not refused; a host without program tabs closes the input and shows lines.
- An ended tab of the same name is replaced in its place, so the next run of the same configuration reuses its spot.
- The output is read on the way for Problems and the activity (`LineTap`), so a run's build errors still reach Problems.
- (B) was fixed in Craft.CSharp, where it lives; Idrak's helpers were not on the path.

**Commits**: Idrak 9e63a92; Craft: this commit (contracts, program tab, Run view, approve-each, the colours' idle source,
the tests, the API dumps of Craft.Abstractions (and its contract list), Craft.Core, Craft.Editor, Craft.CSharp and
Craft.Plugins.Sandbox, lines added only).

**Tests.** New: `RunView.AConsoleProgramReadsTwoLinesTypedIntoItsTabAndStopEndsIt` (a fixture script, `.sh` here and `.cmd`
on Windows, asks for two lines; "Ada" and "Bobx" with a Backspace typed through the terminal view's keyboard come back as
"got Ada" and "got Bob", echoed before the answers, exit code 0 said; Stop ends a program waiting for input; the next run
is not asked again and replaces its ended tab; Ctrl+C in the tab ends it), `Terminal.AProgramTabEchoesAndEditsTheLine…`,
`Terminal.TheManagerReplacesAnEndedTab…`; extended: `RunView.TheProvidersFind…` (which targets read input),
`RunView.RunJsonRoundTrips…` ("interactive"), `PhaseP.P5…` (approve-each's interactive start asked once and passed through,
a denied one refused), `Inventory.OptionsReadEveryVariable`. First run: 80 of 82 (Ctrl+C after Ctrl+D was dropped; P5's
loader check expected contract 1.0, fixed upstream meanwhile); then both passed. After the rebase onto X parts 7, 8, 10, 11
and the health fixes, the full filter `RunView|Terminal|Paint|Editor|Surface|Shell|Ui|Experience|Conversation|Settings|ApiGuard|Inventory|PhaseP`:
180 of 180.

**Not compiled here**: nothing under `Platforms/` changed. **Not run here** (the owner's): the program tab on WinUI (the
keyboard in it, the echo after a prompt), a real `dotnet run` of the test project reading input, `.cmd` scripts.

**Left**: programs that need a console (ReadKey, full-screen) need a pseudo-console start (a "conpty" sandbox, not asked);
"container"'s interactive start; a session without a workspace still asks for a colour pass each delay while frames come
(no allocation now, but wasted passes); `CSharpLanguageSession._diagnosticsIdle` makes its source the old way on the
keystroke path (not the paint's; same fix if measured).

**The owner's commands.** Idrak first (Craft builds against it):
```powershell
cd D:\Projects\Idrak; git checkout main; git pull origin main
dotnet build -c Release tests\Idrak.Tests
$env:IDRAK_DEVICES="cpu"; $env:IDRAK_FILTER="command sandboxes|public API|abstraction inventory"; dotnet run -c Release --no-build --project tests\Idrak.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:IDRAK_FILTER; Remove-Item Env:IDRAK_DEVICES
```
Then Craft:
```powershell
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="RunView|Terminal|Paint|Editor|Surface|Shell|Ui|Experience|Conversation|Settings|ApiGuard|Inventory|PhaseP"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
```
In the app: make CarftTestPOC's `Program.cs` read input (for example `Console.Write("Name? "); var name = Console.ReadLine(); Console.WriteLine($"Hello {name}");`
and a second `ReadLine`), then the rail's Run, CarftTestPOC, F5: the question says "Input: its terminal tab takes the
keyboard"; Run; the tab "Run: CarftTestPOC" shows the build, then "Name? " with the keyboard in it: type a name (Backspace
works) and Enter, then the second line; it answers and ends with its exit code. F5 again (not asked again) and Ctrl+C in the
tab, or Shift+F5: "[stopped, with its process tree]". Add configuration with "Takes the keyboard: Off" gives the old
output-only tab.

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

### X part 9, editing helpers 1, 2, 6-9, as built (2026-11-01)

Slot s2: X part 9's word wrap (1), bracket pair colours (2), diagnostics at the line end (6), the lightbulb (7), rendered
white space, rulers, trim and final newline on save (8), and `.editorconfig` applied per file (9). Craft only; nothing in
Idrak (no cause there).
- **Word wrap** (`Craft.Editor/WordWrap.cs`, `EditorModel.Helpers.cs`). `CRAFT_WORD_WRAP` (off, on, column),
  `CRAFT_WORD_WRAP_COLUMN` (80), Alt+Z (`craft.editor.toggleWordWrap`; Visual Studio's keymap Ctrl+E Ctrl+W) for the editor
  in hand, the line at the top kept. `WrappedLine`: a line's row starts (a character index and its column), the indent its
  continuation rows keep (the line's own, when at most half the width), made from the line's text and kept while that text,
  the width and the tab size stay (whatever the snapshot's version). Rows break after white space; inside a word only when it
  is wider than a row; white space may hang past the width. The rows of all lines are a binary indexed tree over the lines
  (a folded line counts none): a line's row and a row's line are each one walk of the tree's height; a line never shown counts
  one row until it is, so a frame wraps only the lines it shows (`PrepareFrame`: the viewport's lines wrapped until their rows
  stop moving), the kept wraps are trimmed like the run cache, and an edit splices the counts of the lines it touched
  (`LineEdits`, shared with the bracket states). A new width wraps again keeping the top line where it was. The model's row
  API (`RowCount`, `RowOfLine`, `LineAtRow`, `RowsOfLine`, `VisualOf`, `PositionAtVisual`, `PointFromPosition`,
  `PositionFromPoint`, `EnsureCaretVisible`) follows the rows, so the hit tests, the popups' anchors, sticky scroll, the
  overview ruler and the line marks need no change of their own. Up/Down (and PageUp/Down, Ctrl+Alt+Up/Down) move by visual
  row at the screen's column; Home on a continuation row goes to the row's start, again to the line's Home; End on a row
  before the last goes to the row's end, again to the line's end. The renderer draws a wrapped line row by row, each row a
  kept `StyledLine` of its own (B1's one layout a shown line becomes one a shown row), and splits the selection, find hits,
  the current word, the bracket match and squiggles per row; a folded header's "..." and the line-end message follow its last
  row; the line number and the chevron stay on the first.
- **Bracket pair colours** (`Craft.Editor/BracketColors.cs`). `CRAFT_BRACKET_COLORS` (on), `CRAFT_BRACKET_COLORS_SCAN_LINES`
  (20,000). New theme tokens `BracketDepth1..6` (the theme's accent, info and success, then those three mixed a third toward
  the text's colour), `BracketUnmatched` (the error colour) and `EditorColors.BracketDepth(int)`. The state at each line's
  start (the depth, a block comment open) is kept per line; strings and comments are found by the language's rules, so a
  depth needs no parse: new `LanguageRules.StringDelimiters` and `StringEscape` (contract 1.1, additions), set by the C#
  plug-in's languages (C# `"'`, Razor, JSON `"`; XML and .sln `"` with no escape). A frame reads at most the option's lines of
  states it does not know (a jump deep into a long file colours over a few frames, asking for each next frame, never one long
  one); an edit splices the states and the lines after it are read again only until one starts in the state it had. Each
  coloured bracket is a run of its own colour in the run cache, its key holding the colours, so a warm frame builds nothing.
  An opening bracket is unmatched when the depth never comes back below it before the file's end (known once every line was
  read: the least depth from each line to the end, one pass back); a closing one with nothing open at once.
- **Diagnostics at the line end**. `CRAFT_INLINE_DIAGNOSTICS` (off, errors, all; errors). The first line of the message of the
  line's most severe diagnostic (those starting on the line), italic, in new faded tokens `InlineError`, `InlineWarning`,
  `InlineInfo`, two cells after the text (after the fold marker), cut with an ellipsis to the room left of the overview ruler;
  a cut message is kept per line, so a warm frame makes no string.
- **The lightbulb** (`Craft.Editor/Lightbulb.cs`). `CRAFT_LIGHTBULB` (on), `CRAFT_LIGHTBULB_DELAY_MS` (250). After the caret and
  the diagnostics stop changing for the delay, the host's probe is asked off the UI thread about the diagnostics at the caret,
  else those on its line, else the selection; the answer is kept per text version and range (a caret coming back asks
  nothing). The shell's probe (`ShellModel.LightbulbProbe(tab)`) asks the registered code action providers on the tab's
  document. Drawn at the gutter's left on the caret's line (new token `Lightbulb`, the warning colour); a click puts the caret
  on the problem when the bulb was for one elsewhere on the line, then `EditorModel.QuickFixRequested` runs `craft.quickFix`.
- **White space, rulers, save actions**. `CRAFT_RENDER_WHITESPACE` (none, boundary, selection, all; selection): a dot a
  space, an arrow a tab, token `Whitespace`, only on the columns and rows on screen. `CRAFT_RULERS` ("80,120"; empty, the
  default, none) plus a file's `max_line_length`, token `VerticalRuler`, under the text, the columns parsed once per value.
  `CRAFT_TRIM_TRAILING_WHITESPACE` and `CRAFT_INSERT_FINAL_NEWLINE` (both off): Save (after format on save) trims and ends the
  text with a break, each one undo step, through the tab's editor (`TrimTrailingWhitespace`, `InsertFinalNewline`, also the
  command `craft.editor.trimTrailingWhitespace`) or, with no editor, on the document (`SaveActions`).
- **`.editorconfig` applied** (new contract `FileSettings`, `IFileSettingsProvider` and the `FileSettingsProviders` registry in
  `Craft.Abstractions`, the library's `editorconfig` provider its default; `CRAFT_EDITORCONFIG`, on). The files from the file's
  folder up to the first `root = true`, sections matched per file (`*`, `**`, `?`, `[set]`, `[!set]`, `{a,b}`, `{1..9}`; a glob
  without '/' at any depth), the nearer file and the later section winning, `unset` taking a value back. Read off the UI thread
  and kept per folder with its write time (`ForFileAsync` reads again a file whose time changed; `Cached` answers at once
  when every folder was read). `indent_style`, `indent_size`/`tab_width` (the tab size), `end_of_line` (the break the editor
  inserts), `charset` (a new file's encoding on its first save), `trim_trailing_whitespace`, `insert_final_newline`,
  `max_line_length` (a ruler), each over Craft's options (`EditorModel.ApplyFileSettings`; the model is not made again). The page
  applies them when a tab's view is made (at once when cached, else when read) and again when the shell says a file's settings
  changed: an `.editorconfig` saved in the editor or made, removed or renamed in the workspace (`FolderTreeWatch.EntryChanged`),
  and any file saved (its path may be new). The status bar's Spaces and line ending items name the `.editorconfig` in their
  tooltips (`ShellText.IndentationTooltip`, `LineEndingTooltip`).
- **Measured** (headless, Linux container, Release, the tests' fixed metrics; `WordWrap.AWarmFrameOfAWrapped100000Line...`,
  written to `test-output/benches/editing-helpers.txt`): a wrapped 100,000-line file (each line about 115 characters, two rows
  at 800 wide) with bracket colours, inline diagnostics (10,000 of them) and two rulers, 800 x 600, scrolled to line 50,000:
  cold frame 76.1 ms alone (the JIT, 20,000 bracket states read) and 16.2 ms in the full filter's run; the rest of the states
  in 5 frames (24.6-26.9 ms in all); **warm frame median 14.4-22.7 us, p99 37.8-49.3 us, 0 B a frame** (1,000 frames), nothing
  wrapped, read or laid out; 16 lines in 32 rows drawn, 16 lines' wraps kept of 100,000. B1's C# paint test (bracket colours on
  by default now) still 0 B a warm frame.
- **Decisions on the recommendation (one line each)**:
  - Unshown lines count one row until shown (a height map, as CodeMirror's), not a whole-file wrap at a width change.
  - The rows are a binary indexed tree over the lines, rebuilt in one pass when the folds or the line count change.
  - Rows break after white space; white space hangs past the width; a continuation row keeps the line's indent when it is at most half the width.
  - "on" wraps at the editor's width less the gutter, the ruler and a cell, never under 8 columns; "column" wraps at the option's column and keeps sideways scrolling.
  - Alt+Z switches the editor in hand (a settings change makes the model again with the options' mode).
  - End on a wrapped row stops before its last character (the next row's start is drawn at the row's end; no caret affinity).
  - Column selection and the line commands stay by line under wrap; the sticky header and a fold's preview show a wrapped line's first row.
  - Bracket depth is one counter for every pair (a ')' closing a '[' is not told apart), as VS Code's default.
  - Strings and comments for the depth come from new `LanguageRules` members, not the classifier (no parse, the same answer on every frame).
  - A string never spans a line for the depth (a verbatim or raw string over lines is approximate there; a block comment does span).
  - The budget per frame is an option (20,000); the frames that could not finish ask for the next frame.
  - Unmatched openings are told once every line is read; until then they take their depth's colour.
  - Inline messages: the line's most severe diagnostic that starts on it, its first line, italic, two cells after the text.
  - The lightbulb asks after a pause off the UI thread (the providers' `ActionsFor` is cheap and touches no UI) and is drawn at the gutter's left.
  - Render white space defaults to "selection" (VS Code's); rulers default to none.
  - Trim and the final newline run after format on save, each its own undo step.
  - `.editorconfig` is the library's default `IFileSettingsProvider` in `Craft.Abstractions` (as `FolderWatchers` keeps its defaults), in its registry.
  - `indent_size` sets the tab size with spaces, `tab_width` with tabs (Craft has one tab size).
  - `end_of_line` sets the breaks the editor inserts; the file's own breaks are kept (no silent edit).
  - `charset` applies to a new file's first save only; an existing file keeps the encoding it was read in.
  - A content change to an `.editorconfig` outside the editor is seen when a file's settings are next read (its write time); the folder watcher reports names only.
- **Found on the way**: a lambda over `Draw`'s `model` parameter made a closure at every frame's start (32 B a frame) even
  where it never ran; the post moved into a method of its own (rule 63's warm frame caught it).
- **Windows-only lines**: none. `EditorView` (the file settings and the probe kept over a model's rebuild, Quick fix forwarded)
  and `ShellPage` (applying the settings, the tooltips) are shared code, compiled here for `net10.0`. Not run here: the drawing
  on Windows, Alt+Z reaching the editor through WinUI (an Alt chord may open the menu bar's access keys: the owner's check).
- **Left**: the minimap (piece 2b) draws lines unwrapped; ghost text at a wrapped caret is drawn unwrapped; Razor's and XAML's strings inside markup text are
  approximate for the depth.
- **Tests**: new groups `WordWrap` (6: rows, indent and row-by-row drawing; Up/Down/Home/End; hit tests and points; folds and
  sticky scroll; 300 random edits against a fresh wrap and a fresh editor; Alt+Z and the option; the measured 100,000-line warm
  frame), `BracketColors` (4: depth colours, strings and comments skipped and unmatched, 200 random edits against a fresh read
  and a keystroke reading one line, the per-frame budget and 0 B warm), `InlineDiagnostics` (2), `Lightbulb` (2), `Whitespace`
  (4: modes, rulers and 0 B warm, trim and final newline as one undo step each, Save with the options), `EditorConfigApplied`
  (4: globs and precedence, read off the UI thread and refreshed, applied to the editor and the tooltips, Save's trim, final
  newline and a new file's charset); `StickyScroll` (a header's brackets are runs of their depth's colour now), `Inventory`
  (the twelve options). The full filter (`WordWrap|BracketColors|InlineDiagnostics|Lightbulb|Whitespace|EditorConfigApplied|Editor|Surface|Folding|StickyScroll|OverviewRuler|MultiCaret|Document|Settings|ProjectLanguages|ApiGuard|Inventory`):
  159 passed, 0 failed (after the rebase over pieces 2b and 3b); `Paint|Shell|CSharp|Plugins|Keys|ReadOnly|Metrics|OutputLog|ProblemsPanel`:
  114 passed, 0 failed. The API dumps of Craft.Abstractions, Craft.Core and Craft.Editor, the contract list (1.1) and
  `plans/inventory.md` regenerated: lines added only.
- **Options added** (each with its shape on the settings page): Editor › Display: `CRAFT_WORD_WRAP` (off), `CRAFT_WORD_WRAP_COLUMN`
  (80), `CRAFT_BRACKET_COLORS` (on), `CRAFT_INLINE_DIAGNOSTICS` (errors), `CRAFT_LIGHTBULB` (on), `CRAFT_LIGHTBULB_DELAY_MS`
  (250), `CRAFT_RENDER_WHITESPACE` (selection), `CRAFT_RULERS` (none); Editor › Files: `CRAFT_TRIM_TRAILING_WHITESPACE` (off),
  `CRAFT_INSERT_FINAL_NEWLINE` (off), `CRAFT_EDITORCONFIG` (on); Editor › Advanced: `CRAFT_BRACKET_COLORS_SCAN_LINES` (20,000).
- **The owner's check** on `test-output\CarftTestPOC`: open `Program.cs`, Alt+Z (and Settings › Editor › Display › Word wrap "on"
  and "column"), make a long line and narrow the window: the rows follow, Up/Down/Home/End move by row, a click on a
  continuation row, fold a method holding a wrapped line, scroll so the class header sticks; see the brackets' colours (a
  `)` too many in red); make an error: its message faded at the line's end, the lightbulb in the gutter, click it; Settings ›
  Editor › Display: render white space "all", rulers "80,120"; add `.editorconfig` beside the project with `[*.cs]`
  `indent_style = tab`, `trim_trailing_whitespace = true`, `insert_final_newline = true`, `max_line_length = 100`: reopen
  `Program.cs`, the status bar's Spaces item and its tooltip, Tab, a ruler at 100, type trailing spaces and save, then Ctrl+Z
  twice.
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt --filter "WordWrap|BracketColors|InlineDiagnostics|Lightbulb|Whitespace|EditorConfigApplied|Editor|Surface|Folding|StickyScroll|OverviewRuler|MultiCaret|Document|Settings|ProjectLanguages|Paint|Shell|CSharp|ApiGuard|Inventory"
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```

### X part 9, navigation 13-17, as built (2026-11-01)

Slot s4: bookmarks, preview tabs, the last session restored, local history with its Timeline, links in the terminal and
Output. Craft only; Idrak unchanged (nothing in the library was needed: the links' registry is Craft's, on Idrak's
`KindTable`).
- **13 Bookmarks** (`Craft.Editor.Shell.BookmarkList`, `BookmarksFile`, `ShellModel.Bookmarks.cs`): Toggle at the caret
  (Ctrl+K Ctrl+K; Visual Studio's keymap Ctrl+F2 too), Next and Previous across files (Ctrl+K Ctrl+N, Ctrl+K Ctrl+P; past
  the last the first, `CRAFT_BOOKMARKS_WRAP`), Label (asked through the shell's prompt), Clear all with Undo (Visual
  Studio's Ctrl+K Ctrl+L), the Bookmarks side view (rail item with its count; Visual Studio's Ctrl+K Ctrl+W): a group per
  file, a row per bookmark (line, text, label; Label and Remove on hover; a click goes there). An open document's
  bookmarks move with its edits (each bookmark's line start mapped through the change's edits: an insertion at a line's
  start moves it down with its text, a removal around it takes it to where the removal began, two that meet become one).
  Drawn through X part 8's line marks: a new `LineMarkKind.Bookmark` (contract 1.1, at the enum's end), the accent's tag
  at the gutter's left edge; not clickable (the change bar's click keeps going to git), not on the overview ruler (its
  eight lanes are taken). Kept in `.craft/bookmarks.json` (relative paths, lines from 1, the line's text and label) on
  the thread pool, latest wins: at each toggle, label and clear, and when a document with bookmarks is saved; read off
  the UI thread when the folder opens. The editor's context menu has Toggle bookmark, the View menu Bookmarks.
- **14 Preview tabs** (`DocumentTab.IsPreview`, `DocumentTabs.Preview`, `AddPreview`, `Keep`, `ShellModel.OpenPreviewAsync`,
  `OpenKeptAsync`, `KeepTab`): a single click on a file in the explorer opens it in the focused group's preview tab
  (italic title, its tooltip says so), which the next preview replaces in its place (its document closed unless another
  group shows it; a file open in another group previews as a shared tab); an edit, a double click on the tab or on the
  file in the explorer, a pin, or Tab: Keep open (Ctrl+K Enter; the tab's menu shows it on a preview only) keeps it.
  Quick open, Ctrl+O, links, bookmarks and every other way open a kept tab. `CRAFT_PREVIEW_TABS` (on).
- **15 The last session** (`ShellModel.Session.cs`): the groups (piece 3a's layout), their tabs, active tabs, the focus,
  and each tab's pin, selection, scroll and folds (`EditorTabState`, beside its file in `EditorLayoutNodeState.Tabs`)
  written per workspace to the data folder (`sessions/<hash of the folder>.json`, `ShellModel.SessionPathOf`; the same
  format as `.craft/editor-layout.json` through `EditorLayoutFile(root, path)`) on the thread pool, latest wins, at every
  change of the groups, tabs, focus or shares, when another folder opens, and when the window closes (the close guard
  asks about unsaved tabs, then awaits the write; carets and scroll are as they are then). At start with a folder
  (`RestoreSessionAsync`, `CRAFT_RESTORE_SESSION`, on): read off the UI thread with the recovery folder's sidecars
  (`RecoveryStore.RecoveredFiles`) and which files exist; the groups shaped (`EditorGroups.Shape`), the files opened in
  order, pins and the active tabs and focus as they were; each tab's caret, scroll and folds applied when its editor is
  made (`ApplyViewState`: the selection at once, the scroll once the view knows its viewport, the folds once the fold
  ranges arrive, then the scroll again). Files that are gone are skipped and said once (one status line); a file with a
  recovery copy is not reopened (said: "left for the recovery"). No session kept: the workspace's
  `.craft/editor-layout.json` (piece 3a) reopens the groups. File: Reopen the last session runs it by hand.
- **16 Local history** (`Craft.Core.Documents.LocalHistory`, `ShellModel.History.cs`): every save through the host keeps a
  snapshot of the saved text in the data folder's `history` (never the workspace), a folder per file (a hash of its path,
  `source.txt` naming it), each snapshot a UTF-8 file named by its time and the first 64 bits of its SHA-256, written in
  16 K blocks from a pooled buffer while hashed (rule 74), one write at a time on the thread pool. Deduplicated by hash:
  the newest snapshot's text again writes nothing; an older one's moves that file to now. Bounds: past
  `CRAFT_LOCAL_HISTORY_DAYS` (30) the snapshots go (the saved file's folder at each write, all at most once an hour),
  past `CRAFT_LOCAL_HISTORY_MAX_MB` (256; 0 turns it off) the oldest go, a file over `CRAFT_LOCAL_HISTORY_FILE_MB` (16)
  is not kept; the history holds one number in memory (the bytes kept), nothing per snapshot. The Timeline side view
  (rail item; View › Timeline; the tab's menu Open timeline) lists the active file's snapshots (time, size; Compare with
  now and Restore on hover): a click shows the diff of the snapshot with the text now (`ShowDiffAsync`), Restore takes the
  text back as one undo step (not saved) with Undo in the inline bar.
- **17 Links** (`Craft.Abstractions.OutputLinkDetectors`, `IOutputLinkDetector`, `OutputLink`, the library's
  `FileLineLinks` "file-line"): `a.cs(12,5)` and MSBuild's `(l,c,l,c)`, `a.cs:12:5`, `a.cs:12`, .NET's `a.cs:line 12`,
  drive letters (`C:\`, `C:/`), UNC paths; the markers found with `SearchValues`, the path's start with `LastIndexOfAny`,
  no regular expression, nothing allocated; a path must end in a file name whose extension holds a letter (so `10:30`,
  `Main(1)`, `http://host:80`, `1.2:3` are no links). A plug-in adds a format by registering a detector. Output's
  read-only editor (`EditorModel.DetectsLinks`, `LinkAt`, `LinkActivated`; `LinkUnderlineLayer`, wrap-aware) underlines the
  link under the pointer and opens it on a click without a drag; the terminal (`TerminalLinks`, the row read one character
  per cell; `TerminalRenderer.HoveredLink`, `LinksUnderline`) underlines it while Ctrl is held and opens it on Ctrl+click.
  `CRAFT_LINKS_UNDERLINE`: hover (default), always (every link on screen), off. The path is resolved off the UI thread
  (`ShellModel.ResolveLink`): as it is when absolute, else against the terminal's folder (its shell's reported folder,
  else where it started), then the workspace; a missing file is said in the status bar.
- **Settings page**: `CRAFT_BOOKMARKS_WRAP` (Editor › Bookmarks), `CRAFT_PREVIEW_TABS` (Files › Tabs),
  `CRAFT_RESTORE_SESSION` (Files › Session), `CRAFT_LOCAL_HISTORY_MAX_MB`, `_DAYS`, `_FILE_MB` (Files › Local history),
  `CRAFT_LINKS_UNDERLINE` (Terminal › Links); each with its variable and a workspace's `.craft/settings.json`.
- **Decisions on the recommendation (the owner asleep; one line each)**:
  - Bookmarks' keys: Ctrl+K Ctrl+K, Ctrl+K Ctrl+N, Ctrl+K Ctrl+P in both keymaps (Visual Studio's chords, free in Craft's VS Code keymap); Visual Studio's keymap adds Ctrl+F2, Ctrl+K Ctrl+W (the list) and Ctrl+K Ctrl+L (clear).
  - The Bookmarks list is a side view of the rail (the Search and Run views' blocks), not a bottom-panel tab.
  - Bookmarks are drawn through the line marks (X part 8's host API) as a new kind, not a layer of their own.
  - Bookmarks are not on the overview ruler: its byte of lanes is full (git's four, find, the diagnostics, the carets).
  - A bookmark's line moves are written when its document is saved (the file's lines are then the document's), toggles at once.
  - Two bookmarks that meet after a deletion become one, keeping the first one's label (else the second's).
  - Labels are asked with the shell's prompt; an empty label takes it away.
  - A preview is replaced in its place (VS Code's); only the explorer's single click previews (quick open does not).
  - A file already open is shown as it is by a single click (a kept tab is never turned into a preview).
  - The session lives in the user's data folder, not the workspace (a single group adds no file to the user's folder, piece 3a's rule); the workspace's layout file stands in when no session is kept.
  - The session is written at each change of tabs and groups and at the close (not at every caret move): a crash keeps the carets of the last tab change.
  - A restored preview tab comes back kept (the session keeps pins, not previews).
  - A file with a recovery copy is left out of the restore and said (the recovery prompt is not in the app yet; the session never opens a file past it).
  - The restore runs only into an empty editor area (a file named on the command line opens alone).
  - Local history dedups per file (a snapshot of the same text moves to now), not across files.
  - Snapshots are UTF-8 text (the file's encoding and line endings are not kept); Restore restores the text, unsaved.
  - The Timeline is a side view following the active file (VS Code's is in the explorer), not a tab.
  - 0 MB turns the local history off; no separate on/off setting.
  - Links: a regular expression was not used anywhere; detectors are a registry (`OutputLinkDetectors`, "not registered" for an unknown name), so a plug-in adds a format.
  - A link's path must end in a name with an extension holding a letter (fewer false links than accepting any word).
  - Paths with spaces are not links (a space ends a path walking back); a quoted path with spaces is not either (said here).
  - Output opens a link on a plain click (its click selects nothing worth keeping); the terminal needs Ctrl (its click selects).
  - `CRAFT_LINKS_UNDERLINE` "hover" underlines the link under the pointer in Output and, with Ctrl held, in the terminal; "always" every link on screen; "off" no links.
  - No disk access while drawing: a link is underlined when it parses; its file is looked for only when it is followed.
  - The shell fixtures of the tests keep `CRAFT_DATA_FOLDER` in their temp folder (`TempFolder.Isolated`, under `obj/`, which the tree ignores), so no test writes sessions or snapshots into the machine's data folder.
- **Found and fixed**: none outside this work. **Seen, not this work's**: `Ui.TheUiSideHasNoSynchronousWait` flags
  `src/Craft.Editor/Lightbulb.cs:169` (`done.Result` on a completed task, s2's editing helpers; fails on `main` too);
  `Terminal.TheManagerReplacesAnEndedTabGivesItTheKeyboardAndStopsAProgramWhoseTabCloses` is timing-dependent (it types
  before the program's prompt reached the screen; 1 of 3 runs failed on a clean `main`, 0 of 3 here).
- **Tests**: new groups `Bookmarks` (4: toggle, gutter tag, moved with edits and merged, both keymaps' keys; next and previous
  across files with wrap; the view, labels, a row's click, Clear with Undo, the file written and read back by a new shell;
  Visual Studio's keymap), `PreviewTabs` (3: replaced in place, kept by an edit, a double click, a pin, Keep open; off;
  shared with another group), `SessionRestore` (3: two groups, pins, focus, active tabs, caret, scroll once the viewport is
  known, folds once the ranges arrive, a gone file said once, the file in the data folder; a recovery copy left out;
  off, and the workspace's layout standing in), `LocalHistory` (4: dedup by hash and the move to now, UTF-8; the size,
  days and file-size bounds and 0 MB off; the heap within 256 KB over 500 more saves; a save's snapshot, the Timeline's
  rows, Restore as one undo step and Undo), `OutputLinks` (6: the formats, drive letters, UNC, .NET's frames, seven
  lookalikes that are not links; 1,000 scans 0 B and a plug-in's detector; resolution against the run's folder and the
  workspace, open at the place, a missing file said; Output's hover underline, click, a drag that opens nothing, Ctrl where
  needed; 200 warm frames underlining two links a line under 1 KB; the terminal's link under a cell after a wide
  character, the Ctrl underline, off, always, 200 warm frames under 1 KB). Updated: `Inventory.OptionsReadEveryVariable`
  (the seven options), `Experience` (the tab's menu has Open timeline), the shell fixtures' data folder. First run: 23 of
  26 passed (the keymap test read the host's keys without the Visual Studio keymap registered, as only the app's edit
  commands register it; the inventory's new registry; the Timeline's refresh raced a dispatched one, made one awaited
  task); then all passed, and a leak they showed fixed (the shell's new panels were disposed while it still listened to
  panel changes, registering the Search and Run side views again). After the rebase onto s1's piece 2b, s2's editing
  helpers and s3's piece 3b (one build: the link underline follows s2's wrapped rows, the hit test goes through the
  model's own point-to-position), the full filter (`Bookmarks|PreviewTabs|SessionRestore|LocalHistory|OutputLinks|
  EditorGroups|PeekDefinition|TreeMove|ContextMenus|Shell|Terminal|Recovery|Workspace|Document|Settings|Keys|Ui|Design|
  Layout|Experience|SourceControl|RunView|Diagnostics|ProjectLanguages|Paint|OverviewRuler|Editor|ApiGuard|Inventory`,
  289 tests): 286 passed; the tab menu's expectation (fixed, passed), and the two above that are not this work's. API dumps
  of Craft.Abstractions, Craft.Core and Craft.Editor, the contract list (1.1) and `plans/inventory.md` regenerated
  (additions only).
- **Not compiled here**: Craft.Maui's Windows target (nothing under `Platforms/` changed; the views compiled on net10.0).
  **Not run here** (the owner's run): the italic preview tab and double clicks in the explorer and the strip, the
  bookmark's tag in the gutter, the Bookmarks and Timeline views, the restore at start and the write at close, Ctrl+hover
  and Ctrl+click in the terminal (the Windows input connection's modifiers), a click in Output.
- **Left**: bookmarks on the overview ruler; a session's preview tab kept as a preview; the recovery prompt at start (the
  store is still not used by the app; the restore leaves those files closed); quoted paths with spaces as links.
- **The owner's commands**:
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  $env:CRAFT_FILTER="Bookmarks|PreviewTabs|SessionRestore|LocalHistory|OutputLinks|EditorGroups|ContextMenus|Shell|Terminal|Recovery|Workspace|Document|Settings|Keys|Ui|Experience|ApiGuard|Inventory"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```
  In the app: click files in the explorer (one italic tab replaced), double-click one, type in a preview; Ctrl+K Ctrl+K on
  a few lines of `Program.cs`, Enter above them (the tags move), Ctrl+K Ctrl+N and Ctrl+K Ctrl+P, the rail's Bookmarks
  view; save `Program.cs` a few times, open the rail's Timeline, click a row (the diff), Restore, then Undo; in the
  terminal run `dotnet build` with an error in `Program.cs`, hold Ctrl over `Program.cs(…)` (underlined) and Ctrl+click it;
  the same line in Output by a click; split the editor, fold a block, close the window and start it again on the same
  folder (the groups, tabs, carets, folds and pins back); `$env:CRAFT_LINKS_UNDERLINE="always"` underlines every link.

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

### X part 10 as built (2026-11-01)

Slot s4, Craft only (nothing was missing in Idrak: the chat and the agent use its `FakeChatModel`, `CodingAgent`,
`CommandSandboxes` as they are). The spec above, built as one conversation view for the chat and the agent; its logic
headless in `Craft.Editor.Shell`, the MAUI view only draws it.
- **One selectable transcript** (`ConversationTranscript`): the panel's entries written once each into one read-only
  document shown by Craft's own editor surface (note 26's read-only `EditorModel`, B1's line cache: only the shown lines
  laid out and drawn). A streamed piece rewrites only its message (one buffer change; `MessagesWritten` counts them);
  the selection keeps its place. Drag across messages, Shift+click, Ctrl+A (the editor's own); Ctrl+C copies the
  selection as `CRAFT_CHAT_COPY_FORMAT` (markdown: each whole message as written, a part as shown with code fenced, the
  speakers named "**You:**", "**Assistant (model · device):**" when it spans messages), Ctrl+Shift+C the other format;
  the chrome (speakers, actions, code headers, folds, metrics) is never copied. Its own language (`craft-conversation`,
  in no registry) colours the speakers, Markdown, actions and steps; a code block is coloured by the language plug-ins'
  tokenizers (the fence's name, else a file of that extension) once the reply finished, lazily when first drawn.
  `ILineBackdrop` (new, `EditorModel.LineBackdrop`, one call in `EditorRenderer`) draws the user's soft bubble at the
  right, the code blocks' fill and the streaming caret; `EditorModel.PointerActivity` (new: hover, exit, a click that
  dragged nothing) gives the message under the pointer (its Copy, Edit and ⋯ show on hover) and the clicked action.
- **Messages** (`TranscriptWriter`, `ChatMarkdown.cs`): headings, lists, quotes, tables (aligned columns, bold header),
  rules, links (clickable), inline code, bold, italic; code blocks with a header (language, Copy, Insert at caret, the
  entry's Apply as diff, Run in terminal for sh/bash/powershell/cmd blocks), folded past `CRAFT_CHAT_FOLD_CODE_LINES` (30)
  with Show all / Fold; the model's message full width under "model · device" (`ConversationEntry.Header`, the provider's
  role device); tokens, rate and time under a finished reply (`ConversationEntry.Metrics`, `CRAFT_CHAT_SHOW_METRICS`);
  a streaming reply (`ConversationEntry.IsStreaming`) followed unless the user scrolled up, then "Jump to latest".
- **The message menu** (`EditorContextMenus.Conversation`, commands `craft.chat.*` in `ConversationCommands`): Copy, Copy
  as Markdown, Quote in reply, Retry from here, Edit and resend (the user's only), Delete from here; right-click or the
  message's ⋯. The panel menu (`EditorContextMenus.ConversationPanel`, the header's ⋯): Copy conversation, Export
  conversation… (a Markdown file through the save dialog, written off the UI thread), New conversation, Past
  conversations, Expand. Contract (additive, 1.1): `ConversationCapabilities`, `IConversationPanel.Capabilities`,
  `RetryFromAsync`, `EditAndResendAsync`, `DeleteFrom`, `ShowsTimeline`, `SlashCommands`, `AskInline`; the chat
  implements retry, edit-and-resend and delete by its exchanges (`Turn`: what was sent, the reply).
- **The agent's timeline** (`AiAgentPanel.ShowsTimeline`): the model's text as "Thinking" / "Answer" steps (open by
  default), each tool call a step (✓ ✗ ⋯, its summary; ▸ opens its arguments and output, `ConversationEntry.Details`),
  a command waiting with its answers inline (`EditorQuestion.Kind` = `EditorQuestionKinds.Command`, set by the
  approve-each sandbox; `EditorHost.AskAsync` offers such a question to the conversation panels first, and the agent
  shows the sandbox's own choices as buttons on the command's step; no dialog), the files changed with +/− and Review,
  the end in plain words (note 27's "nothing was changed" kept).
- **The composer** (`ConversationComposer`): grows to `CRAFT_COMPOSER_MAX_LINES` (12) then scrolls; the context chips
  (incl. dropped files and images, as piece 1b); "/" lists the panel's slash commands (chat: /explain /fix /doc /refactor
  /tests /clear; agent: /clear), "@" the workspace's files (listed once per folder on the thread pool, the ignored
  folders skipped) and the open document's symbols (its language's scopes); Up/Down/Tab/Enter/Escape in the picker; the
  sent history with Up and Down while the box is empty; Send, Stop beside it while busy ("Add note" for the agent), the
  model chip; Edit and resend puts the message back and Send replaces it.
- **Expand** (`PanelLayout.ChatExpanded`, `IsChatExpanded`, `ToggleChatExpandedAsync`, `ExpandedArea`): the header's ⤢,
  "Chat: Expand" (Ctrl+Shift+Alt+M, registered with its command so both keymaps have it) and Escape (transcript or
  composer) toggle; expanded, the chat takes the editor area's grid rows (the editor groups hidden, the bottom panel
  below), so it sits between the rail plus side view and the window's edge and follows their widths; restored, the
  column at its kept width. `CRAFT_CHAT_EXPANDED` per workspace; expanded by default on a narrow window, and when the
  editor would have less than `CRAFT_PANEL_MIN_SIZE` beside the column (restored for the run by the toggle).
- **Past conversations** (`IConversationHistory`, `ConversationList`): the chat keeps every conversation of the
  workspace in `.craft/chat.json` (version 2: id, title from the first message, time, exchanges, entries; version 1 read
  as one), read off the UI thread; a list in place of the transcript (search, New, open, Rename in place, Delete with
  Undo on the shell's undo bar). New chat and /clear start a new one (the old stays listed).
- **Options** (each with its shape, Settings › Chat › Conversation): `CRAFT_CHAT_EXPANDED` (false),
  `CRAFT_COMPOSER_MAX_LINES` (12), `CRAFT_CHAT_COPY_FORMAT` (markdown), `CRAFT_CHAT_FOLD_CODE_LINES` (30),
  `CRAFT_CHAT_SHOW_METRICS` (true).
- **Measured** (headless, Linux container, Release, fixed metrics 10 × 20, 800 × 400): a conversation of 900 entries
  (4,801 lines): only the ~20 shown lines drawn and laid out; warm frame median 5.7-6.2 µs, p99 13-33 µs, **0 B a frame**
  (found on the way, rule 90 by type: a lambda capturing a parameter in the code block's colour lookup made its closure
  at every call, 40 B a frame; moved to a cold method); a hover lays nothing out off its message.
- **Decisions (the recommended option each time, the owner asleep):**
  - The transcript is Craft's editor surface in monospace with drawn fills, not MAUI labels: one selection across messages.
  - The bubble is right-aligned by padding inside the text (the padding is decoration, never copied).
  - Code in a reply is coloured once the reply has finished (its text still grows while streaming).
  - A partial selection of a message copies what is shown of it; a whole message copies its Markdown as written.
  - Plain copy names no speakers; Markdown names them when the selection spans messages.
  - Retry from here asks the exchange's message again with the context of now; the dropped replies are not sent back.
  - Delete from a reply keeps its question unanswered (to retry); from a question drops the whole exchange.
  - The approval is shown inline by routing questions of a known kind (`EditorQuestion.Kind`) to the panels, the sandbox unchanged otherwise; without a waiting agent it stays a dialog.
  - Expand covers the editor groups only (tab strips, breadcrumbs, editors); the rail, side view and bottom panel stay.
  - Ctrl+Shift+Alt+M is registered with Chat: Expand (not in `StandardShortcuts`, whose commands are all headless).
  - The past conversations are the chat's (the agent's run is tied to its working copy).
  - New chat keeps the old conversation in the list (deletable with Undo) instead of erasing it.
  - "@" symbols come from the active document's language scopes (no symbol contract exists yet; X part 9's would replace it).
  - Run in terminal types the shell block into the active terminal and runs it: the user's click is the approval.
  - `SelectableLabel` and its Windows mapping are gone: the transcript is the selectable surface.
- **Windows-only lines (not compiled here; the rest of Craft.Maui compiled for net10.0):**
  `Platforms/Windows/ComposerKeys.cs` routes Up, Down and Tab too (the picker, the history);
  `Platforms/Windows/WindowsShellPlatform.cs` no longer configures `SelectableText` (file removed). The owner's run checks
  the look (bubble, code fills, fonts), the hover actions, the expand placement and the keys.
- **Tests** (group `Conversation`, 9): selection across messages and Ctrl+A with Markdown and plain copy; the message
  menu's rows and actions and a click on Copy; retry, edit-and-resend and delete in the chat (fake model); streaming
  follows unless scrolled up and Jump to latest; the agent's timeline from a scripted run (read_file, run_command
  approved inline through a sandbox that asks, write_file) with a step opened and closed; the composer's chips, history,
  slash commands and mentions; expand and restore keep the rail, the side view and the widths; past conversations with
  Undo, read back; only visible lines laid out and 0 B a warm frame. Updated: `AiPlugins` (New chat keeps the old
  conversation), `Inventory.OptionsReadEveryVariable` (the five options). After the rebase onto 2a, 3a and X part 6, the filter
  `Conversation|AiPlugins|Ai.|ChatDrop|ModelsPage|ModelLoading|Settings|Keys|Layout|Shell|Ui|Experience|LookAndFeel|Paint|ApiGuard|Inventory|TheUiSideHasNoSynchronousWait`:
  159 of 160 passed; the one left, `Paint.AWarmFrame…` (88 B in a few frames), fails only in a long run after the
  `Shell|Ui|Experience` groups, also without this slot's group, and passes with `DOTNET_TieredCompilation=0` (rule 90
  step 3: unoptimized code's boxing, B1's earlier symptom; not this change's, left for the paint slot to trace; traced by
  slot s4: the C# colours' idle source, not a box, see "X part 6b and the paint allocation as built"). API
  dumps (`Craft.Abstractions`, its contract list, `Craft.Ai`, `Craft.Core`, `Craft.Editor`) regenerated, lines added only.
- **Left:** the bubble's corners are square (`IEditorCanvas` has no rounded fill); the transcript's code font is the
  editor's (no proportional text); a conversation's own context menu has no keyboard opening (Shift+F10) yet; the
  agent's past runs are not listed.
- **The owner's commands** (Craft only; Idrak is unchanged since the last list):
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  $env:CRAFT_FILTER="Conversation|AiPlugins|Ai.|ChatDrop|ModelsPage|ModelLoading|Settings|Keys|Layout|Shell|Ui|Experience|LookAndFeel|ApiGuard|Inventory"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```
  In the app: ask the chat about `Program.cs`; drag across several messages and Ctrl+C, paste (Markdown, speakers named);
  Ctrl+Shift+C (plain); hover a message (Copy, ⋯) and right-click it (Retry, Edit and resend, Delete from here); a long
  code block (Show all), its Insert at caret (a diff) and Copy; scroll up while it streams ("Jump to latest"); type "/"
  and "@Pro" in the composer, Up in the empty box; the header's ⤢ or Ctrl+Shift+Alt+M (the chat over the editors, the
  explorer and rail still there; resize the explorer; Escape restores); ☰ (the past conversations: New, Rename, Delete
  then Undo); run the agent on "Create C# class House…" and approve a command on its step.

### X part 8 as built (2026-11-01)

Slot s3: the Source Control view as a pre-installed, removable plug-in, with Generate commit message. Craft only; nothing
in Idrak (no cause there: git is the user's own program, the model through Craft's existing leases).
- **Contracts** (`Craft.Abstractions`, additions to contract 1.1): `ISourceControlProvider : IKind` and
  `SourceControlProviders` (with `BranchCommand` "craft.sourceControl.branch", which the status bar's branch runs, and
  `ShowCommand`), `ISourceControlRepository`, `SourceControlStatus`/`SourceControlFile`/`SourceControlState`/
  `SourceControlStates` (letters, colour roles, words); `ICommitMessageProvider : IKind` and `CommitMessageProviders`
  (`CommitMessageRequest`, `CommitModelPlan`, `CommitModelChoice`); `LineMark`/`LineMarkKind`/`LineMarkEventArgs`,
  `InlineCard`/`InlineCardAction`; on `IEditorHost` (default members): `WorkspaceFolderChanged`, `SetLineMarks`,
  `LineMarkClicked`, `ShowInlineCard`, `AddRepository`, `ShowDiffAsync`; `IEditorPanel.Badge` (and `ViewPanel.Badge`);
  three blocks for B11's view panels: `PanelFileList`/`PanelFileRow` (a group of files with icons, letters, colours,
  actions on hover), `PanelIconButton` (with an inline menu), `PanelMessageBox` (a multi-line box, its submit keys, its
  buttons inside, a version that replaces its text); `SettingSections.SourceControl`.
- **The plug-in** (`plugins/Craft.Plugins.Git`, "git", contract 1.1): `GitProgram` finds git on PATH or at
  `CRAFT_PLUGIN_GIT_PATH` and runs it with an argument list (never a shell), `GIT_TERMINAL_PROMPT=0`,
  `core.quotepath=false`, `LC_ALL=C`, output read as bytes, a cancelled run killed with its tree; `GitParsers` parses
  `status --porcelain=v2 -z --branch --show-stash` (ordinary, renamed with the old path after its NUL, unmerged, untracked;
  ignored skipped), `for-each-ref` branches, `-U0` hunks and conflict blocks as bytes; `GitRepository` reads the status
  after a pause (`_REFRESH_MS` 500), one read at a time, `Changed` only when git's output changed; `GitSession` (one per
  host) opens the open folder's repository, publishes it, watches the folder (the "system" watcher; `.git/objects` and
  locks are not changes) and the saves, runs the user's actions (each an activity, the network ones with Cancel; no sandbox
  question: they are the user's own), the gutter, and Generate.
- **The view** (`SourceControlView`, headless: state to blocks; the rail's "Source Control" item with the count of changed
  files as its badge; Ctrl+Shift+G): the commit box (Ctrl+Enter commits on the branch; the sparkle button inside with the
  loaded models in its menu, Stop while it writes, the line under it), Commit (nothing staged: asks to stage everything
  first), Amend, Pull ↓n, Push ↑n (no upstream: publishes to origin), Sync (asks first, `_CONFIRMSYNC`), Fetch
  (`_AUTOFETCHMINUTES`, 0 off), Stash and Pop (n), the branch picker with ahead and behind and a New branch field, then
  Merge conflicts (accept current, incoming, both per file; ✓ marks resolved), Staged changes (Unstage all) and Changes
  (Stage all, Discard all), each row the file's icon, name, folder, letter and colour (deleted struck through), a click the
  read-only diff (staged: against the last commit; else the working copy against the index), its actions on hover. No
  git: "Git is not available" with what to do, Try again and the settings; no folder: Open folder; not a repository:
  Initialize repository. Discard is done at once with the shell's Undo bar (the files' bytes kept, `_DISCARDUNDOMB` 16; a
  larger discard asks first); an untracked file is deleted and Undo writes it back.
- **The editor**: `DocumentLineMarks` (per text document, shared by its every editor, sorted, versioned) drawn by the
  renderer as a bar in the gutter's strip left of the fold column (added: the theme's success, changed: info, removed: a
  wedge in the error colour, a conflict: warning; a binary search to the first visible mark) and by the overview ruler as a
  strip at its left edge (bucketed with the other marks, rebuilt only when the marks' version changes). A click on a mark
  goes to its source (`LineMarkClicked`); the git plug-in answers with an inline card (`EditorTooltipKind.Card`) under the
  line: the old lines on the diff's removed fill, the new on its added fill, and a row of actions (Revert, Stage; a
  conflict: Accept current, Accept incoming, Accept both); it stays until Escape, a press outside or an action. The marks
  are the index's blob (`git show :path`) against the document's text now (unsaved too), Craft's `TextDiff` with no
  context, worked out on the thread pool after `_GUTTERDELAYMS` (300); Stage writes the index's text with that hunk from
  the editor (`hash-object -w --stdin` and `update-index --cacheinfo`, the file's mode kept).
- **The shell**: the rail draws a panel's badge as a pill on its item; the status bar's branch is the published
  repository's with ↑ahead ↓behind and a click runs `craft.sourceControl.branch` (the branches as a question, "Create a
  branch…" opens the view); the tree's file names take their state's colour and letter (`FolderTreeView.StateOf`); the
  tree's and the tab's menus get Stage, Unstage, Discard changes, Open changes and Reveal in Source Control (each shows
  only when it applies; X part 7 extends the menus); View › Source Control. The read-only diff is `ChangesDiffPage` (the
  diff view's rows, Close) through `IEditorPresenter.ShowDiffAsync`.
- **Generate commit message**: the staged diff (else every change of the working copy, the untracked files as new files,
  said so), trimmed by file to `_COMMITDIFFTOKENS` (3000; within the model's context less the reply and 512), the largest
  files summarised by name and counts; the instructions (`_COMMITPROMPT`, else the plug-in's), the style (`_COMMITSTYLE`
  auto: the last `_COMMITEXAMPLES` 10 subjects as examples; conventional; plain); asked of the first registered commit
  message provider, the AI plug-in's "ai" (`AiCommitMessages`): with `_COMMITMODEL` "loaded" the first loaded of the chat's,
  the agent's, the completion's model ("chat", "agent", "completion" or a model's name pick one), through that role's
  existing lease (no load, no second copy) and its temperature, context and reply limit (`_COMMITMAXTOKENS` 0: the role's).
  The pieces stream into the box (one dispatch a UI turn), the reply is cleaned (a fence, quotes, "Commit message:"), the
  line under the box names the model; Stop keeps what was written; nothing commits by itself. Nothing loaded: says so and
  offers "Load the chat model" (`ai.loadModel`, run only when pressed); the button's tooltip names the model it would use.
- **Decisions (the recommended option each time, the owner asleep):**
  - The view is a block panel with three new general blocks in `Craft.Abstractions`, not a git-only MAUI view: any plug-in can draw file groups and a message box.
  - The plug-in publishes its repository through `IEditorHost.AddRepository`; the shell never runs git itself (one status read per change, rule 75).
  - The rail's count is `IEditorPanel.Badge` on the rail item of the panel's id; the shell rebuilds the rail only when a badge changed.
  - The gutter diffs the index's blob against the document's text in-process (it follows typing, unsaved too) rather than `git diff` of the file on disk; git's `-U0` hunks give the same marks (`GitParsers.Hunks`, tested).
  - Mark colours are the theme's success, info, error and warning; no new theme tokens.
  - The inline card is a new tooltip kind under its line; while it shows, no hover replaces it.
  - Revert, and a conflict's accepts in an open document, go through the diff (`ProposeAsync`, one undo step the user accepts): no silent edit; a closed conflicted file is rewritten on disk with the Undo bar.
  - Discard is done at once with the Undo bar (B11's rule), up to 16 MiB kept (`_DISCARDUNDOMB`); beyond, it asks first.
  - Commit with nothing staged asks to stage everything and commit; an empty message says so.
  - Push without an upstream publishes to "origin" (else the first remote) with `--set-upstream`; no remote: says how to add one.
  - `CRAFT_PLUGIN_GIT_PATH` is machine-only: added to the settings' not-from-workspace list (a folder someone else wrote names no program to run).
  - The folder watcher is "system" (the plug-in reads no Craft option); `.git/objects` and `*.lock` events are not changes.
  - Tokens of the commit diff are estimated at four bytes each (the plug-in has no tokenizer); the budget is a setting and the model's context bounds it.
  - "loaded" means chat, then agent, then completion; the menu lists every role that has a model, the loaded first (the agent's entry is the chat's copy by default).
  - The model menu opens inline under the box (no platform sheet, the look-and-feel rule).
  - Generate has no default keys (its tooltip shows them once bound); Ctrl+Shift+G shows the view.
  - A new settings section "Source Control" holds the plug-in's settings.
  - A folder that is not a repository is offered Initialize repository (`git init`).
- **Options added** (each with its shape, Settings › Source Control): `CRAFT_PLUGIN_GIT_PATH` (machine only),
  `_REFRESHMS` (500), `_AUTOFETCHMINUTES` (0), `_CONFIRMSYNC` (on), `_DISCARDUNDOMB` (16), `_GUTTER` (on),
  `_GUTTERDELAYMS` (300), `_COMMITMODEL` (loaded; its choices are the loaded models, source "commit-models"),
  `_COMMITSTYLE` (auto), `_COMMITPROMPT` (empty), `_COMMITMAXTOKENS` (0), `_COMMITDIFFTOKENS` (3000), `_COMMITEXAMPLES` (10).
- **Tests** (group `SourceControl`, 9; plus `Plugins.EveryPreinstalledPluginLoadsFromTheAppsLayout` expects "git" and
  the source control provider): porcelain v2 fixtures (renames, conflicts, untracked, spaces, unicode, detached, initial);
  branches, `-U0` hunks, marks and conflict blocks with their three resolutions; marks from a fixture diff drawn in the
  gutter and on the ruler, a click reaching the source, the card in the editor clicked only, its Stage, Escape; the view's
  groups, rows and buttons, the rail badge through the shell (rebuilt only on a change), the published repository's branch
  and the tree's states; a real repository in a temp folder (stage, unstage, commit, commit with nothing staged, discard
  with Undo of a tracked and an untracked file, a new branch and a switch back); an open file's gutter against the index
  after typing, a hunk staged alone from its inline card, a Revert through the diff; the budget trimming by file; a fake
  model's message streaming into the box from the staged diff with the repository's subjects, every change when nothing
  is staged, nothing committed; Stop keeping the draft, no model said with the load offered and run only when pressed; the
  AI provider writing with the loaded chat lease and its temperature and reply limit, never loading; the plug-in loaded from
  its folder and removed. First run: 9 of 12 passed; the three failures were the tests' own expectations (the badge's
  count, the agent's menu entry, a file already tracked), fixed. After the rebase onto X parts 6 and 10, the full filter
  (`SourceControl|Plugins|PluginKit|PhaseP|AiPlugins|Ai.|Shell|Editor|Paint|OverviewRuler|Settings|Ui|Experience|ApiGuard|Inventory`):
  180 passed, 3 failed, none this part's: `PhaseP.EveryPluginOfPhasePHasItsFolderItsManifestAndNoSharedContracts` and
  `PhaseP.P5ApproveEachAsksBeforeEachCommandAndRefusesWithoutTheUser` expect the Sandbox plug-in's `plugin.json` at 1.0,
  which X part 10 (`b068e63`) set to 1.1; `Paint.AWarmFrameLaysNothingOutAndAllocatesNothing…` saw 88 B in 12 of 1000
  frames in the long run and passes alone and with `DOTNET_TieredCompilation=0` (rule 90: the background tier-up, as B1
  and piece 2a recorded). API dumps regenerated (additions only): `Craft.Abstractions`, `Craft.Ai`, `Craft.Editor`, the
  new `Craft.Plugins.Git`, the contract list (1.1) and `plans/inventory.md` (the two registries).
- **Windows only (not run here; `Craft.Maui` compiled on net10.0, its Windows target cannot restore in this container):**
  the view's new blocks (`PanelBlocksView.FileList`, `MessageBox`, `IconButton`), Ctrl+Enter in the commit box (the
  existing `Platforms/Windows/ComposerKeys.cs` hooks every `ComposerEditor`), the rail's badge pill, the tree's letters and
  colours, the status bar's branch tap, `ChangesDiffPage`. No file under `Platforms/` changed.
- **Left:** blame and history views and pull requests (not asked, as the plan decided); Copy remote link (the plan's
  "later"); a conflict's accepts in the editor are reached through its gutter mark's card (no row drawn over the markers);
  Pop takes the last stash (no stash list); folders in the tree are not coloured by their files' states; the PhaseP
  expectations for the Sandbox manifest (X part 10's slot).
- **The owner's commands:**
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  $env:CRAFT_FILTER="SourceControl.|Plugins.|PluginKit.|PhaseP.|AiPlugins.|Ai.|Shell.|Editor.|Paint.|OverviewRuler.|Settings.|Ui.|Experience.|ApiGuard.|Inventory."; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```
  In the app (`CarftTestPOC` is not a repository: the view offers Initialize repository, or run `git init` there first):
  the rail's branch icon opens Source Control with its count; edit `Program.cs` and see its bars in the gutter and on the
  ruler, click one (Revert, Stage), the tree's name in its colour and letter; stage, unstage, discard and Undo at the
  window's bottom; type a message and Ctrl+Enter; the branch in the status bar (click: switch, create); with the chat
  model loaded, the sparkle writes a message into the box (Stop while it writes; its tooltip names the model); with none
  loaded it offers Load the chat model; light and dark themes.

### X part 11: AI features on Idrak (the owner, 2026-11-01: "go with your pick" of five proposed)
Each runs on the models already loaded with their roles' settings (note 25's leases, one copy; a load only when asked,
note 23), every model change to a document is a diff the user accepts (no silent edit), every command through the
sandbox with approval.
1. **Speculative decoding (Idrak first).** Checked 2026-11-01: Idrak has none (no draft/verify loop in its text
   generator). Idrak gap, on Idrak `main` by Idrak's rules: a draft model proposes K tokens, the target verifies them in
   one batched forward pass, accepted tokens kept, the first rejected one resampled from the target, so the output
   distribution is the target's own (greedy: identical tokens, proven by rule 71 on fixed prompts); K adapts to the
   acceptance rate; both models' KV caches kept in step; a contract (`GenerationOptions.Draft` or a decoding strategy in
   a registry) so any pair with the same tokenizer works, refused with the reason otherwise; measured tokens/s with and
   without on CPU here and the owner's GPU numbers. Craft: `CRAFT_PLUGIN_AI_DRAFTMODEL` per role (empty: the completion
   role's model when its tokenizer matches the role's, else off), `_DRAFTTOKENS` (auto); the chip and the status bar show
   "draft: 1.5B, 72% accepted".
2. **Next-edit suggestions.** After an edit, the completion model predicts the next place to change and the change
   (from the recent edits, the cursor's surroundings and the diagnostics), shown as a ghost marker in the gutter and a
   ghost diff at that place; Tab jumps there, Tab again applies (one undo step), Escape dismisses; off the UI thread,
   cancelled by the next keystroke, `CRAFT_PLUGIN_AI_NEXTEDIT` (on), `_NEXTEDITDELAYMS` (800); a contract
   `INextEditProvider : IKind` so another plug-in can offer them.
3. **Repo-aware completion.** Inline completion adds the workspace's most similar snippets to its prompt (BM25 over
   identifiers, and embeddings when an encoder is set; shared with P6's index when present, else a small index of its
   own kept on disk, updated per saved file), within `CRAFT_PLUGIN_AI_COMPLETIONCONTEXTTOKENS`; the snippets chosen
   shown in the completion's tooltip; measured: acceptance rate with and without on the test project.
4. **AI review before commit.** Source Control's "Review changes" (X part 8): the staged diff (else all changes) reviewed
   by the chat role's loaded model; findings as inline notes on the diff view and the editor (severity, one line, the
   reason), each with "Apply fix" as a diff, "Dismiss", and "Copy all"; trimmed to the role's budget by file; Stop.
5. **Fix the build with AI.** When a Run-view build or test (X part 6) fails, its Problems rows and the run's panel get
   "Fix with AI": the errors with their code around them go to the agent (or the chat for one error), the fix arrives as
   a diff, and "Run again" re-runs the same configuration after the user accepts.
- Decisions (recommended, noted): speculative decoding built in Idrak, used by Craft only where the tokenizers match;
  next-edit on by default with its delay; repo-aware completion shares P6's index when it exists.
- Tests: Idrak `speculative decoding` (greedy output identical to the target alone, sampling distribution checked on a
  tiny model, acceptance tracked, K adapts, a tokenizer mismatch refused, memory flat); Craft `DraftModel`, `NextEdit`,
  `RepoCompletion`, `CommitReview`, `BuildFix` (scripted fake models, diffs of one undo step, cancel on keystroke).
- Slot: after X part 10; item 1's Idrak part may start earlier in any free slot (it touches only Idrak's generation).

#### Speculative decoding as built (2026-11-01)
Item 1, slot s1. Idrak `main` 2ab9c3a, then Craft `main` (this commit and the one before it).
- **Idrak.** A contract `IDraftModel`/`IDraftSession` (`Idrak.Abstraction.Generation`), carried per request by
  `GenerationOptions.Draft` with `DraftTokens` (0: adapted), `DraftMinTokens` (1), `DraftMaxTokens` (12); `TextGenerator`
  is a draft (`StartDraft`; its drafting cache kept apart from `KeepCache`'s, `HoldsDraftCache`, freed by `ReleaseCache`).
  `TextGenerator.Stream` with a draft runs rounds: the draft proposes K tokens, the target checks them in one pass over its
  KV cache (every position's logits), both caches cut back to what was kept (`DecodingContext.Truncate`; a full window is
  re-read from its last half as without a draft). Greedy (`TopK` 1): a proposal is kept while it is the target's pick, so
  the tokens are the target's alone. Sampled: kept with min(1, p/q), a refused one replaced from max(0, p - q), p and q the
  sampler's own distributions (`TokenSampler.Probabilities`/`Penalize`: the CPU kernel's per-row weights are now one
  helper, `SamplingWeights`, so the check reads exactly what a step samples from). Refused with the reason
  (`SpeculativeDecoding.Refusal`): a vocabulary that differs (entry by entry, padding allowed: a family pads its models to
  different widths), and a generator with a sampler of its own. Telemetry: `InferenceStageKind.Draft` and `Verify`;
  `GenerationStats.DraftTokens`, `DraftAccepted`, `DraftAcceptance`, `DraftRounds`. The reply's text handling (stops,
  loops, bytes held back) is one `ReplyText` both paths use. CLI: `idrak bench MODEL --draft DRAFT [--draft-tokens N]`
  (with and without, the draft's first run apart, the acceptance, and whether the greedy tokens are the same);
  `--verbose` sums the rounds. Tests `speculative decoding` (8).
- **Craft.** Settings `draftModel` (`CRAFT_PLUGIN_AI_DRAFTMODEL`), `draftTokens` (`_DRAFTTOKENS`, 0 adapted),
  `agentDraftModel` (`_AGENTDRAFTMODEL`) and `agentDraftTokens` (`_AGENTDRAFTTOKENS`), empty agent ones the chat's; on the
  settings page under Chat and Agent, group "Speculative decoding". `AiModels` gives a role's lease its draft once ready:
  the completion model's own lease (one copy for completion and drafting), loaded after the role's model and off its
  request (the first replies may go without it; nothing waits), checked with Idrak's `Refusal`. `ModelLease.Draft`
  (Craft.Abstractions) is the copy's generator behind its gate (`GatedDraftModel`). The chip and the status bar show
  "draft: 1.5B, 72% accepted"; the tooltip names the draft in full or why a role has none. Tests `DraftModel` (5) and
  `Ai.TheSettingsReadEveryVariable` extended.
- Decisions (recommended, one line each):
  - The contract is the smallest additive one, a draft per request (`GenerationOptions.Draft`), not a strategy registry: a draft is an instance the app holds (a lease), like a tokenizer, not a named kind.
  - Greedy is `TopK` 1 (what the library's tests and bench use); `Temperature` 0 samples at 1/1000 as without a draft, by the standard rule.
  - The check runs on the host from the target's logits of the round (copied once a round), by the sampler's exact rule; a device-side check is a later step if the GPU numbers show the copy matters.
  - Rounds adapt as transformers' heuristic does: +2 after a round kept whole, -1 after a refusal, within the bounds, first round `DraftMinTokens` + 3.
  - A prompt with images, a batch decoded together and `UseCache` false decode without the draft (documented); `UseCache` false ignores it.
  - Acceptance on the chip comes from each reply's `GenerationStats` (the library's counts, per target and draft pair); the `Verify` stage events serve the telemetry panel and the CLI.
  - Empty `draftModel` uses the completion model only while inline completion is on (it would otherwise load a model the user turned off).
  - A draft session passes the copy's gate without waiting (`CopyGate.TryEnter`): during a training round the reply goes on alone.
- **Measured (CPU, this container, `speculative decoding: tokens a second`).** A pair built in the test: the target 8 blocks
  (dim 512, 25.2M parameters), the draft its first block with its embedding, norm and head (3.19M), the target's other
  blocks scaled toward the identity (x0.15: a draft that agrees; x0.3: one that agrees half the time). Greedy, 64 tokens,
  the same tokens with and without in every run.
  - Quiet machine, x0.15: alone 64 tokens/s cold, 83 warm; with the draft 167 cold, 182 warm (x2.20); 56 of 58 kept (97%) in 7 rounds; the draft 1.68 ms a proposal, the target's pass 37.3 ms a round of 9.3 positions, 12.1 ms a token alone.
  - Loaded machine (other slots building), x0.15: alone 12 / 12, with the draft 42 / 35 (x2.84). x0.3: alone 20 / 25, with the draft 17 / 37 (x1.49); 46 of 83 kept (55%) in 17 rounds; 7.28 ms a proposal, 126 ms a pass of 5.9 positions, 40.6 ms a token alone.
  - An earlier pair (x0.35, 45% kept) gained nothing (x0.99): on a CPU a pass of K+1 positions costs about three single steps, so speculation pays from roughly half kept. The GPU numbers are the owner's (below).
- Left: a draft's steps are not recorded as a device graph (each runs as it is); the round's logits come to the host (on a large vocabulary a few MB a round on a GPU); the Models page does not show drafts; `bench --draft` has no CLI test of its own; the owner's GPU and Windows runs.
- **Owner's commands, Idrak** (TARGET: the chat model, DRAFT: the completion model, both as named in Craft's settings):
  ```powershell
  cd D:\Projects\Idrak; git checkout main; git pull origin main
  dotnet build -c Release tests\Idrak.Tests
  $env:IDRAK_FILTER="speculative decoding|generation:|sampler:|conformance kit|public API|abstraction inventory|cli measure"; dotnet run -c Release --no-build --project tests\Idrak.Tests -- --progress --failures-only --failures-file test-output\fails.txt
  dotnet run -c Release --no-build --project src\Idrak.Cli -- bench TARGET --draft DRAFT -d cuda:0 -w int8 --tokens 256 --save draft-cuda > test-output\bench-draft.txt
  ```
- **Owner's commands, Craft:**
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  $env:CRAFT_FILTER="DraftModel|Ai.|AiPlugins|ModelLoading|ModelsPage|Settings|ApiGuard|Inventory"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```
  In the app: send a chat message; once the completion model has loaded the chip reads "draft: …, NN% accepted".

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

### W0 as built (2026-11-01)

Slot w1, on Craft `main` 0f14e72 and Idrak `main` 9e63a92. Craft only: no cause was found in Idrak.
- **The line-state kit is public** (`Craft.Abstractions/LineStates.cs`, contract 1.1, additions only): `LineTokenizer`,
  `LineStateSession`, `ITextFormatter` (with `ITextFormatter.Malformed`, the "line 3, column 9: …" message JSON and XML
  shared), `FormatStyle` and `LineStateLanguage` (the C# plug-in's internal `ProjectLanguage`, made public), unchanged in
  behaviour. New: `LineStates`, a thread-safe interning table for states past 64 bits (a stack of template, JSX or
  embedded frames gets one number with `LineStates.InternedBit` set; a held stack is found with nothing allocated),
  bounded to `Capacity` stacks (4,096) of `MaxDepth` frames (64), past which `Intern` gives the `Fallback` state (the
  tokenizer's reset state) and counts `Overflows` (rule 77); `LineStateSession.ChangeDialect(tokenizer)` (a version
  learned later) drops the states and raises `Reclassified`; `LineStateLanguage.TokenizerFor(context)` picks a dialect per
  file; `LineTokenizer.RulesAt(line, column, state)` lets an embedded part answer its own rules; `KnownLines` and
  `LinesScanned` are public (a third party's incremental tests). The C# plug-in's project languages (MSBuild, XML, XAML,
  Razor, .editorconfig, .sln) are `LineStateLanguage`s on the public kit (proof a third party can do the same).
- **`ILanguageSession.RulesAt(snapshot, position)`** (default null: the language's own rules), forwarded by
  `SyntacticLanguageSession`; `LineStateSession` answers from its tokenizer at the line's state. `EditorModel.RulesAt(position)`.
  Toggle comment (Ctrl+/, Ctrl+K Ctrl+C, Ctrl+K Ctrl+U) reads the rules at the first caret's first non-blank character, and
  a language with no line comment but a block comment (CSS, HTML, and today XML, XAML, MSBuild) wraps each caret's lines in
  it (`/* … */`, `<!-- … -->`) or unwraps them, one undo step.
- **JSON is its own pre-installed, removable plug-in** `plugins/Craft.Plugins.Json` ("json"; package `Craft.Plugins.Json`):
  the language (.json, .jsonc: package.json, tsconfig.json, appsettings.json, launchSettings.json, global.json), its
  colours, brackets, folding (`JsonFolding`, moved), Format document (`JsonFormatter`, moved) and the icons of .json and
  .jsonc, on `Craft.Abstractions` alone; JSON with comments and trailing commas as before. The C# plug-in keeps only what
  is C#'s (`ProjectLanguages.Json` and `JsonFolding` left its public API; its summary and plugin.json say so). Craft.App's
  `CraftPreinstalledPlugins` targets copy it with the others (`plugins\*\*.csproj`), so no app change was needed.
- **`src/Craft.Web`**, empty (net10.0, `IsAotCompatible`, no reflection), its folders `Script/`, `Style/`, `Markup/` (one
  file each naming its slot), in `Craft.slnx`, referenced by the tests and the API guard (`api/Craft.Web.txt`, empty).
  How each web plug-in carries its own copy: its project references `src/Craft.Web` as a plain (copied) project reference,
  so `Craft.Web.dll` lands in the plug-in's output folder and the app's targets copy that folder whole; the app has no
  Craft.Web of its own, so each plug-in's load context loads the copy in its folder (HTML's `<script>` colours with the
  JavaScript plug-in removed).
- **Android and AOT**: `Craft.Web` and `Craft.Plugins.Json` are pure C# with no reflection and nothing native
  (`IsAotCompatible` true, trim-safe); the kit in `Craft.Abstractions` adds none either.
- **Decisions (each the recommended option, one line each)**:
  - The kit lives in the `Craft.Abstractions` namespace (no `Craft.Abstractions.Text`): one `using` for a plug-in, as every other contract.
  - `ILanguageSession.RulesAt` returns null for "the language's own" (a session does not know its language's rules).
  - A dialect is a tokenizer: `ChangeDialect` swaps it; the formatter stays the language's.
  - `LineStates` is per tokenizer (shared by its sessions), append-only and bounded; full, it gives the reset state rather than clearing (an id a session holds never changes meaning).
  - `FormatStyle`'s equality is its indent and line break (the record's cache of breaks left out).
  - The comment fallback wraps whole lines (first line's indent to last line's last non-blank character), one pair per caret's block, and unwraps when every block is wrapped; blank blocks are left.
  - The comment's language is read once, at the first caret's first line (a multi-caret toggle across a `<script>` boundary uses that one).
  - JSON also opens `.jsonc` (VS Code's settings); names without an extension (`.babelrc`) wait for W-files' patterns.
  - The JSON plug-in registers .json's icon over the library's (the library's answers again when it is removed) and .jsonc's.
  - `LineStates`' capacity and depth are constructor arguments (each web plug-in carries them as its settings when its lexer needs them).
- **Tests** (CPU, here): new groups `WebKit` (5: a test-only third-party language on the public kit with interned frames,
  400 random edits equal to a fresh session's, typing settling on the next line, a warm 60-line screen at 0 B, its
  formatter through the editor as one undo step and `Malformed`'s message; the C# plug-in's languages on the kit; the
  dialect switch raising `Reclassified` through the session and the editor, per-file dialects; interning bounded with the
  reset fallback, 0 B lookups, a tokenizer resuming at the top level past the bound; `RulesAt` and the block-comment toggle
  for a block-only language, XML and an embedded part) and `WebPlugins` (5: the JSON plug-in from its own folder,
  removed and disabled, `package.json` coloured with the C# plug-in disabled, icons and folding with it; the C# plug-in
  holds no JSON; JSON's colours, format, format undo and format on save, moved from `ProjectLanguages`). Touched: `Plugins`
  (the project languages' tokenizers from the plug-in's context; the app layout's probe opens package.json as JSON),
  `ProjectLanguages` (JSON's cases moved out, the rest unchanged), `Folding` (JSON's ranges from the plug-in). First run:
  49 of 51 passed; the two failures were the tests' "from the plug-in's own context" read on the language object, which is
  now the host's `LineStateLanguage` (fixed to read its tokenizer). API dumps of Craft.Abstractions, Craft.CSharp and
  Craft.Editor regenerated (Craft.CSharp lost its JSON members), `api/Craft.Plugins.Json.txt` and `api/Craft.Web.txt`
  added, the contract list (1.1) with lines added only.
- **Left**: nothing of W0. The lexers, the `LineStates` settings per plug-in and the plug-ins' copies of Craft.Web come with
  W-script, W-style and W-markup.
- **The owner's check** on `test-output\CarftTestPOC`: open `appsettings.json` (colours, folding, Shift+Alt+F); Settings ›
  Plug-ins: disable Craft.CSharp and restart, `appsettings.json` still coloured, then disable Craft.Plugins.Json too and it
  opens as plain text; in a `.csproj`, Ctrl+/ on a line wraps it in `<!-- -->` and again unwraps it.
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt --filter "WebKit|WebPlugins|ProjectLanguages|Plugins|PluginKit|PhaseP|Folding|BracketColors|Editor|Settings|ApiGuard|Inventory"
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```

### W-files as built (2026-11-01)

Slot w2, Craft only (nothing in Idrak was needed: the icons' paths are parsed by MAUI Graphics, the registries are Idrak's
`KindTable`). Contracts 3, 4 and 5 and the rich icon theme.
- **Languages by name** (contract 3): `ILanguage.FilePatterns` (a default member, none: exact names and `*`/`?` globs);
  `EditorLanguages.ForFile` ranks an exact name, then the longest pattern (its characters other than wildcards), then the
  extension; the first language registered wins a tie. `FilePatternTable<T>` (public, any plug-in can use it) holds names,
  compound suffixes (".d.ts", and a star with a literal suffix: "*.component.html"), globs and extensions, each ignoring
  case: a walk over the name's dots with alternate lookups (rules 15, 17), the globs longest first, made again only when a
  registration changed the registry (its list is then a new array). Measured: 0 bytes over 230,000 warm lookups of
  languages, file icons and folder icons together (`WebFiles.MatchingNamesAllocatesNothing`).
- **Languages by workspace** (contract 4): `ILanguageSelector : IKind` (`Select(path, workspaceRoot)` from its own cache,
  no I/O; `PrepareAsync(root)` off the UI thread) and `LanguageSelectors` (`PrepareAsync` runs them together, a failing one
  named and skipped); `ForFile(path, workspaceRoot)` asks them first. The shell's call sites (open, new, save as, rename,
  peek, session restore, groups, the context menu's "Open with" and Format) go through `ShellModel.LanguageFor`, which
  passes the open folder; opening a folder prepares the selectors off the UI thread and then `RepickLanguages` gives the
  open tabs their languages again.
- **Rich icons** (contract 5): `FileIcon` gains `Shape`, `OpenShape` (a folder's open look) and `LightArgb`;
  `FileIconShape` (layers on a 16-unit box; `Problem` checks SVG path data, `Transform` places a glyph) and
  `FileIconLayer` (path data, dark and light colours, fill or a round stroke). `FileIcons` matches by exact name, compound
  suffix, glob and extension (the same table as the languages), `ForFolder(name)` by folder name, `RegisterFolder`,
  `PlainFolder`; a shape that cannot be drawn fails at registration. `FileIconThemes : IKind` (`IFileIconTheme`, and
  `FileIconTheme` for a plug-in made of icons by key) with the library's "craft"; `CRAFT_ICON_THEME` (Settings ›
  Appearance › Theme, its choices the registered themes) picks one, a theme's gaps drawn from Craft's set.
- **Craft's own set** (`CraftIcons`, 112 file keys, 31 folders, 417 layers): original drawings in the design's style (the
  logo's rounded tile with an ink mark): sources on a rounded tile, documents and data on a page with a folded corner,
  Angular's parts on a hexagon of Craft's own, each in the design's hues with a monogram of stroked letters or a small
  drawing (cube, gear, globe, branch, lock, tick, crates); folders open and closed with an emblem. The C# set redrawn
  (.cs .csx .csproj .props .targets .sln .slnx .razor .cshtml .xaml .xml .config .resx .json appsettings*.json
  launchSettings.json global.json .editorconfig .md images .svg .ps1 .cmd .bat .sh .gitignore .gitattributes Dockerfile
  .yml .txt) and the web set (.html/.htm, .css .scss .sass .less, .js .mjs .cjs .jsx jsconfig.json, .ts .mts .cts .tsx
  .d.ts tsconfig*.json, the Angular parts and angular.json, package.json, package-lock.json, .npmrc, .nvmrc,
  eslint.config.*, .eslintrc*, tslint.json, .prettierrc*, .spec/.test files); folders src, test(s), docs, .git, .github,
  .vscode, .vs, .craft, node_modules, dist/out/build, bin, obj, assets/images, public/wwwroot, components, services, scripts,
  .angular, Properties. Light: each base moved to 3:1 on the light theme's grounds (`ColorContrast`), a white ink; dark:
  the design's hues, a near-black ink. Checked here: every layer parsed by MAUI Graphics' own `PathBuilder` (a scratch
  program against the package's net10.0 assembly: 417 of 417, all inside the box) and drawn to a preview sheet.
- **Drawing**: `IconPathCache<TPath>` (Craft.Editor) keeps each layer's parsed path per size, by the layer's identity,
  bounded (4,096); `MauiFileIcons` builds them with `PathBuilder.Build` scaled once and caches each colour; `FileIconView`
  (a bindable `GraphicsView`) shows one; the badge is the fallback (no shape, or a path the parser refused). `IIconCanvas`
  (optional on an `IEditorCanvas`, MAUI's implements it) and `FileIconDrawing.Draw` let a drawn list (Problems) draw icons.
  Measured with the cache and a recording canvas: the first frame parses each layer once, 0 bytes and no parse over 1,000
  warm frames of 22 icons, a new size parses once more.
- **Where they show**: the tree (the chevron, then the folder's open or closed icon or the file's), the tabs, quick open
  (folders too), the breadcrumb's file, the welcome page's recent folders, Problems' file headers, search, bookmark,
  history and Run results (`PanelResultGroup.FileIcon`), the menus' "file:" icons, the Source Control file lists.
- **node_modules**: `Workspace.ListFolder` keeps the skipped folders (`CRAFT_WORKSPACE_IGNORE`) marked
  `WorkspaceEntry.IsIgnored` (and every entry listed inside one) when `CRAFT_WORKSPACE_SHOW_IGNORED` is on (the default):
  the tree draws them at half opacity, closed, read only when opened; `EnumerateFiles` (quick open's list, indexing) and
  the search's walk never enter them, as before; `Workspace.IsInIgnoredFolder`.

**Decisions on the recommendation (the owner away; one line each).**
- A star followed by a literal suffix is a compound suffix (found by the dot walk), any other wildcard a glob; both rank as patterns, above every extension.
- An extension a language lists keeps its rank as an extension even when it has two dots (".d.ts" listed as an extension ranks as a suffix-pattern).
- A selector returns the language itself (not a name); a disabled plug-in takes its selector with it, so nothing names a missing language.
- The selectors learn a folder when it opens, not on every call; the host re-picks the open tabs after (a later change of angular.json waits for W-npm's `WorkspaceChanged`).
- The icon set is one registry keyed by any kind of key (`FileIcons`), and is the "craft" theme; a theme a plug-in registers answers first and Craft's set fills its gaps.
- An unregistered `CRAFT_ICON_THEME` keeps Craft's own, said in the log (as `CRAFT_THEME` falls back), while `FileIconThemes.Use` itself fails with "not registered".
- Shapes are 16-unit boxes of SVG path data per layer (fill or round stroke), not a font or images: no package, any plug-in can draw its own, light and dark per layer.
- The library's set is built once from bases and glyphs placed by a path transform (one walk, InvariantCulture), so each glyph is drawn once and reused.
- Light icons: the base moved to 3:1 (a graphic's contrast) on the light theme's grounds, white ink; the badge's text colour to 4.5:1.
- The path cache is by the layer's identity and the size, bounded, cleared when the icon theme changes; a layer the platform's parser refuses draws the badge from then on.
- Problems draws icons through an optional canvas interface (as `IStyledLineCanvas`), so the headless tests and any canvas without shapes keep the badge.
- The tree's folder rows keep their chevron and add the folder icon; file rows leave the chevron's place empty so names line up.
- Skipped folders show by default (VS Code shows git-ignored folders dimmed); off, the tree is as before.
- The JSON plug-in's .json and .jsonc icons take Craft's drawn JSON shape (its registration over the library's had turned them back into badges); package.json keeps its own icon (an exact name above .json).
- Angular's parts use a hexagon of Craft's own and letters, never Angular's mark; no vendor logo anywhere in the set.

**Tests**: new group `WebFiles` (7): `ForFile` by name, longest pattern, extension and tie, the table's grammar and globs, a
registration changing the table; 0 bytes warm for languages and icons by name; a selector first from its cache, a failing
one named, the shell's folder passed and the open tab re-picked; icons by name, suffix, glob, extension and folder, open and
closed, light at 3:1, a plug-in's icons and Unregister; every built-in shape parses and is written again the same, the bad
forms refused, the C# and web lists covered; quick open, the tree and Problems on the same table (the badge without shapes,
the icon with them); warm icon frames parse once and allocate nothing, per size, bounded; a plug-in's icon theme picked by
`CRAFT_ICON_THEME`, an unknown name kept to Craft's, `Use` failing loudly; node_modules listed dimmed and closed, never in the
workspace's files or a search, hidden with the switch off. First run of the group and the touched ones: all 7 passed;
`Inventory` wanted the regeneration. After the rebase onto W0, the full filter
(`WebFiles|Inline|Shell|ContextMenus|SourceControl|WorkspaceSearch|Breadcrumb|Settings|Design|LookAndFeel|Ui|ApiGuard|Inventory|ProblemsPanel|WebKit|WebPlugins|ProjectLanguages`,
153): 150 passed; two expectations this work changes on purpose were updated (`Shell.TheFolderTreeListsAFolderWhenItOpens`:
bin now shows dimmed by default, the old order kept with the switch off; `WebPlugins`: package.json shows its npm icon, a
.json file the plug-in's drawn one), and `Layout.TheTreeFollowsTheDisk…` (bin's own row now shows, still no batch for the
churn inside it); then those and `Experience|TreeMove|Layout|EditorGroups|Navigation|Plugins` passed. One failure is not
this work's: `LookAndFeel.NoPlatformMessageBoxIsLeft` matches X part 8's `PanelMessageBox` (`MessageBox(box)` in
`src/Craft.Maui/PanelBlocksView.cs` and `plugins/Craft.Plugins.Git/SourceControlView.cs`), left to its owner. API dumps of
Craft.Abstractions, Craft.Core and Craft.Editor, the contract list (1.1) and `plans/inventory.md` regenerated, lines added only.

**Windows only (not run here; Craft.Maui compiled on net10.0)**: `FileIconView` and `MauiFileIcons` on Win2D (the look, the
antialiasing at 16 units, whether Win2D's `FillPath` allocates per call), the tree's chevron and icon row, the tabs',
breadcrumb's, quick open's, welcome page's, menus' and panels' icons, Problems' icons through `MauiEditorCanvas`, the dimmed
node_modules. No file under `Platforms/` changed.

**Left**: folder icons in the breadcrumb's folder parts; a tab's icon follows a rename only when the strip is rebuilt; the
selectors re-read a folder only when it opens (W-npm's `WorkspaceChanged` can drive it); files inside an opened skipped
folder do not follow the disk (the watcher drops them, as before); the closed file of a review (`BackgroundDocument`) is
named by its file name only.

**The owner's commands:**
```powershell
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="WebFiles|Inline|Shell|ContextMenus|SourceControl|WorkspaceSearch|Breadcrumb|Settings|Design|LookAndFeel|Ui|ApiGuard|Inventory|ProblemsPanel|WebKit|WebPlugins|ProjectLanguages|Layout|Experience|Plugins"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
```
In the app: the explorer's icons beside every file and folder (open a folder: its icon opens), `bin` and `obj` dimmed and
closed (open one: its files dimmed); the tabs', quick open's (Ctrl+P, and a folder browsed), the breadcrumb's, the welcome
page's and Problems' icons; the light theme (Settings › Appearance › Theme) for the light variants; Settings ›
Appearance › File icon theme lists "craft"; Settings › Files › "Show skipped folders in the explorer, dimmed" off hides them.
With W's web project (`test-output\WebTestPOC`), node_modules dimmed, package.json, angular.json, the component parts.

### W-npm as built (2026-11-01)

Slot w3. Idrak first (its gap fixed in Idrak, on `main`), then Craft.

**Idrak** (`Idrak.Abstraction/CommandSandboxes.cs`, additive; commits 27e7560, e3d656c): `CommandLimits.Requester` (who asks
for a command, as the words of a sentence; default "the agent", which the coding tools keep) and `CommandLimits.Purpose`
(what the command is for when its command line does not say it plainly, null by default); neither changes how a command
runs; the container sandbox carries both to the run of its program. Test `command sandboxes: requester` (defaults, copies,
equality, the same run whoever asks); the public API dump (two lines added). The filter `command sandboxes|public API|abstraction inventory`:
10 of 10 on the CPU, both times.

**Craft contracts** (`Craft.Abstractions`, additive, contract 1.1):
- (9) `PluginSetting.MachineOnly`, `EditorPlugins.IsMachineOnly(variable)` (compared in place, nothing allocated),
  `PluginSetting.VariablePrefix` and `IsVariable`. `CraftSettings` passes a workspace's value over (the user's or the
  default is read), refuses to write it into the workspace's file, and the settings page's row says "This machine's only …
  (its value there is passed over)". Git's `path` and approve-each's `inner` are machine-only now too.
- (10) `IEditorHost.WorkspaceChanged` with `WorkspaceChangedEventArgs` (`Previous`, `Current`, `Kind`: `Opened`, `Closed`,
  `Switched`, `Refreshed`); the host raises it with `WorkspaceFolderChanged`, and `EditorHost.RefreshWorkspace()` (the
  explorer's Refresh) raises `Refreshed`.
- **approve-each** words its question through `Requester` ("The npm plug-in wants to run a command") and shows `Purpose`
  ("Runs: the script build of package.json: ng build"); a plug-in's command is a dialog (the agent's stays inline in its
  timeline), says plain "Deny", gets no second activity and no copy of its output in "Agent sandbox" (its command and end
  only); a run with no time limit says "Time limit: none (it runs until stopped)" (it said "-0 s").

**The npm plug-in** (`plugins/Craft.Plugins.Npm`, pre-installed and removable, contract 1.1, capabilities files and sandbox):
- Node: `CRAFT_PLUGIN_NPM_NODE` (machine-only; a path or a name on PATH), else `node` on PATH; npm as `node npm-cli.js`
  (never npm.cmd): `CRAFT_PLUGIN_NPM_NPM` (machine-only; the file or npm's package folder), else beside Node in each layout
  (Windows' installer `node_modules\npm\bin`, Unix `../lib/node_modules/npm/bin`, Debian `../share/nodejs/npm/bin`), through
  Node's link (Homebrew, nvm), else npm on PATH followed to its npm-cli.js. Found off the UI thread with file checks only;
  the version read once (`node --version` through Idrak's "process" sandbox, 10 s) into the status item "Node 22.23.4"
  (its click opens the Run view). No Node, or Android: a status entry in the notification centre says why with "Look for
  Node again", and nothing else is added.
- Scripts: every package.json of the workspace (`node_modules` never walked; the folders of `ignore` and each `.gitignore`
  passed over), read as UTF-8 bytes with `Utf8JsonReader` (rule 74: BOM passed over, comments and trailing commas taken,
  odd names and escapes kept, a duplicate name keeps its last text in its first place as `JSON.parse`, non-string values
  skipped unread, a broken file logged once per problem). Each script is a palette command ("npm: run build (src/app)",
  "npm: run build in a terminal (src/app)"), each package an "npm: install (src/app)"; the tree's and the tab's menu on
  package.json get "Run npm script ▸", "Run npm script in a terminal ▸" (rows made as the menu opens, `EditorMenuSources`)
  and "npm install", with icons and a `When` on package.json; the Run view's provider "npm" gives a target per script
  (build also builds, test also tests: F5 and Ctrl+Shift+B), run as `node npm-cli.js run SCRIPT -- arguments` in the
  package's folder. All follow the file: the folder watcher (a package.json or lock file made, removed, renamed, a folder
  holding one removed; a burst gathered for `scanDelayMs`), the editor's saves, and `WorkspaceChanged` (open, close,
  switch, Refresh).
- Runs: through the sandbox `CRAFT_PLUGIN_NPM_SANDBOX` (machine-only, default "approve-each"; "not registered" said,
  nothing runs) with `Requester` "the npm plug-in" and `Purpose` the script's text; output streamed to the terminal tab
  "npm: build" (the folder and the command first, colours kept: `FORCE_COLOR=1`, `npm_config_color=always`; one append
  per UI turn), the end in green or red with the time; an activity "npm: build" whose Cancel (and "npm: Stop every npm
  run") stops the process tree, its text the elapsed time and the last line; long-running scripts (`longRunning`,
  "start;serve;watch;dev", and their ":" variants) have no time limit, the rest `timeLimitMinutes` (30). "In a terminal"
  is X part 6b's interactive start: asked once, then a program tab that takes the keyboard (Ctrl+C stops it).
  Install: `install` (or `ci`; without npm's lock file install runs, said in the tab) as the same kind of run. Every run
  gets `npm_config_fund=false`, `npm_config_audit=false`, `npm_config_update_notifier=false`, `NG_CLI_ANALYTICS=false`.
  `.npmrc` is never read, so never logged.
- yarn, pnpm and bun: found by `packageManager` or a lock file in the package's folder or above it within the workspace;
  said once per workspace ("npm: pnpm is not supported yet"); their scripts still run with npm, install is refused, said.
- Options (`CRAFT_PLUGIN_NPM_*`, Settings › Terminal › npm): `NODE`, `NPM`, `SANDBOX` (machine-only), `INSTALL`,
  `LONGRUNNING`, `TIMELIMITMINUTES`, `COLOR`, `IGNORE`, `SCANDELAYMS`.

**Decisions on the recommendation (the owner away; one line each).**
- `Requester` is a phrase in a sentence ("the agent", "the npm plug-in"), not a bare name, so any sandbox words its question without a lookup.
- The question also needed the script's text, which the command line (`node …/npm-cli.js run build`) does not show: a second additive Idrak field, `CommandLimits.Purpose`, rather than a Craft side channel.
- approve-each treats any requester but "the agent" as a plug-in: a dialog, a plain Deny, no activity or output of its own (the plug-in shows them), only the command and its end in its tab.
- `WorkspaceChanged` is added beside X part 8's `WorkspaceFolderChanged` (kept, B3), with the folders and the kind, and a `Refreshed` kind from the explorer's Refresh so a plug-in re-reads what the watcher (names only) cannot see.
- `MachineOnly` is enforced where Craft's own list is (`CraftSettings`), asking the registered plug-ins; Git's path and approve-each's inner sandbox take it too.
- Node's version is read through the plain "process" sandbox without a question: a fixed argument to a program only this machine's settings can name.
- The provider, commands and menus are registered only once Node is found, so a machine without Node gets none of them.
- Each script gets two palette commands (in its tab, in a terminal) rather than a guess at which scripts read input; the Run view's npm targets do not take the keyboard by default (its Interactive setting turns it on).
- The palette and menus run through the npm sandbox setting (approve-each); F5 through the Run view's own approval and `CRAFT_RUN_SANDBOX`, as every Run view target.
- `npm ci` without npm's lock file runs `npm install` and says so; another manager's package is never installed with npm.
- The tab is named "npm: build" for the root's package and "npm: build (src/app)" for another's; a second run of a running script is refused, said.
- The file icons are W-files' drawn library set (package.json, package-lock.json, .npmrc, .nvmrc, node_modules), merged before this slot; the plug-in registers none to shadow them.
- The plug-in uses the "system" folder watcher, as the git plug-in does.

**Tests**: new group `WebNpm` (9): package.json from bytes (BOM, comments, trailing commas, odd and escaped names, a
duplicate, values skipped, broken and empty files, `packageManager`, lock files in the folder and above, node_modules and
`.gitignore` never walked); Node on a fake machine (Windows', Unix's and a linked layout, both settings, the reasons, the
version, long-running and time limits, the variables, the command); a workspace refused for the Node path (read, write,
the page's note, back once the plug-in is gone; Git's and approve-each's settings); the scripts as commands, menu rows and
Run targets following Refresh, the watcher, a switch and a close; no Node says why and adds nothing; approve-each's question
naming the npm plug-in and the script's text, Deny, Allow, the limits and variables, the tab, one activity, no time limit for
dev and Cancel stopping it, `.npmrc` nowhere; install as an activity with its elapsed time and last line, ci and install,
yarn and pnpm said once, a pnpm package not installed, a sandbox not registered; loaded from its folder and removed;
real Node (skipped with its reason without it; it ran here, Node 22.23.4): `npm run` through the process sandbox with the
variables, Cancel ending an endless script, a script reading "Ada" typed into its terminal tab. First run: 5 of 9 (a real
bug: a `u8` literal of "\xEF" is U+00EF's bytes, so a BOM was not passed over; a Windows test separator and case; the
activity checks looked at Recent, which keeps only longer activities); then all passed. After the rebase onto W0 and
W-files, the full filter `WebNpm|WebPlugins|RunView|Terminal|ContextMenus|Settings|Plugins|PluginKit|PhaseP|ApiGuard|Inventory|SourceControl`:
138 of 138. API dumps of Craft.Abstractions, Craft.Editor, the new Craft.Plugins.Npm and the contract list (1.1)
regenerated, lines added only; the inventory unchanged.

**Not run here** (the owner's): the menus, tabs, program tab and status item on WinUI; Windows' Node layout and PATHEXT on
a real machine; `npm install` over the network.

**Left**: a package.json edited outside the editor is read again on Refresh, a save or a name change (the watcher sees
names only); yarn, pnpm and bun runners; npm workspaces' `--workspace` runs; problems from a script's output use the
Run view's MSBuild form only (tsc's `file(line,col)` lines), not Angular's or ESLint's.

**The owner's commands.** Idrak first (Craft builds against it):
```powershell
cd D:\Projects\Idrak; git checkout main; git pull origin main
dotnet build -c Release tests\Idrak.Tests
$env:IDRAK_DEVICES="cpu"; $env:IDRAK_FILTER="command sandboxes|public API|abstraction inventory"; dotnet run -c Release --no-build --project tests\Idrak.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:IDRAK_FILTER; Remove-Item Env:IDRAK_DEVICES
```
Then Craft (a small npm project under `test-output` to try the scripts):
```powershell
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="WebNpm|WebPlugins|RunView|Terminal|ContextMenus|Settings|Plugins|PluginKit|PhaseP|ApiGuard|Inventory|SourceControl"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$p="D:\Projects\Craft\test-output\NpmTestPOC"; New-Item -ItemType Directory -Force $p | Out-Null; cd $p; npm init -y; Set-Content ask.js "process.stdout.write('Name? '); process.stdin.once('data', d => { console.log('Hello ' + String(d).trim()); process.exit(0); });"; Set-Content tick.js "setInterval(() => console.log(new Date().toISOString()), 1000);"; npm pkg set scripts.hello="node --version" scripts.ask="node ask.js" scripts.dev="node tick.js"; cd D:\Projects\Craft
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\NpmTestPOC
```
In the app: "Node 22.x" in the status bar (its click opens the Run view, which lists hello, ask, dev); Ctrl+Shift+P "npm: run
hello" (the question "The npm plug-in wants to run a command" with "Runs: the script hello of package.json: node --version";
Allow once; the tab "npm: hello"); right-click package.json › Run npm script › dev (no time limit in the question; the
activity's Cancel in the status bar stops it); "Run npm script in a terminal" › ask (type a name, Enter); "npm install" from
the same menu (an activity with its time and last line); Settings › Terminal › npm › Node says "This machine's only"; with
`"CRAFT_PLUGIN_NPM_NODE": "C:\\nowhere\\node.exe"` in the project's `.craft\settings.json` nothing changes (passed over).

### W-style as built (2026-11-01)

Slot w1, on Craft `main` f787030 (W0) and Idrak `main` e3d656c. Craft only: no cause was found in Idrak. Not rebased: pushed
to `slot/w-style` for the main session to merge (the owner's rule of 2026-11-01).
- **The lexers** (`src/Craft.Web/Style/`): `StyleTokenizer`, one `LineTokenizer` per dialect (`StyleDialect` Css, Scss, Less,
  Sass) and version (`StyleTokenizer.For(dialect, year)`, made once, shared). CSS Syntax 3 tokens, so new syntax always
  lexes; nesting: a statement in a block is a nested rule when its first `{` comes before a `;` or `}` (strings, comments,
  parentheses and `#{}`/`@{}` skipped), a declaration otherwise, and on a line with neither a declaration when a known
  property, a `--name` or a ':' followed by a space starts it (`b:hover,` is a selector, `color:` a declaration). The latest
  CSS: `@layer`, `@container`, `@scope … to …`, `@starting-style`, `@property`, `@position-try`, `@view-transition`,
  `@function --x()` and `if(… ; else: …)` (a ';' inside parentheses does not end a declaration), `:has()`, `:is()`,
  `:where()`, `color-mix()`, `oklch()`/`oklab()`, relative colours (`rgb(from …)`), `light-dark()`, anchor positioning
  (`anchor()`, `anchor-size()`, `anchor-name`, `position-anchor`, `position-try-fallbacks`), custom properties, units and
  hex and named colours as numbers, `!important`, attribute selectors, escapes in strings. SCSS: `$var`, `#{}` in selectors,
  properties, values and strings and across lines (frames to five deep), `@use`/`@forward` (`as`, `with`, `show`, `hide`),
  `@mixin`/`@include`/`@content`, `@function`/`@return`, `@if`/`@else if`/`@each … in`/`@for … from … through`/`@while`,
  `@extend`, `%placeholder`, `&-suffix`, `module.function()` and `module.$var`, maps (a key before ':' in parentheses),
  `!default`, `//`. LESS: `@var:`, `@@var`, `@{}`, mixins (`.m(@a; @b: 2)`, calls `.m();`), guards (`when`), detached
  rulesets (`@r: { }`, `@r();`), `~"…"`, `:extend()`, `@plugin`, `//`. .sass (indented) lexes as SCSS whose statements end
  at the line's end. Colours through the theme's existing classes only: properties `PropertyName`, class selectors and
  placeholders `ClassName` (a LESS mixin `MethodName`), id selectors `ConstantName`, type selectors, `&` and `*` `Keyword`,
  pseudo-classes and pseudo-elements `TypeParameterName`, attribute names `ParameterName`, at-rules `ControlKeyword`,
  functions `MethodName`, custom properties and SCSS/LESS variables `LocalName`, layer names and SCSS modules
  `NamespaceName`, value keywords `EnumMemberName` (CSS-wide ones and `true`/`false`/`null`/`and`/`or`/`not` `Keyword`),
  numbers, units and colours `Number`, strings, escapes, comments; interpolation delimiters `Operator`. The state is one
  number under 63 bits (mode, comment, parentheses, an at-rule's prelude kind, frames), so HTML's `<style>` can carry it;
  nothing is allocated per token (names by span through frozen alternate lookups, specials in `SearchValues`, no boxing).
- **The names** (`StyleNames`): data lists of the properties, at-rules (CSS's, SCSS's, LESS's), functions, pseudo-classes
  and pseudo-elements and the named colours, each newer one with the year it became Baseline (`Future` for none yet:
  `if()`, `@function`, `calc-size()`, `field-sizing`, `interpolate-size`, scroll-driven animations, `::scroll-marker`,
  `contrast-color()`, `sibling-index()`). They only tell a declaration from a rule where a line does not, and let an older
  version read a newer name as plain; any name lexes by its syntax.
- **The formatter** (`StyleFormatter`, CSS, SCSS, LESS): only the white space between tokens changes (never inside a string,
  comment, interpolation or unquoted `url()`, never where a space changes a selector): one declaration and nested rule per
  line at the editor's indent, one selector of a list per line, `' {'`, `': '` after a declaration's colon only, a space after
  a value's comma, none before `;` or `,`, a value's own line breaks kept (one step in, or by its parentheses: SCSS maps),
  one empty line kept, comments where they were, `} @else {`. Unclosed `{`, `(`, string, comment or interpolation, or a
  close with nothing open: the file is left unchanged with "line L, column C: …". Format Selection and format on save
  (`CRAFT_FORMAT_ON_SAVE`) through the existing paths.
- **Format Selection in the editor** (it lacked one): `EditorModel.FormatSelectionAsync`/`FormatSelection` (Ctrl+K Ctrl+F,
  `craft.editor.formatSelection`, the Edit menu and the palette): the selection's lines (the caret's when none), from the
  end of the text before the first (so its indentation is set) to the end of the last, one undo step, the selection kept on
  its characters; for every language that formats (C#'s through Roslyn's span).
- **Folding** (`StyleFolding`, the plug-in's `CssFolding` "css"): blocks by braces, `/* #region */` and `// #region` to
  `#endregion` (marked), block comments and runs of `//` lines (comments), with the language's own tokenizer (a brace in a
  string, a comment, a `url()` or an interpolation never folds).
- **The plug-in** `plugins/Craft.Plugins.Css` (pre-installed, removable, contract 1.1): languages "css" (.css), "scss"
  (.scss, and .sass as SCSS-like, not formatted), "less" (.less), on `LineStateLanguage` with a copy of Craft.Web in its own
  folder; `CssFolding`; icons .css "#", .scss and .sass "S", .less "L" (today's `FileIcons` badges: W-files' vector icons had
  not merged when this was written; the main session moves them onto its API on merge); the editor plug-in "css" with
  `CRAFT_PLUGIN_CSS_VERSION` (latest; or a Baseline year 2020-2026), `CRAFT_PLUGIN_CSS_PROJECTVERSION` (true),
  `CRAFT_PLUGIN_CSS_FORMAT` (true), each on the settings page (Editor › CSS, SCSS, LESS) and in `.craft/settings.json`.
  `StyleProjectVersion` reads the nearest `.browserslistrc` or `package.json` `browserslist` (`baseline 2024`, production
  first) as UTF-8 bytes (`Utf8JsonReader`), off the UI thread, once per folder (bounded at 1,024 folders), and changes the
  open session's dialect when it lands (`ChangeDialect`, `Reclassified`).
- **Contract** (Craft.Abstractions 1.1, one addition): `LineStateLanguage.OpenSession(buffer, context)`, protected virtual,
  so a language picks its formatter per file and keeps the session to change its dialect when a version is learned later.
- **Decisions (each the recommended option, one line each)**:
  - The language objects and settings live in the plug-in; the lexers, formatter, folding, names and the project-version reader in Craft.Web (HTML's `<style>` reuses them).
  - A version is a Baseline year ("latest" by default), the year a name became Baseline in every major engine; a known name newer than the version is plain, an unknown name keeps its syntax's colour.
  - Precedence: the setting when it names a year, else the project's browserslist `baseline YYYY`, else latest; "baseline widely available" names no year (a moving target; confirm with the owner).
  - Years marked as of 2026-11-01 (confirm): `@scope` 2025, `@view-transition` 2025, anchor positioning and `@position-try` 2026, `if()` and `@function` not Baseline yet (only "latest" colours them).
  - SCSS's own `@function` is never version-gated (it is not CSS's newer `@function`).
  - Pseudo-classes and pseudo-elements take `TypeParameterName`, id selectors `ConstantName`, value keywords `EnumMemberName`, type selectors `Keyword`: distinct hues from the existing classes, no new class.
  - Interpolation delimiters are `Operator`, so a block's `{`/`}` (`Punctuation`) alone folds and colours as a bracket.
  - A ':' followed by a space at a line's end means a declaration (a selector's pseudo-class never has one), beside the known-property rule.
  - `RulesAt` is not answered (the style languages embed none); toggle comment wraps CSS lines in `/* */` (W0's fallback) and comments SCSS/LESS lines with `//`.
  - The formatter never adds a space inside a selector (`a>b` stays), never spaces a colon other than a declaration's, keeps `{}` empty blocks on one line.
  - .sass is lexed as SCSS whose statements end at the line's end and never formatted (its line breaks are its syntax), said in the language's summary.
  - Format Selection works on whole lines (VS Code's and Visual Studio's Ctrl+K Ctrl+F), the formatter's edits within them; it is not in the editor's context menu (the menu stays as it was).
  - The format setting off gives a session without a formatter: Format document says "already formatted" and changes nothing (a later Prettier through W-tools' `DocumentFormatters` takes over).
- **Measured** (this container, Release, 10,000-line files of the fixtures repeated; `test-output/benches/web-style.txt`):
  a 60-line screen at line 5,000 cold (5,000 states learned) CSS 8.1-12.3 ms, SCSS 2.1-3.1 ms, LESS 1.9-3.7 ms; warm
  21-36 µs a screen and **0 B**; a keystroke (a letter, or a `{` at a selector's end) re-lexes the 60 painted lines and
  **0** more (the states below settle on the next line); `/*` typed at the top of the SCSS and LESS files reads the 4,999
  lines above the screen once (0.5-1.1 ms; the CSS fixture's first line is a comment, so there it closed at once: 1 line);
  Format document 14-19 ms cold, 5-6 ms warm (CSS), 4-7 ms (SCSS, LESS); folding 4-12 ms cold, 3-7 ms warm. Target (guess)
  a warm screen under 0.5 ms: met (about 0.03 ms).
- **Proven** (rule 71): 6,000 random edits (1,500 per dialect, .sass included, weighted to `{ } ; : / * " ' #` and line
  breaks, plus `#{`, `@{`, `/*`, `*/`, `//`, `url(`), every 15th compared whole: the incremental session's spans equal a fresh
  session's, in order, never overlapping; each fixture's formatted text equals the original with the white space removed,
  formats to itself with no edits, and a messy copy formats to the same text.
- **Tests** (CPU, here): new group `WebStyle` (9: CSS, SCSS and LESS fixtures with expected spans; rule vs declaration and
  .sass, rules, toggle comment; random edits equal a fresh session; a warm screen at 0 B, lines re-lexed per keystroke, the
  bench file; the formatter's exact outputs, white space only, idempotent, malformed left with the line and column; through
  the editor: the caret kept, one undo step, Format Selection and Ctrl+K Ctrl+F, malformed with its message, the format
  setting off, .sass not formatted; folding of CSS and SCSS and the provider; versions, the settings, browserslist and the
  project's version landing on an open session, the setting winning); `WebPlugins` extended (the CSS plug-in from its own
  folder with its Craft.Web copy named in its deps.json, its languages, colours, folding, settings and icons; removed and
  disabled, .css, .scss, .sass and .less open as plain text; in the test host the context shares the host's Craft.Web, as it
  shares any `Craft.*` the host carries: the app carries none, so there the plug-in's copy loads). First run: 13 of 17
  passed; the four failures were the tests' (the load-context rule above, a timing text left unbalanced by the edits, a
  malformed SCSS case whose `}` closed its interpolation) and one real one (SCSS's `@function` was version-gated as CSS's),
  fixed. The full filter `WebStyle|WebKit|WebPlugins|WebFiles|ProjectLanguages|Plugins|Folding|BracketColors|Editor|Settings|ApiGuard|Inventory`:
  149 of 149 passed. Dumps: Craft.Abstractions (and its contract list, one line added), Craft.Editor, Craft.Web regenerated,
  `api/Craft.Plugins.Css.txt` added.
- **Android and AOT**: Craft.Web's style lexers and Craft.Plugins.Css are pure C# with no reflection and nothing native
  (`IsAotCompatible`, trim-safe); JSON is read with `Utf8JsonReader` only.
- **Left, for later**: the indented Sass syntax's own lexer and formatter (.sass is SCSS-like and not formatted today); a
  selector list or a value longer than the line length is not broken (the formatter changes white space only, it never
  wraps); "baseline widely available" in a browserslist names no year; the icons move to W-files' vector icons on merge.
- **The owner's check** on `test-output\CarftTestPOC` (add a `site.css`, an `app.scss` and a `theme.less`, or open the
  Angular project's `.scss` under `test-output\WebTestPOC`): the colours of nesting, `@layer`, `@container`, `:has()`,
  `oklch()`, `$var`, `#{}`, `@mixin`/`@include`, LESS's `@var:` and guards; folding of blocks and `/* #region */`;
  Ctrl+/ (CSS wraps in `/* */`, SCSS uses `//`); Shift+Alt+F and Ctrl+K Ctrl+F on a messy rule; a missing `}` leaves the
  file unchanged with a toast; Settings › Editor › CSS, SCSS, LESS: version 2023 makes `@starting-style` plain; Settings ›
  Plug-ins: disable Craft.Plugins.Css and restart, the files open as plain text.
  ```powershell
  cd D:\Projects\Craft; git checkout main; git pull origin main
  dotnet build -c Release tests\Craft.Tests
  dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt --filter "WebStyle|WebKit|WebPlugins|WebFiles|ProjectLanguages|Plugins|Folding|BracketColors|Editor|Settings|ApiGuard|Inventory"
  dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
  $env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
  ```

### W-script as built (2026-11-01)

Slot w2, on Craft `main` dff114e and Idrak `main` e3d656c. Craft only: no cause was found in Idrak (the kit, `LineStates`,
the registries and `Utf8JsonReader` did all that was needed).
- **Main's failing test first**: `LookAndFeel.NoPlatformMessageBoxIsLeft` matched Craft's own `PanelMessageBox` block
  (`MessageBox(box)`). Its pattern now catches only the platform's boxes: MAUI's `DisplayAlert`/`DisplayActionSheet`/
  `DisplayPrompt`, WinUI's `ContentDialog` and `MessageDialog`, `MessageBox.Show`, Win32's `MessageBoxW`/`MessageBoxA` and a
  `MessageBox` P/Invoke declaration (`extern`, a `LibraryImport` partial, an `EntryPoint`); a new fixture test
  (`LookAndFeel.ThePlatformDialogPatternCatchesOnlyThePlatformsBoxes`) proves both sides.
- **The lexer** (`src/Craft.Web/Script/ScriptTokenizer.cs`, public `ScriptTokenizer : LineTokenizer`): one incremental lexer
  with five dialects (`ScriptDialect`: JavaScript, Jsx, TypeScript, Tsx and the AngularExpression stub W-markup builds on),
  one state per line, a stack of frames (code, a template's `${ }`, a JSX `{ }`, a template's text, a JSX tag, a JSX
  element's children) to any depth across lines, more than one frame interned in its `LineStates` table. The token before
  (carried in the state across lines) decides a `/` (regex or division), a `{` (block or object), a `<` (JSX or less-than)
  and the class of the next name. Keywords from a `FrozenDictionary` by span (rule 15), specials in `SearchValues` (rule
  17), no generic range search and no boxing (rules 88, 89), nothing allocated per token. Covers: hashbang, `/// <reference>`,
  JSDoc and its `@tags`, standard decorators (`@a.b(...)`), private names and `#x in obj`, numeric separators, BigInt,
  `using`/`await using`, `accessor`, `async *m()`, import attributes (`with { type: "json" }`), `import defer`/`import source`
  (confirm), regexes with classes (nested in the v flag's), escapes (`\p{L}`, `\k<n>`, `\u{...}`), named groups and the
  ES2025 modifiers `(?i:...)`, flags `d` `v`; templates nested across lines; JSX elements, fragments, attributes, spreads,
  entities, children and `{expr}` as frames; TypeScript's `satisfies`, `accessor`, `const` and `in`/`out` type parameters,
  `declare`, `declare global`, `namespace`, `module`, `abstract`, `override`, `readonly`, `keyof`, `infer`, `unique symbol`,
  `asserts x is T`, `import type`, `export type *`, `import x = require()`, template literal types, `enum`, the predefined
  types; `<T,>`, `<T extends U>` and `<const T,>` generics in .tsx; .ts never lexes JSX.
- **Colours** through the theme's existing classes: decorators `PreprocessorKeyword`, a regex's body `String` with escapes and
  group openers `StringEscape` and flags `Keyword`, `${` `}` `Punctuation`, private names `FieldName`, `class`/`new`/`extends`
  names `ClassName`, `interface`/`implements` names `InterfaceName`, `enum` names `EnumName`, `type` alias names `ClassName`,
  `namespace` names `NamespaceName`, a name before `(` `MethodName`, a member after `.` `PropertyName`, `/// <reference>` and
  the hashbang `PreprocessorText`, JSDoc `DocComment`/`DocCommentTag`, JSX tags `Keyword`, components (capitalised or dotted)
  `ClassName`, attributes `PropertyName`, entities `StringEscape`.
- **Versions** (`ScriptVersion`, `ScriptProjects`): per language `version` (latest) and `projectVersion` (true); read from
  `jsconfig.json`/`tsconfig.json` (`compilerOptions.target`, `extends` followed as a path or a package's config in
  `node_modules`, arrays too, 8 deep, cycles cut) and `node_modules/typescript/package.json`, else `devDependencies`/
  `dependencies`, as UTF-8 bytes with `Utf8JsonReader` (comments and trailing commas allowed, a BOM taken off), off the UI
  thread, once per folder (bounded cache of 4,096 folders). A file whose project was not read opens at once at the latest;
  the read switches its session through `ChangeDialect`, so `Reclassified` repaints it. An older version reads a newer word
  as a name.
- **The formatter** (`ScriptFormatter`, JS, TS, JSX, TSX): only white space between tokens changes; lines indented by brackets
  and JSX nesting (several brackets opened on a line indent the next once), a continued expression one step more; one space
  after keywords and around binary operators, none inside `()`/`[]`, one inside `{ }`, after `,` and `;`; trailing spaces
  off, at most one empty line kept; a template's text, a continued string and JSX text kept; JSDoc's ` * ` lines under their
  `/**`; no line broken unless `CRAFT_PLUGIN_TYPESCRIPT_FORMATLINEWIDTH`/`CRAFT_PLUGIN_JAVASCRIPT_FORMATLINEWIDTH` is set
  (then after commas, greedy and idempotent); a malformed file unchanged with "line, column: what" (a bracket, template,
  comment, string or regex not closed, `</span>` closing `<div>`); Format Selection through the kit; the caret kept.
- **Folding** (`ScriptFolding : IFoldingRangeProvider`): `{}` `[]` `()` over several lines, JSX elements (from `<` to the
  closing tag), multi-line templates, `//#region`/`/* #region */` to `#endregion` (Marked), block comments and runs of `//`
  lines (Comment), a run of imports (Imports). Inside JSX, `RulesAt` gives `{/* */}` (toggle comment wraps lines in it).
- **The plug-ins** (contract 1.1, pre-installed and removable, each carrying `Craft.Web` as a copied reference):
  `plugins/Craft.Plugins.JavaScript` ("javascript": .js .mjs .cjs .jsx; jsconfig.json stays JSON) and
  `plugins/Craft.Plugins.TypeScript` ("typescript": .ts .mts .cts .tsx .d.ts): the language (`ScriptLanguage`, a
  `LineStateLanguage`), folding, a project selector (`ScriptProjectSelector`), the icons (W-files' drawn shapes), and an
  editor part declaring the settings (`ScriptEditorPlugin`), Settings › Editor under "JavaScript" and "TypeScript":
  `CRAFT_PLUGIN_JAVASCRIPT_{VERSION,PROJECTVERSION,JSX,FORMATLINEWIDTH,STATECAPACITY,STATEDEPTH}` and
  `CRAFT_PLUGIN_TYPESCRIPT_{VERSION,PROJECTVERSION,FORMATLINEWIDTH,STATECAPACITY,STATEDEPTH}`, each also in `.craft/settings.json`.
  `ScriptRules`: `//` and `/* */`, `{}` `[]` `()`, strings `" ' \``.
- **Android and AOT**: `Craft.Web` (Script/) and both plug-ins are pure C#, no reflection, nothing native, `Utf8JsonReader`
  only (`IsAotCompatible`, trim-safe); the working stack is stack-allocated and bounded (at most 128 frames, rule 45).

**Decisions on the recommendation (the owner away; one line each).**
- One `ScriptTokenizer` per (dialect, version, table capacity, depth), shared by its sessions, each with its own `LineStates` table.
- A frame is 64 bits (kind, what continues, the token before, 255 braces with 24 kinds, 31 parens with 12 kinds); the lexer's stack holds the table's depth plus one, so a deeper stack reaches the table, gets its fallback and is counted in `Overflows`.
- A regex follows `)` of if/while/for/with, a block's `}`, an operator, `(` `,` `[` `;`, `=>`, return/typeof/case/yield/await/else/do; a name, a literal, `)` `]` and an object's `}` divide; `++`/`--` keep what was before them.
- A `{` is an object after an operator, a return-like keyword or import/export, else a block.
- JSX starts only where an expression may start and a name or `>` follows; `<T,>`, `<T extends U>` (not `extends=`) and `<const T>` are generics in .jsx and .tsx alike.
- Contextual words are read from what follows on the same line (`using x`, `accessor x`, `type X =`, `async *m`, `from "`, `x as T`); a predefined type is a keyword in a type's place (after `:` `|` `&` `<`) or when not followed by `=` `(` `:` `.`.
- The version gates words only (`satisfies` TS 4.9, `using` TS 5.2 / ES2026, `accessor` TS 4.9 / ESNext, `override` 4.3, `out` 4.7, `infer` 2.8, `keyof` 2.1, `unique` 2.7, `asserts` 3.7, `import type` 3.8, `export type *` 5.0, `import defer` TS 5.9 / ESNext (confirm), `import source` ESNext (confirm)); separators, BigInt, `#x` and regex flags lex alike in every version.
- In TypeScript the target gates nothing (TypeScript parses and downlevels); JavaScript reads jsconfig.json's target, else tsconfig.json's.
- TypeScript's version is the installed one (`node_modules/typescript`) before the one package.json asks for; the setting wins over both.
- `with` after a module's string is the import attributes' keyword (its `{` an object), not the old `with` statement.
- A regex's flags are their own `Keyword` span; `undefined` is a `Keyword` like `null`.
- Each session has its own formatter holding its dialect (.tsx vs .ts), switched with the session; `LineStateLanguage.Formatter` stays the default dialect's.
- `ScriptLanguage` re-implements `ILanguage.Open` to start the project read, so the kit needed no change.
- The selectors pick no language (the names do) but read a folder's project when it opens, so files opened after start in their dialect.
- `jsx` = auto|on|off for .js (auto: react or preact in dependencies, devDependencies or peerDependencies); .jsx and .tsx always, .ts never.
- The formatter keeps `<` `>` `>>` `>>>` `?` and the space before `:` as written (generics or comparisons), and JSX children's spaces.
- A continued expression indents one more step only inside braces (not inside `()`/`[]`, which already indent); `case x:` bodies indent by it.
- Line width breaks only after commas, greedily, measuring the formatted text, so a second format changes nothing; no line is ever joined.
- Folding hides through the closing line, as JSON's does; a comment run needs two `//` lines alone on their lines.
- Settings go to the Editor section grouped "JavaScript"/"TypeScript" (no new section constant, which another slot may add).
- The plug-ins register their icons over the library's with its drawn shapes (as JSON's), so removing one leaves the library's.
- In the tests Craft.Web is shared (a Craft.Web.dll sits beside the runner and the loader shares Craft.* assemblies the host has); in the app each plug-in loads its own copy.

**Tests**: new group `WebScript` (12): the latest JavaScript, TypeScript, JSX and TSX with expected spans; the regex-or-division
table (36 cases); templates nested across lines, 40 deep, and past the table's depth counted; 4,500 random edits (weighted to
`` ` `` `${ } / * < >`, quotes, line breaks) equal a fresh session in three dialects; 0 bytes over 200 warm 60-line screens; a
keystroke settles; the worst flip; the version gate and the parsers; the project read once and the session switched through
`Reclassified`, the setting's precedence, JSX by project; the formatter (expected output, idempotent on every fixture, only
white space changed, tabs and CRLF, seven malformed cases with where and what), its line width, the caret kept, one undo
step, Format Selection; folding (11 ranges of every kind); JSX's rules and the Angular stub. `WebPlugins` extended (2): both
plug-ins from their folders, their files, colours from the loaded copy, folding, selectors, settings and variables, icons,
removed and disabled; the settings through the host (`CRAFT_PLUGIN_JAVASCRIPT_JSX=on`, `CRAFT_PLUGIN_TYPESCRIPT_VERSION=4.8`,
the line width) and back to defaults when the host closes. `LookAndFeel` (+1, the fixture above). First run: 38 of 41 passed;
the three failures were the tests' expectations (a regex's flags and a comment's continued line are their own spans; in the
test host Craft.Web is shared, so the plug-in's language is told by its instance, not its assembly). Rerun: two real lexer
gaps found and fixed (a stack deeper than the table was cut off without reaching its `Overflows`; `async *m()` was a name);
then all passed, and the full filter
(`WebScript|WebKit|WebPlugins|WebFiles|WebStyle|ProjectLanguages|Plugins|Folding|BracketColors|Editor|Settings|LookAndFeel|ApiGuard|Inventory`):
172 of 172. API dumps: `api/Craft.Web.txt` (additions), `api/Craft.Plugins.JavaScript.txt` and `api/Craft.Plugins.TypeScript.txt`
(new); the inventory and the contract list unchanged (no contract added).

**Measured** (here, 4 cores, Release; `test-output/benches/web-script.txt`): a 100,000-line TypeScript file (5.8 MiB): the
first screen of 60 lines 0.12 ms (cold for the file; the lexer was already compiled by the group's earlier tests), the rest
of the file's states on first read 108 ms (off the screen's path), a warm screen of 60 lines 0.075 ms and 0 bytes (target
0.5 ms); a backtick typed on line 1 then the screen 0.09 ms, only the screen's 60 lines lexed and none below; a letter typed
in a name: its line, the states below settle at once. A 2,000-line TSX file: Format document 3.7 ms cold, 3.5 ms warm
(113 edits); folding 1.3 ms (336 ranges).

**Left, for later**: the semantic layer (W-semantic: completion, diagnostics, go to definition); re-reading a project when its
files change (W-npm's `WorkspaceChanged`); a contextual word whose partner is on the next line reads as a name; generics in a
JSX tag (`<Foo<T> />`) lex `<T>` plainly; the formatter neither joins lines nor breaks them anywhere but after commas (no
Prettier-style layout; Prettier waits for W-tools); the Angular dialect is the stub W-markup completes.

**The owner's check** on `test-output\CarftTestPOC`: add `scratch.ts`, `scratch.tsx` and `scratch.js` there (git ignores the
folder) with a template over several lines, a regex after `if (x)`, a JSX element; check their colours and icons, folding,
Ctrl+/ inside JSX (`{/* */}`), Shift+Alt+F (and a malformed file's message); Settings › Editor › JavaScript/TypeScript; type
a backtick at the top of a long .ts and watch the paint.
```powershell
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="WebScript|WebKit|WebPlugins|WebFiles|WebStyle|ProjectLanguages|Plugins|Folding|BracketColors|Editor|Settings|LookAndFeel|ApiGuard|Inventory"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
```

### W-tools as built (2026-11-01)

Slot w3, Craft only: Idrak needed nothing more (W-npm's `CommandLimits.Requester` and `Purpose` carry the lint plug-in's
questions); no cause was found in Idrak.

**Contracts** (`Craft.Abstractions`, additive, contract 1.1; the dumps and the contract list grew by lines only):
- (6) `IEditorHost.PublishDiagnostics(path, source, snapshot or null, items)` and `ClearDiagnostics(source)` (default: false,
  nothing); `FileDiagnostic` (lines and UTF-16 columns from 1, end exclusive; `In(snapshot)`, `Of(snapshot, diagnostic)`,
  `PositionIn`); `EditorDiagnostic.Source`. The host (`EditorHost.Diagnostics.cs`) gives each item the id "source(id)"
  ("eslint(no-unused-vars)"): an open document's lists live in `DocumentDiagnostics` (Craft.Editor, per document, shared by
  its editors, moved through every edit; a list published for an older text is moved through the edits since, 512 kept,
  else placed by line and column), a closed file's are kept by line and column (`ClosedProblemFiles`,
  `PublishedDiagnosticsChanged`) and go onto the text when it opens and back when it closes; a plug-in's diagnostics leave
  every file when it is deactivated. `EditorModel` joins the language's own with the published ones (both sorted, one
  merge, only once the published ones are in the current text's positions), so squiggles, the hover's problem text, the
  line-end message ("Missing semicolon.  eslint(semi)", cut per line and kept: a warm frame makes no string), the
  lightbulb, the counts and Problems take them, also for a file with no semantic session (a .ts today). `ProblemSource`
  takes a closed file (`ForClosedFile(path, lines)`: no snapshot, no text read); its rows open the file at their line and
  column, and Fix on such a row opens it first.
- (7) `ILinter : IKind` (`Title`, `Languages`, `Extensions`, `Unavailable`, `DetectAsync(path, root)` off the UI thread,
  `LintAsync(LintRequest)`), `LinterProject` (folder, config, version, problem, note, variables, program), `LintRequest`
  (the project, the files, the sandbox and limits its driver chose, a scratch folder), `LintResult`, `LintFileResult`,
  `LintFix` (UTF-16 range, title, its diagnostic, `Suggestion`), and the registry `Linters` (`ForFile` by extension).
- (8) `IDocumentFormatter : IKind` (`Formats`, `Cached`, `FitAsync`, `FormatAsync` returning the whole text; FormatException
  for a text it cannot format), `DocumentFormatterFit`, `DocumentFormatRequest` (tab size, spaces, `Automatic`), and
  `DocumentFormatters` (`Handles`, `ChooseAsync`, `Describe`). `EditorModel.FormatDocumentAsync` asks them first, turns the
  text into one edit per changed run of lines (`LineEdits.Between`) as one undo step, then falls back to the language's
  own; `CanFormat`, `FormatterDescription`; format on save passes `automatic: true`; the status bar's language tooltip
  says "Formatter: Prettier 3.8.1, the project's (.prettierrc)" (`ShellText.FormatterTooltip`).

**The lint plug-in** (`plugins/Craft.Plugins.Lint`, pre-installed and removable, package `Craft.Plugins.Lint` with the
plug-ins "lint" and "prettier", contract 1.1, capabilities files and sandbox; it carries a copy of the npm plug-in's
assembly for Node: `CRAFT_PLUGIN_NPM_NODE` honoured when the npm plug-in is there, else Node on PATH):
- **ESLint** ("eslint"): the nearest `eslint.config.{js,mjs,cjs,ts,mts,cts}` from the file up to the workspace's root
  first; only without one an `.eslintrc*` or package.json's `eslintConfig`, run on ESLint 9 with
  `ESLINT_USE_FLAT_CONFIG=false` (said once, with `npx @eslint/migrate-config`), refused on ESLint 10 (said, with the
  migration), as is on ESLint 8; the project's own `node_modules/eslint/bin/eslint.js` (hoisted folders up to the root),
  a global one beside Node only with `CRAFT_PLUGIN_LINT_ALLOWGLOBAL` (machine-only); its version read from its
  package.json as bytes. Run as `node eslint.js --format json --output-file <data folder>/lint/tmp/eslint-<id>.json
  [--no-warn-ignored] FILES` (or `.`) in the configuration's folder; exit 0 and 1 are results, 2 a failure said with its
  first lines; the report read as bytes (`LintJson.ReadEslint`, `Utf8JsonReader`: messages, `fix.range`, suggestions,
  fatal parse errors; `source` and the deprecated rules skipped unread) and deleted.
- **TSLint** ("tslint"): only `tslint.json`/`tslint.yaml` where ESLint found no configuration (registered after ESLint,
  the first that detects a project wins), `node tslint --format json --out REPORT --project tsconfig.json FILES`, exit 0
  and 2 are results; said deprecated once per workspace (with `npx tslint-to-eslint-config`).
- **Runs**: `linter` = auto|eslint|tslint|off, `runOn` = save|manual; saves of linted files (by extension, inside the
  workspace) within `delayMs` (500) are one run per project; a newer run cancels the older and takes its files; each run
  an activity "ESLint: 2 files" with Cancel, its time `timeLimitSeconds` (120), telemetry `lint.run`. What a run found is
  published per file (a clean file's empty list clears it; a requested file the report left out, and a whole project's
  file it no longer lists, cleared); a failure is a status entry per linter with "Lint workspace". Commands: "Lint: Lint
  this file", "Lint: Lint workspace" (every folder with a configuration, `ignore` never walked, each project linted whole:
  Problems lists the closed files), "Lint: Fix ESLint problems in file", "Lint: Set up ESLint", "Lint: Forget this
  workspace's approvals".
- **Fixes**: the code action provider "lint": for a problem at the caret its fix ("Fix: Missing semicolon. (semi)") and
  its suggestions ("Remove unused variable 'x'."), and "Fix ESLint problems in file" (every automatic fix in order, one
  that overlaps an earlier one left out, as one ESLint pass does); each a diff the user accepts, one undo step, offered
  only while the document's text is the one linted (a file linted while closed takes the text it opens with).
- **The sandbox and the approval**: a run the user asked for goes through `sandbox` (approve-each, asking each time, as
  "the lint plug-in"); a run on save asks once per workspace and program ("Let ESLint run on its own in this workspace?":
  Allow in this workspace, Not now), the approval written to the user's settings in the data folder as
  `CRAFT_PLUGIN_LINT_ALLOWED` = "folder|program;…" (machine-only: a workspace's file cannot allow itself), shown and
  removable on the settings page (Editor › Lint), or taken back by "Lint: Forget this workspace's approvals"; once
  allowed, that program's runs there go through `autoSandbox` (process). "Not now" holds for the window.
- **Set up ESLint**: a saved JS/TS file of a workspace with a package.json and no configuration shows a status entry once
  per workspace with "Set up ESLint", which runs `node npm-cli.js init @eslint/config@latest` in the workspace through the
  asking sandbox's interactive start, in the program tab "Lint: set up ESLint" (no time limit: it asks questions), its
  end said; the projects are read again after it.
- **Prettier** ("prettier", `PrettierFormatter`): the project's own `node_modules/prettier` (a global one only with
  `CRAFT_PLUGIN_PRETTIER_ALLOWGLOBAL`) and a Prettier configuration (`.prettierrc*`, `prettier.config.*`, or "prettier" in
  package.json; `requireConfig`, on) make it the formatter of `.js .mjs .cjs .jsx .ts .mts .cts .tsx .css .scss .less .html
  .htm .json .jsonc .json5 .md .markdown .yaml .yml` (`extensions`); found once per folder off the UI thread (when the
  active file changes), forgotten when the workspace changes or a configuration or package.json is saved; Craft's built-in
  formatters otherwise. A format writes the text to `<data folder>/lint/tmp` and runs Craft's runner script
  (`craft-prettier.cjs`, written there once) with Node: Prettier's own `getFileInfo` (.prettierignore), `resolveConfig`
  (with .editorconfig) and `format` for the file's path; through the same gate (Format Document asks through `sandbox`;
  format on save asks once per workspace; declined, the file is saved as it is).
- **Options** (each with its shape; Editor › Lint and Editor › Prettier): `CRAFT_PLUGIN_LINT_LINTER` (auto), `_RUNON`
  (save), `_DELAYMS` (500), `_ALLOWGLOBAL` (false, machine-only), `_TIMELIMITSECONDS` (120), `_SANDBOX` (approve-each,
  machine-only), `_AUTOSANDBOX` (process, machine-only), `_ALLOWED` (machine-only list), `_IGNORE`;
  `CRAFT_PLUGIN_PRETTIER_ENABLED` (true), `_REQUIRECONFIG` (true), `_ALLOWGLOBAL` (false, machine-only),
  `_TIMELIMITSECONDS` (30), `_EXTENSIONS`.
- **Android and AOT**: the plug-in is C# on `Utf8JsonReader` with no reflection; Node does not run on Android, so the
  linters and Prettier say Unavailable there (`LintNode.DeviceProblem`) and nothing runs.

**Decisions on the recommendation (the owner away; one line each).**
- Prettier lives inside the lint package as a second plug-in "prettier" (its own `CRAFT_PLUGIN_PRETTIER_*`), sharing the lint plug-in's sandboxes and approvals.
- The lint plug-in carries a copy of the npm plug-in's assembly (as each web plug-in carries Craft.Web) for one Node finder; it reads npm's settings only when that plug-in is registered.
- A published diagnostic's id is "source(id)" and it keeps `Source`, so every surface (Problems, hover, quick fix titles) shows it with no change of its own; the line-end message adds the id after the message.
- `IEditorHost.ClearDiagnostics(source)` was added beside `PublishDiagnostics` (a linter turned off, a workspace switched, a plug-in deactivated).
- Closed files' diagnostics are kept by line and column and placed only when the file opens: no file is read to list a problem.
- A linter returns the fixes its tool gives in the linted text's positions; the driver offers them only while the document's version is the one linted (no stale offsets), never by text search.
- "Fix … problems in file" applies one pass of non-overlapping automatic fixes from the report (no second `--fix` run); suggestions only one at a time.
- The linter is chosen per file by the first registered linter whose configuration is found (ESLint before TSLint); the lint plug-in drives any registered linter, a third party's too.
- ESLint runs in its configuration's folder with paths relative to it; `--no-warn-ignored` from ESLint 9 (a saved ignored file is not a warning).
- `.eslintrc` on ESLint 10 is refused with the migration command rather than guessed; on ESLint 8 a flat configuration gets `ESLINT_USE_FLAT_CONFIG=true`.
- The question's requester is "the lint plug-in" (a phrase, as W-npm's "the npm plug-in"), not the bare word "lint".
- Manual runs go through `sandbox` (approve-each asks); runs on save need the once-per-workspace approval and then go through `autoSandbox` (process); an allowed program's manual runs take the allowed path too.
- The approval is a machine-only plug-in setting (a list) written to the user's settings file in the data folder: shown and removable on the settings page, never the workspace's.
- "Not now", and an approval that could not be written, are kept for the process, shared by both plug-ins' gates.
- Lint reports and Prettier's text go through files in the data folder's `lint/tmp` (the sandboxes take no standard input), deleted after each run.
- A formatter returns the whole formatted text; the editor makes the line edits (so the caret on unchanged lines stays) and the undo step.
- Format on save with Prettier not allowed saves the file as it is (no built-in formatter in Prettier's place, which would fight the project's style).
- Lint as you type waits for W-semantic (as planned); only saved text is linted.

**Measured** (the 4-core Linux container, Release, the real test): the project's ESLint 10.1.0 linting a two-file project
whole ("Lint workspace", through the process sandbox, report read and published): 338 ms; Prettier 3.8.1 formatting one
file through Craft's runner: 176 ms (both cold: one Node start each).

**Tests**: new group `WebLint` (10): ESLint's and TSLint's JSON reports from fixtures (BOM, suggestions, a fatal parse
error, fixes of two replacements, severities, a cut report, package.json's version and keys); the detection matrix (flat
nearest, flat over a nearer .eslintrc, ESLint 9's legacy mode said, ESLint 10 refusing it, ESLint 8, no install said, a
global one only when allowed, TSLint deprecated and its command, the walk stopping at the root, the projects of Lint
workspace, the registry); published diagnostics merged per source with the language's, moved and dropped by edits, a list
for an older text moved, the line-end message's source, Problems for a closed file by line and column, onto the text when
opened and back when closed, ClearDiagnostics; a plug-in's diagnostics on a file with no language, gone with the plug-in;
a fix as one diff of two hunks and one undo step, the lightbulb's actions and suggestions, none on a changed text; two saves
joined, a newer run cancelling the older and taking its files; the approval asked once, Not now, a manual run through the
asking sandbox, remembered in the user's settings as folder|program, machine-only (a workspace's value passed over), not
asked again, revoked and asked again; Set up ESLint offered and run as `npm init @eslint/config@latest` through the
sandbox, TSLint said deprecated once; Prettier only with a configuration (package.json's key, .prettierrc, requireConfig,
read once per folder), chosen by the registry, Format Document through it as one undo step, the tooltip, format on save
declined; the plug-in from its own folder (its copy of the npm assembly, no shared contracts) and removed; real ESLint and
Prettier (linked into the test project from the machine's install or `CRAFT_TEST_ESLINT`/`CRAFT_TEST_PRETTIER`; skipped
with the reason without them; they ran here). First run of the filter
`WebLint|WebNpm|WebPlugins|ProblemsPanel|InlineDiagnostics|Lightbulb|Settings|ApiGuard|Inventory` (68): 62 passed; the six
were the API dumps and the inventory (regenerated), one wrong expectation of the detection test, one real bug (the lint and
Prettier plug-ins' gates kept separate in-window approvals, so an approval given through one was unknown to the other: now
shared), and `WebNpm.InstallIsAnActivityAndYarnPnpmAndBunAreSaid` (npm's own test, untouched here; it passed on its rerun:
a timing race between its install's own rescan and the test's). The rerun of the failed: 12 of 12. API dumps of
Craft.Abstractions and Craft.Editor, the new `api/Craft.Plugins.Lint.txt`, the contract list (1.1) and `plans/inventory.md`
(Linters, DocumentFormatters) regenerated, lines added only.

**Not run here** (the owner's): the squiggles, line-end messages, lightbulb and Problems rows of a linted file on WinUI; the
status bar's formatter tooltip; the approval and approve-each dialogs; "Set up ESLint" answering npm's questions in the
program tab and installing over the network; Windows' global ESLint layout beside node.exe.

**Left, for later**: lint as you type (W-semantic); a Problems row's Fix on a closed file opens it rather than fixing it;
ESLint's multi-pass `--fix` (one pass of the report's fixes is applied); a yarn or pnpm project's setup runs npm; the Run
view's problem parser does not read ESLint's or Angular's output (W-npm's line).

**The owner's commands:**
```powershell
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="WebLint|WebNpm|WebPlugins|ProblemsPanel|InlineDiagnostics|Lightbulb|Settings|ApiGuard|Inventory"; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$p="D:\Projects\Craft\test-output\LintTestPOC"; New-Item -ItemType Directory -Force $p | Out-Null; cd $p; npm init -y; npm i -D eslint prettier; Set-Content eslint.config.mjs 'export default [{ rules: { "no-unused-vars": "error", "semi": ["warn", "always"] } }];'; Set-Content .prettierrc '{ "semi": false, "singleQuote": true }'; Set-Content a.js "const x = 1`nconsole.log(`"hi`")"; cd D:\Projects\Craft
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\LintTestPOC
```
In the app: open a.js and save (Ctrl+S): the question "Let ESLint run on its own in this workspace?" (Allow in this
workspace), then the squiggles, "'x' is assigned a value but never used.  eslint(no-unused-vars)" at the line's end, the
lightbulb (its "Fix ESLint problems in file": a diff, Accept, then Ctrl+Z once); Ctrl+Shift+P "Lint: Lint workspace" with
a.js closed fills Problems; hover the status bar's language item: "Formatter: Prettier …, the project's (.prettierrc)";
Shift+Alt+F (approve-each asks for node; Allow once): quotes and semicolons by .prettierrc; Settings › Editor › Lint ›
"Allowed to run on their own" lists the folder (remove it: the next save asks again). In `test-output\WebTestPOC` (W's
Angular project, no ESLint): save a .ts, the status entry "Set up ESLint", run it and answer npm's questions in its tab.

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

## B11 as built (2026-11-01)

Slot s2. The Learning side view was a list of text rows ("Learn on this machine: off — nothing is recorded · click to
choose", "Episodes: 0 (0 B of 64 MiB)", …). It is now a view that says what learning does, shows where you are and
guides the first steps, with real controls. Idrak is unchanged (no cause was in the library).

- **A contract for views drawn from blocks** (Craft.Abstractions, additive, contract 1.0 kept): `IEditorViewPanel`
  (an `IEditorPanel` with `Blocks` and `ActAsync(actionId, value)`), the blocks `PanelCard` (tone, icon, badge),
  `PanelText` (body, secondary, caption, heading, code; a tone), `PanelSteps`/`PanelStep` (an illustration of steps, each
  next, current or done, with an SVG icon), `PanelChecklist`/`PanelCheck`, `PanelButtons`/`PanelButton` (primary,
  secondary, flat, danger; enabled, tooltip, value), `PanelSwitch`, `PanelMeter` (a fraction, or work without a known
  end), `PanelChart` (a sparkline with marked points and a legend), `PanelPicker`/`PanelOption`, `PanelInput` (numeric,
  unit, code font, its button), `PanelFacts`/`PanelFact`, `PanelDisclosure`; every block carries a tooltip and the
  variable that sets it. `ViewPanel` is the usual way to make one; `PanelBlocks` gives the blocks as lines (`Items`, for
  a host that draws only lines) and as text. Any plug-in can use it: the learning plug-in uses nothing a third party
  cannot.
- **What a plug-in reaches through its host** (default members, so older hosts and plug-ins are unaffected):
  `IEditorHost.ShowUndo(text, undo, kept)` (the shell's inline Undo bar; `kept` runs when the bar goes without an Undo:
  its time, ×, a newer bar, the window's end), `IEditorHost.PickFolderAsync` (the shell's folder dialog),
  `IPluginSettings.SetAsync(plugin, key, value)` (writes the setting as the settings page does: the user's file, else
  the workspace's; held at once, the file written off the UI thread; a refusal in words). `EditorHost` takes them as
  `UndoPresenter`, `FolderPicker` and `SettingWriter`, set by `ShellModel` (`StoreSettingAsync`); the plug-in scope
  forwards them. `UndoBar.Show` takes the `kept` callback.
- **The shell draws the blocks** (`Craft.Maui.PanelBlocksView`, from `PanelView.SetPalette`, two lines in `ShellPage`):
  cards in the design's form (raised fill, 10-unit radius, the border or the tone's colour), a card inside a card as a
  titled section (no box inside a box), the palette's buttons with their hover, pressed, focus and disabled states
  (`InteractiveStates`), `CraftInput` around fields, MAUI's `Switch` and `Picker` in the accent, the steps as circles with
  stroke icons joined by lines, the checklist's numbered badges (a check once done), meters and sparklines drawn on a
  `GraphicsView`; each block's tooltip with its variable, and the variable small under it as on the settings page. Only
  the top-level blocks whose signature changed are drawn again (`Craft.Editor.Shell.PanelBlockLayout`, headless: the
  signatures, which to redraw, a chart's points, a meter's width), so a training step redraws its card only; text typed
  in a field and an open disclosure stay across redraws.
- **The Learning view** (`Craft.Plugins.Learning.LearningView`, headless: `LearningViewState` → blocks; the session
  builds the state from what it already read, the folder and episode figures off the UI thread as before):
  - a header card "Learn from your edits" with the two sentences and Record → Train → Better suggestions, each step's
    state and figure (the episodes, the rounds, the adapter in use), and the phase as its badge (`LearningPhase`: off,
    recording, ready, training, trained);
  - an error card when a round failed: "Training failed at its step stage", the trainer's words (B7: what to change,
    the variables), what the stage does, Learning settings and Dismiss;
  - "Getting started" until the first round: 1 turn on recording (Turn on), 2 code with the AI (Open the chat), 3 train
    when there are `CRAFT_PLUGIN_LEARNING_MINEPISODES` episodes ("7 of 20 so far", Train now), and "What is recorded?"
    opening a sample episode exactly as the folder keeps it;
  - "Recording and sharing": the recording switch with the consent's words under it (on: its scope and date; off by the
    setting: says which variable), the sharing switch (no destination: asks for it inline, then the consent; on: Share
    now…), the folder in the code font with Open and Change… (the folder dialog);
  - "Storage": a meter of the bytes used of `capMb` (warning colour past 90%), the verdicts, the three newest episodes,
    Export, and Delete with Undo (moved aside at once to `episodes.jsonl.held`, the shell's bar "Deleted 12 episodes
    (34 KB)." with Undo; deleted for good when the bar goes; Undo puts them back before any recorded since; a host
    without the bar asks first);
  - "Training": the model picker (the chat's model first, then the provider's models with "loaded", each option saying
    what a round does with it under B7's rule: the chat's copy trained in place, or loaded by the provider), the device,
    weights and adapter's settings as facts, the method picker with its line (preference, practice tasks, both), Train
    now as the primary button in its state (waiting for episodes as a meter with the count; ready; training as a meter
    with the stage, step of steps, loss, time left, device and Cancel), the idle-training switch with its minutes; after
    rounds, the loss and the preference margin as sparklines over the rounds (promoted filled, kept back hollow) and the
    last round in words ("v3 was better (−1.20 → −0.40) and was promoted: it answers now" or "v4 was kept back …"), Roll back;
  - "Adapter": which answers (an adapter, or the base model), its age, how much better it scored on how many held-out
    pairs, its base model, Use base model / Use adapter (new: `AdapterVersions.UseBase`, `use-base.txt`; a new promotion
    clears it), Roll back, Delete a version…;
  - "Practice tasks from tests": the line "Turn failing tests into practice tasks the model is rewarded for solving.",
    the failing tests the last run found and the tasks written, Make tasks (the sandbox said, "each command asks you
    first" for approve-each), and "Mark Class.Method as a task" only while the caret is in a test (`TestAtCaret`: the
    nearest [Fact], [Theory], [Test], [TestMethod], [TestCase], [DataTestMethod], [SkippableFact] whose body the caret has
    not left, read from at most 8,192 characters before the caret through a pooled buffer, only when the caret's line or
    the text changed; a mark names the class and the method).
  - The Learning view has its own rail icon (a cap) through the plug-in's `RailItem`.
- **Decisions (each the recommended option, the owner asleep):**
  - A general contract (`IEditorViewPanel` and blocks in Craft.Abstractions) rather than a learning-only view in the shell: plug-ins first, any plug-in can draw such a view.
  - Delete with Undo through the shell's own bar (`IEditorHost.ShowUndo`) rather than a bar inside the panel: one Undo bar in the window.
  - The recording switch still asks the consent question with every detail (the stored yes keeps its text's version); a switch never says yes alone.
  - The model and when training runs are in the consent's words (R), so a change of them from the view asks the consent again at once with the new words (so recording never stops unseen); a folder change too.
  - Train now is enabled from `minEpisodes` episodes (any count for the tasks trainer); the command "Learning: train now" still trains on demand as before.
  - The view's Train now shows the outcome in place (no message box); the command keeps its message.
  - Getting started shows until the first round has run, then the view is the training's.
  - Change folder: what is kept from now on goes to the new folder; the old folder's data stays there (said), nothing is moved.
  - Use base model is a kept switch (`use-base.txt`), not a rollback: the adapter comes back with Use adapter; a new promotion answers at once.
  - The meter for work without a known end is a still third of the bar (no animation: reduced motion needs nothing).
- **Tests**: `Learning.TheViewSaysWhereLearningStandsFromOffToReadyAndTheChecklistActs` (off, recording with too few
  episodes, ready; the checklist's Open the chat and Turn on; a failed round's error card with its stage and no message
  box), `Learning.TheTrainingCardShowsProgressAndEachRoundPromotedOrKeptBack` (training with progress and Cancel; a round
  promoted and one kept back, the charts, the adapter card, Use base model), `Learning.DeleteTakesTheEpisodesAwayWithUndoAndTheBarsEndDeletesThem`,
  `Learning.ThePickerListsTheLoadedModelsAndAPickWritesTheSetting` (a fake provider's models and loaded copy, a model it
  loads joining the picker, a pick and the idle switch written, the consent asked again, a refused write said),
  `Learning.MarkTheTestAtTheCaretShowsOnlyWhileTheCaretIsInATest`, `Experience.AViewPanelsBlocksRedrawOnlyWhatChangedAndThePluginReachesTheShell`,
  `Inline.TheUndoBarKeepsTheChangeWhenItsTimeIsUp` (the kept callback). The new tests: 5 of 7 passed on the first run; the
  two failures (a block's line repeated its variable; a model pick changed the consent's words and recording stopped
  unseen) were fixed as above; then the full filter (`Learning`, `Plugins`, `PluginKit`, `PhaseP`, `Settings`, `Ui`, `Design`,
  `LookAndFeel`, `Experience`, `Inline`, `Shell`, `Layout`, `ApiGuard`, `Inventory`): 122 passed, 0 failed. API dumps regenerated: `Craft.Abstractions`, `Craft.Editor`, `Craft.Plugins.Learning`, and the
  contract list `api/Craft.Abstractions.contract.txt` (additions only).
- **Not compiled here**: nothing; no file under `Platforms/` changed, and `Craft.Maui` compiled on its net10.0 target.
  Not run here: the window (the view's look on both themes, the switches, pickers and the folder dialog on WinUI).

**Commands for the owner** (Craft only; Idrak unchanged):
```powershell
cd D:\Projects\Craft; git checkout main; git pull origin main
dotnet build -c Release tests\Craft.Tests
$env:CRAFT_FILTER="Learning.|Plugins.|PluginKit.|PhaseP.|Settings.|Ui.|Design.|LookAndFeel.|Experience.|Inline.|Shell.|Layout.|ApiGuard.|Inventory."; dotnet run -c Release --no-build --project tests\Craft.Tests -- --progress --failures-only --failures-file test-output\fails.txt; Remove-Item Env:CRAFT_FILTER
dotnet build -c Release src\Craft.App -f net10.0-windows10.0.19041.0
$env:CRAFT_CRASH_FOLDER="D:\Projects\Craft\test-output\crashes"; src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe D:\Projects\Craft\test-output\CarftTestPOC
```
In the app: the rail's cap opens Learning: the header's three steps, Getting started's checklist (Turn on asks the
consent), What is recorded?, the switches, Delete with Undo at the window's bottom, the model picker listing the
loaded model, Train now with its progress and Cancel, after a round the two sparklines and the adapter card; the view
on the light and dark themes.
