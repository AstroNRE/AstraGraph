using System;
using System.IO;
using System.Text;

namespace AstraGraph.Persistence.Cache;

/// <summary>
/// On-disk cache for compiled bytecode and execution artifacts.
/// Validates cache file magic header, key identity, and data CRC32 checksum.
/// </summary>
public sealed class CompilationCache
{
    private static readonly uint MagicHeader = 0x43434741; // 'A', 'G', 'C', 'C' (little-endian)
    private static readonly uint FormatVersion = 1;

    private readonly StorageLayout _layout;

    public CompilationCache(StorageLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _layout.EnsureDirectories();
    }

    /// <summary>
    /// Attempts to retrieve compiled payload from disk cache for the given key.
    /// Returns false if cache misses, is corrupted, or fails checksum validation.
    /// </summary>
    public bool TryGet(CompilationCacheKey key, out byte[]? payload)
    {
        ArgumentNullException.ThrowIfNull(key);
        payload = null;

        var keyString = key.ComputeKeyString();
        var cachePath = _layout.GetCachePath(keyString);

        if (!File.Exists(cachePath))
            return false;

        try
        {
            var rawBytes = AtomicFileStore.ReadAllBytes(cachePath);
            if (rawBytes.Length < 16)
                return false;

            using var reader = new BinaryReader(new MemoryStream(rawBytes));
            var magic = reader.ReadUInt32();
            if (magic != MagicHeader)
                return false;

            var version = reader.ReadUInt32();
            if (version != FormatVersion)
                return false;

            var storedKey = reader.ReadString();
            if (!string.Equals(storedKey, keyString, StringComparison.Ordinal))
                return false;

            var payloadLen = reader.ReadInt32();
            if (payloadLen < 0 || payloadLen > rawBytes.Length)
                return false;

            var data = reader.ReadBytes(payloadLen);
            var expectedCrc = reader.ReadUInt32();

            var actualCrc = ChecksumUtility.ComputeCrc32(data);
            if (actualCrc != expectedCrc)
                return false;

            payload = data;
            return true;
        }
        catch (Exception)
        {
            // If cache file is invalid or reading failed, treat as cache miss
            return false;
        }
    }

    /// <summary>
    /// Stores the compiled payload into the on-disk cache using crash-proof atomic writes.
    /// </summary>
    public void Store(CompilationCacheKey key, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(key);

        var keyString = key.ComputeKeyString();
        var cachePath = _layout.GetCachePath(keyString);

        var crc = ChecksumUtility.ComputeCrc32(payload);

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(MagicHeader);
            writer.Write(FormatVersion);
            writer.Write(keyString);
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Write(crc);
        }

        AtomicFileStore.WriteAllBytesAtomic(cachePath, ms.ToArray());
    }

    /// <summary>
    /// Evicts a cached compilation entry if it exists.
    /// </summary>
    public bool Evict(CompilationCacheKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var keyString = key.ComputeKeyString();
        var cachePath = _layout.GetCachePath(keyString);
        if (File.Exists(cachePath))
        {
            try
            {
                File.Delete(cachePath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// Clears all files in the cache directory.
    /// </summary>
    public int Clear()
    {
        if (!Directory.Exists(_layout.CacheDirectory))
            return 0;

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(_layout.CacheDirectory, "*.agcache"))
        {
            try
            {
                File.Delete(file);
                count++;
            }
            catch (IOException)
            {
            }
        }
        return count;
    }
}
