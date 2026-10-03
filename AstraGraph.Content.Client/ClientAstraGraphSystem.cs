// SPDX-FileCopyrightText: 2026 Astro
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileComment: Community Funding Additional Permission applies; see COMMUNITY-FUNDING-PERMISSION.md.

using Content.AstraGraph.Network;
using Content.AstraGraph.Portable;
using Robust.Client;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.AstraGraph.Robust.Client;

/// <summary>
/// Sandboxed client host. It only receives validated portable packages and activates them on simulation ticks.
/// Authoring, compilation, persistence, reflection, and HTTP remain server/tool responsibilities.
/// </summary>
public sealed class ClientAstraGraphSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IBaseClient _baseClient = default!;
    [Dependency] private readonly IUriOpener _uriOpener = default!;

    private readonly Dictionary<Guid, AstraPackage> _active = new();
    private readonly Dictionary<Guid, AstraPackage> _staged = new();
    private readonly Dictionary<Guid, AstraNetManifestEntry> _expected = new();

    public IReadOnlyDictionary<Guid, AstraPackage> ActivePackages => _active;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<AstraManifestResponse>(OnManifest);
        SubscribeNetworkEvent<AstraPackageResponse>(OnPackage);
        _baseClient.RunLevelChanged += OnRunLevelChanged;
    }

    public override void Shutdown()
    {
        _baseClient.RunLevelChanged -= OnRunLevelChanged;
        base.Shutdown();
    }

    public bool TryStagePackage(ReadOnlySpan<byte> encoded, ulong expectedHash = 0)
    {
        if (expectedHash != 0 && AstraPackageCodec.ComputeContentHash(encoded) != expectedHash)
            return false;

        if (!AstraPackageCodec.TryDecode(encoded, out var package) || package == null)
            return false;

        _staged[package.Identity.GraphId] = package;
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var tick = (int) _timing.CurTick.Value;
        if (_staged.Count == 0)
            return;

        List<Guid>? activated = null;
        foreach (var (graphId, package) in _staged)
        {
            if (package.Identity.ActivationTick > tick)
                continue;

            _active[graphId] = package;
            (activated ??= []).Add(graphId);
        }

        if (activated == null)
            return;

        foreach (var graphId in activated)
            _staged.Remove(graphId);
    }

    public void OpenStudio(string address)
    {
        _uriOpener.OpenUri(address);
    }

    private void OnRunLevelChanged(object? sender, RunLevelChangedEventArgs args)
    {
        if (args.NewLevel != ClientRunLevel.Connected)
            return;

        // A reconnect may target another server, so no package state is trusted across sessions.
        _active.Clear();
        _staged.Clear();
        _expected.Clear();
        RaiseNetworkEvent(new AstraManifestRequest());
    }

    private void OnManifest(AstraManifestResponse message)
    {
        if (message.ProtocolVersion != AstraNetworkProtocol.Version ||
            message.Entries == null ||
            message.Entries.Count > AstraNetworkProtocol.MaxManifestEntries)
            return;

        _expected.Clear();
        var missing = new List<Guid>();
        foreach (var entry in message.Entries)
        {
            if (entry.EncodedLength < AstraPackageCodec.HeaderSize ||
                entry.EncodedLength > AstraNetworkProtocol.MaxEncodedPackageBytes)
                return;

            if (_active.TryGetValue(entry.GraphId, out var active) &&
                active.Identity.RevisionId == entry.RevisionId)
                continue;

            _expected[entry.GraphId] = entry;
            missing.Add(entry.GraphId);
        }

        if (missing.Count == 0)
        {
            SendReady();
            return;
        }

        RaiseNetworkEvent(new AstraPackageRequest(missing));
    }

    private void OnPackage(AstraPackageResponse message)
    {
        if (message.Encoded == null || message.Encoded.Length > AstraNetworkProtocol.MaxEncodedPackageBytes)
            return;

        if (!_expected.TryGetValue(message.GraphId, out var expected) ||
            expected.RevisionId != message.RevisionId ||
            expected.ContentHash != message.ContentHash ||
            AstraPackageCodec.ComputeContentHash(message.Encoded) != message.ContentHash ||
            !AstraPackageCodec.TryDecode(message.Encoded, out var package) ||
            package == null)
        {
            return;
        }

        var identity = package.Identity;
        if (identity.GraphId != expected.GraphId || identity.RevisionId != expected.RevisionId ||
            (byte) identity.Side != expected.Side || identity.ActivationTick != expected.ActivationTick ||
            identity.BindingCatalogHash != expected.BindingCatalogHash || identity.SchemaHash != expected.SchemaHash)
        {
            return;
        }

        _staged[identity.GraphId] = package;

        _expected.Remove(message.GraphId);
        if (_expected.Count == 0)
            SendReady();
    }

    private void SendReady()
    {
        var revisions = _staged.Values.Select(package => package.Identity.RevisionId).ToList();
        revisions.AddRange(_active.Values.Select(package => package.Identity.RevisionId));
        if (revisions.Count > AstraNetworkProtocol.MaxReadyRevisions)
            return;

        RaiseNetworkEvent(new AstraClientReady(revisions));
    }
}
