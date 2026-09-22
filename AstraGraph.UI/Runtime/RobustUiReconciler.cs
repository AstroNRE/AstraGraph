using AstraGraph.UI.Compiler;

namespace AstraGraph.UI.Runtime;

public sealed class ReconcileResult
{
    public required IRobustUiControl RootControl { get; init; }
    public required IReadOnlyDictionary<string, IRobustUiControl> ControlsById { get; init; }
}

/// <summary>
/// Builds and reconciles a Robust UI control tree from a compiled UiIrProgram without flickering or losing input focus.
/// </summary>
public sealed class RobustUiReconciler
{
    private readonly IRobustUiControlFactory _factory;

    public RobustUiReconciler(IRobustUiControlFactory factory)
    {
        _factory = factory;
    }

    public ReconcileResult Reconcile(UiIrProgram program, IRobustUiControl? existingRoot = null)
    {
        var existingMap = new Dictionary<string, IRobustUiControl>(StringComparer.Ordinal);
        if (existingRoot != null)
        {
            CollectExisting(existingRoot, existingMap);
        }

        var newControls = new Dictionary<string, IRobustUiControl>(StringComparer.Ordinal);

        // 1. Process widget creations
        foreach (var inst in program.Instructions)
        {
            if (inst is CreateWidgetInstruction cw)
            {
                if (existingMap.TryGetValue(cw.ElementId, out var existing) && existing.ElementType == cw.ElementType)
                {
                    existing.Name = cw.Name;
                    newControls[cw.ElementId] = existing;
                }
                else
                {
                    var created = _factory.CreateControl(cw.ElementId, cw.ElementType, cw.Name, cw.MinWidth, cw.MinHeight, cw.Orientation);
                    newControls[cw.ElementId] = created;
                }
            }
        }

        // 2. Process property assignments
        foreach (var inst in program.Instructions)
        {
            if (inst is SetPropertyInstruction sp)
            {
                if (newControls.TryGetValue(sp.ElementId, out var ctrl))
                {
                    ctrl.SetProperty(sp.PropertyName, sp.Value);
                }
            }
        }

        // 3. Process attachments (parent -> child)
        foreach (var inst in program.Instructions)
        {
            if (inst is AttachChildInstruction ac)
            {
                if (newControls.TryGetValue(ac.ParentId, out var parent) &&
                    newControls.TryGetValue(ac.ChildId, out var child))
                {
                    if (child.Parent != parent)
                    {
                        child.Parent?.RemoveChild(child);
                        parent.AddChild(child);
                    }
                }
            }
        }

        if (!newControls.TryGetValue(program.RootElementId, out var rootControl))
        {
            throw new InvalidOperationException($"Failed to reconcile: root element '{program.RootElementId}' was not created.");
        }

        return new ReconcileResult
        {
            RootControl = rootControl,
            ControlsById = newControls
        };
    }

    private static void CollectExisting(IRobustUiControl control, Dictionary<string, IRobustUiControl> map)
    {
        map[control.Id] = control;
        foreach (var child in control.Children)
        {
            CollectExisting(child, map);
        }
    }
}
