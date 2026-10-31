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
| `Craft.Ai` | The AI plug-ins on Idrak's contracts: inline completion, chat panel, code actions, agent, and the editor's tools. |
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
  status bar, never silently.
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

## Phases

Phases are a dependency order, not a barrier. At most three agents work at a time, in every phase of this plan, including S0 and A0 in Idrak (the owner's exception for Craft's plan; Idrak's own work keeps its limit of two). "Done" is what the owner can check.

| Phase | What | Done when | Needs |
|---|---|---|---|
| S0 | Idrak.Mcp served from a running app: `IMcpServerTransport` and `McpServerTransports` (`stdio`, `http`), `McpHost`, the CLI's `mcp serve` and `serve --mcp` on it with `--transport` and the variables, the `Mcp` test group, the API dump. | `IDRAK_FILTER=Mcp` passes on the CPU; the owner's two-window check (`mcp serve --transport http`, `chat --mcp http://127.0.0.1:PORT`) lists and calls a tool; the `http` overhead against `stdio` is written here. | the owner's go (first) |
| E0 | Craft skeleton: solution, `Directory.Build.props/targets` with Idrak's settings and the `IdrakFromSource` switch, `MauiVersion`, `Craft.Abstractions` with the registries, the inventory test, the API guard per package, the test runner, `Craft.Benches` empty. Windows and `net10.0` targets only. | `dotnet build` passes; the inventory and API-guard tests pass. | the owner's go |
| E1 | `Craft.Core`: piece table, snapshots, edits, undo and redo (grouped by typing pause), file open and save (encodings and line endings kept), autosave and recovery, workspace folder. | Random-edit equivalence against a plain string; a 100,000-line file opens and edits in the bench with memory ≈ text + pieces. | E0 |
| E2 | `Craft.CSharp`, syntactic layer: the snapshot as `SourceText`, the incremental parse, classified spans for a line range, Roslyn's classification names mapped to theme classes, light and dark themes; C# shipped as a pre-installed, removable plug-in loaded from `plugins/` (`Craft.Core.Plugins.CraftPlugins`, `CRAFT_PLUGIN_FOLDERS`, `CRAFT_PLUGINS_DISABLED`). | Spans of fixed C# 14 files (every string kind, comments, preprocessor lines, contextual keywords) match; spans after random edits equal a fresh parse; the snapshot-`SourceText` equals `SourceText.From` on random edits. | E1 |
| E3 | `Craft.Maui` editor control over `IEditorCanvas`: drawn surface, caret, selection, scrolling, gutter, bracket match, find and replace; Windows `ITextInputAdapter` (keyboard, mouse, IME, clipboard). **A minimal window in `Craft.App` opens one file**, so the owner runs the control before the shell exists. | Headless tests of layout, caret, selection and hit-testing pass; the owner opens, edits and saves a file on Windows. | E2 |
| E4 | `Craft.App` shell on Windows: folder tree, tabs, settings page (every setting above), recent files, light and dark, the narrow layout chosen by width. | The owner opens a folder, switches files, changes settings, resizes to the phone layout. | E3 |
| E5 | `Craft.CSharp`, semantic layer: `WorkspaceReaders` ("csproj", "loose"), semantic colours, diagnostics as squiggles and a list, completion, signature help, hover, go to definition, format document. | Diagnostics and completion on Craft's own solution; memory and cold start measured on Windows and written here. | E3 (parallel with E4) |
| A0 | Idrak: the FIM contract, registry and suite; the coder families already in `Idrak.Text.Plugins` register their formats; the reuse and cancellation bench, with fixes. | The bench shows prefix reuse, a stop within one token of cancellation and flat memory over a thousand requests. | the owner's go (parallel with E1-E2) |
| K | The plug-in contracts and kit ("Plug-ins first" above): `IEditorPlugin`, `IEditorHost`, commands and key bindings, status bar items, panels, plug-in settings, `plugin.json` and contract versioning, the sample plug-in, the template, `docs/plugins.md`. | The sample plug-in, built outside Craft's solution against `Craft.Abstractions` alone, dropped into `plugins/`, adds its command and status item; removed, the app runs without it. | E3, and the owner's first try of the Windows version; E4's shell is built on it |
| A1 | `Craft.Ai` host on the plug-in contracts (K): the diff-and-accept view, the editor's tools with both hosts, the app serving the live tools through `McpHost` ("http", a setting with address and token shown in Settings), `IModelProvider` "local". | A fake chat model proposes an edit that arrives as a diff and is accepted into one undo step; `idrak tools test` lists the tools from the built assembly; `idrak chat --mcp` against the running app reads an open document. | E4, K, A0, S0 |
| A2 | The plug-ins: inline completion, chat panel, code actions, agent (desktop). | On a real model the owner names: ghost text appears after the pause and dies on a keystroke; a chat reply applies as a diff; the agent runs a task with each command approved. | A1 |
| P | P1, P2, P3 and P5 first, then P4, P6, P9, P10, then P8 (P7 is built in phase V); P11 only if measured necessary. | Each plug-in's targeted tests and its conformance suite pass; the five checks hold on the Plug-ins page. | A1 (P2 also A0) |
| V | Voice coding on Idrak.Audio ("Voice coding" above): capture, live transcription, dictation, voice commands, spoken code as a diff, read back; P7 is its first part. | A recorded WAV dictates the expected text, runs a command and asks for code that arrives as a diff; the owner codes by voice on Windows; the latency written here. | A2 (the chat model and the diff view), K |
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
