using System.Globalization;
using System.Xml.Linq;
using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Serialization;

public sealed class UiXamlResult
{
    public UiDocument? Document { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
    public string? Xaml { get; init; }
}

/// <summary>
/// Imports and exports a small XAML subset. XAML is an interchange format; the document stays the source of truth.
/// </summary>
public static class UiXamlAdapter
{
    public static UiXamlResult Import(string xaml, string? name = null)
    {
        var diagnostics = new List<Diagnostic>();
        if (string.IsNullOrWhiteSpace(xaml))
        {
            diagnostics.Add(new Diagnostic("UIXAML001", DiagnosticSeverity.Error, "XAML document is empty."));
            return new UiXamlResult { Diagnostics = diagnostics };
        }

        XDocument parsed;
        try
        {
            parsed = XDocument.Parse(xaml);
        }
        catch (System.Xml.XmlException ex)
        {
            diagnostics.Add(new Diagnostic("UIXAML001", DiagnosticSeverity.Error, "XAML could not be parsed: " + ex.Message));
            return new UiXamlResult { Diagnostics = diagnostics };
        }

        if (parsed.Root == null)
        {
            diagnostics.Add(new Diagnostic("UIXAML001", DiagnosticSeverity.Error, "XAML document has no root."));
            return new UiXamlResult { Diagnostics = diagnostics };
        }

        var document = new UiDocument
        {
            Id = GraphId.New(),
            Name = name ?? parsed.Root.Name.LocalName,
            Root = ReadElement(parsed.Root, diagnostics)
        };
        return new UiXamlResult { Document = document, Diagnostics = diagnostics };
    }

    public static string Export(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = WriteElement(document.Root);
        return root.ToString();
    }

    private static UiElementNode ReadElement(XElement element, List<Diagnostic> diagnostics)
    {
        var typeName = element.Name.LocalName;
        var node = new UiElementNode
        {
            Id = Guid.NewGuid().ToString("D"),
            Name = (string?)element.Attribute("Name")
        };
        if (UiControlIds.TryParseLegacy(typeName, out var legacy))
        {
            node.ElementType = legacy;
        }
        else
        {
            node.ControlTypeId = typeName.Contains('.', StringComparison.Ordinal)
                ? typeName
                : UiControlIds.Prefix + typeName;
        }

        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.LocalName is "Name" or "xmlns")
            {
                continue;
            }

            var raw = attribute.Value;
            if (raw.Contains('{', StringComparison.Ordinal) || attribute.Name.NamespaceName.Length > 0)
            {
                diagnostics.Add(new Diagnostic(
                    "UIXAML001",
                    DiagnosticSeverity.Error,
                    $"Unsupported XAML feature '{attribute.Name.LocalName}' on {typeName}.",
                    ElementId: node.Id,
                    PropertyName: attribute.Name.LocalName));
                node.SetAuthoredProperty("xaml." + attribute.Name.LocalName, raw);
                continue;
            }

            ApplyAttribute(node, attribute.Name.LocalName, raw);
        }

        if (element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)) && node.Text == null)
        {
            node.Text = string.Concat(element.Nodes().OfType<XText>().Select(text => text.Value)).Trim();
        }

        foreach (var child in element.Elements())
        {
            node.AddChild(ReadElement(child, diagnostics));
        }

        return node;
    }

    private static void ApplyAttribute(UiElementNode node, string name, string raw)
    {
        switch (name)
        {
            case "Text":
                node.Text = raw;
                return;
            case "Visible":
                node.Visible = !bool.TryParse(raw, out var visible) || visible;
                return;
            case "Orientation":
                if (Enum.TryParse<UiOrientation>(raw, true, out var orientation))
                {
                    node.Orientation = orientation;
                }
                return;
            case "MinWidth" when int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width):
                node.MinWidth = width;
                return;
            case "MinHeight" when int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height):
                node.MinHeight = height;
                return;
            case "StyleClasses":
                node.StyleClasses = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                return;
            default:
                node.SetAuthoredProperty(name, raw);
                return;
        }
    }

    private static XElement WriteElement(UiElementNode node)
    {
        var typeName = node.ElementType == UiElementType.Custom
            ? node.ControlTypeId
            : node.ElementType.ToString();
        var element = new XElement(typeName);
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            element.SetAttributeValue("Name", node.Name);
        }

        foreach (var pair in node.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (pair.Key.StartsWith("editor.", StringComparison.Ordinal) || pair.Key.StartsWith("xaml.", StringComparison.Ordinal))
            {
                continue;
            }

            element.SetAttributeValue(pair.Key, pair.Value);
        }

        if (node.StyleClasses.Count > 0)
        {
            element.SetAttributeValue("StyleClasses", string.Join(' ', node.StyleClasses));
        }

        foreach (var child in node.Children)
        {
            element.Add(WriteElement(child));
        }

        return element;
    }
}
