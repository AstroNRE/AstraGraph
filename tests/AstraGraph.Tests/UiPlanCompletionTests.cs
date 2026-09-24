using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Compiler;
using AstraGraph.UI.Logic;
using AstraGraph.UI.Model;
using AstraGraph.UI.Runtime;
using AstraGraph.UI.Serialization;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class UiPlanCompletionTests
{
    [Test]
    public void Samples_RunMothroachVehicleAndSurgery()
    {
        var analyzer = new SemanticAnalyzer();
        foreach (var document in new[] { UiSamples.Mothroach(), UiSamples.VehicleMaintenance(), UiSamples.Surgery() })
        {
            var bundle = UiLogicGraph.Build(document);
            var client = analyzer.Analyze(bundle.Client);
            var server = analyzer.Analyze(bundle.Server);
            Assert.That(client.Success, Is.True, document.Name + " " + string.Join("; ", client.Diagnostics.Select(item => item.Message)));
            Assert.That(server.Success, Is.True, document.Name + " " + string.Join("; ", server.Diagnostics.Select(item => item.Message)));
        }

        var mothroach = UiLogicGraph.Build(UiSamples.Mothroach());
        var player = new UiLogicInterpreter(mothroach);
        var local = new UiStateManager(new Dictionary<string, object?> { ["count"] = 3 });
        object? sent = null;
        Assert.That(player.DispatchClient("convert", "OnPressed", local, (_, value) => sent = value, out var action), Is.True);
        Assert.That(action, Is.EqualTo("Convert"));
        Assert.That(sent, Is.EqualTo(3));
        var session = new BuiAuthoritativeSession(UiSamples.Mothroach().Contract!)
        {
            Context = new BuiSessionContext("Yuri", "mothroach", "converter", "target")
        };
        Assert.That(player.DispatchServer(session, "Convert", new Dictionary<string, object?> { ["count"] = 3 }, 0, out var error), Is.True, error);
        Assert.That(session.State["count"], Is.EqualTo(3));
        Assert.That(player.Trace.Any(item => item.Kind == "context" && item.Detail.Contains("Yuri", StringComparison.Ordinal)), Is.True);

        var surgery = UiLogicGraph.Build(UiSamples.Surgery());
        Assert.That(surgery.Server.Nodes.Any(node => node.NodeType == "Flow.DoAfter"), Is.True);
        Assert.That(surgery.Client.Nodes.Any(node => node.NodeType == "UI.OnNotification"), Is.True);
        var surgeon = new UiLogicInterpreter(surgery);
        var authority = new BuiAuthoritativeSession(UiSamples.Surgery().Contract!);
        Assert.That(surgeon.DispatchServer(authority, "StartOperation", new Dictionary<string, object?> { ["progress"] = 0.7f }, 0, out error), Is.True, error);
        Assert.That(authority.State["progress"], Is.EqualTo(0.7f));
        Assert.That(authority.Notifications.Single().Name, Is.EqualTo("OperationFailed"));
        var clientState = new UiStateManager();
        Assert.That(surgeon.DispatchNotification("OperationFailed", new Dictionary<string, object?> { ["reason"] = "interrupted" }, clientState, out error), Is.True, error);
        Assert.That(clientState.GetVariable("reason"), Is.EqualTo("interrupted"));

        var bridge = new AstraBuiBridge(clientState, _ => { });
        Assert.That(bridge.ApplyAuthoritative(authority.Snapshot()), Is.True);
        Assert.That(clientState.GetVariable("progress"), Is.EqualTo(0.7f));
    }

    [Test]
    public void ClientGraph_CannotMutateServerState()
    {
        var bundle = UiLogicGraph.Build(UiSamples.Mothroach());
        var call = bundle.Client.Nodes.Single(node => node.NodeType == "Native.Call");
        var cheat = new NodeDocument
        {
            Id = NodeId.New(),
            Name = "Cheat",
            NodeType = "UI.SetBuiState",
            Pins =
            [
                new PinDocument { Id = PinId.New(), Name = "In", Direction = PinDirection.Input, Kind = PinKind.Execution }
            ]
        };
        bundle.Client.Nodes.Add(cheat);
        var output = call.FindPin("Out", PinDirection.Output)!;
        bundle.Client.Connections.Add(new ConnectionDocument
        {
            FromNode = call.Id,
            FromPin = output.Id,
            ToNode = cheat.Id,
            ToPin = cheat.Pins[0].Id
        });
        var interpreter = new UiLogicInterpreter(bundle);
        var sent = false;
        Assert.That(interpreter.DispatchClient("convert", "OnPressed", new UiStateManager(), (_, _) => sent = true, out _), Is.False);
        Assert.That(sent, Is.False);
        Assert.That(interpreter.Trace.Any(item => item.Kind == "security"), Is.True);
    }

    [Test]
    public void ElementReference_ReadsPropertyAndFocuses()
    {
        var document = UiSamples.Mothroach();
        document.Logic.Add(new UiLogicStep
        {
            Id = "read",
            Kind = "GetProperty",
            ElementId = "convert",
            PropertyName = "Text",
            Arguments = new Dictionary<string, string> { ["Target"] = "count" }
        });
        document.Logic.Add(new UiLogicStep
        {
            Id = "focus",
            Kind = "Focus",
            ElementId = "convert",
            Arguments = new Dictionary<string, string> { ["Target"] = "count" }
        });
        var bundle = UiLogicGraph.Build(document);
        Assert.That(new SemanticAnalyzer().Analyze(bundle.Client).Success, Is.True);
        var count = new MockRobustUiControl("count", UiElementType.LineEdit) { Text = "3" };
        var controls = new Dictionary<string, IRobustUiControl> { ["count"] = count };
        var interpreter = new UiLogicInterpreter(bundle);
        Assert.That(interpreter.DispatchClient("convert", "OnPressed", new UiStateManager(new Dictionary<string, object?> { ["count"] = 3 }), (_, _) => { }, out _, controls), Is.True);
        Assert.That(interpreter.Trace.Any(item => item.Kind == "get" && item.Detail.Contains('3')), Is.True);
        Assert.That(count.IsFocused, Is.True);
        Assert.That(UiGraphApi.ForDocument(BuiltinUiCatalog.Create(), document).Any(item => item.Kind == "Get" && item.Name == "Text" && item.ElementId == "convert"), Is.True);
    }

    [Test]
    public void Compiler_ChecksExpressionsAndStyleClasses()
    {
        var catalog = BuiltinUiCatalog.Create();
        catalog.RegisterStyleClass("danger");
        var button = new UiElementNode { Id = "button", ElementType = UiElementType.Button, Text = "Go" };
        button.StyleClasses.Add("missing");
        var document = new UiDocument
        {
            Id = GraphId.New(),
            Name = "Checked",
            Root = new UiElementNode { Id = "root", ElementType = UiElementType.BoxContainer, Children = [button] },
            StateVariables = [new UiStateVariable { Id = "count", Name = "count", TypeName = "int", DefaultValue = 3 }],
            Bindings =
            [
                new UiBindingDefinition
                {
                    BindingId = "label",
                    ElementId = "button",
                    TargetProperty = "Text",
                    StateVariable = "count",
                    Expression = "missingName"
                }
            ]
        };
        var failed = UiCompiler.Compile(document, catalog, strict: true);
        Assert.That(failed.Diagnostics.Any(item => item.Code == "UI0020"), Is.True);
        Assert.That(failed.Diagnostics.Any(item => item.Code == "UI0021"), Is.True);
        Assert.That(UiExpression.TryEvaluate("count + \" left\"", new Dictionary<string, object?> { ["count"] = 3 }, out var value, out _), Is.True);
        Assert.That(value, Is.EqualTo("3 left"));
    }

    [Test]
    public void PatchStyleAssetsAndComponents_AreUsable()
    {
        var document = UiSamples.Mothroach();
        Assert.That(UiDocumentPatch.Apply(document,
        [
            new UiPatchOperation("SetProperty", "convert", "Text", "Convert"),
            new UiPatchOperation("InsertElement", "note", ParentId: "root", ControlTypeId: UiControlIds.Label),
            new UiPatchOperation("ReorderChildren", "root", ParentId: "root", Index: 0, FromIndex: 1)
        ], out var error), Is.True, error);
        Assert.That(document.FindElement("convert")!.Text, Is.EqualTo("Convert"));
        Assert.That(document.FindElement("note"), Is.Not.Null);
        Assert.That(UiDocumentPatch.Apply(document, [new UiPatchOperation("DeleteElement", "note")], out error), Is.True, error);
        Assert.That(document.FindElement("note"), Is.Null);

        var catalog = BuiltinUiCatalog.Create();
        UiStyleCatalog.Register(catalog, ".danger { }\nStyleClass(\"windowTitle\")");
        Assert.That(catalog.StyleClasses, Does.Contain("danger"));
        Assert.That(catalog.StyleClasses, Does.Contain("windowTitle"));
        var assets = new UiAssetCatalog();
        assets.Add("texture", "Resources/Textures/mothroach.png");
        Assert.That(assets.OfKind("texture").Single().Label, Is.EqualTo("mothroach.png"));

        var slot = new UiElementNode { Id = "name", ElementType = UiElementType.Label, Text = "Slot" };
        var component = new UiComponentDefinition
        {
            Id = "slot",
            Name = "WeaponSlot",
            Root = slot,
            Inputs = [new UiComponentInput { Name = "name", TargetElementId = "name", PropertyName = "Text" }],
            Outputs = [new UiComponentOutput { Name = "Selected", ElementId = "name" }]
        };
        var instance = UiComponents.Instantiate(component, new Dictionary<string, string> { ["name"] = "Sabre" });
        Assert.That(instance.Id, Is.Not.EqualTo("name"));
        Assert.That(instance.Text, Is.EqualTo("Sabre"));
        Assert.That(BuiltinUiCatalog.Create().TryGet(UiControlIds.ItemList, out var list), Is.True);
        Assert.That(list!.Events.Any(item => item.Name == "OnItemSelected"), Is.True);
    }
}
