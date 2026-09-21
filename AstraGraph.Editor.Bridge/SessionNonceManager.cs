using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AstraGraph.Editor.Bridge;

/// <summary>
/// Contains metadata associated with a single-use launch nonce.
/// </summary>
public sealed record NonceInfo(
    string Nonce,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    object? Metadata)
{
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}

/// <summary>
/// Manages cryptographically secure, single-use launch nonces for the local loopback bridge.
/// Guarantees that each nonce can be redeemed at most once before expiration.
/// </summary>
public sealed class SessionNonceManager
{
    private readonly ConcurrentDictionary<string, NonceInfo> _activeNonces = new(StringComparer.Ordinal);
    private readonly TimeSpan _defaultTtl;

    public SessionNonceManager(TimeSpan? defaultTtl = null)
    {
        _defaultTtl = defaultTtl ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Gets the count of currently registered nonces (including potentially expired ones before sweep).
    /// </summary>
    public int ActiveNonceCount => _activeNonces.Count;

    /// <summary>
    /// Generates and stores a new cryptographically secure single-use nonce.
    /// </summary>
    public NonceInfo CreateNonce(TimeSpan? ttl = null, object? metadata = null)
    {
        byte[] buffer = new byte[32];
        RandomNumberGenerator.Fill(buffer);
        string nonce = Convert.ToHexString(buffer).ToLowerInvariant();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.Add(ttl ?? _defaultTtl);

        var info = new NonceInfo(nonce, now, expiresAt, metadata);
        _activeNonces[nonce] = info;
        return info;
    }

    /// <summary>
    /// Atomically redeems the specified nonce. If valid and not expired, the nonce is removed
    /// and returns true. If invalid, already redeemed, or expired, returns false.
    /// </summary>
    public bool TryRedeemNonce(string? nonce, out NonceInfo? info)
    {
        info = null;
        if (string.IsNullOrWhiteSpace(nonce))
            return false;

        if (_activeNonces.TryRemove(nonce, out var existing))
        {
            if (existing.IsExpired)
            {
                return false;
            }

            info = existing;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes any nonces that have expired.
    /// </summary>
    public int CleanupExpired()
    {
        int removedCount = 0;
        foreach (var kvp in _activeNonces)
        {
            if (kvp.Value.IsExpired)
            {
                if (_activeNonces.TryRemove(kvp.Key, out _))
                {
                    removedCount++;
                }
            }
        }
        return removedCount;
    }

    /// <summary>
    /// Clears all nonces (e.g. on bridge shutdown).
    /// </summary>
    public void Clear()
    {
        _activeNonces.Clear();
    }
}
