using System;
using System.Collections.Generic;
using System.IO;
using AstraGraph.Core;
using AstraGraph.State;

namespace AstraGraph.Runtime.Network;

/// <summary>
/// Orchestrates network synchronization of active graphs, schema tables, and bytecode packages
/// between server and client during join-in-progress, reconnect, and live updates.
/// </summary>
public sealed class AstraNetworkSyncService
{
    public const int CurrentProtocolVersion = 1;

    private readonly AstraGraphHost _host;
    private readonly SharedActivationCoordinator _activationCoordinator;
    private readonly Dictionary<SchemaId, SchemaType> _schemas = new();

    public SharedActivationCoordinator ActivationCoordinator => _activationCoordinator;

    public AstraNetworkSyncService(AstraGraphHost host, SharedActivationCoordinator? activationCoordinator = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _activationCoordinator = activationCoordinator ?? new SharedActivationCoordinator(_host);
    }

    public void RegisterSchema(SchemaType schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schemas[schema.Id] = schema;
    }

    /// <summary>
    /// Server-side: builds the complete JoinHandshakeMessage for a newly connected or reconnecting client.
    /// </summary>
    public JoinHandshakeMessage BuildHandshakeMessage(int currentServerTick)
    {
        var entries = new List<SharedGraphEntry>();

        // Find all active shared or predicted programs
        // We can inspect the host's active programs or registered systems
        foreach (var reg in _host.Scheduler.Systems)
        {
            if (_host.GetProgram(reg.GraphId) is { } prog)
            {
                entries.Add(new SharedGraphEntry(
                    prog.Id,
                    prog.Revision,
                    prog.SemanticHash,
                    string.Empty,
                    GraphSide.Shared,
                    currentServerTick));
            }
        }

        var schemaMetaList = new List<NetworkSchemaMetadata>();
        foreach (var kvp in _schemas)
        {
            var schema = kvp.Value;
            var fields = new List<NetworkFieldMetadata>();
            foreach (var f in schema.Fields)
            {
                fields.Add(new NetworkFieldMetadata(
                    f.Id,
                    f.Name,
                    f.Type.ToString(),
                    NetworkFieldReplicationMode.Replicated));
            }
            schemaMetaList.Add(new NetworkSchemaMetadata(schema.Id, schema.Name, fields));
        }

        var manifest = new SharedGraphManifest(CurrentProtocolVersion, entries);
        return new JoinHandshakeMessage(CurrentProtocolVersion, manifest, schemaMetaList, currentServerTick);
    }

    /// <summary>
    /// Server-side: serializes an active BytecodeProgram into a portable GraphPackageMessage.
    /// </summary>
    public GraphPackageMessage? ExportGraphPackage(GraphId graphId)
    {
        var prog = _host.GetProgram(graphId);
        if (prog == null) return null;

        var payload = BytecodeSerializer.SerializeToBytes(prog);
        return new GraphPackageMessage(prog.Id, prog.Revision, payload);
    }

    /// <summary>
    /// Client-side: processes an incoming JoinHandshakeMessage from the server.
    /// Identifies any missing or outdated graphs that must be requested from the server.
    /// </summary>
    public RequestMissingPackagesMessage? ProcessHandshake(JoinHandshakeMessage handshake)
    {
        ArgumentNullException.ThrowIfNull(handshake);

        var missing = new List<GraphId>();

        foreach (var entry in handshake.Manifest.Entries)
        {
            var localProg = _host.GetProgram(entry.Id);
            if (localProg == null || localProg.Revision != entry.Revision)
            {
                missing.Add(entry.Id);
            }
        }

        return missing.Count > 0 ? new RequestMissingPackagesMessage(missing) : null;
    }

    /// <summary>
    /// Client-side: deserializes and installs an incoming GraphPackageMessage from the server into the host.
    /// </summary>
    public BytecodeProgram ApplyGraphPackage(GraphPackageMessage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        using var ms = new MemoryStream(package.BytecodePayload);
        var program = BytecodeSerializer.Deserialize(ms);

        _host.RegisterProgram(program);
        return program;
    }

    /// <summary>
    /// Client-side: confirms synchronization is complete and client is ready to execute simulation.
    /// </summary>
    public static ClientReadyMessage CreateReadyMessage(int clientId, int currentClientTick)
    {
        return new ClientReadyMessage(clientId, currentClientTick);
    }
}
