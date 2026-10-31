// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Vision;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(VisionPackage))]

namespace Craft.Plugins.Vision;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Vision\.</summary>
public sealed class VisionPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.Vision";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new VisionRegistration()];
}

/// <summary>Registers the editor plug-in "vision", its commands and their keys, each with how to take it back.</summary>
public sealed class VisionRegistration() : IdrakPlugin(VisionSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorPlugins.Register(VisionPlugin.Instance), () => EditorPlugins.Unregister(VisionSettings.PluginName));
        foreach (var command in VisionPlugin.Commands)
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
        }

        foreach (var binding in VisionPlugin.Bindings)
        {
            Part(() => EditorKeyBindings.Register(binding), () => EditorKeyBindings.Unregister(binding.Chord));
        }

        Part(() => EditorMenus.Register(new MenuItem(EditorMenus.Plugins, VisionPlugin.ScreenshotCommand)), () => EditorMenus.Unregister(EditorMenus.Plugins, VisionPlugin.ScreenshotCommand));
    }
}

/// <summary>The plug-in's settings (plug-in "vision", variables <c>CRAFT_PLUGIN_VISION_&lt;KEY&gt;</c>).</summary>
public sealed record VisionSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "vision";

    /// <summary>The prompt "screenshot to code" sends with the picture (<c>prompt</c>); "{language}" is the active document's language.</summary>
    public string Prompt { get; init; } = DefaultPrompt;

    /// <summary>The conversation panel the pictures go to (<c>chatPanel</c>): the AI plug-in's chat, "ai.chat".</summary>
    public string ChatPanel { get; init; } = "ai.chat";

    /// <summary>The Idrak plug-in packages with the vision-language families, registered at activation (<c>idrakPlugins</c>).</summary>
    public IReadOnlyList<string> IdrakPlugins { get; init; } = ["Idrak.Vision.Plugins"];

    /// <summary>The prompt's default.</summary>
    public const string DefaultPrompt = "Write the {language} code for what this picture shows (a user interface: its layout, controls and text). "
                                        + "Give the code in one fenced block, then say in one line what you could not read from the picture.";

    /// <summary>The settings it declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("prompt", DefaultPrompt, "What 'Screenshot to code' asks with the picture; {language} is the active document's language."),
        new("chatPanel", "ai.chat", "The conversation panel pictures go to (the AI plug-in's chat)."),
        new("idrakPlugins", "Idrak.Vision.Plugins", "The Idrak plug-in packages with the vision-language families, registered when the plug-in starts (';' between them)."),
    ];

    /// <summary>The settings from <paramref name="settings"/>.</summary>
    public static VisionSettings From(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = new VisionSettings();
        string? Text(string key) => settings.Get(PluginName, key) is { } text && text.Trim() is { Length: > 0 } trimmed ? trimmed : null;
        return new VisionSettings
        {
            Prompt = Text("prompt") ?? defaults.Prompt,
            ChatPanel = Text("chatPanel") ?? defaults.ChatPanel,
            IdrakPlugins = settings.Get(PluginName, "idrakPlugins") is { } list
                ? list.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : defaults.IdrakPlugins,
        };
    }
}

/// <summary>
/// The editor plug-in "vision": when it activates, the Idrak libraries in its folder and the vision-language packages of
/// the setting idrakPlugins register (Idrak's <c>VisionFamilies</c>; once per process, taken back when it deactivates), and
/// its commands put pictures in the chat.
/// </summary>
public sealed class VisionPlugin : IEditorPlugin
{
    /// <summary>The clipboard's picture into the chat.</summary>
    public const string PasteCommand = "vision.pasteImage";

    /// <summary>A picked picture into the chat.</summary>
    public const string AttachCommand = "vision.attachImage";

    /// <summary>The clipboard's picture (else a picked one) into the chat, sent with the prompt.</summary>
    public const string ScreenshotCommand = "vision.screenshotToCode";

    private static readonly Lock Gate = new();
    private static IReadOnlyList<IIdrakPluginPackage>? s_registered;
    private static int s_active;

    private VisionPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static VisionPlugin Instance { get; } = new();

    /// <summary>Its commands.</summary>
    public static IReadOnlyList<IEditorCommand> Commands { get; } =
    [
        new EditorCommand(PasteCommand, "Chat: Attach the clipboard's picture", (host, token) => AttachAsync(host, EditorImageSource.Clipboard, token)),
        new EditorCommand(AttachCommand, "Chat: Attach a picture…", (host, token) => AttachAsync(host, EditorImageSource.File, token)),
        new EditorCommand(ScreenshotCommand, "Chat: Screenshot to code", (host, token) => ScreenshotToCodeAsync(host, token)),
    ];

    /// <summary>Its keys: Ctrl+Alt+V pastes the clipboard's picture into the chat, Ctrl+Alt+Shift+S sends a screenshot to code.</summary>
    public static IReadOnlyList<KeyBinding> Bindings { get; } =
    [
        new(KeyChord.Parse("Ctrl+Alt+V"), PasteCommand),
        new(KeyChord.Parse("Ctrl+Alt+Shift+S"), ScreenshotCommand),
    ];

    /// <inheritdoc />
    public string Name => VisionSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "Photo or screenshot to code: pictures into the chat (pasted, dropped, picked) as image parts; vision-language families for the local models";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => VisionSettings.Declared;

    /// <summary>The Idrak plug-in packages it registered (the vision families'), while it is active.</summary>
    public static IReadOnlyList<IIdrakPluginPackage> Registered
    {
        get
        {
            lock (Gate)
            {
                return s_registered ?? [];
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Include(VisionSettings.From(host.Settings));
        return new Deactivation();
    }

    /// <summary>
    /// Registers the Idrak libraries of the plug-in's folder (their built-ins: image codecs, formats) and the packages of
    /// <paramref name="settings"/> (the families), once while any host has the plug-in active. A package that is not in the
    /// folder fails, naming it.
    /// </summary>
    /// <exception cref="FileNotFoundException">A package of the setting is not there.</exception>
    internal static void Include(VisionSettings settings)
    {
        lock (Gate)
        {
            // A package that is not there throws before the count: a failed activation leaves nothing to take back.
            s_registered ??= PluginIdrakLibraries.IncludePackages(typeof(VisionPlugin).Assembly, settings.IdrakPlugins);
            s_active++;
        }
    }

    // The last active host gone: the families it registered are taken back.
    private static void Exclude()
    {
        lock (Gate)
        {
            if (--s_active > 0 || s_registered is not { } registered)
            {
                return;
            }

            foreach (var package in registered)
            {
                IdrakPlugins.UnregisterAll(package);
            }

            s_registered = null;
        }
    }

    /// <summary>A picture from <paramref name="source"/> into the chat panel of the settings; false (said so) when there is none or it is refused.</summary>
    public static async Task<bool> AttachAsync(IEditorHost host, EditorImageSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        var settings = VisionSettings.From(host.Settings);
        if (Chat(host, settings) is not { } panel)
        {
            await host.ShowMessageAsync("Chat", NoChat(settings)).ConfigureAwait(true);
            return false;
        }

        if (await host.ReadImageAsync(source, cancellationToken).ConfigureAwait(true) is not { } image)
        {
            return false;                                                             // none, or cancelled (the host says which)
        }

        if (panel.AttachImage(image) is { } refusal)
        {
            await host.ShowMessageAsync("Picture not attached", refusal).ConfigureAwait(true);
            return false;
        }

        host.ShowPanel(panel.Id);
        return true;
    }

    /// <summary>
    /// The clipboard's picture (a screenshot just taken), else one the user picks, into the chat, sent with the prompt of
    /// the settings; false (said so) when there is no picture, no chat, or the chat refuses it.
    /// </summary>
    public static async Task<bool> ScreenshotToCodeAsync(IEditorHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        var settings = VisionSettings.From(host.Settings);
        if (Chat(host, settings) is not { } panel)
        {
            await host.ShowMessageAsync("Screenshot to code", NoChat(settings)).ConfigureAwait(true);
            return false;
        }

        var image = await host.ReadImageAsync(EditorImageSource.Clipboard, cancellationToken).ConfigureAwait(true)
                    ?? await host.ReadImageAsync(EditorImageSource.File, cancellationToken).ConfigureAwait(true);
        if (image is null)
        {
            return false;
        }

        if (panel.AttachImage(image) is { } refusal)
        {
            await host.ShowMessageAsync("Screenshot to code", refusal).ConfigureAwait(true);
            return false;
        }

        string language = host.ActiveDocument is { LanguageName: { Length: > 0 } name } && name != "plaintext" ? Language(name) : "C#";
        host.ShowPanel(panel.Id);
        await panel.SubmitAsync(settings.Prompt.Replace("{language}", language, StringComparison.Ordinal), cancellationToken).ConfigureAwait(true);
        return true;
    }

    private static IConversationPanel? Chat(IEditorHost host, VisionSettings settings) => host.FindPanel(settings.ChatPanel) as IConversationPanel;

    private static string NoChat(VisionSettings settings) =>
        $"No conversation panel '{settings.ChatPanel}' is shown (CRAFT_PLUGIN_VISION_CHATPANEL): the AI plug-in (Craft.Ai) brings the chat; it is removed or disabled.";

    private static string Language(string name) => name.ToLowerInvariant() switch
    {
        "csharp" => "C#",
        "fsharp" => "F#",
        "javascript" => "JavaScript",
        "typescript" => "TypeScript",
        "html" => "HTML",
        "css" => "CSS",
        "xml" => "XAML or XML",
        _ => name,
    };

    private sealed class Deactivation : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                Exclude();
            }
        }
    }
}
