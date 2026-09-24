using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Logic;
using AstraGraph.UI.Model;
using AstraGraph.UI.Runtime;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class UiReleaseTests
{
    [Test]
    public void LogicGraphs_CompileAndRunTheBuiRoundTrip()
    {
        var document = Converter();
        var bundle = UiLogicGraph.Build(document);
        var analyzer = new SemanticAnalyzer();

        var client = analyzer.Analyze(bundle.Client);
        var server = analyzer.Analyze(bundle.Server);
        Assert.That(client.Success, Is.True, string.Join("; ", client.Diagnostics.Select(item => item.Message)));
        Assert.That(server.Success, Is.True, string.Join("; ", server.Diagnostics.Select(item => item.Message)));
        Assert.That(bundle.Client.Nodes.Any(node => node.NodeType == "UI.OnEvent"), Is.True);
        Assert.That(bundle.Client.Nodes.Any(node => node.Properties.GetValueOrDefault("Method") == "Ui.SendAction"), Is.True);
        Assert.That(bundle.Server.Nodes.Any(node => node.NodeType == "UI.OnAction"), Is.True);

        var interpreter = new UiLogicInterpreter(bundle);
        var local = new UiStateManager(new Dictionary<string, object?> { ["inputText"] = "hello" });
        string? sent = null;
        object? payload = null;
        Assert.That(interpreter.DispatchClient("submit", "OnPressed", local, (action, value) =>
        {
            sent = action;
            payload = value;
        }, out var actionName), Is.True);
        Assert.That(actionName, Is.EqualTo("Submit"));
        Assert.That(sent, Is.EqualTo("Submit"));
        Assert.That(payload, Is.EqualTo("hello"));

        var session = new BuiAuthoritativeSession(document.Contract!);
        Assert.That(interpreter.DispatchServer(session, "Submit", new Dictionary<string, object?> { ["text"] = "hello" }, session.Revision, out var error), Is.True, error);
        Assert.That(session.State["result"], Is.EqualTo("hello"));
        Assert.That(session.Revision, Is.EqualTo(1));
    }

    [Test]
    public void Package_PublishesOneRevisionAndRollsBack()
    {
        var store = new UiPackageStore();
        var first = store.Publish(Converter());
        var edited = Converter();
        edited.Name = "Mothroach Converter v2";
        var second = store.Publish(edited);

        Assert.That(first.Revision, Is.EqualTo(1));
        Assert.That(second.Revision, Is.EqualTo(2));
        Assert.That(second.ClientLogic.Nodes, Is.Not.Empty);
        Assert.That(second.ServerLogic.Nodes, Is.Not.Empty);
        Assert.That(store.TryRollback(out var restored), Is.True);
        Assert.That(restored!.Revision, Is.EqualTo(1));
        Assert.That(store.Current!.Document.Name, Is.EqualTo("Mothroach Converter"));
    }

    [Test]
    public void Preview_HotReloadKeepsLineEditText()
    {
        var document = Converter();
        var preview = new UiPreviewSession(new MockRobustUiControlFactory());
        Assert.That(preview.Open(document, out var diagnostics), Is.True, string.Join("; ", diagnostics.Select(item => item.Message)));
        preview.Session!.StateManager.SetVariable("inputText", "kept");
        var input = preview.Session.ControlsById["input"];
        input.Text = "kept";

        var next = Converter();
        next.Root.Children.Add(new UiElementNode
        {
            Id = "note",
            ElementType = UiElementType.Label,
            Text = "Saved"
        });
        Assert.That(preview.Update(next, out diagnostics), Is.True, string.Join("; ", diagnostics.Select(item => item.Message)));
        Assert.That(preview.PreservedIds, Does.Contain("input"));
        Assert.That(ReferenceEquals(input, preview.Session.ControlsById["input"]), Is.True);
        Assert.That(preview.Session.ControlsById["input"].Text, Is.EqualTo("kept"));
        Assert.That(preview.Session.StateManager.GetVariable("inputText"), Is.EqualTo("kept"));
    }

    [Test]
    public void ComponentsLocalizationAndProfiler_AreReady()
    {
        var button = new UiElementNode { Id = "craft", ElementType = UiElementType.Button, Text = "Craft" };
        button.Properties["LocId"] = "craft";
        var component = new UiComponentDefinition { Id = "craft-button", Name = "Craft Button", Root = button };
        var instance = UiComponents.Instantiate(component);
        Assert.That(instance.Id, Is.Not.EqualTo("craft"));
        Assert.That(instance.Text, Is.EqualTo("Craft"));

        var document = new UiDocument
        {
            Id = GraphId.New(),
            Name = "Localized",
            Root = new UiElementNode { Id = "root", ElementType = UiElementType.BoxContainer, Children = [button] }
        };
        UiLocalization.Apply(document, "ru", new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["ru"] = new Dictionary<string, string> { ["craft"] = "Создать" }
        });
        Assert.That(document.Root.Children[0].Text, Is.EqualTo("Создать"));

        var compiled = AstraGraph.UI.Compiler.UiCompiler.Compile(document);
        var reconciler = new RobustUiReconciler(new MockRobustUiControlFactory());
        var result = reconciler.Reconcile(compiled.Program!);
        Assert.That(result.ControlsById, Is.Not.Empty);
        Assert.That(reconciler.LastControlCount, Is.EqualTo(result.ControlsById.Count));
        Assert.That(reconciler.LastReconcileMicroseconds, Is.GreaterThanOrEqualTo(0));
    }

    private static UiDocument Converter()
    {
        var label = new UiElementNode { Id = "result", ElementType = UiElementType.Label, Text = "" };
        var input = new UiElementNode { Id = "input", ElementType = UiElementType.LineEdit, Text = "" };
        var button = new UiElementNode { Id = "submit", ElementType = UiElementType.Button, Text = "Submit" };
        return new UiDocument
        {
            Id = GraphId.New(),
            Name = "Mothroach Converter",
            DocumentKind = "BUI",
            Root = new UiElementNode
            {
                Id = "root",
                ElementType = UiElementType.BoxContainer,
                Children = [label, input, button]
            },
            StateVariables =
            [
                new UiStateVariable { Id = "inputText", Name = "inputText", TypeName = "string", Scope = UiStateScope.Local, DefaultValue = "" },
                new UiStateVariable { Id = "result", Name = "result", TypeName = "string", Scope = UiStateScope.Server, DefaultValue = "" }
            ],
            Bindings =
            [
                new UiBindingDefinition { BindingId = "b1", ElementId = "input", TargetProperty = "Text", StateVariable = "inputText", Direction = BindingDirection.TwoWay },
                new UiBindingDefinition { BindingId = "b2", ElementId = "result", TargetProperty = "Text", StateVariable = "result", Direction = BindingDirection.OneWay }
            ],
            Events =
            [
                new UiEventSubscription { SubscriptionId = "e1", ElementId = "submit", EventName = "OnPressed", TargetAction = "Submit" }
            ],
            Logic =
            [
                new UiLogicStep { Id = "l1", Kind = "OnEvent", ElementId = "submit", EventName = "OnPressed" },
                new UiLogicStep { Id = "l2", Kind = "SendAction", ElementId = "submit", ActionName = "Submit", StateVariable = "inputText" },
                new UiLogicStep { Id = "l3", Kind = "SetState", ActionName = "Submit", StateVariable = "result", PropertyName = "text" }
            ],
            Contract = new UiBuiContractDocument
            {
                Id = "mothroach",
                State =
                [
                    new UiStateVariable { Id = "result", Name = "result", TypeName = "string", Scope = UiStateScope.Server, DefaultValue = "" }
                ],
                Actions =
                [
                    new UiBuiAction
                    {
                        Id = "submit",
                        Name = "Submit",
                        Parameters = [new UiEventPayloadField("text", "string")]
                    }
                ]
            }
        };
    }
}
