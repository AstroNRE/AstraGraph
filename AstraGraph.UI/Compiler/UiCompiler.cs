using AstraGraph.Core;
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
    public static UiCompilerResult Compile(UiDocument doc)
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

        CompileElement(doc.Root, parentId: null, visitedIds, instructions, diagnostics);

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

            instructions.Add(new RegisterEventInstruction(
                ev.SubscriptionId,
                ev.ElementId,
                ev.EventName,
                ev.TargetAction,
                ev.PayloadExpression));
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
            InitialState = new Dictionary<string, object?>(doc.LocalStateDefaults, StringComparer.Ordinal)
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
        List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(node.Id))
        {
            diagnostics.Add(new Diagnostic(
                "UI0004",
                DiagnosticSeverity.Error,
                $"Element of type '{node.ElementType}' has an empty or null ID."));
            return;
        }

        if (!visitedIds.Add(node.Id))
        {
            diagnostics.Add(new Diagnostic(
                "UI0005",
                DiagnosticSeverity.Error,
                $"Duplicate element ID detected: '{node.Id}'. Element IDs must be unique within a UI Document."));
            return;
        }

        // 1. Create widget
        instructions.Add(new CreateWidgetInstruction(
            node.Id,
            node.ElementType,
            node.Name,
            node.MinWidth,
            node.MinHeight,
            node.Orientation));

        // 2. Set default properties if specified
        if (!string.IsNullOrEmpty(node.Text))
            instructions.Add(new SetPropertyInstruction(node.Id, "Text", node.Text));
        if (!node.Visible)
            instructions.Add(new SetPropertyInstruction(node.Id, "Visible", false));
        if (!node.Enabled)
            instructions.Add(new SetPropertyInstruction(node.Id, "Enabled", false));

        foreach (var kv in node.CustomProperties)
        {
            instructions.Add(new SetPropertyInstruction(node.Id, kv.Key, kv.Value));
        }

        // 3. Attach to parent if exists
        if (parentId != null)
        {
            instructions.Add(new AttachChildInstruction(parentId, node.Id));
        }

        // 4. Recurse children
        foreach (var child in node.Children)
        {
            CompileElement(child, node.Id, visitedIds, instructions, diagnostics);
        }
    }
}
