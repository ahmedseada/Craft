// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Ai;

/// <summary>
/// Registers Idrak's library assemblies that this plug-in carries in its own folder and load context
/// (<see cref="IdrakInPlugins.IncludeLibraries"/>, shared with the voice plug-in): without it their built-ins (model
/// sources such as Hugging Face's, model formats, MCP transports) never register, since Idrak finds its libraries by name
/// in the default load context. Once per process.
/// </summary>
internal static class IdrakLibraries
{
    public static void EnsureIncluded() => IdrakInPlugins.IncludeLibraries(typeof(IdrakLibraries).Assembly);
}
