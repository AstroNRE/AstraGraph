// SPDX-FileCopyrightText: 2026 Astro
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileComment: Community Funding Additional Permission applies; see COMMUNITY-FUNDING-PERMISSION.md.

using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.AstraGraph.Network;

public static class AstraNetworkProtocol
{
    public const int Version = 1;
}

[Serializable, NetSerializable]
public readonly record struct AstraNetManifestEntry(
    Guid GraphId,
    Guid RevisionId,
    byte Side,
    int ActivationTick,
    ulong BindingCatalogHash,
    ulong SchemaHash,
    int EncodedLength,
    ulong ContentHash);

[Serializable, NetSerializable]
public sealed class AstraManifestRequest : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class AstraManifestResponse(int protocolVersion, List<AstraNetManifestEntry> entries) : EntityEventArgs
{
    public int ProtocolVersion { get; } = protocolVersion;
    public List<AstraNetManifestEntry> Entries { get; } = entries;
}

[Serializable, NetSerializable]
public sealed class AstraPackageRequest(List<Guid> graphIds) : EntityEventArgs
{
    public List<Guid> GraphIds { get; } = graphIds;
}

[Serializable, NetSerializable]
public sealed class AstraPackageResponse(Guid graphId, Guid revisionId, ulong contentHash, byte[] encoded) : EntityEventArgs
{
    public Guid GraphId { get; } = graphId;
    public Guid RevisionId { get; } = revisionId;
    public ulong ContentHash { get; } = contentHash;
    public byte[] Encoded { get; } = encoded;
}

[Serializable, NetSerializable]
public sealed class AstraClientReady(List<Guid> revisions) : EntityEventArgs
{
    public List<Guid> Revisions { get; } = revisions;
}
