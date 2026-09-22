using AstraGraph.Editor.Bridge;

namespace AstraGraph.Studio.DevHost;

/// <summary>
/// Origin rules for Astra Studio DevHost. Local Bridge keeps <see cref="BridgeSecurityPolicy"/> unchanged.
/// </summary>
public sealed class DevHostOriginPolicy
{
    private readonly BridgeSecurityPolicy _loopback;
    private readonly HashSet<string> _allowed;

    public DevHostOriginPolicy(IEnumerable<string>? allowedOrigins)
    {
        _loopback = new BridgeSecurityPolicy();
        _allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (allowedOrigins == null)
        {
            return;
        }

        foreach (var origin in allowedOrigins)
        {
            if (!string.IsNullOrWhiteSpace(origin))
            {
                _allowed.Add(origin.Trim().TrimEnd('/'));
            }
        }
    }

    public SecurityCheckResult Validate(string? origin, int port)
    {
        var local = _loopback.ValidateOrigin(origin, port);
        if (local.IsAllowed || string.IsNullOrWhiteSpace(origin))
        {
            return local;
        }

        var normalized = origin.Trim().TrimEnd('/');
        if (_allowed.Contains(normalized) || IsCodespacesOrigin(normalized))
        {
            return SecurityCheckResult.Allowed;
        }

        return SecurityCheckResult.Denied($"Invalid origin '{origin}'.");
    }

    public static bool IsCodespacesOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        return host.EndsWith(".github.dev", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".app.github.dev", StringComparison.OrdinalIgnoreCase);
    }
}
