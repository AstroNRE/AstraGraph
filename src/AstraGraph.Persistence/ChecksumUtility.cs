using System;

namespace AstraGraph.Persistence;

/// <summary>
/// Fast IEEE 802.3 CRC32 checksum calculator with precomputed lookup table.
/// Used to detect corruption in cache files and state snapshots.
/// </summary>
public static class ChecksumUtility
{
    private static readonly uint[] CrcTable = new uint[256];

    static ChecksumUtility()
    {
        for (uint i = 0; i < 256; i++)
        {
            var entry = i;
            for (var j = 0; j < 8; j++)
            {
                if ((entry & 1) == 1)
                    entry = (entry >> 1) ^ 0xEDB88320;
                else
                    entry >>= 1;
            }
            CrcTable[i] = entry;
        }
    }

    /// <summary>
    /// Computes CRC32 checksum for the given byte span.
    /// </summary>
    public static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFF;
        for (var i = 0; i < data.Length; i++)
        {
            var index = (byte)((crc & 0xFF) ^ data[i]);
            crc = (crc >> 8) ^ CrcTable[index];
        }
        return ~crc;
    }
}
