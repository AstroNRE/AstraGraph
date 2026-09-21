using System;
using System.Collections.Generic;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Network;

/// <summary>
/// Replication mode of schema fields across the network boundary.
/// </summary>
public enum NetworkFieldReplicationMode
{
    Local,
    ServerOnly,
    Replicated,
    Predicted,
    OwnerOnly,
    Persistent
}

/// <summary>
/// A single synchronized graph revision entry across the network.
/// </summary>
public sealed record SharedGraphEntry(
    GraphId Id,
    RevisionId Revision,
    string SemanticHash,
    string SchemaHash,
    GraphSide Side,
    int ActivationTick);

/// <summary>
/// Complete manifest of active shared graphs distributed from server to connected clients.
/// </summary>
public sealed record SharedGraphManifest(
    int ProtocolVersion,
    IReadOnlyList<SharedGraphEntry> Entries);

/// <summary>
/// Schema metadata description exchanged during join-in-progress handshake.
/// </summary>
public sealed record NetworkSchemaMetadata(
    SchemaId Id,
    string Name,
    IReadOnlyList<NetworkFieldMetadata> Fields);

public sealed record NetworkFieldMetadata(
    FieldId Id,
    string Name,
    string TypeName,
    NetworkFieldReplicationMode Mode);

/// <summary>
/// Server handshake sent to newly joined clients.
/// </summary>
public sealed record JoinHandshakeMessage(
    int ProtocolVersion,
    SharedGraphManifest Manifest,
    IReadOnlyList<NetworkSchemaMetadata> Schemas,
    int CurrentServerTick);

/// <summary>
/// Client request for any graph packages not present in local client cache.
/// </summary>
public sealed record RequestMissingPackagesMessage(
    IReadOnlyList<GraphId> MissingGraphIds);

/// <summary>
/// Bytecode package distributed to client for a specific graph.
/// </summary>
public sealed record GraphPackageMessage(
    GraphId Id,
    RevisionId Revision,
    byte[] BytecodePayload);

/// <summary>
/// Client confirmation that all shared graphs and schemas are compiled and ready.
/// </summary>
public sealed record ClientReadyMessage(
    int ClientId,
    int CurrentClientTick);
