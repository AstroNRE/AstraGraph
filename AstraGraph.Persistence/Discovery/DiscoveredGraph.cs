using System;
using AstraGraph.Core;

namespace AstraGraph.Persistence.Discovery;

/// <summary>
/// Origin source of a discovered graph document.
/// </summary>
public enum GraphOrigin
{
    /// <summary>
    /// Bundled with the game project in Resources/AstraGraph.
    /// </summary>
    ProjectResource,

    /// <summary>
    /// Created live on a running server in data/AstraGraph/Live.
    /// </summary>
    Live,

    /// <summary>
    /// Overriding a project resource in data/AstraGraph/Overrides.
    /// </summary>
    Override
}

/// <summary>
/// Represents a graph document discovered on disk with its storage origin and relative path.
/// </summary>
public sealed record DiscoveredGraph(
    GraphId Id,
    string RelativePath,
    string FullPath,
    GraphOrigin Origin,
    GraphDocument Document,
    string? BaseRelativePath = null);
