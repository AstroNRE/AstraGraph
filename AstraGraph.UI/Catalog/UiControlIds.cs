using AstraGraph.UI.Model;

namespace AstraGraph.UI.Catalog;

/// <summary>
/// Stable control type ids. Native Robust controls use their CLR full name.
/// </summary>
public static class UiControlIds
{
    public const string Prefix = "Robust.Client.UserInterface.Controls.";

    public const string Window = Prefix + "Window";
    public const string Panel = Prefix + "PanelContainer";
    public const string BoxContainer = Prefix + "BoxContainer";
    public const string Button = Prefix + "Button";
    public const string Label = Prefix + "Label";
    public const string LineEdit = Prefix + "LineEdit";
    public const string TextureRect = Prefix + "TextureRect";
    public const string ProgressBar = Prefix + "ProgressBar";
    public const string ScrollContainer = Prefix + "ScrollContainer";
    public const string GridContainer = Prefix + "GridContainer";
    public const string LayoutContainer = Prefix + "LayoutContainer";
    public const string ItemList = Prefix + "ItemList";
    public const string Custom = "AstraGraph.UI.Custom";

    public static string FromLegacy(UiElementType type) => type switch
    {
        UiElementType.Window => Window,
        UiElementType.Panel => Panel,
        UiElementType.BoxContainer => BoxContainer,
        UiElementType.Button => Button,
        UiElementType.Label => Label,
        UiElementType.LineEdit => LineEdit,
        UiElementType.TextureRect => TextureRect,
        UiElementType.ProgressBar => ProgressBar,
        UiElementType.ScrollContainer => ScrollContainer,
        UiElementType.GridContainer => GridContainer,
        UiElementType.LayoutContainer => LayoutContainer,
        UiElementType.ItemList => ItemList,
        _ => Custom
    };

    public static UiElementType ToLegacy(string? typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            return UiElementType.Window;
        }

        var name = typeId.Contains('.', StringComparison.Ordinal)
            ? typeId[(typeId.LastIndexOf('.') + 1)..]
            : typeId;
        if (name.Equals("PanelContainer", StringComparison.Ordinal))
        {
            return UiElementType.Panel;
        }

        return Enum.TryParse<UiElementType>(name, ignoreCase: true, out var parsed) && parsed != UiElementType.Custom
            ? parsed
            : UiElementType.Custom;
    }

    public static bool TryParseLegacy(string? token, out UiElementType type)
    {
        type = UiElementType.Window;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (Enum.TryParse(token, ignoreCase: true, out type) && type != UiElementType.Custom && !token.Contains('.', StringComparison.Ordinal))
        {
            return true;
        }

        var mapped = ToLegacy(token);
        if (mapped == UiElementType.Custom)
        {
            return false;
        }

        type = mapped;
        return token.Equals(FromLegacy(mapped), StringComparison.Ordinal)
            || token.EndsWith("." + mapped, StringComparison.Ordinal)
            || token.Equals(mapped.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
