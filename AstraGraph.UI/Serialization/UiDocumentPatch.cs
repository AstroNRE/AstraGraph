using AstraGraph.UI.Model;

namespace AstraGraph.UI.Serialization;

public sealed record UiPatchOperation(
    string Kind,
    string ElementId,
    string? PropertyName = null,
    string? Value = null,
    string? ParentId = null,
    int? Index = null,
    int? FromIndex = null,
    string? ControlTypeId = null);

/// <summary>
/// Incremental document edits. Dragging stays local; a patch is sent when the edit is committed.
/// </summary>
public static class UiDocumentPatch
{
    public static bool Apply(UiDocument document, IReadOnlyList<UiPatchOperation> operations, out string? error)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var operation in operations)
        {
            if (!ApplyOne(document, operation, out error))
            {
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool ApplyOne(UiDocument document, UiPatchOperation operation, out string? error)
    {
        switch (operation.Kind.ToLowerInvariant())
        {
            case "setproperty":
                var target = document.FindElement(operation.ElementId);
                if (target == null || string.IsNullOrWhiteSpace(operation.PropertyName))
                {
                    error = $"Cannot set property on '{operation.ElementId}'.";
                    return false;
                }

                if (string.IsNullOrEmpty(operation.Value))
                {
                    target.Properties.Remove(operation.PropertyName);
                }
                else
                {
                    target.Properties[operation.PropertyName] = operation.Value;
                }

                error = null;
                return true;
            case "insertelement":
                var parent = document.FindElement(operation.ParentId ?? document.Root.Id);
                if (parent == null || string.IsNullOrWhiteSpace(operation.ControlTypeId))
                {
                    error = "Insert is missing a parent or control type.";
                    return false;
                }

                var child = new UiElementNode
                {
                    Id = string.IsNullOrWhiteSpace(operation.ElementId) ? Guid.NewGuid().ToString("D") : operation.ElementId,
                    ControlTypeId = operation.ControlTypeId
                };
                var insertAt = operation.Index ?? parent.Children.Count;
                parent.Children.Insert(Math.Clamp(insertAt, 0, parent.Children.Count), child);
                error = null;
                return true;
            case "deleteelement":
                if (operation.ElementId == document.Root.Id)
                {
                    error = "The root element cannot be deleted.";
                    return false;
                }

                var owner = FindParent(document.Root, operation.ElementId);
                var removing = owner?.Children.FirstOrDefault(item => item.Id == operation.ElementId);
                if (owner == null || removing == null)
                {
                    error = $"Element '{operation.ElementId}' was not found.";
                    return false;
                }

                owner.Children.Remove(removing);
                error = null;
                return true;
            case "moveelement":
                return Move(document, operation.ElementId, operation.ParentId ?? document.Root.Id, operation.Index ?? int.MaxValue, out error);
            case "reorderchildren":
                var reorderParent = document.FindElement(operation.ParentId ?? operation.ElementId);
                if (reorderParent == null || operation.FromIndex == null || operation.Index == null)
                {
                    error = "Reorder is missing a parent or indexes.";
                    return false;
                }

                var from = operation.FromIndex.Value;
                var to = operation.Index.Value;
                if (from < 0 || from >= reorderParent.Children.Count)
                {
                    error = "Reorder index is outside the child list.";
                    return false;
                }

                var moving = reorderParent.Children[from];
                reorderParent.Children.RemoveAt(from);
                reorderParent.Children.Insert(Math.Clamp(to, 0, reorderParent.Children.Count), moving);
                error = null;
                return true;
            default:
                error = $"Unknown patch '{operation.Kind}'.";
                return false;
        }
    }

    private static bool Move(UiDocument document, string elementId, string parentId, int index, out string? error)
    {
        var owner = FindParent(document.Root, elementId);
        var node = owner?.Children.FirstOrDefault(item => item.Id == elementId);
        var parent = document.FindElement(parentId);
        if (owner == null || node == null || parent == null)
        {
            error = $"Cannot move '{elementId}'.";
            return false;
        }

        owner.Children.Remove(node);
        parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), node);
        error = null;
        return true;
    }

    private static UiElementNode? FindParent(UiElementNode node, string id)
    {
        foreach (var child in node.Children)
        {
            if (child.Id == id)
            {
                return node;
            }

            var nested = FindParent(child, id);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
