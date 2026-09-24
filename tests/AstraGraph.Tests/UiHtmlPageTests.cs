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
    public void TryReadAction_ReadsNameAndPayload()
    {
        var ok = UiHtmlPage.TryReadAction("astra-bui://action?name=Submit&payload=%7B%22text%22%3A%22hi%22%7D", out var name, out var payload);

        Assert.That(ok, Is.True);
        Assert.That(name, Is.EqualTo("Submit"));
        Assert.That(payload["text"], Is.EqualTo("hi"));
        Assert.That(UiHtmlPage.TryReadAction("https://example.test", out _, out _), Is.False);
    }
}
