// SPDX-FileCopyrightText: 2026 Astro
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileComment: Community Funding Additional Permission applies; see COMMUNITY-FUNDING-PERMISSION.md.

using Content.AstraGraph.Network;
using Content.AstraGraph.Portable;
using Robust.Shared.GameObjects;

namespace Content.AstraGraph.Robust.Server;

/// <summary>
/// Authoritative distributor for validated portable packages. Compilation and persistence feed this system;
/// clients can only request immutable revisions already accepted by the server.
/// </summary>
public sealed class ServerAstraPackageSystem : EntitySystem
{
    private readonly Dictionary<Guid, PublishedPackage> _packages = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<AstraManifestRequest>(OnManifestRequest);
        SubscribeNetworkEvent<AstraPackageRequest>(OnPackageRequest);
        SubscribeNetworkEvent<AstraClientReady>(OnClientReady);
    }

    public bool TryPublish(AstraPackage package)
    {
        if (!AstraPackageCodec.TryEncode(package, out var encoded))
            return false;

        if (!_packages.ContainsKey(package.Identity.GraphId) &&
            _packages.Count >= AstraNetworkProtocol.MaxManifestEntries)
            return false;

        _packages[package.Identity.GraphId] = new PublishedPackage(
            package.Identity,
            encoded,
            AstraPackageCodec.ComputeContentHash(encoded));
        return true;
    }

    private void OnManifestRequest(AstraManifestRequest message, EntitySessionEventArgs args)
    {
        var entries = new List<AstraNetManifestEntry>(_packages.Count);
        foreach (var package in _packages.Values)
        {
            var identity = package.Identity;
            entries.Add(new AstraNetManifestEntry(
                identity.GraphId,
                identity.RevisionId,
                (byte) identity.Side,
                identity.ActivationTick,
                identity.BindingCatalogHash,
                identity.SchemaHash,
                package.Encoded.Length,
                package.ContentHash));
        }

        RaiseNetworkEvent(new AstraManifestResponse(AstraNetworkProtocol.Version, entries), args.SenderSession);
    }

    private void OnPackageRequest(AstraPackageRequest message, EntitySessionEventArgs args)
    {
        if (message.GraphIds == null ||
            message.GraphIds.Count > AstraNetworkProtocol.MaxPackageRequestEntries)
            return;

        foreach (var graphId in message.GraphIds)
        {
            if (!_packages.TryGetValue(graphId, out var package))
                continue;

            RaiseNetworkEvent(new AstraPackageResponse(
                graphId,
                package.Identity.RevisionId,
                package.ContentHash,
                package.Encoded), args.SenderSession);
        }
    }

    private void OnClientReady(AstraClientReady message, EntitySessionEventArgs args)
    {
        if (message.Revisions == null || message.Revisions.Count > AstraNetworkProtocol.MaxReadyRevisions)
            return;

        // Readiness is deliberately a network boundary only. Activation remains tick-authoritative.
    }

    private sealed record PublishedPackage(AstraPackageIdentity Identity, byte[] Encoded, ulong ContentHash);
}
