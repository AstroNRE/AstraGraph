using AstraGraph.Core;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Logic;

/// <summary>
/// One published UI revision. The view, both graphs, and the contract move together.
/// </summary>
public sealed class UiPackage
{
    public required string Id { get; init; }

    public required int Revision { get; init; }

    public required UiDocument Document { get; init; }

    public required GraphDocument ClientLogic { get; init; }

    public required GraphDocument ServerLogic { get; init; }
}

public sealed class UiPackageStore
{
    private readonly List<UiPackage> _revisions = [];

    public UiPackage? Current { get; private set; }

    public IReadOnlyList<UiPackage> Revisions => _revisions;

    public UiPackage Publish(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var logic = UiLogicGraph.Build(document);
        var package = new UiPackage
        {
            Id = document.Id.ToString(),
            Revision = (Current?.Revision ?? 0) + 1,
            Document = document,
            ClientLogic = logic.Client,
            ServerLogic = logic.Server
        };
        _revisions.Add(package);
        Current = package;
        return package;
    }

    public bool TryRollback(out UiPackage? package)
    {
        package = null;
        if (_revisions.Count < 2 || Current == null)
        {
            return false;
        }

        _revisions.RemoveAt(_revisions.Count - 1);
        Current = _revisions[^1];
        package = Current;
        return true;
    }
}
