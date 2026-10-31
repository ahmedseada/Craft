# Plan 1: Craft, a code editor (C# first) with AI plug-ins on Idrak's contracts

**Status:** planned 2026-10-31 against Idrak `main` d01a3b1 and Craft `main` e68f88b. Nothing is built. The owner's
answers (2026-10-31): the editor lives in Craft; .NET MAUI with Craft's own drawn editor control; Windows built and
run first, with every project compiling for Android from E0 (the Android runs are phase M); Roslyn from the start; the
plug-ins of phase P test Idrak's plug-in points. Next: E0, when the owner says.

## Why

The owner's request (October 2026): a code editor, mainly for C# for now, with syntax highlighting, that runs on the
desktop and on a phone, and whose AI features are plug-ins built on Idrak's contracts.

There are two parts. **The editor** is the buffer, the highlighting, the editing surface and the app shell. **The AI
plug-ins** are completion, chat, code actions and the agent. The editor never names a model, a family or a vendor: it
talks to `IChatModel`, `ITextModel`, `ITool` and `IToolRegistry`, so any model Idrak loads (local, on the device, or
served) drives it.

## What exists today (surveyed 2026-10-31)

- **Craft** (`ahmedseada/Craft`) is empty: one LICENSE, one commit. It is the editor's home (the owner's answer).
- **Idrak.Maui** (plan 34) is a MAUI library with Android and Windows targets. It provides `UseIdrak`, the options as
  saved settings, `IMobileFeature` in `MobileFeatures` (a `SlotTable`), models kept loaded and released on memory
  pressure, a model store with pull progress, device information and light and dark themes. An editor on MAUI reuses
  all of it.
- **Chat and tools** (`Idrak.Abstraction.Generation`):
  - `IChatModel.StreamAsync` streams `ChatChunk`s, including tool calls.
  - `ITextModel.StreamAsync` streams `GenerationChunk`s.
  - `ITool`, `IToolRegistry` and `IToolProvider` (with `[assembly: ToolProvider]`) cover tools.
  - `IToolChatModel` runs tool loops.
- **The coding agent** (`Idrak.Text`):
  - `CodingAgent(IChatModel, AgentOptions)` and `RunAsync(prompt, CodingTools)`.
  - `CodingTools(workspace, options, sandbox)` reads, edits, searches and runs commands in a sandbox
    (`CommandSandboxes`).
  - `ICodingLanguage` is registered in `CodingLanguages`.
- **KV reuse**: `TextGenerator.KeepCache` (default true) keeps the cache between generations, so a completion request
  whose prompt shares its start with the previous one can skip that start. This has not been measured on an editor's
  pattern (one request per pause in typing, each cancelled by the next keystroke).
- **Missing in Idrak**:
  - No fill-in-the-middle (FIM) contract: a model completing between a prefix and a suffix needs its family's FIM
    tokens.
  - No `IChatModel` over a remote OpenAI-compatible endpoint, which a phone too small for a coder model would use to
    reach a desktop's `idrak serve`.

## The shape

### Projects (in Craft, referencing Idrak)

| Project | What it holds |
|---|---|
| `Craft.Abstractions` | The editor's contracts and their registries (`SlotTable` / `KindTable`). Reference it alone to write a plug-in. |
| `Craft.Core` | Text buffer, documents, undo, workspace, the classification layers, themes, the plug-in host. No UI, `net10.0`, AOT-compatible, tested on any machine. |
| `Craft.CSharp` | C# on Roslyn (`Microsoft.CodeAnalysis.CSharp.Workspaces` and `.Features`): syntactic and semantic classification, diagnostics, completion, signature help, hover, go to definition, formatting. It is registered as the "csharp" language, on both the desktop and the phone. |
| `Craft.Ai` | The AI plug-ins on Idrak's contracts: inline completion, chat panel, code actions, agent, and the editor's tools. |
| `Craft.Maui` | The editor control and the pages: drawn surface, input adapters per platform, and features registered into Idrak.Maui. |
| `Craft.App` | The app: Windows first, Android compiled alongside from E0 and run in phase M. It makes the registrations and hosts the models. |
| `Craft.Plugins.*` | The plug-ins of phase P, each its own assembly (see "Plug-ins that test Idrak's plug-in points"). |
| `Craft.Tests` | Targeted groups (`CRAFT_FILTER`), with the same runner conventions as Idrak's tests. |

Idrak comes in as packages. A build switch points at a local Idrak clone instead, as `apps/Directory.Build.targets`
already does, so a library fix is tested in the editor before a release.

### Editor contracts (`Craft.Abstractions`)

| Contract | Registry | What it is |
|---|---|---|
| `ITextBuffer` / `ITextSnapshot` | n/a | A piece table with a line index. A snapshot is immutable and read as `ReadOnlySpan<char>` by line or range. An edit is a `TextEdit(start, length, text)` list applied atomically, giving a new version. |
| `ILanguage : IKind` | `EditorLanguages` | A language: file patterns, comment and bracket rules, and its language service. "csharp" is registered by `Craft.CSharp`. A language that is not registered fails with "not registered". |
| `ILanguageService` | (from the language) | Two layers: a syntactic one (classified spans of a line range, from an incremental parse, fast enough for the keystroke) and a semantic one (async and off the UI thread: semantic spans, diagnostics, completion, signature help, hover, definition, formatting). Every async method can be cancelled. |
| `ITheme : IKind` | `EditorThemes` | Token class to colour and style. Light and dark built in, following Idrak.Maui's themes. |
| `IEditorPlugin : IKind` | `EditorPlugins` | A plug-in: what it needs (a chat model, a text model, the workspace, a network) and `Activate(IEditorHost)`. |
| `IEditorHost` | n/a | What a plug-in reaches: documents, selection, `ApplyEdits` (always shown as a diff for the user to accept), diagnostics, commands, panels, ghost text, settings, progress and telemetry. |
| `IInlineCompletionProvider` | `CompletionProviders` | Ghost text at the caret, streamed and cancelled by the next keystroke. |
| `ICodeActionProvider` | `CodeActionProviders` | Actions over a selection or a diagnostic (explain, fix, refactor, document, write tests), returning edits. |
| `IModelProvider : IKind` | `ModelProviders` | How the plug-ins' `IChatModel` and `ITextModel` are reached: "local" (a model name resolved by Idrak's `ModelSources`, loaded once by Idrak's `ModelHost`) or "remote" (an endpoint, phase A3). It never resolves model names itself. |

Model names are always resolved by Idrak's `ModelSources` (an existing folder, `store:name`, a `.gguf` file, a Hugging
Face id), the same list the `idrak` tool uses, so a name that works in the CLI works in the editor. Craft adds no name
registry of its own; a new kind of name (such as `craft:`, plug-in P10) is a source registered into `ModelSources`.

Each registry is a `SlotTable` with library defaults and app overrides. Craft gets its own inventory test and its own
public API guard (`api/Craft.txt`), as Idrak has.

### AI plug-ins (`Craft.Ai`), each only on Idrak's contracts

- **Inline completion.** Prefix and suffix around the caret, laid out by the model's FIM format (phase A0), streamed
  from `ITextModel`. It starts after a pause in typing (a setting) and is cancelled at the next keystroke. The layout
  puts the prefix first so `KeepCache` reuses everything up to the caret. A model with no FIM format falls back to
  prefix-only completion, said so in the status bar, never silently.
- **Chat panel.** `IChatModel` with the open document, the selection, Roslyn's diagnostics and the symbols in scope as
  context, and the editor's
  tools (below) through `IToolRegistry`. Replies stream with tokens per second shown. Code blocks have "apply", which
  goes through the diff view.
- **Code actions.** Explain, fix this diagnostic, refactor, add doc comments, write a test. Each is an
  `ICodeActionProvider` whose edits go through the diff view.
- **Agent.** Idrak's `CodingAgent` and `CodingTools` over the open folder, in Idrak's sandbox. Every edit it makes is
  shown as a diff and its progress (turn, tool, tokens) is live. It needs a shell, so it is desktop-first; on a phone
  it is offered only when a sandbox is registered there.
- **The editor's tools** (`IToolProvider`):
  - `open_documents`, `read_document`, `get_selection`, `get_diagnostics`, `find_symbol`;
  - `propose_edit`, which the user approves.

  Any Idrak chat model, and any MCP client through `Idrak.Mcp`, can then use the editor.

### Idrak library gaps (in Idrak, on `main`, phase A0)

- **FIM as a contract.** `IFillInMiddleFormat` in `Idrak.Abstraction.Generation`, registered in `FillInMiddleFormats`
  (a `SlotTable`), with prefix-suffix-middle and suffix-prefix-middle layouts.
  - The library has no family format. Each family's format (its FIM tokens, read from the tokenizer's special tokens)
    is registered by its plug-in in `Idrak.Text.Plugins`.
  - A model whose family registers none gets "not registered".
- **Measured reuse.** A bench of the editor's pattern: one request per pause, each cancelled by the next, with the
  prefix growing by a few tokens. It measures cold and warm separately (rule 75) and checks:
  - that `KeepCache` reuses the shared prefix;
  - that a cancelled generation stops at the next token and frees what it holds;
  - that device memory does not grow across a thousand requests (rule 76).

  Anything slow is fixed in the library.
- **A remote `IChatModel` / `ITextModel`** over an OpenAI-compatible endpoint (`/v1/chat/completions`,
  `/v1/completions`). It is generic, so it would belong in `Idrak.Abstraction` or `Idrak.Text`. It is held until the
  owner says whether the phone should reach a desktop's model (A3).

### Windows first, Android-ready code

Windows is built and run first. The code never shuts out Android:

- Every project builds for `net10.0-android` from E0, beside `net10.0` and the Windows target, as Idrak.Maui does. A
  build that breaks the Android target is a broken build.
- Platform code lives only under `Platforms/Windows` and `Platforms/Android`, behind Craft's contracts. The text input
  (`ITextInputAdapter`: keys, composition, selection handles, the clipboard), the file picker and the window or screen
  size are contracts, with the Windows implementations built in E3 and the Android ones in M.
- The layout is chosen from the measured window width, not the platform, so a narrow Windows window already shows the
  phone layout. That layout is built and checked on Windows before M.
- Nothing calls a Windows-only API (Win32, WinUI types) outside `Platforms/Windows`, and the agent's need for a shell
  is a registered `CommandSandboxes` entry, not an assumption.
- Every library added must be AOT- and trim-checked for Android when it is added, not in M.

### Speed and memory (docs/optimization.md applies to every phase)

- The buffer is a piece table with a line index. A keystroke costs the edit plus the re-lex of the lines it changed,
  never the file.
- Only visible lines are laid out and drawn. Glyph runs are cached per line and version, and lines are read as spans,
  never as one string per line on the hot path.
- Roslyn on the keystroke: the edit is a `TextChange` on Roslyn's `SourceText` (`WithChanges`), so the syntax tree is
  re-parsed incrementally, never from scratch. Only the visible lines are classified, and the classified spans are
  copied into a pooled array returned in `finally`.
  - Until a line's spans arrive it is drawn in the default colour, so a frame never waits for them.
  - If the measured keystroke-to-paint shows Roslyn's syntactic pass too slow on a device, a hand-written incremental
    lexer for the visible lines is the candidate (rule 71: its spans proven equal to Roslyn's).
- Semantic work (Roslyn's compilation, diagnostics, completion) and AI work run off the UI thread, debounced and
  cancellable. The UI never waits on them.
  - One `AdhocWorkspace` per open folder, and its compilation's references are loaded once.
  - The memory Roslyn holds is reported in the editor's telemetry and released on the phone's memory-pressure signal
    (Idrak.Maui's).
- Roslyn on Android: its MEF composition (`MefHostServices`) uses reflection, so Roslyn's assemblies are kept whole by
  the trimmer (`TrimmerRootAssembly`). The APK size, the cold start (first classification, first completion) and the
  warm costs are measured with full AOT and with profiled AOT (`IdrakAndroidAot`), and the numbers are written into
  this plan (phase M).
- The benches measure, card-agnostic and cold and warm separately:
  - keystroke to paint;
  - scrolling a 100,000-line file;
  - re-classifying after an edit that opens a block comment or a raw string (the classification of every following
    visible line changes);
  - the first and warm completion list, and the first and warm diagnostics;
  - completion latency, first token and per token.

  The results go under `test-output\`.
- Every threshold is a setting with a measured or sensible default: the debounce, the completion's token limit, the
  context size sent, the device, the model, the font, the tab size, the theme. That includes the AI's context budget,
  which is measured from the model's context length, not fixed.

## Plug-ins that test Idrak's plug-in points (phase P)

Each plug-in is a real feature of the editor and, at the same time, a test of one of Idrak's registries. Together they
check five things:

1. **A new kind:** a name added to a registry works end to end.
2. **An override:** an app registration shadows a library default, the slot's guard reports it, and `Unregister`
   brings the default back.
3. **A missing name:** an unregistered name fails with "not registered"; nothing falls back silently.
4. **Loaded from outside:** a plug-in assembly dropped into a folder loads through `IdrakPlugins.Load` (what the CLI's
   `-P` uses), with no rebuild of the editor.
5. **One assembly, every host:** the same plug-in works in the editor, the `idrak` CLI and `idrak mcp serve`.

| # | Plug-in | Idrak contract | Checks |
|---|---|---|---|
| P1 | **Editor tools**: `read_document`, `get_selection`, `get_diagnostics`, `find_symbol`, `propose_edit` (approved by the user) | `IToolProvider`, `[assembly: ToolProvider]` | 5: listed by `idrak tools list`, run by `idrak tools test`, served by `idrak mcp serve` |
| P2 | **A coder model family** not yet in `Idrak.Text.Plugins`, with its fill-in-the-middle format | `IIdrakPlugin` / `IdrakPlugins`, the FIM registry (A0) | 4 and 3: loaded from a folder; without it, completion says "not registered" |
| P3 | **Bracket-balanced completion sampler**: masks tokens that close a brace or a string the prefix never opened | `TokenSamplers` (over the built-in default) | 2 on the hottest path: its state through a generation, its cost in tokens per second |
| P4 | **C#-aware edit placement**: places a model's "replace this snippet" draft by Roslyn tokens, ignoring whitespace | `TextMatchers` | 1 and 2 against the library's `SemiGlobal` |
| P5 | **Approve-each-command sandbox**: the agent's commands run in the editor's terminal panel, each approved | `CommandSandboxes` | 1 and 3: where none is registered (a phone), the agent is refused loudly |
| P6 | **Code search**: Roslyn-shaped chunks (type, member), camelCase-split identifiers, keyword index plus embeddings | `KeywordIndexes`, `PretrainedEncoders` / `SequencePoolers` | 1 in retrieval; feeds the chat panel's context |
| P7 | **Voice dictation and commands** ("go to line 40", "rename this") | `SpeechFamilies`, `AudioCaptures` | another domain (audio) through the same editor host |
| P8 | **Photo or screenshot to code** | `VisionFamilies`, `IChatModel.PartKinds` with "image" | image parts through the chat contract; a model without "image" refuses, never ignores the picture |
| P9 | **Status-bar progress and telemetry**: model pull and load progress, tokens per second, device memory | `ProgressReporters`, telemetry | 1 on a registry without a guard |
| P10 | **Workspace model names**: `craft:` names resolved from the project's `.craft/model.json` | `ModelSources` | 1 on model name resolution |

P1, P2, P3 and P5 come first: together they cover all five checks. Where `Idrak.Abstraction.Testing` has a
conformance check for the contract (the sampler for P3), the plug-in runs it.

On Android, whether an assembly loaded at run time works under the app's ahead-of-time setting (`IdrakAndroidAot`), and
whether trimming removes what a plug-in needs, is measured in M. If it does not work, Android plug-ins are compiled into
the app and registered through `RegisterAll()`, and loading from a folder stays a desktop feature.

## Phases

Phases are a dependency order, not a barrier. At most two agents work at a time.

| Phase | What | Needs |
|---|---|---|
| E0 | Craft skeleton: solution, `Directory.Build.props` with Idrak's settings (net10.0, nullable, warnings as errors), the Idrak package reference and its local-clone switch, `Craft.Abstractions` with the registries, the inventory test and the API guard. | answers |
| E1 | `Craft.Core`: piece table, snapshots, edits, undo and redo (grouped by typing pause), file open and save (encodings, line endings kept), workspace folder. Tests on fixed and random edit sequences against a plain string model. | E0 |
| E2 | `Craft.CSharp` on Roslyn, the syntactic layer: `SourceText` kept in step with the buffer, the incremental parse, classified spans for a line range, Roslyn's classification names mapped to theme classes, and the light and dark themes. Tests prove the spans of fixed C# 14 files (strings of every kind, comments, preprocessor lines, contextual keywords) and that the spans after random edits equal those of a fresh parse. | E1 |
| E3 | `Craft.Maui` editor control: drawn surface, caret, selection, scrolling, gutter with line numbers, bracket match. Keyboard, mouse and IME on Windows behind `ITextInputAdapter`. Find and replace. The Android target compiles. | E2 |
| E4 | `Craft.App` shell on Windows: folder tree, tabs, settings page (every setting above), recent files, light and dark, and the narrow (phone) layout chosen by width. | E3 |
| E5 | `Craft.CSharp` on Roslyn, the semantic layer: a workspace per folder (`.csproj` read for its sources and references, loose files as a script-like project), semantic colours over the syntactic ones, diagnostics as squiggles and in a list, the completion list, signature help, hover, go to definition, format document. Memory and cold start measured on Windows. | E3 (parallel with E4) |
| A0 | Idrak: the FIM contract and registry, the formats of the coder families already supported (in `Idrak.Text.Plugins`), the reuse and cancellation bench, and fixes for what it finds. | answers (parallel with E1-E2) |
| A1 | `Craft.Ai` host: `IEditorPlugin`, `IEditorHost`, the diff-and-accept view, the editor's tools as an `IToolProvider`, `IModelProvider` with the "local" provider (names through `ModelSources`, models through `ModelHost` and Idrak.Maui's model store). | E4, A0 |
| A2 | The plug-ins: inline completion, chat panel, code actions, agent (desktop). | A1 |
| P | The plug-ins above: P1, P2, P3 and P5 first, then P4, P6, P9, P10, then P7 and P8. Each with its targeted tests and, where it applies, the conformance check. | A1 (P2 also A0) |
| M | Android: the `ITextInputAdapter` for the soft keyboard, IME composition and touch selection handles; the file picker; Roslyn on Android (APK size, cold start, memory, full and profiled AOT); loading plug-ins at run time under AOT, or compiled in. | E5, A2 |
| A3 | Held: the "remote" model provider (a desktop's `idrak serve` from the phone), iOS and macOS. | the owner |

## Commands for the owner (once a phase is built)

These are given per phase with the exact project names. The shape follows `CLAUDE.md`: one pull, one build, then the
targeted runs with `--no-build`, outputs under `test-output\`.

## The owner's answers (2026-10-31)

1. **Where:** the Craft repository, referencing Idrak as packages, with a switch to a local clone.
2. **UI stack:** .NET MAUI with Craft's own drawn editor control. Avalonia and a Monaco web view were weighed and set
   aside.
3. **Platforms first:** Windows built and run first; the code compiles for Android from E0 and the Android runs are
   phase M; iOS and macOS are held (A3).
4. **C# intelligence:** Roslyn from the start, on both platforms. There is no hand-written lexer unless a measurement
   asks for one (Speed and memory, above).
5. **Plug-ins:** the phase P set, to test Idrak's plug-in points.
6. **Model names:** resolved by Idrak's `ModelSources`; Craft's own contract (`IModelProvider`) only chooses local or
   remote.
