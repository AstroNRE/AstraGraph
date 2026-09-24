using System.Diagnostics;
using AstraGraph.UI.Catalog;
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
    private readonly Stopwatch _reconcile = new();

    public RobustUiReconciler(IRobustUiControlFactory factory)
    {
        _factory = factory;
    }

    public long LastReconcileMicroseconds { get; private set; }

    public int LastControlCount { get; private set; }

    public ReconcileResult Reconcile(UiIrProgram program, IRobustUiControl? existingRoot = null)
    {
        _reconcile.Restart();
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
                var typeId = string.IsNullOrEmpty(cw.ControlTypeId) ? UiControlIds.FromLegacy(cw.ElementType) : cw.ControlTypeId;
                if (existingMap.TryGetValue(cw.ElementId, out var existing) && SameType(existing, typeId))
                {
                    existing.Name = cw.Name;
                    newControls[cw.ElementId] = existing;
                }
                else
                {
                    if (existing != null)
                    {
                        existing.Parent?.RemoveChild(existing);
                    }

                    var created = _factory.CreateControl(cw.ElementId, typeId, cw.Name, cw.MinWidth, cw.MinHeight, cw.Orientation);
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

        foreach (var existing in existingMap.Values)
        {
            if (!newControls.ContainsKey(existing.Id))
            {
                existing.Parent?.RemoveChild(existing);
            }
        }

        var desiredChildren = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var inst in program.Instructions)
        {
            if (inst is AttachChildInstruction ac)
            {
                if (!desiredChildren.TryGetValue(ac.ParentId, out var children))
                {
                    children = [];
                    desiredChildren[ac.ParentId] = children;
                }

                children.Add(ac.ChildId);
            }
        }

        foreach (var parent in newControls.Values)
        {
            var desired = desiredChildren.TryGetValue(parent.Id, out var ids) ? ids : [];
            var current = parent.Children.Select(child => child.Id).ToArray();
            if (current.SequenceEqual(desired, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var child in parent.Children.ToList())
            {
                parent.RemoveChild(child);
            }

            foreach (var childId in desired)
            {
                if (!newControls.TryGetValue(childId, out var child))
                {
                    continue;
                }

                child.Parent?.RemoveChild(child);
                parent.AddChild(child);
            }
        }

        if (!newControls.TryGetValue(program.RootElementId, out var rootControl))
        {
            throw new InvalidOperationException($"Failed to reconcile: root element '{program.RootElementId}' was not created.");
        }

        _reconcile.Stop();
        LastReconcileMicroseconds = (long)(_reconcile.Elapsed.TotalMilliseconds * 1000);
        LastControlCount = newControls.Count;

        return new ReconcileResult
        {
            RootControl = rootControl,
            ControlsById = newControls
        };
    }

    private static bool SameType(IRobustUiControl control, string typeId) =>
        string.Equals(control.ControlTypeId, typeId, StringComparison.Ordinal)
        || (string.IsNullOrEmpty(control.ControlTypeId) && control.ElementType == UiControlIds.ToLegacy(typeId));

    private static void CollectExisting(IRobustUiControl control, Dictionary<string, IRobustUiControl> map)
    {
        map[control.Id] = control;
        foreach (var child in control.Children)
        {
            CollectExisting(child, map);
        }
    }
}
