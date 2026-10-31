# Writing a Craft plug-in

A plug-in is a .NET assembly in a folder of its own that Craft loads at start-up. It adds to the editor through public
contracts in `Craft.Abstractions` only (and `Idrak.Abstraction` through it): commands, key bindings, status bar items,
panels, settings, languages and themes. Craft's own C# support is a plug-in on the same contracts.

## The contracts

| Contract | What it is |
|---|---|
| `IEditorPlugin` (registry `EditorPlugins`) | The plug-in: a name, a summary, the settings it declares, and `Activate(IEditorHost)`, which returns an `IDisposable` that deactivates it. |
| `IEditorHost` | What the plug-in reaches: the open documents (`IEditorDocument`: its `ITextDocument`, file, language, selection), the active one, events (opened, changed, saved, closed, active changed, selection changed), `ProposeEditsAsync`, commands, status items, panels, settings, progress, log and telemetry, and `Dispatch` to the UI thread. |
| `IEditorCommand` (registry `EditorCommands`) | A command: its id (`Name`, "myplugin.hello"), `Title` for the palette, `CanExecute`, `ExecuteAsync`. `EditorCommand` builds one from delegates. |
| `KeyBinding` (registry `EditorKeyBindings`) | A chord (`KeyChord`, "Ctrl+Shift+W") that runs a command. |
| `IStatusItem` / `StatusItem` | A status bar item: text, tooltip, end of the bar, priority, a command a click runs. |
| `IEditorPanel` / `ListPanel` | A panel in the side area: a title and lines (`PanelItem`), each of which may run a command. |
| `PluginSetting` | A setting the plug-in declares: key, default, description. |
| `MenuItem` (registry `EditorMenus`) | An item of a title-bar menu (File, Edit, View, Run, Plug-ins, Help, or a menu of its own): the menu, the command it runs, its group and order. It shows while its command is registered, with the command's chord. |
| `RailItem` (registry `EditorRailItems`) | An icon of the activity rail: its title, the command it runs, its icon as SVG path data on a 24 by 24 box (`RailIcons` has the design's), its order, whether it sits at the foot. It shows while its command is registered. |
| `ILanguageSession.ScopeAt` | The named scopes (types, members) around a position, for the breadcrumb; none by default. |
| `IModelProvider.Status` / `StatusChanged` | What a provider holds (where it runs, the models by role, the device and what it measured, the settings the Models page edits): the title bar's model chip, the status bar and the Models and devices page show it. |
| `IModelProvider` (registry `ModelProviders`) | How a plug-in reaches a model: `ModelProviders.Get("local").AcquireAsync(new ModelRequest(name))` gives a `ModelLease` with Idrak's `IChatModel` and `ITextModel` and the context window ("local" comes with the pre-installed AI plug-in, Craft.Ai). |
| `TextDiff` | A line diff of edits or texts, as the diff view shows it, or a unified patch. |
| `IInlineCompletionProvider` (registry `InlineCompletionProviders`) | Ghost text at the caret: the editor asks after the provider's `DelayMilliseconds` of no typing, on the thread pool, streams the `InlineCompletionPiece`s it yields, cancels the request at the next keystroke (stop within a token of the cancellation), and inserts the text only when the user accepts (Tab: all, one undo step; Ctrl+Right: a word; Escape dismisses). `CRAFT_INLINE_COMPLETION_PROVIDER` names one (empty: the first that `Accepts` the document; `none`: off). |
| `ICodeActionProvider` (registry `CodeActionProviders`) | Code actions for a `CodeActionContext` (a document, a range, its diagnostics): each `CodeAction` has a title, a kind (`CodeActionKinds`: fix, explain, refactor, document, tests) and `RunAsync`. The Problems row shows the fix and explain ones as buttons; edits still go through `ProposeAsync`. `CodeActionProviders.ActionsFor(context)` gathers every provider's. |
| `IConversationPanel` | A panel with a conversation and an input (the chat column, a sheet in the narrow layout): `ConversationEntry`s by `ConversationRole` (user, assistant, tool row, code block with `PanelAction`s and Copy, note, error, approval card, section, changed file), `ContextChip`s, a status line, header buttons and `SubmitAsync`. Add it with `AddPanel`; raise `ConversationChanged` with `EntryUpdated` for one streamed entry. The AI plug-in's chat and agent run are two. |
| `IEditorHost.OpenDocumentAsync` | The open document of a file, opening it in the editor when needed (a path with no file yet opens a new document): for a proposal to a closed file. |
| `IEditorToolSet` (registry `EditorToolSets`) | Tools for models (Idrak `ITool`s) over the running editor (`Live(host)`) and over a folder (`Folder(path)`). The pre-installed tools plug-in registers "editor"; `EditorToolSets.Live(host)` joins every set's tools by name, the newest set first, so a plug-in adds tools (or shadows one of the editor's) by registering a set. What chat, the agent and the editor's MCP server give a model. |
| `IEditorHost.AskAsync(EditorQuestion)` | Asks the user a question with answers to pick (the approve-each sandbox's card: the command, its facts, Allow once / Allow for this run / Deny); -1 when the host cannot ask, which a plug-in treats as no. |
| `ISemanticLanguageSession.FindSymbolsAsync`, `ILanguage.FindSymbolsAsync` | Declared symbols by name (`SymbolLocation`: name, kind, container, file, line, column, signature), from an open document's session (its unsaved text included) or over a folder; `IEditorDocument.SemanticSession` gives a document's session. The tools' `find_symbol`. |
| `CraftContracts.Version` | The contracts' version (now 1.0). |

Everything a plug-in is called with runs on the UI thread, and every host event is raised there. Work that takes time
runs elsewhere and comes back with `host.Dispatch`.

**No silent edits.** A plug-in never edits a document itself: `ProposeEditsAsync(document, edits, title)` (or
`ProposeAsync(document, new ProposedEdits(edits, title) { Placement = ..., Origin = ... })`) shows the user the change as
a diff, hunk by hunk, and applies the hunks the user accepts as one undo step. A document that changed meanwhile is not
edited, and a host with no way to ask applies nothing.

**What the host takes back.** The `IEditorHost` a plug-in gets is its own view of the host: on deactivation, the host
also removes every handler, status item, panel and progress the plug-in left behind. A plug-in whose `Activate` throws is
reported on the Plug-ins page and skipped; a handler that throws is logged and the editor goes on.

## Registering

The assembly names its Idrak plug-in package; the package's plug-in registers the parts in `RegisterParts`, each with
how to take it back, so a removed or disabled plug-in leaves nothing:

```csharp
[assembly: IdrakPluginPackage(typeof(MyPackage))]

public sealed class MyPackage : IIdrakPluginPackage
{
    public string Name => "MyPlugin";
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new MyRegistration()];
}

public sealed class MyRegistration() : IdrakPlugin("myplugin")
{
    protected override void RegisterParts()
    {
        Part(() => EditorPlugins.Register(new MyEditorPlugin()), () => EditorPlugins.Unregister("myplugin"));
        Part(() => EditorCommands.Register(MyEditorPlugin.Hello), () => EditorCommands.Unregister("myplugin.hello"));
        Part(() => EditorKeyBindings.Register(MyEditorPlugin.Binding), () => EditorKeyBindings.Unregister(MyEditorPlugin.Binding.Chord));
    }
}
```

The sample, `samples/Craft.Plugins.Sample`, is a complete one: a word count in the status bar, kept up to date from the
lines each change touched (never the whole text on a keystroke), and the command `sample.wordCount` on Ctrl+Shift+W.

## Menus and the rail

The title bar's menus and the activity rail are registries of commands, so a plug-in adds to them as the app does:

```csharp
Part(() => EditorMenus.Register(new MenuItem(EditorMenus.Plugins, "myplugin.hello")), () => EditorMenus.Unregister(EditorMenus.Plugins, "myplugin.hello"));
Part(() => EditorRailItems.Register(new RailItem("myplugin.view", "My view", "myplugin.show", RailIcons.SourceControl, Order: 20)),
    () => EditorRailItems.Unregister("myplugin.view"));
```

An item shows only while its command is registered: the app's own rail item for the agent waits for the agent's
command (phase A2), and source control has no item until a plug-in brings one. A menu item's title is the command's
title without its "Area: " prefix, unless it gives its own; its chord is the command's key binding.

## Key bindings

A key goes, in this order, to:

1. the user's binding (`CRAFT_KEY_BINDINGS`), which always wins;
2. a plug-in's binding made with `OverridesEditor: true`;
3. the editor's own keys (arrows, Ctrl+C, Ctrl+Z, Ctrl+F and the rest);
4. a plug-in's binding for a chord the editor did not handle.

So the built-in keys keep working, and plug-ins add new chords. The last registration of a chord wins.
`CRAFT_KEY_BINDINGS` takes `Chord=command` entries separated by `;`: `Ctrl+Shift+W=sample.wordCount;Ctrl+K=`. An empty
command unbinds a plug-in's chord (the editor's own key stays). In bindings written as text
(`KeyBinding.ParseList`), a `!` before the command (`Ctrl+D=!myplugin.duplicate`) sets `OverridesEditor`; a user's
binding comes first either way. Keys: the letters, `D0` to `D9` (or the digit), `F1` to
`F12`, and the editor's keys (`Enter`, `Tab`, `Escape`, the arrows); modifiers `Ctrl`, `Shift`, `Alt`.

## Settings

A plug-in declares its settings (`IEditorPlugin.Settings`) and reads them with `host.Settings.Get("myplugin", "key")`.
Each one's value comes from the variable `CRAFT_PLUGIN_<PLUGIN>_<KEY>` (upper-cased; any character that is not a letter or
a digit becomes `_`): from the environment, else the open folder's `.craft/settings.json`, else the user's
`settings.json` in the app's data folder (both written by the settings page, Ctrl+Comma), else its default: `sample`'s `showCharacters` is
`CRAFT_PLUGIN_SAMPLE_SHOWCHARACTERS`. A key the plug-in did not declare is an error naming the declared ones. The
settings page lists them (`host.Settings.All`) with Craft's own.

## The folder and plugin.json

A plug-in is a folder named after its assembly:

```
plugins/
  MyPlugin/
    MyPlugin.dll
    MyPlugin.deps.json
    plugin.json
    (its own dependencies)
```

It never carries `Craft.Abstractions.dll` or `Idrak.Abstraction.dll`: those come from the host, so both see the same
types. Its project references `Craft.Abstractions` with `Private=false` and `ExcludeAssets=runtime`, sets
`EnableDynamicLoading`, and removes `Idrak.Abstraction` from the copied files (the template does all three).

`plugin.json`:

```json
{
  "name": "MyPlugin",
  "version": "1.0.0",
  "summary": "What it brings, in one line",
  "contractVersion": "1.0",
  "capabilities": ["files", "network"]
}
```

`name` matches the folder; `version` is the plug-in's own; `contractVersion` is the `CraftContracts.Version` it was built
against; `capabilities` (any of `files`, `network`, `sandbox`, `model`) are what it asks for: shown on the Plug-ins page
now, enforced later. The file is checked before any of the plug-in's code loads: a file that does not parse, or names a
wrong field, refuses the plug-in with every reason. A folder without `plugin.json` still loads, marked as having none.

## Versions

- A plug-in built against a **newer** contract version than the running Craft's (a newer major, or a newer minor of the
  same major, whose additions this Craft lacks) is refused with the reason, never loaded half-way.
- One built against an **older major** is loaded and marked outdated on the Plug-ins page.
- An older minor of the same major is current: a minor only adds.

## Installing

- **Pre-installed** plug-ins ship in `plugins/` beside `Craft.App.exe` (C# is `plugins/Craft.CSharp/`). Deleting a folder
  removes the plug-in; the app still starts.
- **User folders** are off until the user names one: `CRAFT_PLUGIN_FOLDERS` takes folders separated by `;`, each holding
  plug-in folders, loaded after the app's own. A plug-in is code; only name folders you trust.
- **Disabling**: `CRAFT_PLUGINS_DISABLED` takes folder names separated by `,` (`Craft.CSharp,MyPlugin`), pre-installed
  ones included.

The Plug-ins button lists every folder found, with its version, summary, and whether it loaded, was refused, is outdated
or disabled. Ctrl+Shift+P (or the Commands button) lists the commands and runs the one picked.

## Idrak's libraries in a plug-in

A plug-in may carry Idrak's libraries in its folder (Idrak, Idrak.Text, …: a model family needs Idrak's
`PretrainedArchitectures`, a text matcher Idrak.Text's `TextMatchers`). Craft loads each Idrak library once per process,
from the first plug-in folder that needs it, and hands that copy to every other plug-in: a library's registries are its
statics, so a family one plug-in registers is the one the "local" model provider of another reads. `Idrak.Abstraction`
is always the host's. Other dependencies (Roslyn, say) stay each plug-in's own.

## Craft's own plug-ins of phase P

Each is a folder under `plugins/` in this repository (`plugins/<Name>/<Name>.csproj` and its `plugin.json`), built and
copied by Craft.App to `plugins\<Name>\` beside the program, pre-installed and removable like C#. They use nothing a
third party cannot.

| Folder | What it adds | Registry | Settings |
|---|---|---|---|
| `Craft.Plugins.Tools` (P1) | The editor's tools: `open_documents`, `read_document`, `get_selection`, `get_diagnostics`, `search_workspace`, `find_symbol`, `propose_edit` (live; a diff the user accepts) and `list_files`, `read_document`, `search_workspace`, `find_symbol`, `propose_edit` (a folder; a patch file) | `EditorToolSets` "editor"; `[assembly: ToolProvider]` for the idrak tool | `CRAFT_PLUGIN_TOOLS_*`: `readLines`, `searchResults`, `symbolResults`, `searchFileKb`, `ignore`, `patchFolder`, `toolsFolder` |
| `Craft.Plugins.StarCoder2` (P2) | The StarCoder2 family (BigCode): `Starcoder2ForCausalLM` for the "local" provider, and the fill-in-the-middle format "starcoder2" for inline completion | Idrak's `PretrainedArchitectures`, `FillInMiddleFormats` | none |
| `Craft.Plugins.Sampler` (P3) | Bracket-balanced completion: on a fill-in-the-middle completion, tokens that would close a brace, parenthesis or bracket the prefix never opened are masked (brackets in strings are text) | Idrak's `TokenSamplers` "default", over the library's (which it wraps, and which `Unregister` brings back) | `CRAFT_PLUGIN_SAMPLER_MODE`: `fim` (default), `always`, `off` |
| `Craft.Plugins.Placement` (P4) | C#-aware placement of a model's snippet: by Roslyn's tokens, whitespace and comments ignored ("placed by csharp-tokens · exact / whitespace differed"), else the library's semi-global | Idrak.Text's `TextMatchers` "csharp-tokens" (and "semi-global" with the setting) | `CRAFT_PLUGIN_PLACEMENT_REPLACESEMIGLOBAL` (false) |
| `Craft.Plugins.Sandbox` (P5) | The approve-each command sandbox: every command an agent asks to run is shown to you first (program, time limit, network, folder; Allow once, Allow PROGRAM for this run, Deny and tell the agent), then runs in the inner sandbox with its output live in the panel "Terminal (sandbox)"; denied, the agent gets a refusal; with no editor to ask, every command is refused | Idrak's `CommandSandboxes` "approve-each" (the agent names it: `CodingToolOptions.Sandbox`, `IDRAK_SANDBOX`) | `CRAFT_PLUGIN_SANDBOX_*`: `inner` (process), `runGapMinutes` (15), `panelLines` (400); commands `sandbox.endRun`, `sandbox.clear` |

The tools also work outside the editor, from the same dll (the folder backing over `CRAFT_PLUGIN_TOOLS_TOOLSFOLDER`, else
the current folder):

```powershell
dotnet run -c Release --project D:\Projects\Idrak\src\Idrak.Cli -- tools test D:\Projects\Craft\plugins\Craft.Plugins.Tools\bin\Release\net10.0\Craft.Plugins.Tools.dll
dotnet run -c Release --project D:\Projects\Idrak\src\Idrak.Cli -- mcp serve D:\Projects\Craft\plugins\Craft.Plugins.Tools\bin\Release\net10.0\Craft.Plugins.Tools.dll
```

## The template

```powershell
dotnet new install D:\Projects\Craft\templates\craft-plugin
cd D:\Projects
dotnet new craft-plugin -n MyPlugin --craftPath D:\Projects\Craft --pluginName myplugin
dotnet build -c Release MyPlugin
```

It makes a project like the sample: the csproj, the package, a plug-in with a status item, a command on Ctrl+Alt+H, a
setting, and `plugin.json`. `--craftPath` is the Craft repository whose `src/Craft.Abstractions` it builds against
(absolute, or relative to the new project's folder; the default `../Craft` is a sibling of the project); a build can
override it with `-p:CraftPath=FOLDER`. `--pluginName` is the editor plug-in's short name (commands' prefix, settings'
variables). `dotnet new uninstall D:\Projects\Craft\templates\craft-plugin` removes the template.

## Debugging against the app

1. Build the plug-in, then copy its output folder (`bin\Release\net10.0\`) to a folder named after it under a plug-in
   folder, for example `D:\Projects\CraftPlugins\MyPlugin\`.
2. Point Craft at it and start the app:
   ```powershell
   $env:CRAFT_PLUGIN_FOLDERS = "D:\Projects\CraftPlugins"
   D:\Projects\Craft\src\Craft.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Craft.App.exe
   ```
3. Attach the debugger to `Craft.App.exe` (Visual Studio: Debug, Attach to Process); breakpoints in the plug-in's code
   bind once its folder loaded (its `.pdb` beside the dll).
4. The Plug-ins button shows why a plug-in did not load or start. A loaded assembly stays loaded (and, on Windows,
   locked) until the app closes: close the app before copying a new build.
