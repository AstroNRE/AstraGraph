using AstraGraph.Core;
using AstraGraph.Editor.InGame;
using AstraGraph.Editor.Protocol;
using AstraGraph.UI.Compiler;
using AstraGraph.UI.Model;
using AstraGraph.UI.Runtime;
using NUnit.Framework;

#if NET10_0_OR_GREATER
using AstraGraph.Robust.Client;
#endif

namespace AstraGraph.Tests;

[TestFixture]
public sealed class RobustUiReconciliationTests
{
#if NET10_0_OR_GREATER
    [Test]
    public void RobustUiControlFactory_InstantiatesControlsAndBindsProperties()
    {
        var factory = new RobustUiControlFactory();

        // 1. BoxContainer
        var boxWrapper = factory.CreateControl("box1", UiElementType.BoxContainer, "MainBox", 100, 200, UiOrientation.Horizontal);
        Assert.That(boxWrapper, Is.Not.Null);
        Assert.That(boxWrapper.ElementType, Is.EqualTo(UiElementType.BoxContainer));

        // 2. Button with Event Wireup
        var btnWrapper = factory.CreateControl("btn1", UiElementType.Button, "SubmitBtn", null, null, UiOrientation.Vertical);
        Assert.That(btnWrapper, Is.Not.Null);
        Assert.That(btnWrapper.ElementType, Is.EqualTo(UiElementType.Button));

        string? triggeredEvent = null;
        btnWrapper.OnEventTriggered += (evt, _) => triggeredEvent = evt;

        btnWrapper.SetProperty("Text", "Click Here");
        Assert.That(btnWrapper.Text, Is.EqualTo("Click Here"));

        btnWrapper.TriggerEvent("OnPressed");
        Assert.That(triggeredEvent, Is.EqualTo("OnPressed"));

        // 3. LineEdit
        var editWrapper = factory.CreateControl("edit1", UiElementType.LineEdit, "InputBox", null, null, UiOrientation.Vertical);
        Assert.That(editWrapper, Is.Not.Null);
        editWrapper.SetProperty("Text", "Hello SS14");
        Assert.That(editWrapper.Text, Is.EqualTo("Hello SS14"));

        // 4. Label
        var lblWrapper = factory.CreateControl("lbl1", UiElementType.Label, "StatusLabel", null, null, UiOrientation.Vertical);
        Assert.That(lblWrapper, Is.Not.Null);
        lblWrapper.SetProperty("Text", "Status: Active");
        Assert.That(lblWrapper.Text, Is.EqualTo("Status: Active"));
    }

    [Test]
    public void RobustUiReconciler_BuildsVisualTreeStructure()
    {
        var factory = new RobustUiControlFactory();
        var reconciler = new RobustUiReconciler(factory);

        var doc = new UiDocument
        {
            Id = GraphId.New(),
            Name = "PlayerStatsWindow",
            Root = new UiElementNode
            {
                Id = "root",
                ElementType = UiElementType.BoxContainer,
                Name = "RootBox",
                Orientation = UiOrientation.Vertical,
                Children =
                [
                    new UiElementNode
                    {
                        Id = "lbl_title",
                        ElementType = UiElementType.Label,
                        Name = "TitleLabel",
                        Text = "Character Stats"
                    },
                    new UiElementNode
                    {
                        Id = "btn_upgrade",
                        ElementType = UiElementType.Button,
                        Name = "UpgradeButton",
                        Text = "Upgrade Skill"
                    }
                ]
            }
        };

        var ir = UiCompiler.Compile(doc);
        Assert.That(ir.Success, Is.True);
        var result = reconciler.Reconcile(ir.Program!);

        Assert.That(result.RootControl, Is.Not.Null);
        var root = result.RootControl;

        // Verify child count and types
        Assert.That(root.Children.Count, Is.EqualTo(2));
        Assert.That(root.Children[0].ElementType, Is.EqualTo(UiElementType.Label));
        Assert.That(root.Children[1].ElementType, Is.EqualTo(UiElementType.Button));

        Assert.That(root.Children[0].Text, Is.EqualTo("Character Stats"));
        Assert.That(root.Children[1].Text, Is.EqualTo("Upgrade Skill"));
    }

    [Test]
    public void RobustUiReconciler_ZeroFlicker_RetainsControlInstancesAcrossReconcile()
    {
        var factory = new RobustUiControlFactory();
        var reconciler = new RobustUiReconciler(factory);

        // V1 Document
        var docV1 = new UiDocument
        {
            Id = GraphId.New(),
            Name = "InventoryWindow",
            Root = new UiElementNode
            {
                Id = "root",
                ElementType = UiElementType.BoxContainer,
                Children =
                [
                    new UiElementNode
                    {
                        Id = "persistent_btn",
                        ElementType = UiElementType.Button,
                        Text = "Initial Action"
                    }
                ]
            }
        };

        var irV1 = UiCompiler.Compile(docV1);
        Assert.That(irV1.Success, Is.True);
        var resV1 = reconciler.Reconcile(irV1.Program!);
        var btnV1 = resV1.ControlsById["persistent_btn"];

        // V2 Document: Text changes, but element ID and type are the same
        var docV2 = new UiDocument
        {
            Id = docV1.Id,
            Name = "InventoryWindow",
            Root = new UiElementNode
            {
                Id = "root",
                ElementType = UiElementType.BoxContainer,
                Children =
                [
                    new UiElementNode
                    {
                        Id = "persistent_btn",
                        ElementType = UiElementType.Button,
                        Text = "Updated Action"
                    },
                    new UiElementNode
                    {
                        Id = "new_lbl",
                        ElementType = UiElementType.Label,
                        Text = "New Item Added"
                    }
                ]
            }
        };

        var irV2 = UiCompiler.Compile(docV2);
        Assert.That(irV2.Success, Is.True);
        var resV2 = reconciler.Reconcile(irV2.Program!, existingRoot: resV1.RootControl);
        var btnV2 = resV2.ControlsById["persistent_btn"];

        // ZERO FLICKER ASSERTION: The Control instance MUST be retained across hot reload reconciliations!
        Assert.That(ReferenceEquals(btnV1, btnV2), Is.True, "Control instance must be retained to avoid flickering or input focus loss!");
        Assert.That(btnV2.Text, Is.EqualTo("Updated Action"));

        // New child was attached
        Assert.That(resV2.RootControl.Children.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ClientAstraGraphSystem_Launcher_BuildsDeepLinkUrlsCorrectly()
    {
        var clientSystem = new ClientAstraGraphSystem();
        clientSystem.EnsureBridge(new PingPongHandler());

        var launcher = clientSystem.Launcher;
        Assert.That(launcher, Is.Not.Null);

        // Start bridge to get a valid URL
        await clientSystem.LocalBridge!.StartAsync();

        // 1. Inspect Entity URL
        var inspectUrl = launcher!.BuildUrl(new StudioDeepLinkContext
        {
            Action = StudioAction.InspectEntity,
            EntityUid = "42"
        });
        Assert.That(inspectUrl, Does.Contain("action=inspectentity"));
        Assert.That(inspectUrl, Does.Contain("entity=42"));
        Assert.That(inspectUrl, Does.Contain("nonce="));

        // 2. Open Runtime Error URL
        var errorUrl = launcher.BuildUrl(new StudioDeepLinkContext
        {
            Action = StudioAction.OpenRuntimeError,
            GraphId = "test-graph",
            NodeId = "node-101",
            DiagnosticCode = "AG0042",
            ExecutionTick = 12345
        });
        Assert.That(errorUrl, Does.Contain("action=openruntimeerror"));
        Assert.That(errorUrl, Does.Contain("graph=test-graph"));
        Assert.That(errorUrl, Does.Contain("node=node-101"));
        Assert.That(errorUrl, Does.Contain("error=AG0042"));
        Assert.That(errorUrl, Does.Contain("tick=12345"));

        await clientSystem.LocalBridge.StopAsync();
    }

    [Test]
    public void RobustPreview_StaysHeadlessWithoutAClientWindow()
    {
        var host = new RobustUiPreviewHost(new RobustUiControlFactory());
        var document = new UiDocument
        {
            Id = GraphId.New(),
            Name = "Preview",
            Root = new UiElementNode
            {
                Id = "root",
                ElementType = UiElementType.BoxContainer,
                Children = [new UiElementNode { Id = "input", ElementType = UiElementType.LineEdit, Text = "open" }]
            }
        };

        Assert.That(host.Update(document, out var diagnostics), Is.True, string.Join("; ", diagnostics.Select(item => item.Message)));
        Assert.That(host.Mode, Is.EqualTo("headless"));
        Assert.That(host.IsWindowOpen, Is.False);
        Assert.That(host.Session!.ControlsById["input"].Text, Is.EqualTo("open"));
    }
#endif
}
