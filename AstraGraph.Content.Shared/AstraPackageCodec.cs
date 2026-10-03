// SPDX-FileCopyrightText: 2026 Astro
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileComment: Community Funding Additional Permission applies; see COMMUNITY-FUNDING-PERMISSION.md.

using System.Buffers.Binary;

namespace Content.AstraGraph.Portable;

/// <summary>
/// Bounded binary package codec used by sandboxed clients. It intentionally has no JSON, stream, filesystem,
/// reflection, crypto, or networking dependency.
/// </summary>
public static class AstraPackageCodec
{
    public const uint Magic = 0x41535452;
    public const byte FormatVersion = 1;
    public const int HeaderSize = 64;
    public const int MaxBytecodeBytes = 8 * 1024 * 1024;

    public static bool TryEncode(AstraPackage package, out byte[] encoded)
    {
        encoded = [];
        if (package.Bytecode.Length > MaxBytecodeBytes)
            return false;

        encoded = new byte[HeaderSize + package.Bytecode.Length];
        var span = encoded.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        span[4] = FormatVersion;
        span[5] = (byte) package.Identity.Side;
        package.Identity.GraphId.TryWriteBytes(span[8..24]);
        package.Identity.RevisionId.TryWriteBytes(span[24..40]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..44], package.Identity.ActivationTick);
        BinaryPrimitives.WriteUInt64LittleEndian(span[44..52], package.Identity.BindingCatalogHash);
        BinaryPrimitives.WriteUInt64LittleEndian(span[52..60], package.Identity.SchemaHash);
        BinaryPrimitives.WriteInt32LittleEndian(span[60..64], package.Bytecode.Length);
        package.Bytecode.CopyTo(span[HeaderSize..]);
        return true;
    }

    public static bool TryDecode(ReadOnlySpan<byte> encoded, out AstraPackage? package)
    {
        package = null;
        if (encoded.Length < HeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(encoded) != Magic ||
            encoded[4] != FormatVersion ||
            encoded[5] > (byte) AstraExecutionSide.Client)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(encoded[60..64]);
        if (payloadLength < 0 || payloadLength > MaxBytecodeBytes || encoded.Length != HeaderSize + payloadLength)
            return false;

        var identity = new AstraPackageIdentity(
            new Guid(encoded[8..24]),
            new Guid(encoded[24..40]),
            (AstraExecutionSide) encoded[5],
            BinaryPrimitives.ReadInt32LittleEndian(encoded[40..44]),
            BinaryPrimitives.ReadUInt64LittleEndian(encoded[44..52]),
            BinaryPrimitives.ReadUInt64LittleEndian(encoded[52..60]));

        package = new AstraPackage(identity, encoded[HeaderSize..].ToArray());
        return true;
    }

    public static ulong ComputeContentHash(ReadOnlySpan<byte> data)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var value in data)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }
}
