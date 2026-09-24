using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Logic;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Compiler;

/// <summary>
/// Result of compiling a UiDocument into a UiIrProgram.
/// </summary>
public sealed class UiCompilerResult
{
    public bool Success => Diagnostics.Count == 0 && Program != null;
    public UiIrProgram? Program { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Compiles a declarative UiDocument into an optimized, validated UiIrProgram.
/// </summary>
public sealed class UiCompiler
{
    public static UiCompilerResult Compile(UiDocument doc, UiControlCatalog? catalog = null, bool strict = false)
    {
        var diagnostics = new List<Diagnostic>();

        if (doc.Root == null)
        {
            diagnostics.Add(new Diagnostic(
                "UI0001",
                DiagnosticSeverity.Error,
                "UI Document has no root element."));
            return new UiCompilerResult { Diagnostics = diagnostics };
        }

        // Check for duplicate element IDs
        var visitedIds = new HashSet<string>(StringComparer.Ordinal);
        var instructions = new List<UiIrInstruction>();

        CompileElement(doc.Root, parentId: null, visitedIds, instructions, diagnostics, catalog, strict);

        // Compile bindings
        foreach (var b in doc.Bindings)
        {
            if (!visitedIds.Contains(b.ElementId))
            {
                diagnostics.Add(new Diagnostic(
                    "UI0002",
                    DiagnosticSeverity.Error,
                    $"Binding '{b.BindingId}' references non-existent element '{b.ElementId}'."));
                continue;
            }

            ValidateBinding(doc, b, catalog, strict, diagnostics);
            instructions.Add(new RegisterBindingInstruction(
                b.BindingId,
                b.ElementId,
                b.TargetProperty,
                b.StateVariable,
                b.Direction,
                b.Converter));
        }

        // Compile event subscriptions
        foreach (var ev in doc.Events)
        {
            if (!visitedIds.Contains(ev.ElementId))
            {
                diagnostics.Add(new Diagnostic(
                    "UI0003",
                    DiagnosticSeverity.Error,
                    $"Event subscription '{ev.SubscriptionId}' references non-existent element '{ev.ElementId}'."));
                continue;
            }

            ValidateEvent(doc, ev, catalog, strict, diagnostics);
            instructions.Add(new RegisterEventInstruction(
                ev.SubscriptionId,
                ev.ElementId,
                ev.EventName,
                ev.TargetAction,
                ev.PayloadExpression));
        }

        if (strict)
        {
            ValidateLogic(doc, visitedIds, diagnostics);
        }

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new UiCompilerResult { Diagnostics = diagnostics };
        }

        var program = new UiIrProgram
        {
            DocumentId = doc.Id,
            Name = doc.Name,
            RootElementId = doc.Root.Id,
            Instructions = instructions,
            InitialState = InitialState(doc)
        };

        return new UiCompilerResult
        {
            Program = program,
            Diagnostics = diagnostics
        };
    }

    private static void CompileElement(
        UiElementNode node,
        string? parentId,
        HashSet<string> visitedIds,
        List<UiIrInstruction> instructions,
        List<Diagnostic> diagnostics,
        UiControlCatalog? catalog,
        bool strict)
    {
        if (string.IsNullOrWhiteSpace(node.Id))
        {
            diagnostics.Add(new Diagnostic(
                "UI0004",
                DiagnosticSeverity.Error,
                $"Element of type '{node.ControlTypeId}' has an empty or null ID.",
                ElementId: node.Id));
            return;
        }

        if (!visitedIds.Add(node.Id))
        {
            diagnostics.Add(new Diagnostic(
                "UI0005",
                DiagnosticSeverity.Error,
                $"Duplicate element ID detected: '{node.Id}'. Element IDs must be unique within a UI Document.",
                ElementId: node.Id));
            return;
        }

        UiControlDescriptor? descriptor = null;
        if (catalog != null && !catalog.TryGet(node.ControlTypeId, out descriptor))
        {
            diagnostics.Add(new Diagnostic(
                "UI0010",
                DiagnosticSeverity.Error,
                $"Unknown control '{node.ControlTypeId}'.",
                ElementId: node.Id));
        }

        if (parentId != null && catalog != null && catalog.TryGet(ParentType(instructions, parentId), out var parentDescriptor) && parentDescriptor is { CanHaveChildren: false })
        {
            diagnostics.Add(new Diagnostic(
                "UI0013",
                DiagnosticSeverity.Error,
                $"Control '{parentDescriptor.TypeId}' cannot contain children.",
                ElementId: node.Id));
        }

        instructions.Add(new CreateWidgetInstruction(
            node.Id,
            node.ElementType,
            node.Name,
            node.MinWidth,
            node.MinHeight,
            node.Orientation,
            node.ControlTypeId));

        foreach (var pair in node.Properties)
        {
            if (pair.Key.StartsWith("editor.", StringComparison.Ordinal) ||
                pair.Key.Equals("valueSource", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (descriptor != null && descriptor.Properties.All(property => !property.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(new Diagnostic(
                    "UI0011",
                    DiagnosticSeverity.Warning,
                    $"Unknown property '{pair.Key}' on '{node.ControlTypeId}'.",
                    ElementId: node.Id,
                    PropertyName: pair.Key));
            }
            else if (descriptor != null && !ValueMatches(descriptor, pair.Key, pair.Value))
            {
                diagnostics.Add(new Diagnostic(
                    "UI0012",
                    DiagnosticSeverity.Error,
                    $"Property '{pair.Key}' on '{node.ControlTypeId}' has an invalid value.",
                    ElementId: node.Id,
                    PropertyName: pair.Key));
                continue;
            }

            instructions.Add(new SetPropertyInstruction(node.Id, pair.Key, pair.Value));
        }

        if (strict && catalog != null && catalog.StyleClasses.Count > 0)
        {
            foreach (var styleClass in node.StyleClasses)
            {
                if (!catalog.StyleClasses.Contains(styleClass, StringComparer.Ordinal))
                {
                    diagnostics.Add(new Diagnostic(
                        "UI0020",
                        DiagnosticSeverity.Error,
                        $"Unknown style class '{styleClass}' on '{node.ControlTypeId}'.",
                        ElementId: node.Id));
                }
            }
        }

        if (node.StyleClasses.Count > 0)
        {
            instructions.Add(new SetPropertyInstruction(node.Id, "StyleClasses", node.StyleClasses.ToArray()));
        }

        if (parentId != null)
        {
            instructions.Add(new AttachChildInstruction(parentId, node.Id));
        }

        foreach (var child in node.Children)
        {
            CompileElement(child, node.Id, visitedIds, instructions, diagnostics, catalog, strict);
        }
    }

    private static void ValidateBinding(
        UiDocument doc,
        UiBindingDefinition binding,
        UiControlCatalog? catalog,
        bool strict,
        List<Diagnostic> diagnostics)
    {
        if (!strict)
        {
            return;
        }

        var variable = doc.StateVariables.FirstOrDefault(item => item.Name.Equals(binding.StateVariable, StringComparison.Ordinal));
        var known = variable != null || doc.LocalStateDefaults.ContainsKey(binding.StateVariable);
        if (!known)
        {
            diagnostics.Add(new Diagnostic(
                "UI0015",
                DiagnosticSeverity.Error,
                $"Binding '{binding.BindingId}' references missing state variable '{binding.StateVariable}'.",
                ElementId: binding.ElementId,
                PropertyName: binding.TargetProperty,
                BindingId: binding.BindingId));
            return;
        }

        if (variable is { Scope: UiStateScope.Server } && binding.Direction is BindingDirection.TwoWay or BindingDirection.OneWayToSource)
        {
            diagnostics.Add(new Diagnostic(
                "UI0017",
                DiagnosticSeverity.Error,
                $"Client binding '{binding.BindingId}' cannot mutate server state '{binding.StateVariable}'.",
                ElementId: binding.ElementId,
                PropertyName: binding.TargetProperty,
                BindingId: binding.BindingId));
        }

        var element = doc.FindElement(binding.ElementId);
        if (element == null || catalog == null || !catalog.TryGet(element.ControlTypeId, out var descriptor) || descriptor == null)
        {
            return;
        }

        var property = descriptor.Properties.FirstOrDefault(item => item.Name.Equals(binding.TargetProperty, StringComparison.OrdinalIgnoreCase));
        if (strict && !string.IsNullOrWhiteSpace(binding.Expression))
        {
            var scope = new Dictionary<string, object?>(doc.LocalStateDefaults, StringComparer.Ordinal);
            foreach (var stateVariable in doc.StateVariables)
            {
                scope.TryAdd(stateVariable.Name, stateVariable.DefaultValue);
            }

            if (!UiExpression.TryEvaluate(binding.Expression, scope, out _, out var expressionError))
            {
                diagnostics.Add(new Diagnostic(
                    "UI0021",
                    DiagnosticSeverity.Error,
                    expressionError ?? $"Binding '{binding.BindingId}' has an invalid expression.",
                    ElementId: binding.ElementId,
                    PropertyName: binding.TargetProperty,
                    BindingId: binding.BindingId));
            }
        }

        var sourceType = variable?.TypeName ?? InferType(doc.LocalStateDefaults.GetValueOrDefault(binding.StateVariable));
        if (property != null && !UiValueCoercion.TypesCompatible(property.TypeName, sourceType, binding.Converter))
        {
            diagnostics.Add(new Diagnostic(
                "UI0014",
                DiagnosticSeverity.Error,
                $"Binding '{binding.BindingId}' cannot assign {sourceType} to {property.Name} ({property.TypeName}) without a converter.",
                ElementId: binding.ElementId,
                PropertyName: binding.TargetProperty,
                BindingId: binding.BindingId));
        }
    }

    private static void ValidateEvent(UiDocument doc, UiEventSubscription ev, UiControlCatalog? catalog, bool strict, List<Diagnostic> diagnostics)
    {
        if (!strict || catalog == null)
        {
            return;
        }

        var element = doc.FindElement(ev.ElementId);
        if (element == null || !catalog.TryGet(element.ControlTypeId, out var descriptor) || descriptor == null)
        {
            return;
        }

        if (descriptor.Events.Count > 0 && descriptor.Events.All(item => !item.Name.Equals(ev.EventName, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new Diagnostic(
                "UI0016",
                DiagnosticSeverity.Error,
                $"Unknown event '{ev.EventName}' on '{element.ControlTypeId}'.",
                ElementId: ev.ElementId));
        }
    }

    private static void ValidateLogic(UiDocument doc, HashSet<string> visitedIds, List<Diagnostic> diagnostics)
    {
        foreach (var step in doc.Logic)
        {
            if (!string.IsNullOrWhiteSpace(step.ElementId) && !visitedIds.Contains(step.ElementId))
            {
                diagnostics.Add(new Diagnostic(
                    "UI0018",
                    DiagnosticSeverity.Error,
                    $"Logic step '{step.Id}' references missing element '{step.ElementId}'.",
                    ElementId: step.ElementId));
            }

            if (step.Kind.Equals("SendAction", StringComparison.OrdinalIgnoreCase) &&
                doc.Contract != null &&
                doc.Contract.Actions.All(action => !action.Name.Equals(step.ActionName, StringComparison.Ordinal)))
            {
                diagnostics.Add(new Diagnostic(
                    "UI0019",
                    DiagnosticSeverity.Error,
                    $"Logic step '{step.Id}' sends unknown BUI action '{step.ActionName}'."));
            }
        }
    }

    private static bool ValueMatches(UiControlDescriptor descriptor, string propertyName, object? value)
    {
        var property = descriptor.Properties.First(item => item.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            return true;
        }

        var kind = property.EditorKind;
        return kind switch
        {
            UiPropertyEditorKind.Boolean => value is bool || bool.TryParse(value.ToString(), out _),
            UiPropertyEditorKind.Integer => value is int or long or short || int.TryParse(value.ToString(), out _),
            UiPropertyEditorKind.Float => value is float or double or int or long || float.TryParse(value.ToString(), out _),
            UiPropertyEditorKind.Enum => property.EnumValues == null || property.EnumValues.Contains(value.ToString() ?? "", StringComparer.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static string ParentType(List<UiIrInstruction> instructions, string parentId)
    {
        for (var i = instructions.Count - 1; i >= 0; i--)
        {
            if (instructions[i] is CreateWidgetInstruction created && created.ElementId == parentId)
            {
                return string.IsNullOrEmpty(created.ControlTypeId)
                    ? UiControlIds.FromLegacy(created.ElementType)
                    : created.ControlTypeId;
            }
        }

        return UiControlIds.Window;
    }

    private static Dictionary<string, object?> InitialState(UiDocument doc)
    {
        var state = new Dictionary<string, object?>(doc.LocalStateDefaults, StringComparer.Ordinal);
        foreach (var variable in doc.StateVariables)
        {
            if (!state.ContainsKey(variable.Name))
            {
                state[variable.Name] = variable.DefaultValue;
            }
        }

        return state;
    }

    private static string InferType(object? value) => value switch
    {
        bool => "bool",
        int or long => "int",
        float or double => "float",
        _ => "string"
    };
}
