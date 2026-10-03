// SPDX-FileCopyrightText: 2026 Astro
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileComment: Community Funding Additional Permission applies; see COMMUNITY-FUNDING-PERMISSION.md.

using Content.AstraGraph.Portable;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.AstraGraph.Network;

// Robust NetSerializer serializes public fields in network DTOs.
#pragma warning disable CA1051

public static class AstraNetworkProtocol
{
    public const int Version = 1;
    public const int MaxManifestEntries = 4096;
    public const int MaxPackageRequestEntries = 4096;
    public const int MaxReadyRevisions = 4096;
    public const int MaxEncodedPackageBytes = AstraPackageCodec.HeaderSize + AstraPackageCodec.MaxBytecodeBytes;
}

[Serializable, NetSerializable]
public struct AstraNetManifestEntry
{
    public Guid GraphId;
    public Guid RevisionId;
    public byte Side;
    public int ActivationTick;
    public ulong BindingCatalogHash;
    public ulong SchemaHash;
    public int EncodedLength;
    public ulong ContentHash;

    public AstraNetManifestEntry(
        Guid graphId,
        Guid revisionId,
        byte side,
        int activationTick,
        ulong bindingCatalogHash,
        ulong schemaHash,
        int encodedLength,
        ulong contentHash)
    {
        GraphId = graphId;
        RevisionId = revisionId;
        Side = side;
        ActivationTick = activationTick;
        BindingCatalogHash = bindingCatalogHash;
        SchemaHash = schemaHash;
        EncodedLength = encodedLength;
        ContentHash = contentHash;
    }
}

[Serializable, NetSerializable]
public sealed class AstraManifestRequest : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class AstraManifestResponse : EntityEventArgs
{
    public int ProtocolVersion;
    public List<AstraNetManifestEntry> Entries;

    public AstraManifestResponse(int protocolVersion, List<AstraNetManifestEntry> entries)
    {
        ProtocolVersion = protocolVersion;
        Entries = entries;
    }
}

[Serializable, NetSerializable]
public sealed class AstraPackageRequest : EntityEventArgs
{
    public List<Guid> GraphIds;

    public AstraPackageRequest(List<Guid> graphIds)
    {
        GraphIds = graphIds;
    }
}

[Serializable, NetSerializable]
public sealed class AstraPackageResponse : EntityEventArgs
{
    public Guid GraphId;
    public Guid RevisionId;
    public ulong ContentHash;
    public byte[] Encoded;

    public AstraPackageResponse(Guid graphId, Guid revisionId, ulong contentHash, byte[] encoded)
    {
        GraphId = graphId;
        RevisionId = revisionId;
        ContentHash = contentHash;
        Encoded = encoded;
    }
}

[Serializable, NetSerializable]
public sealed class AstraClientReady : EntityEventArgs
{
    public List<Guid> Revisions;

    public AstraClientReady(List<Guid> revisions)
    {
        Revisions = revisions;
    }
}

#pragma warning restore CA1051
