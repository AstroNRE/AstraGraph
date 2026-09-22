using System;
using System.Security.Cryptography;
using System.Text;

namespace AstraGraph.Persistence.Cache;

/// <summary>
/// Immutable specification of the cache validation key.
/// Any change in semantic hash, compiler version, runtime version, binding catalog,
/// target side, or backend version produces a distinct cache key, forcing recompilation.
/// </summary>
public sealed record CompilationCacheKey(
    string SemanticHash,
    string CompilerVersion,
    string RuntimeVersion,
    string BindingCatalogHash,
    string TargetSide,
    string BackendVersion = "VM-1.0",
    string EngineApiVersion = "1")
{
    /// <summary>
    /// Computes the cryptographic SHA-256 hex string identifying this exact compilation context.
    /// </summary>
    public string ComputeKeyString()
    {
        var canonical = $"{SemanticHash}|{CompilerVersion}|{RuntimeVersion}|{BindingCatalogHash}|{TargetSide}|{BackendVersion}|{EngineApiVersion}";
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
