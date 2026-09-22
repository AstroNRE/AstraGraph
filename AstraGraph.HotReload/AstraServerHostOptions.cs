using AstraGraph.Persistence;
using AstraGraph.Runtime.Security;

namespace AstraGraph.HotReload;

/// <summary>
/// Immutable settings a consumer fork supplies before the server host is created.
/// </summary>
public sealed record AstraServerHostOptions
{
    public required StorageLayout Storage { get; init; }

    public string? CompatibilityManifestPath { get; init; }

    public string? RobustToolboxRoot { get; init; }

    public string? RobustCommit { get; init; }

    public IAstraAdminDirectory? AdminDirectory { get; init; }

    public IAstraPermissionProvider? PermissionProvider { get; init; }

    public uint RequiredAdminFlag { get; init; } = SS14AdminFlagsConstants.AdminFlagAstraGraph;

    public static AstraServerHostOptions CreateFallback() => new()
    {
        Storage = new StorageLayout("Resources/AstraGraph", "data/AstraGraph")
    };
}

public interface IAstraServerHostConfiguration
{
    AstraServerHostOptions GetOptions();
}

/// <summary>
/// Fallback configuration for standalone runs and tests.
/// A consumer fork registers its own <see cref="IAstraServerHostConfiguration"/>.
/// </summary>
public sealed class DefaultAstraServerHostConfiguration : IAstraServerHostConfiguration
{
    public AstraServerHostOptions GetOptions() => AstraServerHostOptions.CreateFallback();
}

/// <summary>
/// Process-wide registration used when the consumer configures AstraGraph before IoC creates the server system.
/// </summary>
public static class AstraServerHost
{
    private static readonly object Gate = new();
    private static AstraServerHostOptions? _options;
    private static bool _consumed;

    public static void Configure(AstraServerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (Gate)
        {
            if (_consumed)
            {
                throw new InvalidOperationException("AstraGraph server host is already initialized.");
            }

            _options = options;
        }
    }

    public static bool TryPeek(out AstraServerHostOptions? options)
    {
        lock (Gate)
        {
            options = _options;
            return options != null;
        }
    }

    public static void MarkInitialized()
    {
        lock (Gate)
        {
            if (_options != null)
            {
                _consumed = true;
            }
        }
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            _options = null;
            _consumed = false;
        }
    }
}
