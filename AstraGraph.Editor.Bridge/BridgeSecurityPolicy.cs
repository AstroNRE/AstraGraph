using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AstraGraph.Editor.Bridge;

/// <summary>
/// Result of a security validation check.
/// </summary>
public sealed record SecurityCheckResult(bool IsAllowed, string? RejectionReason = null)
{
    public static readonly SecurityCheckResult Allowed = new(true, null);

    public static SecurityCheckResult Denied(string reason) => new(false, reason);
}

/// <summary>
/// Enforces security rules for the Astra Local Bridge:
/// - Loopback-only client address validation
/// - Strict Origin header verification (anti-CSWSH)
/// - CSRF protection
/// </summary>
public sealed class BridgeSecurityPolicy
{
    private readonly HashSet<string> _customAllowedOrigins;

    public BridgeSecurityPolicy(IEnumerable<string>? additionalAllowedOrigins = null)
    {
        _customAllowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (additionalAllowedOrigins != null)
        {
            foreach (var origin in additionalAllowedOrigins)
            {
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    _customAllowedOrigins.Add(origin.TrimEnd('/'));
                }
            }
        }
    }

    /// <summary>
    /// Validates that an incoming connection originates strictly from loopback.
    /// </summary>
    public static SecurityCheckResult ValidateRemoteEndPoint(IPEndPoint? remoteEndPoint)
    {
        if (remoteEndPoint == null)
        {
            return SecurityCheckResult.Denied("Missing remote endpoint.");
        }

        if (!IPAddress.IsLoopback(remoteEndPoint.Address))
        {
            return SecurityCheckResult.Denied($"Access rejected: client address '{remoteEndPoint.Address}' is not a loopback address.");
        }

        return SecurityCheckResult.Allowed;
    }

    /// <summary>
    /// Validates the Origin header against the local bridge port.
    /// Only loopback origins matching this bridge instance are permitted.
    /// </summary>
    public SecurityCheckResult ValidateOrigin(string? originHeader, int localPort)
    {
        if (string.IsNullOrWhiteSpace(originHeader))
        {
            // For direct non-browser requests (e.g. tools, curl), origin may be absent,
            // but for browser WebSocket upgrades, Origin is mandatory.
            return SecurityCheckResult.Allowed;
        }

        string normalized = originHeader.Trim().TrimEnd('/');

        // Explicitly check localhost and 127.0.0.1 for this port
        if (string.Equals(normalized, $"http://127.0.0.1:{localPort}", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, $"http://localhost:{localPort}", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, $"https://127.0.0.1:{localPort}", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, $"https://localhost:{localPort}", StringComparison.OrdinalIgnoreCase))
        {
            return SecurityCheckResult.Allowed;
        }

        if (_customAllowedOrigins.Contains(normalized))
        {
            return SecurityCheckResult.Allowed;
        }

        return SecurityCheckResult.Denied($"Invalid origin '{originHeader}'. Expected origin matching loopback port {localPort}.");
    }

    /// <summary>
    /// Performs constant-time comparison of CSRF or authentication tokens to prevent timing attacks.
    /// </summary>
    public static bool ValidateTokenConstantTime(string? expectedToken, string? actualToken)
    {
        if (string.IsNullOrEmpty(expectedToken) || string.IsNullOrEmpty(actualToken))
            return false;

        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        byte[] actualBytes = Encoding.UTF8.GetBytes(actualToken);

        if (expectedBytes.Length != actualBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
