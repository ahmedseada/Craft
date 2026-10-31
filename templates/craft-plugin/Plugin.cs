using System.Globalization;
using Craft.Abstractions;
using Craft.Plugins.Template;
using Idrak.Abstraction.Plugins;

// How Craft finds the plug-in when it loads the folder: the assembly names its package.
[assembly: IdrakPluginPackage(typeof(TemplatePluginPackage))]

namespace Craft.Plugins.Template;

/// <summary>The assembly's plug-in package: what Craft registers when it loads the folder.</summary>
public sealed class TemplatePluginPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.Template";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new TemplateRegistration()];
}

/// <summary>Registers the plug-in's parts (and takes each back when it is removed or disabled).</summary>
public sealed class TemplateRegistration() : IdrakPlugin(TemplateEditorPlugin.PluginName)
{
    /// <summary>Ctrl+Alt+H runs the command; the editor does not use it, so the binding does not override the editor.</summary>
    public static KeyBinding Binding { get; } = new(new KeyChord(EditorKey.H, KeyModifiers.Control | KeyModifiers.Alt), TemplateEditorPlugin.HelloId);

    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorPlugins.Register(new TemplateEditorPlugin()), () => EditorPlugins.Unregister(TemplateEditorPlugin.PluginName));
        Part(() => EditorCommands.Register(TemplateEditorPlugin.Hello), () => EditorCommands.Unregister(TemplateEditorPlugin.HelloId));
        Part(() => EditorKeyBindings.Register(Binding), () => EditorKeyBindings.Unregister(Binding.Chord));
    }
}

/// <summary>The editor plug-in: a status item with the caret's position, and a command that greets.</summary>
public sealed class TemplateEditorPlugin : IEditorPlugin
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "templateplugin";

    /// <summary>The command's id.</summary>
    public const string HelloId = PluginName + ".hello";

    /// <summary>The greeting setting (CRAFT_PLUGIN_TEMPLATEPLUGIN_GREETING).</summary>
    public const string GreetingKey = "greeting";

    /// <summary>The command: shows the greeting and the active document's name.</summary>
    public static IEditorCommand Hello { get; } = new EditorCommand(HelloId, "templateplugin: Say hello",
        static (host, _) =>
        {
            var greeting = host.Settings.Get(PluginName, GreetingKey);
            var name = host.ActiveDocument?.FilePath is { } path ? Path.GetFileName(path) : "no file";
            return host.ShowMessageAsync("templateplugin", $"{greeting} ({name})");
        });

    /// <inheritdoc />
    public string Name => PluginName;

    /// <inheritdoc />
    public string Summary => "A status item and the command " + HelloId;

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings { get; } = [new PluginSetting(GreetingKey, "Hello from templateplugin", "What the command says.")];

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new Session(host);
    }

    /// <summary>The plug-in in one host. Everything it adds through the host is taken back when it is disposed.</summary>
    private sealed class Session : IDisposable
    {
        private readonly IEditorHost _host;
        private readonly StatusItem _item = new(PluginName + ".caret", "", StatusAlignment.Right, priority: 0, commandId: HelloId);
        private readonly IDisposable _shown;

        public Session(IEditorHost host)
        {
            _host = host;
            host.SelectionChanged += OnSelectionChanged;
            host.ActiveDocumentChanged += OnActiveChanged;
            _shown = host.AddStatusItem(_item);
            Show();
        }

        public void Dispose()
        {
            _host.SelectionChanged -= OnSelectionChanged;
            _host.ActiveDocumentChanged -= OnActiveChanged;
            _shown.Dispose();
        }

        private void OnSelectionChanged(object? sender, EditorDocumentEventArgs e) => Show();

        private void OnActiveChanged(object? sender, EventArgs e) => Show();

        private void Show() => _item.Text = _host.ActiveDocument is null ? ""
            : string.Create(CultureInfo.InvariantCulture, $"caret {_host.Selection.Caret}");
    }
}
