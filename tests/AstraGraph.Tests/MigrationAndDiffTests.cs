using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.State;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class MigrationAndDiffTests
{
    [Test]
    public void StateMigration_PreservesRenamedFields_WidensTypes_InitializesDefaults()
    {
        var schemaId = SchemaId.New();
        var f1Id = FieldId.New();
        var f2Id = FieldId.New();
        var f3Id = FieldId.New();

        // Schema v1: Heat (float32), Cooling (float32)
        var schemaV1 = new SchemaType(
            schemaId,
            "CyberwareHeat",
            IsComponentSchema: true,
            [
                new SchemaField(f1Id, "Heat", PrimitiveType.Float32, "0.0"),
                new SchemaField(f2Id, "Cooling", PrimitiveType.Float32, "2.0")
            ]);

        // Schema v2:
        // - f1Id renamed to "CurrentHeat"
        // - f2Id widened to float64
        // - f3Id added: "MaxHeat" = 100.0
        var schemaV2 = new SchemaType(
            schemaId,
            "CyberwareHeat",
            IsComponentSchema: true,
            [
                new SchemaField(f1Id, "CurrentHeat", PrimitiveType.Float32, "0.0"),
                new SchemaField(f2Id, "Cooling", PrimitiveType.Float64, "2.0"),
                new SchemaField(f3Id, "MaxHeat", PrimitiveType.Float32, "100.0")
            ]);

        var plan = StateMigrationPlanner.CreatePlan(schemaV1, schemaV2);

        Assert.That(plan.CanAutoMigrate, Is.True);
        Assert.That(plan.Steps.Count, Is.EqualTo(3));

        // Create old instance: Heat = 75.5f, Cooling = 10.0f
        var oldStorage = new PackedFieldStorage(schemaV1, [AstraValue.FromFloat(75.5f), AstraValue.FromFloat(10.0f)]);

        // Execute migration
        var newStorage = plan.Execute(oldStorage);

        Assert.That(newStorage.FieldCount, Is.EqualTo(3));
        Assert.That(newStorage.GetField(f1Id).AsFloat(), Is.EqualTo(75.5f));
        Assert.That(newStorage.GetField(f2Id).AsDouble(), Is.EqualTo(10.0).Within(0.001));
        Assert.That(newStorage.GetField(f3Id).AsFloat(), Is.EqualTo(100.0f));
    }

    [Test]
    public void StateMigration_IncompatibleTypeChange_BlocksAutoMigration()
    {
        var schemaId = SchemaId.New();
        var f1Id = FieldId.New();

        var schemaV1 = new SchemaType(
            schemaId,
            "Comp",
            IsComponentSchema: true,
            [new SchemaField(f1Id, "Data", PrimitiveType.Int32)]);

        var schemaV2 = new SchemaType(
            schemaId,
            "Comp",
            IsComponentSchema: true,
            [new SchemaField(f1Id, "Data", PrimitiveType.String)]);

        var plan = StateMigrationPlanner.CreatePlan(schemaV1, schemaV2);

        Assert.That(plan.CanAutoMigrate, Is.False);
        Assert.Throws<InvalidOperationException>(() => plan.Execute(new PackedFieldStorage(schemaV1)));
    }

    [Test]
    public void SemanticDiffEngine_DetectsAddedAndModifiedElements()
    {
        var node1Id = NodeId.New();
        var node2Id = NodeId.New();
        var varId = SymbolId.New();

        var doc1 = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "Doc1",
            Nodes = [new NodeDocument { Id = node1Id, Name = "N1", NodeType = "Type1" }],
            Variables = [new GraphVariableDocument { Id = varId, Name = "V1", TypeName = "int32", DefaultValue = "10" }]
        };

        var doc2 = new GraphDocument
        {
            Id = doc1.Id,
            Name = "Doc1",
            Nodes =
            [
                new NodeDocument { Id = node1Id, Name = "N1_Renamed", NodeType = "Type1" },
                new NodeDocument { Id = node2Id, Name = "N2_Added", NodeType = "Type2" }
            ],
            Variables = [new GraphVariableDocument { Id = varId, Name = "V1", TypeName = "int32", DefaultValue = "99" }]
        };

        var diff = SemanticDiffEngine.Diff(doc1, doc2);

        Assert.That(diff.HasSemanticChanges, Is.True);
        Assert.That(diff.NodesAdded.Count, Is.EqualTo(1));
        Assert.That(diff.NodesAdded[0].Id, Is.EqualTo(node2Id));
        Assert.That(diff.NodesModified.Count, Is.EqualTo(1));
        Assert.That(diff.VariablesModified.Count, Is.EqualTo(1));
    }
}
