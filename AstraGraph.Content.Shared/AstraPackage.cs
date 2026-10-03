// SPDX-FileCopyrightText: 2026 Astro
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileComment: Community Funding Additional Permission applies; see COMMUNITY-FUNDING-PERMISSION.md.

namespace Content.AstraGraph.Portable;

public enum AstraExecutionSide : byte
{
    Shared,
    Predicted,
    Client
}

public readonly record struct AstraPackageIdentity(
    Guid GraphId,
    Guid RevisionId,
    AstraExecutionSide Side,
    int ActivationTick,
    ulong BindingCatalogHash,
    ulong SchemaHash);

/// <summary>
/// Immutable validated program package. Source documents and CLR metadata never cross into the client runtime.
/// </summary>
public sealed class AstraPackage
{
    public AstraPackageIdentity Identity { get; }
    public byte[] Bytecode { get; }

    public AstraPackage(AstraPackageIdentity identity, byte[] bytecode)
    {
        Identity = identity;
        Bytecode = bytecode;
    }
}

public readonly record struct AstraPackageManifestEntry(
    AstraPackageIdentity Identity,
    int EncodedLength,
    ulong ContentHash);
