using AstraGraph.Core;
using AstraGraph.UI.Compiler;
using AstraGraph.UI.Model;
using AstraGraph.UI.Runtime;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class UiSubsystemTests
{
    private static UiDocument CreateSampleAirlockUi()
    {
        var rootWindow = new UiElementNode
        {
            Id = "window-airlock",
            ElementType = UiElementType.Window,
            Name = "Airlock Control",
            MinWidth = 320,
            MinHeight = 240
        };

        var mainBox = new UiElementNode
        {
            Id = "box-main",
            ElementType = UiElementType.BoxContainer,
            Orientation = UiOrientation.Vertical
        };
        rootWindow.AddChild(mainBox);

        var statusLabel = new UiElementNode
        {
            Id = "lbl-status",
            ElementType = UiElementType.Label,
            Text = "Status: Locked"
        };
        mainBox.AddChild(statusLabel);

        var codeInput = new UiElementNode
        {
            Id = "input-access-code",
            ElementType = UiElementType.LineEdit,
            Text = ""
        };
        mainBox.AddChild(codeInput);

        var toggleBtn = new UiElementNode
        {
            Id = "btn-toggle-door",
            ElementType = UiElementType.Button,
            Text = "Toggle Door"
        };
        mainBox.AddChild(toggleBtn);

        return new UiDocument
        {
            Id = GraphId.New(),
            Name = "AirlockUi",
            Root = rootWindow,
            Bindings =
            [
                new UiBindingDefinition
                {
                    BindingId = "bind-status",
                    ElementId = "lbl-status",
                    TargetProperty = "Text",
                    StateVariable = "DoorStatus",
                    Direction = BindingDirection.OneWay
                },
                new UiBindingDefinition
                {
                    BindingId = "bind-code",
                    ElementId = "input-access-code",
                    TargetProperty = "Text",
                    StateVariable = "AccessCode",
                    Direction = BindingDirection.TwoWay
                }
            ],
            Events =
            [
                new UiEventSubscription
                {
                    SubscriptionId = "ev-toggle",
                    ElementId = "btn-toggle-door",
                    EventName = "OnPressed",
                    TargetAction = "ToggleAirlock"
                }
            ],
            LocalStateDefaults = new Dictionary<string, object?>
            {
                ["DoorStatus"] = "Status: Locked",
                ["AccessCode"] = "1234"
            }
        };
    }

    [Test]
    public void UiDocument_AllElements_FindsAllHierarchyNodes()
    {
        var doc = CreateSampleAirlockUi();
        var all = doc.AllElements().ToList();

        Assert.That(all.Count, Is.EqualTo(5));
        Assert.That(doc.FindElement("btn-toggle-door"), Is.Not.Null);
        Assert.That(doc.FindElement("non-existent"), Is.Null);
    }

    [Test]
    public void UiCompiler_ValidDocument_ProducesValidIrInstructions()
    {
        var doc = CreateSampleAirlockUi();
        var result = UiCompiler.Compile(doc);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Program, Is.Not.Null);
        Assert.That(result.Diagnostics, Is.Empty);

        var instructions = result.Program!.Instructions;
        Assert.That(instructions.OfType<CreateWidgetInstruction>().Count(), Is.EqualTo(5));
        Assert.That(instructions.OfType<AttachChildInstruction>().Count(), Is.EqualTo(4));
        Assert.That(instructions.OfType<RegisterBindingInstruction>().Count(), Is.EqualTo(2));
        Assert.That(instructions.OfType<RegisterEventInstruction>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void UiCompiler_DuplicateId_ReturnsDiagnosticError()
    {
        var doc = CreateSampleAirlockUi();
        // Add element with duplicate ID
        doc.Root.AddChild(new UiElementNode
        {
            Id = "lbl-status", // duplicate!
            ElementType = UiElementType.Label
        });

        var result = UiCompiler.Compile(doc);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Code == "UI0005"), Is.True);
    }

    [Test]
    public void UiBindingEngine_TwoWayBinding_SynchronizesStateAndControl()
    {
        var doc = CreateSampleAirlockUi();
        var handler = new UiHotReloadHandler(new MockRobustUiControlFactory());
        var session = handler.InitializeSession(doc);

        var codeInput = session.ControlsById["input-access-code"];
        var statusLabel = session.ControlsById["lbl-status"];

        // Initial state
        Assert.That(codeInput.Text, Is.EqualTo("1234"));
        Assert.That(statusLabel.Text, Is.EqualTo("Status: Locked"));

        // 1. State changed -> updates Control
        session.StateManager.SetVariable("DoorStatus", "Status: Bolted");
        Assert.That(statusLabel.Text, Is.EqualTo("Status: Bolted"));

        // 2. Control changed (user typing) -> updates State
        codeInput.Text = "9999";
        codeInput.TriggerEvent("TextChanged");
        Assert.That(session.StateManager.GetVariable("AccessCode"), Is.EqualTo("9999"));
    }

    [Test]
    public void RobustUiReconciler_PreservesExistingControls_WhenIdsMatch()
    {
        var doc = CreateSampleAirlockUi();
        var program = UiCompiler.Compile(doc).Program!;

        var factory = new MockRobustUiControlFactory();
        var reconciler = new RobustUiReconciler(factory);

        var firstRun = reconciler.Reconcile(program);
        var originalBtn = firstRun.ControlsById["btn-toggle-door"];

        // Run reconcile again with existing root
        var secondRun = reconciler.Reconcile(program, firstRun.RootControl);
        var reconciledBtn = secondRun.ControlsById["btn-toggle-door"];

        // Same object reference preserved (no flicker / recreation)
        Assert.That(reconciledBtn, Is.SameAs(originalBtn));
    }

    [Test]
    public void AstraBuiBridge_SyncsServerStateAndDispatchesActions()
    {
        var state = new UiStateManager();
        BuiMessage? lastSent = null;

        var bridge = new AstraBuiBridge(state, msg => lastSent = msg);

        var button = new MockRobustUiControl("btn-1", UiElementType.Button);
        bridge.ConnectEventSubscription(new RegisterEventInstruction("sub-1", "btn-1", "OnPressed", "AuthorizeUser", null), button);

        // Click button
        button.TriggerEvent("OnPressed");
        Assert.That(lastSent, Is.Not.Null);
        Assert.That(lastSent!.Action, Is.EqualTo("AuthorizeUser"));

        // Receive state from server
        bridge.ReceiveStateFromServer(new Dictionary<string, object?>
        {
            ["PowerState"] = "Online",
            ["Integrity"] = 100
        });

        Assert.That(state.GetVariable("PowerState"), Is.EqualTo("Online"));
        Assert.That(state.GetVariable<int>("Integrity"), Is.EqualTo(100));
    }

    [Test]
    public void UiHotReloadHandler_HotReloadsWithoutLosingState()
    {
        var docV1 = CreateSampleAirlockUi();
        var handler = new UiHotReloadHandler(new MockRobustUiControlFactory());
        var session = handler.InitializeSession(docV1);

        // User typed new access code in game
        var codeControl = session.ControlsById["input-access-code"];
        codeControl.Text = "7777";
        codeControl.TriggerEvent("TextChanged");
        Assert.That(session.StateManager.GetVariable("AccessCode"), Is.EqualTo("7777"));

        // Create doc V2 with an added Emergency Override button
        var docV2 = CreateSampleAirlockUi();
        var emergencyBtn = new UiElementNode
        {
            Id = "btn-emergency",
            ElementType = UiElementType.Button,
            Text = "EMERGENCY OVERRIDE"
        };
        docV2.FindElement("box-main")!.AddChild(emergencyBtn);

        // Hot reload live
        var ok = handler.TryHotReload(session, docV2, out var diags);
        Assert.That(ok, Is.True);
        Assert.That(diags, Is.Empty);

        // Verify new control appeared
        Assert.That(session.ControlsById.ContainsKey("btn-emergency"), Is.True);
        Assert.That(session.ControlsById["btn-emergency"].Text, Is.EqualTo("EMERGENCY OVERRIDE"));

        // Verify user input state was NOT lost!
        Assert.That(session.StateManager.GetVariable("AccessCode"), Is.EqualTo("7777"));
        Assert.That(session.ControlsById["input-access-code"].Text, Is.EqualTo("7777"));
    }
}
