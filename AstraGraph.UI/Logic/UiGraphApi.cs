using AstraGraph.UI.Catalog;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Logic;

/// <summary>
/// Graph nodes generated from the control catalog. One descriptor per property and event, not a handwritten node per control.
/// </summary>
public static class UiGraphApi
{
    public static IReadOnlyList<UiGraphBinding> ForDocument(UiControlCatalog catalog, UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(document);
        var bindings = new List<UiGraphBinding>();
        foreach (var element in document.AllElements())
        {
            foreach (var binding in catalog.GraphBindings(element.ControlTypeId))
            {
                bindings.Add(binding with { ElementId = element.Id });
            }

            bindings.Add(new UiGraphBinding(element.ControlTypeId, "Focus", "Focus", "void", element.Id));
        }

        foreach (var action in document.Contract?.Actions ?? [])
        {
            bindings.Add(new UiGraphBinding("BUI", "Send", action.Name, "action", action.Id));
        }

        foreach (var notification in document.Contract?.Notifications ?? [])
        {
            bindings.Add(new UiGraphBinding("BUI", "Notify", notification.Name, "notification", notification.Id));
        }

        return bindings;
    }
}
