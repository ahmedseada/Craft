// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Maui;

/// <summary>A platform input connection that knows which modifier keys are held now (for shift-click).</summary>
internal interface IKeyModifierSource
{
    /// <summary>The modifier keys held now.</summary>
    KeyModifiers CurrentModifiers { get; }
}
