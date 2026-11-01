// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// One model role's card on the Models page (X part 5, the owner's note 25: each role edits its own settings): its title,
/// its model and device as shown (an inherited one marked), its state, whether it is on, and the settings it writes.
/// </summary>
/// <param name="Role">The provider's role (<see cref="ModelProviderStatus.Roles"/>).</param>
/// <param name="ModelText">The model as shown: the name, "none set", or "same as chat: NAME" while it is the other role's.</param>
/// <param name="DeviceText">The device as shown: where it runs once loaded, else what it would take, else "auto".</param>
public sealed record ModelRoleCard(ModelRole Role, string ModelText, string DeviceText)
{
    /// <summary>The card's title ("Chat", "Agent", "Inline completion").</summary>
    public string Title => Role.Role;

    /// <summary>Whether the role is on.</summary>
    public bool Enabled => Role.Enabled;

    /// <summary>Whether the card has an on/off switch (the role names its <see cref="ModelRole.EnabledSetting"/>).</summary>
    public bool CanToggle => Role.EnabledSetting is not null;

    /// <summary>Whether the card offers "Same as chat" (a role that takes another's model and device when its own are empty).</summary>
    public bool CanInherit => Role.Inherits is not null && Role.ModelSetting is not null;

    /// <summary>Whether the role takes the other role's model and device now (its own settings are empty).</summary>
    public bool SameAsOther => Role.Inherits is not null && Role.InheritsModel && Role.InheritsDevice;

    /// <summary>The "Same as chat" label of the role it inherits from, or null.</summary>
    public string? SameAsLabel => Role.Inherits is { } other ? $"Same as {other.ToLowerInvariant()}" : null;
}

/// <summary>
/// The Models page's role cards (X part 5): what each shows and what each of its controls writes, every write the
/// card's own role's setting (the chat's card the chat's, the agent's the agent's), headless so the tests check it.
/// </summary>
public static class ModelRoleCards
{
    /// <summary>The cards of <paramref name="status"/>'s roles that name a key, in its order; none without a provider.</summary>
    public static IReadOnlyList<ModelRoleCard> Of(ModelProviderStatus? status)
    {
        if (status is null)
        {
            return [];
        }

        var cards = new List<ModelRoleCard>(status.Roles.Count);
        foreach (var role in status.Roles)
        {
            string model = role.Model is null ? "none set"
                : role is { Inherits: { } other, InheritsModel: true } ? $"same as {other.ToLowerInvariant()}: {role.Model}"
                : role.Model;
            string device = role.Key is not null ? ShellText.PlannedDevice(status, role.Key) ?? role.DeviceChoice ?? "auto" : role.Device ?? "auto";
            if (role is { Inherits: { } from, InheritsDevice: true })
            {
                device = $"{from.ToLowerInvariant()}'s: {device}";
            }

            cards.Add(new ModelRoleCard(role, model, device));
        }

        return cards;
    }

    /// <summary>The write of the card's switch: the role's enabled setting ("true" or "false", written so a workspace's value cannot hide it); null when it has none.</summary>
    public static ModelPickWrite? Enable(ModelRole role, bool on)
    {
        ArgumentNullException.ThrowIfNull(role);
        return role.EnabledSetting is { } setting ? new ModelPickWrite(setting.Variable, on ? "true" : "false", null) : null;
    }

    /// <summary>
    /// The write of the card's model field: the role's own model setting (an empty name clears it: the default, or for a
    /// role that inherits the other's), and the role's load command when a name is given and the role is on.
    /// </summary>
    public static ModelPickWrite? Model(ModelRole role, string? name)
    {
        ArgumentNullException.ThrowIfNull(role);
        string? value = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        return role.ModelSetting is { } setting ? new ModelPickWrite(setting.Variable, value, value is not null && role.Enabled ? role.LoadCommand : null) : null;
    }

    /// <summary>
    /// The write of the card's device choice: the role's own device setting. An empty choice clears it; "auto" is the
    /// default of a role's own device (cleared), but written as such for a role that inherits (where empty is the other's).
    /// </summary>
    public static ModelPickWrite? Device(ModelRole role, string? device)
    {
        ArgumentNullException.ThrowIfNull(role);
        string? value = string.IsNullOrWhiteSpace(device) ? null : device.Trim();
        if (value is not null && role.Inherits is null && value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
        }

        return role.DeviceSetting is { } setting ? new ModelPickWrite(setting.Variable, value, null) : null;
    }

    /// <summary>The writes of "Same as chat": the role's own model and device cleared, so it takes the other's (one copy shared); none for a role that inherits nothing.</summary>
    public static IReadOnlyList<ModelPickWrite> SameAsOther(ModelRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (role.Inherits is null)
        {
            return [];
        }

        var writes = new List<ModelPickWrite>(2);
        if (role.ModelSetting is { } model)
        {
            writes.Add(new ModelPickWrite(model.Variable, null, null));
        }

        if (role.DeviceSetting is { } device)
        {
            writes.Add(new ModelPickWrite(device.Variable, null, null));
        }

        return writes;
    }

    /// <summary>
    /// Applies <paramref name="writes"/> in order through <paramref name="apply"/> (the variable and its value; null when
    /// done, else why not: the shell's <c>ApplySetting</c>); the first problem, or null.
    /// </summary>
    public static string? Apply(IEnumerable<ModelPickWrite?> writes, Func<string, string?, string?> apply)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(apply);
        foreach (var write in writes)
        {
            if (write is not null && apply(write.Variable, write.Value) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }
}
