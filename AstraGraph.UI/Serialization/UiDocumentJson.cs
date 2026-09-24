using System.Text.Json;
using System.Text.Json.Serialization;
using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Serialization;

/// <summary>
/// Human-diffable document JSON. <see cref="UiDocument"/> is the source of truth, not XAML.
/// </summary>
public static class UiDocumentJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static string Serialize(UiDocument document) => JsonSerializer.Serialize(ToDto(document), Options);

    public static UiDocument Deserialize(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        return ReadDocument(parsed.RootElement);
    }

    private static UiDocument ReadDocument(JsonElement element)
    {
        var id = element.TryGetProperty("id", out var idNode) && GraphId.TryParse(idNode.GetString(), out var parsed)
            ? parsed
            : GraphId.New();
        return new UiDocument
        {
            Id = id,
            Name = element.TryGetProperty("name", out var name) ? name.GetString() ?? "UI" : "UI",
            DocumentKind = element.TryGetProperty("documentKind", out var kind) ? kind.GetString() ?? "UI" : "UI",
            Root = ReadNode(element.GetProperty("root")),
            Bindings = ReadBindings(element),
            Events = ReadEvents(element),
            LocalStateDefaults = ReadDefaults(element),
            StateVariables = ReadState(element),
            Logic = ReadLogic(element)
        };
    }

    private static UiElementNode ReadNode(JsonElement element)
    {
        var typeId = element.TryGetProperty("controlTypeId", out var typeNode) ? typeNode.GetString() : null;
        var legacy = element.TryGetProperty("elementType", out var legacyNode) ? legacyNode.GetString() : null;
        var node = new UiElementNode
        {
            Id = element.TryGetProperty("id", out var id) ? id.GetString() ?? Guid.NewGuid().ToString("D") : Guid.NewGuid().ToString("D"),
            Name = element.TryGetProperty("name", out var name) ? name.GetString() : null
        };
        if (!string.IsNullOrWhiteSpace(typeId))
        {
            node.ControlTypeId = typeId;
        }
        else if (UiControlIds.TryParseLegacy(legacy, out var legacyType))
        {
            node.ElementType = legacyType;
        }
        else if (!string.IsNullOrWhiteSpace(legacy))
        {
            node.ControlTypeId = legacy;
        }

        if (element.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                node.SetAuthoredProperty(property.Name, ReadValue(property.Value));
            }
        }

        if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            node.Text = text.GetString();
        }

        if (element.TryGetProperty("styleClasses", out var classes))
        {
            node.StyleClasses = classes.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList();
        }

        if (element.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                node.AddChild(ReadNode(child));
            }
        }

        return node;
    }

    private static object? ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt32(out var number) => number,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.Null => null,
        _ => element.ToString()
    };

    private static List<UiBindingDefinition> ReadBindings(JsonElement element)
    {
        if (!element.TryGetProperty("bindings", out var bindings))
        {
            return [];
        }

        return bindings.EnumerateArray().Select(item => new UiBindingDefinition
        {
            BindingId = item.GetProperty("bindingId").GetString() ?? Guid.NewGuid().ToString("D"),
            ElementId = item.GetProperty("elementId").GetString() ?? "",
            TargetProperty = item.GetProperty("targetProperty").GetString() ?? "Text",
            StateVariable = item.GetProperty("stateVariable").GetString() ?? "",
            Direction = Enum.TryParse<BindingDirection>(item.TryGetProperty("direction", out var direction) ? direction.GetString() : null, true, out var parsed)
                ? parsed
                : BindingDirection.OneWay
        }).ToList();
    }

    private static List<UiEventSubscription> ReadEvents(JsonElement element)
    {
        if (!element.TryGetProperty("events", out var events))
        {
            return [];
        }

        return events.EnumerateArray().Select(item => new UiEventSubscription
        {
            SubscriptionId = item.GetProperty("subscriptionId").GetString() ?? Guid.NewGuid().ToString("D"),
            ElementId = item.GetProperty("elementId").GetString() ?? "",
            EventName = item.GetProperty("eventName").GetString() ?? "",
            TargetAction = item.GetProperty("targetAction").GetString() ?? ""
        }).ToList();
    }

    private static Dictionary<string, object?> ReadDefaults(JsonElement element)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!element.TryGetProperty("localStateDefaults", out var defaults))
        {
            return values;
        }

        foreach (var property in defaults.EnumerateObject())
        {
            values[property.Name] = ReadValue(property.Value);
        }

        return values;
    }

    private static List<UiStateVariable> ReadState(JsonElement element)
    {
        if (!element.TryGetProperty("stateVariables", out var variables))
        {
            return [];
        }

        return variables.EnumerateArray().Select(item => new UiStateVariable
        {
            Id = item.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("D"),
            Name = item.GetProperty("name").GetString() ?? "value",
            TypeName = item.TryGetProperty("typeName", out var type) ? type.GetString() ?? "string" : "string",
            Scope = Enum.TryParse<UiStateScope>(item.TryGetProperty("scope", out var scope) ? scope.GetString() : null, true, out var parsed)
                ? parsed
                : UiStateScope.Local
        }).ToList();
    }

    private static List<UiLogicStep> ReadLogic(JsonElement element)
    {
        if (!element.TryGetProperty("logic", out var logic))
        {
            return [];
        }

        return logic.EnumerateArray().Select(item => new UiLogicStep
        {
            Id = item.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("D"),
            Kind = item.GetProperty("kind").GetString() ?? "OnEvent",
            ElementId = item.TryGetProperty("elementId", out var elementId) ? elementId.GetString() : null,
            EventName = item.TryGetProperty("eventName", out var eventName) ? eventName.GetString() : null,
            StateVariable = item.TryGetProperty("stateVariable", out var state) ? state.GetString() : null,
            ActionName = item.TryGetProperty("actionName", out var action) ? action.GetString() : null
        }).ToList();
    }

    private static object ToDto(UiDocument document) => new
    {
        id = document.Id.ToString(),
        name = document.Name,
        documentKind = document.DocumentKind,
        root = ToNode(document.Root),
        bindings = document.Bindings.Select(binding => new
        {
            bindingId = binding.BindingId,
            elementId = binding.ElementId,
            targetProperty = binding.TargetProperty,
            stateVariable = binding.StateVariable,
            direction = binding.Direction.ToString()
        }),
        events = document.Events.Select(item => new
        {
            subscriptionId = item.SubscriptionId,
            elementId = item.ElementId,
            eventName = item.EventName,
            targetAction = item.TargetAction
        }),
        localStateDefaults = document.LocalStateDefaults,
        stateVariables = document.StateVariables.Select(item => new
        {
            id = item.Id,
            name = item.Name,
            typeName = item.TypeName,
            scope = item.Scope.ToString()
        }),
        logic = document.Logic.Select(item => new
        {
            id = item.Id,
            kind = item.Kind,
            elementId = item.ElementId,
            eventName = item.EventName,
            stateVariable = item.StateVariable,
            actionName = item.ActionName
        })
    };

    private static object ToNode(UiElementNode node) => new
    {
        id = node.Id,
        controlTypeId = node.ControlTypeId,
        name = node.Name,
        properties = node.Properties,
        styleClasses = node.StyleClasses,
        children = node.Children.Select(ToNode)
    };
}
