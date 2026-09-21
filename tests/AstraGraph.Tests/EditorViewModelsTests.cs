using System;
using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.Editor.Core;
using AstraGraph.Editor.Core.ViewModels;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class EditorViewModelsTests
{
    [Test]
    public void InspectorViewModel_SelectNodeAndEditProperties()
    {
        var vm = new InspectorViewModel();
        var node = new VisualNode(NodeId.New(), "InitialName", "Core.Branch", CanvasPoint.Zero);
        var changedFired = false;
        vm.OnChanged += () => changedFired = true;

        vm.SelectNode(node);
        Assert.That(vm.HasSelectedNode, Is.True);
        Assert.That(changedFired, Is.True);

        changedFired = false;
        vm.RenameSelectedNode("NewBranch");
        Assert.That(node.Name, Is.EqualTo("NewBranch"));
        Assert.That(changedFired, Is.True);

        vm.SetProperty("Key1", "Value1");
        Assert.That(node.Properties["Key1"], Is.EqualTo("Value1"));

        vm.RemoveProperty("Key1");
        Assert.That(node.Properties.ContainsKey("Key1"), Is.False);
    }

    [Test]
    public void VariablesViewModel_AddUpdateRemoveAndValidation()
    {
        var vm = new VariablesViewModel();
        var changedFired = false;
        vm.OnChanged += () => changedFired = true;

        // Add variable 1
        var success1 = vm.TryAddVariable("Counter", "System.Int64", "0", persistent: true, replicated: false, out var error1);
        Assert.That(success1, Is.True);
        Assert.That(error1, Is.Null);
        Assert.That(vm.Variables.Count, Is.EqualTo(1));
        Assert.That(changedFired, Is.True);

        // Try duplicate name
        var successDup = vm.TryAddVariable("Counter", "System.Int32", "1", false, false, out var errorDup);
        Assert.That(successDup, Is.False);
        Assert.That(errorDup, Does.Contain("already exists"));

        // Update variable
        var varId = vm.Variables[0].Id;
        var updateSuccess = vm.TryUpdateVariable(varId, "Score", "System.Int64", "10", persistent: true, replicated: true, out _);
        Assert.That(updateSuccess, Is.True);
        Assert.That(vm.Variables[0].Name, Is.EqualTo("Score"));
        Assert.That(vm.Variables[0].IsReplicated, Is.True);

        // ToDocuments and LoadFromDocuments
        var docs = vm.ToDocuments();
        Assert.That(docs.Count, Is.EqualTo(1));
        Assert.That(docs[0].Name, Is.EqualTo("Score"));

        var vm2 = new VariablesViewModel();
        vm2.LoadFromDocuments(docs);
        Assert.That(vm2.Variables.Count, Is.EqualTo(1));
        Assert.That(vm2.Variables[0].Name, Is.EqualTo("Score"));

        // Remove
        vm2.RemoveVariable(varId);
        Assert.That(vm2.Variables.Count, Is.EqualTo(0));
    }

    [Test]
    public void ProblemsViewModel_CategorizesDiagnosticsAndFilters()
    {
        var vm = new ProblemsViewModel();
        var nodeId = NodeId.New();

        var diagnostics = new List<Diagnostic>
        {
            new("ERR001", DiagnosticSeverity.Error, "Type mismatch", nodeId),
            new("WARN001", DiagnosticSeverity.Warning, "Unused pin", nodeId),
            new("SEC002", DiagnosticSeverity.Error, "Calling dangerous native method", nodeId),
            new("PRED003", DiagnosticSeverity.Warning, "Non-deterministic call in predicted graph", nodeId)
        };

        vm.UpdateDiagnostics(diagnostics);

        Assert.That(vm.HasErrors, Is.True);
        Assert.That(vm.ErrorCount, Is.EqualTo(2));
        Assert.That(vm.WarningCount, Is.EqualTo(2));

        // Filter: Errors only
        vm.CurrentFilter = ProblemCategory.Errors;
        Assert.That(vm.FilteredItems.Count, Is.EqualTo(2));

        // Filter: Security only
        vm.CurrentFilter = ProblemCategory.Security;
        Assert.That(vm.FilteredItems.Count, Is.EqualTo(1));
        Assert.That(vm.FilteredItems[0].Code, Is.EqualTo("SEC002"));
        Assert.That(vm.FilteredItems[0].RelatedNodeId, Is.EqualTo(nodeId));

        // Filter: Prediction only
        vm.CurrentFilter = ProblemCategory.Prediction;
        Assert.That(vm.FilteredItems.Count, Is.EqualTo(1));
        Assert.That(vm.FilteredItems[0].Code, Is.EqualTo("PRED003"));
    }

    [Test]
    public void HistoryViewModel_SetsRevisionsAndAllowsRollback()
    {
        var vm = new HistoryViewModel();
        var rev1 = RevisionId.New();
        var rev2 = RevisionId.New();

        var history = new List<(RevisionId Id, RevisionId? ParentId, string Author, string Message, DateTimeOffset Timestamp, string SemanticHash)>
        {
            (rev1, null, "Author1", "Initial version", DateTimeOffset.UtcNow.AddMinutes(-10), "hash1"),
            (rev2, rev1, "Author2", "Bugfix", DateTimeOffset.UtcNow.AddMinutes(-2), "hash2")
        };

        vm.SetHistory(history, activeRevisionId: rev2);

        Assert.That(vm.Revisions.Count, Is.EqualTo(2));
        Assert.That(vm.ActiveRevisionId, Is.EqualTo(rev2));
        Assert.That(vm.SelectedRevision?.Id, Is.EqualTo(rev2));
        Assert.That(vm.CanRollback, Is.False); // Can't rollback to current active revision

        // Select older revision
        vm.SelectRevision(rev1);
        Assert.That(vm.SelectedRevision?.Id, Is.EqualTo(rev1));
        Assert.That(vm.CanRollback, Is.True); // Can rollback to older revision!
    }
}
