using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Html;
using AstraGraph.UI.Model;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class UiHtmlPageTests
{
    [Test]
    public void Render_IncludesButtonLabelAndAuthorCss()
    {
        var button = new UiElementNode
        {
            Id = "button",
            ControlTypeId = UiControlIds.Button,
            Text = "Submit"
        };
        var label = new UiElementNode
        {
            Id = "label",
            ControlTypeId = UiControlIds.Label,
            Text = "Result",
            StyleClasses = ["LabelHeadingBigger"]
        };
        var root = new UiElementNode
        {
            Id = "root",
            ControlTypeId = UiControlIds.BoxContainer,
            Children = [label, button]
        };
        var document = new UiDocument
        {
            Id = GraphId.New(),
            Name = "Surgery",
            Root = root,
            Css = ".LabelHeadingBigger { color: #edbc63; }",
            Events = [new UiEventSubscription { SubscriptionId = "e", ElementId = "button", EventName = "OnPressed", TargetAction = "Submit" }]
        };

        var html = UiHtmlPage.Render(document);

        Assert.That(html, Does.Contain(">Submit</button>"));
        Assert.That(html, Does.Contain("data-astra-action=\"Submit\""));
        Assert.That(html, Does.Contain("LabelHeadingBigger"));
        Assert.That(html, Does.Contain("color: #edbc63"));
        Assert.That(html, Does.Contain(">Result</label>"));
        Assert.That(html, Does.Contain("astraApplyState"));
    }

    [Test]
    public void Render_ListCarriesSelectionAndADisabledRowAction()
    {
        var list = new UiElementNode
        {
            Id = "parts",
            ControlTypeId = UiControlIds.ItemList
        };
        var button = new UiElementNode
        {
            Id = "install",
            ControlTypeId = UiControlIds.Button,
            Text = "Install",
            Properties = new Dictionary<string, object?> { ["Selection"] = "parts" }
        };
        var root = new UiElementNode
        {
            Id = "root",
            ControlTypeId = UiControlIds.BoxContainer,
            Children = [list, button]
        };
        var document = new UiDocument
        {
            Id = GraphId.New(),
            Name = "Bench",
            Root = root,
            Bindings =
            [
                new UiBindingDefinition
                {
                    BindingId = "parts",
                    ElementId = "parts",
                    TargetProperty = "Items",
                    StateVariable = "Parts"
                }
            ],
            Events =
            [
                new UiEventSubscription { SubscriptionId = "select", ElementId = "parts", EventName = "OnItemSelected", TargetAction = "SelectPart" },
                new UiEventSubscription { SubscriptionId = "install", ElementId = "install", EventName = "OnPressed", TargetAction = "InstallPart" }
            ]
        };

        var html = UiHtmlPage.Render(document);

        Assert.That(html, Does.Contain("data-astra-list=\"1\""));
        Assert.That(html, Does.Contain("data-astra-action=\"SelectPart\""));
        Assert.That(html, Does.Contain("data-astra-selection=\"parts\""));
        Assert.That(html, Does.Contain("data-astra-action=\"InstallPart\""));
        Assert.That(html, Does.Contain("astraFillList"));
        Assert.That(html, Does.Contain("data-selected-id"));
        Assert.That(html, Does.Contain("row.disabled"));
        Assert.That(html.IndexOf("const disabled", StringComparison.Ordinal), Is.LessThan(html.IndexOf("button.textContent", StringComparison.Ordinal)));
    }

    [Test]
    public void Render_IncludesBuildTextureSprite()
    {
        var sprite = new UiElementNode
        {
            Id = "sprite",
            ControlTypeId = "Robust.Client.UserInterface.Controls.TextureRect",
            Properties = new Dictionary<string, object?>
            {
                ["Texture"] = "/Textures/Decals/derelictsign.rsi/derelict1.png",
                ["State"] = "derelict1"
            }
        };
        var root = new UiElementNode
        {
            Id = "root",
            ControlTypeId = UiControlIds.BoxContainer,
            Children = [sprite]
        };
        var document = new UiDocument
        {
            Id = GraphId.New(),
            Name = "Sprite",
            Root = root
        };

        var html = UiHtmlPage.Render(document);

        Assert.That(html, Does.Contain("data-texture=\"/Textures/Decals/derelictsign.rsi/derelict1.png\""));
        Assert.That(html, Does.Contain("astra-ui://texture?path="));
        Assert.That(html, Does.Contain("derelict1"));
    }

    [Test]
    public void TryReadAction_ReadsNameAndPayload()
    {
        var ok = UiHtmlPage.TryReadAction("astra-bui://action?name=Submit&payload=%7B%22text%22%3A%22hi%22%7D", out var name, out var payload);

        Assert.That(ok, Is.True);
        Assert.That(name, Is.EqualTo("Submit"));
        Assert.That(payload["text"], Is.EqualTo("hi"));
        Assert.That(UiHtmlPage.TryReadAction("https://example.test", out _, out _), Is.False);
    }
}
