using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Compiler;
using AstraGraph.UI.Diff;
using AstraGraph.UI.Model;
using AstraGraph.UI.Runtime;
using AstraGraph.UI.Serialization;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class UiControlCatalogTests
{
    [Test]
    public void Catalog_FindsBuiltinControlsPropertiesAndEvents()
    {
        var catalog = BuiltinUiCatalog.Create();

        Assert.That(catalog.TryGet(UiControlIds.Button, out var button), Is.True);
        Assert.That(button!.Properties.Any(property => property.Name == "Text"), Is.True);
        Assert.That(button.Properties.Any(property => property.Name == "Disabled" && property.EditorKind == UiPropertyEditorKind.Boolean), Is.True);
        Assert.That(catalog.TryGet(UiControlIds.Label, out _), Is.True);
        Assert.That(catalog.TryGet(UiControlIds.BoxContainer, out var box), Is.True);
        Assert.That(box!.Properties.Single(property => property.Name == "Orientation").EditorKind, Is.EqualTo(UiPropertyEditorKind.Enum));
        Assert.That(button.Events.Any(item => item.Name == "OnPressed"), Is.True);
        Assert.That(catalog.GraphBindings(UiControlIds.Button).Any(binding => binding.Kind == "Event" && binding.Name == "OnPressed"), Is.True);
    }

    [Test]
    public void Catalog_IndexesCustomControlAndRejectsHiddenMembers()
    {
        var catalog = new UiControlCatalog();
        catalog.IndexAssembly(typeof(CatalogButton).Assembly, typeof(CatalogControlBase));

        Assert.That(catalog.TryGet(typeof(NightCityCyberdeckSlot).FullName!, out var slot), Is.True);
        Assert.That(slot!.DisplayName, Is.EqualTo("Night City Cyberdeck Slot"));
        Assert.That(catalog.TryGet(typeof(CatalogButton).FullName!, out var button), Is.True);
        Assert.That(button!.Properties.Any(property => property.Name == "Text"), Is.True);
        Assert.That(button.Properties.Any(property => property.Name == "Mode" && property.EditorKind == UiPropertyEditorKind.Enum), Is.True);
        Assert.That(button.Properties.Any(property => property.Name == "Secret"), Is.False);
        Assert.That(button.Events.Single().Name, Is.EqualTo("OnPressed"));
        Assert.That(button.Events.Single().Payload.Any(field => field.Name == "Text"), Is.True);

        Assert.That(catalog.TryCreate(typeof(CatalogButton).FullName!, out var instance), Is.True);
        Assert.That(instance, Is.InstanceOf<CatalogButton>());
        Assert.That(catalog.TrySetProperty(instance!, "Text", "Hi"), Is.True);
        Assert.That(((CatalogButton)instance!).Text, Is.EqualTo("Hi"));
        Assert.That(catalog.TryCreate(typeof(NightCityCyberdeckSlot).FullName!, out var custom), Is.True);
        Assert.That(custom, Is.InstanceOf<NightCityCyberdeckSlot>());
    }

    [Test]
    public void Document_RoundTripsStableIdsAndPropertyOverrides()
    {
        var button = new UiElementNode
        {
            Id = "button-1",
            ElementType = UiElementType.Button,
            Name = "CraftButton",
            Text = "Craft"
        };
        button.Name = "ConfirmCraftButton";
        var document = new UiDocument
        {
            Id = GraphId.New(),
            Name = "Craft",
            Root = new UiElementNode
            {
                Id = "root",
                ElementType = UiElementType.BoxContainer,
                Children = [button]
            }
        };

        var restored = UiDocumentJson.Deserialize(UiDocumentJson.Serialize(document));
        Assert.That(restored.Root.Children[0].Id, Is.EqualTo("button-1"));
        Assert.That(restored.Root.Children[0].Name, Is.EqualTo("ConfirmCraftButton"));
        Assert.That(restored.Root.Children[0].Text, Is.EqualTo("Craft"));
        Assert.That(restored.Root.Children[0].ControlTypeId, Is.EqualTo(UiControlIds.Button));
        Assert.That(restored.Root.Children[0].Properties.ContainsKey("Visible"), Is.False);
    }

    [Test]
    public void Compiler_ReportsUnknownControlInvalidChildAndBindingMismatch()
    {
        var catalog = BuiltinUiCatalog.Create();
        var label = new UiElementNode { Id = "label", ElementType = UiElementType.Label, Text = "Name" };
        label.AddChild(new UiElementNode { Id = "child", ElementType = UiElementType.Button });
        var unknown = new UiDocument
        {
            Id = GraphId.New(),
            Name = "Broken",
            Root = new UiElementNode { Id = "root", ControlTypeId = "Missing.Control", Children = [label] },
            StateVariables = [new UiStateVariable { Id = "flag", Name = "flag", TypeName = "bool", DefaultValue = true }],
            Bindings =
            [
                new UiBindingDefinition
                {
                    BindingId = "bind",
                    ElementId = "label",
                    TargetProperty = "Text",
                    StateVariable = "flag",
                    Direction = BindingDirection.OneWay
                }
            ]
        };

        var result = UiCompiler.Compile(unknown, catalog, strict: true);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(item => item.Code == "UI0010" && item.ElementId == "root"), Is.True);
        Assert.That(result.Diagnostics.Any(item => item.Code == "UI0013"), Is.True);
        Assert.That(result.Diagnostics.Any(item => item.Code == "UI0014" && item.BindingId == "bind"), Is.True);
    }

    [Test]
    public void Reconciler_ReordersDeletesReparentsAndReplacesType()
    {
        var factory = new MockRobustUiControlFactory();
        var reconciler = new RobustUiReconciler(factory);
        var first = Document("A", "B", "C");
        var initial = reconciler.Reconcile(UiCompiler.Compile(first).Program!);
        var button = initial.ControlsById["B"];
        button.GetType().GetProperty("IsFocused")!.SetValue(button, true);

        var reordered = Document("C", "A", "B");
        var second = reconciler.Reconcile(UiCompiler.Compile(reordered).Program!, initial.RootControl);
        Assert.That(second.RootControl.Children.Select(child => child.Id).ToArray(), Is.EqualTo(new[] { "C", "A", "B" }));
        Assert.That(second.ControlsById["B"], Is.SameAs(button));
        Assert.That(((MockRobustUiControl)second.ControlsById["B"]).IsFocused, Is.True);

        var deleted = Document("A");
        var third = reconciler.Reconcile(UiCompiler.Compile(deleted).Program!, second.RootControl);
        Assert.That(third.ControlsById.ContainsKey("B"), Is.False);
        Assert.That(third.RootControl.Children.Select(child => child.Id).ToArray(), Is.EqualTo(new[] { "A" }));

        var replaced = new UiDocument
        {
            Id = first.Id,
            Name = "Tree",
            Root = new UiElementNode
            {
                Id = "root",
                ElementType = UiElementType.BoxContainer,
                Children = [new UiElementNode { Id = "A", ElementType = UiElementType.Label, Text = "A" }]
            }
        };
        var fourth = reconciler.Reconcile(UiCompiler.Compile(replaced).Program!, third.RootControl);
        Assert.That(fourth.ControlsById["A"].ElementType, Is.EqualTo(UiElementType.Label));
        Assert.That(fourth.ControlsById["A"], Is.Not.SameAs(third.ControlsById["A"]));
    }

    [Test]
    public void Bui_ServerStateReachesControlAndRejectsStaleOrInvalidActions()
    {
        var contract = new UiBuiContractDocument
        {
            Id = "contract",
            State = [new UiStateVariable { Id = "count", Name = "count", TypeName = "int", DefaultValue = 1, Scope = UiStateScope.Server }],
            Actions = [new UiBuiAction { Id = "convert", Name = "Convert", Parameters = [new UiEventPayloadField("count", "int")] }]
        };
        var server = new BuiAuthoritativeSession(contract);
        Assert.That(server.TryHandleAction("Convert", new Dictionary<string, object?> { ["count"] = 3 }, server.Revision, out var error), Is.True, error);
        Assert.That(server.SetState("count", 3, out error), Is.True, error);

        var stateDocument = new UiDocument
        {
            Id = GraphId.New(),
            Name = "Count",
            Root = new UiElementNode { Id = "count-label", ElementType = UiElementType.Label },
            Bindings =
            [
                new UiBindingDefinition
                {
                    BindingId = "count-bind",
                    ElementId = "count-label",
                    TargetProperty = "Text",
                    StateVariable = "count",
                    Direction = BindingDirection.OneWay
                }
            ]
        };
        var session = new UiHotReloadHandler(new MockRobustUiControlFactory()).InitializeSession(stateDocument);
        var bridge = new AstraBuiBridge(session.StateManager, _ => { });
        var engine = AstraBuiRobustAdapterOrSnapshot(server);
        Assert.That(bridge.ApplyAuthoritative(engine), Is.True);
        Assert.That(session.StateManager.GetVariable("count"), Is.EqualTo(3));
        Assert.That(session.ControlsById["count-label"].Text, Is.EqualTo("3"));

        var seen = server.Revision;
        Assert.That(server.SetState("count", 4, out _), Is.True);
        Assert.That(server.TryHandleAction("Convert", new Dictionary<string, object?> { ["count"] = 4 }, seen, out error), Is.False);
        Assert.That(error, Does.Contain("Stale"));
        Assert.That(server.TryHandleAction("Missing", new Dictionary<string, object?>(), server.Revision, out error), Is.False);
        Assert.That(error, Does.Contain("Unknown"));
    }

    [Test]
    public void XamlImport_PreservesHierarchyAndReportsUnsupportedMarkup()
    {
        var imported = UiXamlAdapter.Import("""
            <BoxContainer Orientation="Horizontal">
              <Label Name="Title" Text="Hello" />
              <Button Text="{Binding Craft}" />
            </BoxContainer>
            """);
        Assert.That(imported.Document!.Root.Orientation, Is.EqualTo(UiOrientation.Horizontal));
        Assert.That(imported.Document.Root.Children[0].Text, Is.EqualTo("Hello"));
        Assert.That(imported.Diagnostics.Any(item => item.Code == "UIXAML001"), Is.True);
        var exported = UiXamlAdapter.Export(imported.Document);
        Assert.That(exported, Does.Contain("Title"));
    }

    [Test]
    public void SemanticDiff_DescribesPropertyAndAddedControl()
    {
        var before = Document("A");
        before.Root.Children[0].Text = "Craft";
        var after = Document("A", "B");
        after.Root.Children[0].Text = "Build Weapon";
        var diff = UiSemanticDiff.Compare(before, after);
        Assert.That(diff.Any(entry => entry.Detail.Contains("Craft") && entry.Detail.Contains("Build Weapon")), Is.True);
        Assert.That(diff.Any(entry => entry.Kind == "Added"), Is.True);
    }

    private static AstraBuiContract AstraBuiRobustAdapterOrSnapshot(BuiAuthoritativeSession server)
    {
        var snapshot = server.Snapshot();
        var encoded = UiValueCodec.DecodeMap(UiValueCodec.EncodeMap(snapshot.State));
        return snapshot with { State = encoded };
    }

    private static UiDocument Document(params string[] ids)
    {
        var root = new UiElementNode { Id = "root", ElementType = UiElementType.BoxContainer };
        foreach (var id in ids)
        {
            root.AddChild(new UiElementNode { Id = id, ElementType = UiElementType.Button, Text = id });
        }

        return new UiDocument { Id = GraphId.New(), Name = "Tree", Root = root };
    }
}

public abstract class CatalogControlBase;

    public sealed class CatalogButton : CatalogControlBase
    {
        public string Text { get; set; } = "";

        public bool Disabled { get; set; }

        public CatalogMode Mode { get; set; }

        [AstraHidden]
        public string Secret { get; set; } = "";

        public event EventHandler<CatalogPressedEventArgs>? OnPressed;

        public void Raise() => OnPressed?.Invoke(this, new CatalogPressedEventArgs { Text = Text });
    }

    public sealed class NightCityCyberdeckSlot : CatalogControlBase
    {
        public string SlotId { get; set; } = "";
    }

    public enum CatalogMode
    {
        Idle,
        Armed
    }

public sealed class CatalogPressedEventArgs : EventArgs
{
    public string Text { get; init; } = "";
}
