// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// A chat's or an agent session's model settings, opened from its model chip (the owner, 2026-11-01, "2a"): the sampling
/// (temperature, top-k, top-p, min-p, repeat penalty, seed, most tokens: no reload) and the load-time settings (weights,
/// device, context, offload) and the draft, each showing the shared value beside it with a Reset; Save keeps them with the
/// session only (a load-time change asked first with its measured memory), Apply to the environment writes them as the
/// shared settings (the user's or the workspace's), Reset all. Drawn from blocks (Craft's sliders and drop-downs).
/// Headless: the tests read the blocks and act; called on the UI thread.
/// </summary>
public sealed class ModelSessionPanel : IEditorViewPanel, IDisposable
{
    /// <summary>The panel's id.</summary>
    public const string PanelId = "craft.modelSession";

#pragma warning disable CS1591 // The action ids, named as they act.
    public const string SetPrefix = "ms.set.";
    public const string ResetPrefix = "ms.reset.";
    public const string ResetAll = "ms.resetAll";
    public const string Save = "ms.save";
    public const string Apply = "ms.apply";
    public const string Scope = "ms.scope";
    public const string Close = "ms.close";
#pragma warning restore CS1591

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private readonly IModelSessionHost _session;
    private readonly bool _workspace;
    private readonly Action _close;
    private IReadOnlyList<PanelBlock> _blocks = [];
    private IReadOnlyList<PanelItem>? _items;
    private ModelSettingsScope _scope = ModelSettingsScope.User;
    private bool _busy;

    /// <summary>The panel for <paramref name="session"/>; <paramref name="workspaceOpen"/> offers the workspace's settings to apply to; <paramref name="close"/> closes it.</summary>
    public ModelSessionPanel(IModelSessionHost session, bool workspaceOpen, Action close)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _workspace = workspaceOpen;
        _close = close ?? throw new ArgumentNullException(nameof(close));
        Draft = session.ModelSession;
        _session.ModelSessionChanged += OnSessionChanged;
        Publish();
    }

    /// <inheritdoc />
    public string Id => PanelId;

    /// <inheritdoc />
    public string Title => $"Model settings · {_session.ModelSessionTitle}";

    /// <summary>The settings as edited now (Save keeps them, Apply writes them).</summary>
    public ModelSessionSettings Draft { get; private set; }

    /// <summary>What the last Save or Apply said, or null.</summary>
    public string? Note { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<PanelBlock> Blocks => _blocks;

    /// <inheritdoc />
    public IReadOnlyList<PanelItem> Items => _items ??= PanelBlocks.AsItems(_blocks);

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        if (actionId.StartsWith(SetPrefix, StringComparison.Ordinal))
        {
            Draft = Set(Draft, actionId[SetPrefix.Length..], value);
            Note = null;
            Publish();
            return;
        }

        if (actionId.StartsWith(ResetPrefix, StringComparison.Ordinal))
        {
            Draft = Set(Draft, actionId[ResetPrefix.Length..], null);
            Note = null;
            Publish();
            return;
        }

        switch (actionId)
        {
            case ResetAll:
                Draft = ModelSessionSettings.None;
                Note = "Every field takes the shared setting again: Save to keep that for " + _session.ModelSessionTitle + ".";
                Publish();
                break;
            case Scope:
                _scope = value == "workspace" ? ModelSettingsScope.Workspace : ModelSettingsScope.User;
                Publish();
                break;
            case Save when !_busy:
                _busy = true;
                Publish();
                try
                {
                    bool saved = await _session.SaveModelSessionAsync(Draft, cancellationToken).ConfigureAwait(true);
                    Draft = _session.ModelSession;
                    Note = !saved ? "Not saved: nothing changed." : Draft.IsEmpty ? $"{Capital(_session.ModelSessionTitle)} takes the shared settings."
                        : $"Saved for {_session.ModelSessionTitle} only; the shared settings are unchanged.";
                }
                finally
                {
                    _busy = false;
                    Publish();
                }

                break;
            case Apply when !_busy:
                _busy = true;
                Publish();
                try
                {
                    Note = await _session.ApplyModelSessionToEnvironmentAsync(Draft, _scope, cancellationToken).ConfigureAwait(true);
                    Draft = _session.ModelSession;
                }
                finally
                {
                    _busy = false;
                    Publish();
                }

                break;
            case Close:
                _close();
                break;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _session.ModelSessionChanged -= OnSessionChanged;

    /// <summary>
    /// <paramref name="settings"/> with the field <paramref name="field"/> ("temperature", "topK", "topP", "minP",
    /// "repeatPenalty", "seed", "maxTokens", "context", "weights", "device", "offload", "draft") set from
    /// <paramref name="value"/> (invariant culture); null or empty takes it back to the shared setting. A value that does
    /// not parse changes nothing.
    /// </summary>
    public static ModelSessionSettings Set(ModelSessionSettings settings, string field, string? value)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(field);
        string? text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        float? Real(float? old) => text is null ? null : float.TryParse(text, NumberStyles.Float, Culture, out float number) ? number : old;
        int? Whole(int? old) => text is null ? null : double.TryParse(text, NumberStyles.Float, Culture, out double number) ? (int)Math.Round(number) : old;
        return field switch
        {
            "temperature" => settings with { Temperature = Real(settings.Temperature) },
            "topK" => settings with { TopK = Whole(settings.TopK) },
            "topP" => settings with { TopP = Real(settings.TopP) },
            "minP" => settings with { MinP = Real(settings.MinP) },
            "repeatPenalty" => settings with { RepeatPenalty = Real(settings.RepeatPenalty) },
            "seed" => settings with { Seed = Whole(settings.Seed) },
            "maxTokens" => settings with { MaxTokens = Whole(settings.MaxTokens) },
            "context" => settings with { Context = Whole(settings.Context) },
            "weights" => settings with { Weights = text },
            "device" => settings with { Device = text },
            "offload" => settings with { Offload = text is null ? null : text is "on" or "true" },
            "draft" => settings with { Draft = text },
            _ => settings,
        };
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!_busy)
        {
            Publish();
        }
    }

    private void Publish()
    {
        _blocks = Build();
        _items = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<PanelBlock> Build()
    {
        var shared = _session.ModelShared;
        var draft = Draft;
        var blocks = new List<PanelBlock>
        {
            new PanelText(Title, PanelTextStyle.Heading),
            new PanelText($"{(shared.Model.Length == 0 ? "No model is set" : shared.Model)} · each field is {_session.ModelSessionTitle}'s own once moved; Reset takes the shared setting back.", PanelTextStyle.Caption),
            new PanelText("Sampling · the next request takes it, no reload", PanelTextStyle.Secondary),
            Slider("temperature", "Temperature", draft.Temperature, shared.Temperature, 0, 2, 0.05),
            Slider("topK", "Top-k (0: all)", draft.TopK, shared.TopK, 0, 200, 1),
            Slider("topP", "Top-p", draft.TopP, shared.TopP, 0, 1, 0.01),
            Slider("minP", "Min-p", draft.MinP, shared.MinP, 0, 1, 0.01),
            Slider("repeatPenalty", "Repeat penalty", draft.RepeatPenalty, shared.RepeatPenalty, 1, 2, 0.01),
            Slider("maxTokens", "Most tokens a reply", draft.MaxTokens, shared.MaxTokens, 16, 32768, 16, "tokens"),
            new PanelInput(SetPrefix + "seed", "Seed", draft.Seed?.ToString(Culture) ?? "")
            {
                Placeholder = shared.Seed is { } seed ? "shared: " + seed.ToString(Culture) : "shared: random",
                Tooltip = "The same seed and prompt give the same reply; empty: the shared one",
            },
        };
        if (draft.Seed is not null)
        {
            blocks.Add(ResetButton("seed", "Reset the seed"));
        }

        blocks.Add(new PanelText("Loading · a copy of its own, loaded after a confirm that shows its memory", PanelTextStyle.Secondary));
        blocks.Add(Picker("weights", "Weights", [.. shared.WeightChoices.Select(w => new PanelOption(w, w == "auto" ? "auto (measured)" : w))], draft.Weights, shared.Weights));
        blocks.Add(Picker("device", "Device", [.. shared.Devices.Select(d => new PanelOption(d, d))], draft.Device, shared.Device));
        blocks.Add(Slider("context", "Context (0: the model's own)", draft.Context, shared.Context, 0, 131072, 1024, "tokens"));
        blocks.Add(new PanelSwitch(SetPrefix + "offload", "Offload to system memory when the device is full", draft.Offload ?? shared.Offload)
        {
            Detail = $"shared: {(shared.Offload ? "on" : "off")}{(draft.Offload is null ? "" : " · this session's own")}",
        });
        if (draft.Offload is not null)
        {
            blocks.Add(ResetButton("offload", "Reset offload"));
        }

        var drafts = new List<PanelOption> { new("off", "Off") };
        if (shared.Draft.Length > 0 && !shared.Draft.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            drafts.Insert(0, new PanelOption(shared.Draft, shared.Draft));
        }
        else
        {
            drafts.Insert(0, new PanelOption("", "The completion model (when it matches)"));
        }

        blocks.Add(Picker("draft", "Draft model", drafts, draft.Draft, shared.Draft));
        if (draft.ChangesLoad)
        {
            blocks.Add(new PanelText($"Save loads a separate copy for {_session.ModelSessionTitle} (asked first, with its measured memory); the other sessions keep theirs.", PanelTextStyle.Caption)
            {
                Tone = PanelTone.Warning,
            });
        }

        if (_workspace)
        {
            blocks.Add(new PanelPicker(Scope, "Apply to", [new PanelOption("user", "Your settings"), new PanelOption("workspace", "This workspace (.craft/settings.json)")],
                _scope == ModelSettingsScope.Workspace ? "workspace" : "user") { Drawn = true });
        }

        blocks.Add(new PanelButtons(
        [
            new PanelButton("Save", Save, PanelButtonKind.Primary) { Enabled = !_busy, Tooltip = $"Keep these for {_session.ModelSessionTitle} only" },
            new PanelButton("Apply to the environment", Apply, PanelButtonKind.Secondary)
            {
                Enabled = !_busy && !draft.IsEmpty,
                Tooltip = _scope == ModelSettingsScope.Workspace ? "Write them as this workspace's settings (.craft/settings.json)" : "Write them as your settings (the settings page's)",
            },
            new PanelButton("Reset all", ResetAll, PanelButtonKind.Flat) { Enabled = !_busy && !draft.IsEmpty },
            new PanelButton("Close", Close, PanelButtonKind.Flat),
        ]));
        if (_busy)
        {
            blocks.Add(new PanelMeter("Working…", -1));
        }

        if (Note is { } note)
        {
            blocks.Add(new PanelText(note, PanelTextStyle.Caption) { Tone = PanelTone.Accent });
        }

        return blocks;
    }

    private static PanelSlider Slider(string field, string title, double? own, double shared, double min, double max, double step, string? unit = null) =>
        new(SetPrefix + field, title, Math.Round(own ?? shared, 6), min, max, step)          // a float's value as written (0.3, not 0.30000001)
        {
            Unit = unit,
            Detail = "shared: " + Format(Math.Round(shared, 6)),
            Overridden = own is not null,
            ResetActionId = own is null ? null : ResetPrefix + field,
        };

    private static PanelPicker Picker(string field, string title, List<PanelOption> options, string? own, string shared)
    {
        string selected = own ?? shared;
        if (!options.Exists(o => string.Equals(o.Value, selected, StringComparison.OrdinalIgnoreCase)))
        {
            options.Insert(0, new PanelOption(selected, selected.Length == 0 ? "(shared)" : selected));
        }

        return new PanelPicker(SetPrefix + field, title, options, selected)
        {
            Drawn = true,
            ResetActionId = own is null ? null : ResetPrefix + field,
            Tooltip = "shared: " + (shared.Length == 0 ? "(automatic)" : shared),
        };
    }

    private static PanelButtons ResetButton(string field, string title) => new([new PanelButton(title, ResetPrefix + field, PanelButtonKind.Flat)]);

    private static string Format(double value) => value.ToString("0.###", Culture);

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
