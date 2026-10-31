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
| `CraftContracts.Version` | The contracts' version (now 1.0). |

Everything a plug-in is called with runs on the UI thread, and every host event is raised there. Work that takes time
runs elsewhere and comes back with `host.Dispatch`.

**No silent edits.** A plug-in never edits a document itself: `ProposeEditsAsync(document, edits, title)` shows the
user a summary and applies the edits as one undo step only if the user accepts. A document that changed meanwhile is not
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
