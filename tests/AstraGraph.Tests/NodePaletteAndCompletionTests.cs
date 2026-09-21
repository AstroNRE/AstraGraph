using System.Linq;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Editor.Core;
using AstraGraph.Editor.Core.Search;
using NUnit.Framework;

namespace AstraGraph.Tests;

public class TargetEntitySample
{
    public float Multiplier { get; set; } = 1.0f;
    public void SetCoordinates(long entityUid, float x, float y) { _ = Multiplier + entityUid + x + y; }
    public long GetHealth(long entityUid) => (long)(100L * Multiplier) + entityUid;
}

[TestFixture]
public sealed class NodePaletteAndCompletionTests
{
    [Test]
    public void NodePaletteIndexer_ContainsBuiltInNodes()
    {
        var indexer = new NodePaletteIndexer();
        var items = indexer.AllItems;

        Assert.That(items.Any(i => i.Id == "Core.Branch"), Is.True);
        Assert.That(items.Any(i => i.Id == "Latent.Delay"), Is.True);
        Assert.That(items.Any(i => i.Id == "Component.Get"), Is.True);
        Assert.That(items.Any(i => i.Id == "Math.Add"), Is.True);
    }

    [Test]
    public void NodePaletteIndexer_IndexesBindingCatalogNativeMethods()
    {
        var catalog = new BindingCatalog();
        catalog.IndexType(typeof(TargetEntitySample));

        var indexer = new NodePaletteIndexer();
        indexer.IndexBindingCatalog(catalog);

        var searchResults = indexer.Search("SetCoordinates");
        Assert.That(searchResults.Count, Is.GreaterThan(0));

        var item = searchResults[0].Item;
        Assert.That(item.NodeType, Is.EqualTo("Native.Call"));
        Assert.That(item.Pins.Any(p => p.Name == "entityUid"), Is.True);
        Assert.That(item.Pins.Any(p => p.Name == "x"), Is.True);
        Assert.That(item.Pins.Any(p => p.Name == "y"), Is.True);
        Assert.That(item.Pins.Any(p => p.Name == "In" && p.Kind == PinKind.Execution), Is.True);
    }

    [Test]
    public void NodePaletteIndexer_FuzzySearch_RanksExactAndStartsMatchesHigher()
    {
        var indexer = new NodePaletteIndexer();

        // Exact match
        var exact = indexer.Search("Branch");
        Assert.That(exact[0].Item.DisplayName, Is.EqualTo("Branch"));
        Assert.That(exact[0].Score, Is.EqualTo(1000));

        // Prefix match
        var prefix = indexer.Search("Del");
        Assert.That(prefix[0].Item.DisplayName, Does.Contain("Delay"));
        Assert.That(prefix[0].Score, Is.GreaterThanOrEqualTo(500));

        // Acronym match: "dtm" or "gt" for "Greater Than"
        var acronym = indexer.Search("gt");
        Assert.That(acronym.Any(r => r.Item.DisplayName == "Greater Than"), Is.True);
    }

    [Test]
    public void ContextCompletionEngine_DragExecutionOutput_SuggestsExecutionInputs()
    {
        var indexer = new NodePaletteIndexer();
        var node = new VisualNode(NodeId.New(), "Entry", "Astra.EntryPoint", CanvasPoint.Zero);
        var execOut = new VisualPin(PinId.New(), node.Id, "Out", PinDirection.Output, PinKind.Execution, "Flow");

        var suggestions = ContextCompletionEngine.SuggestForPin(execOut, indexer);

        Assert.That(suggestions.Count, Is.GreaterThan(0));
        // All suggestions must target an Execution Input pin
        foreach (var s in suggestions)
        {
            Assert.That(s.TargetPinToConnect.Direction, Is.EqualTo(PinDirection.Input));
            Assert.That(s.TargetPinToConnect.Kind, Is.EqualTo(PinKind.Execution));
        }

        Assert.That(suggestions.Any(s => s.Item.Id == "Core.Branch"), Is.True);
        Assert.That(suggestions.Any(s => s.Item.Id == "Latent.Delay"), Is.True);
    }

    [Test]
    public void ContextCompletionEngine_DragEntityUid_SuggestsCompatibleComponentNodes()
    {
        var indexer = new NodePaletteIndexer();
        var node = new VisualNode(NodeId.New(), "Entry", "Astra.EntryPoint", CanvasPoint.Zero);
        var entityOut = new VisualPin(PinId.New(), node.Id, "EventEntity", PinDirection.Output, PinKind.Data, "Robust.Shared.GameObjects.EntityUid");

        var suggestions = ContextCompletionEngine.SuggestForPin(entityOut, indexer);

        Assert.That(suggestions.Count, Is.GreaterThan(0));
        // Must target an Input Data pin
        foreach (var s in suggestions)
        {
            Assert.That(s.TargetPinToConnect.Direction, Is.EqualTo(PinDirection.Input));
            Assert.That(s.TargetPinToConnect.Kind, Is.EqualTo(PinKind.Data));
        }

        // Component.Get and DoAfter take EntityUid
        Assert.That(suggestions.Any(s => s.Item.Id == "Component.Get"), Is.True);
        Assert.That(suggestions.Any(s => s.Item.Id == "Latent.DoAfter"), Is.True);
    }

    [Test]
    public void ContextCompletionEngine_WithQuery_FiltersSuggestionsContextually()
    {
        var catalog = new BindingCatalog();
        catalog.IndexType(typeof(TargetEntitySample));

        var indexer = new NodePaletteIndexer();
        indexer.IndexBindingCatalog(catalog);

        var node = new VisualNode(NodeId.New(), "Math", "Math.Add", CanvasPoint.Zero);
        var intOut = new VisualPin(PinId.New(), node.Id, "Result", PinDirection.Output, PinKind.Data, "System.Int64");

        var suggestions = ContextCompletionEngine.SuggestForPin(intOut, indexer, query: "SetCoordinates");
        Assert.That(suggestions.Count, Is.GreaterThan(0));
        Assert.That(suggestions[0].Item.DisplayName, Does.Contain("SetCoordinates"));
        Assert.That(suggestions[0].TargetPinToConnect.Name, Is.EqualTo("entityUid"));
    }
}
