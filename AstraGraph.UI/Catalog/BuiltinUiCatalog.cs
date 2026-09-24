namespace AstraGraph.UI.Catalog;

/// <summary>
/// Well-known Robust controls so Studio and the compiler work before a fork indexes its assemblies.
/// Reflection indexing replaces these descriptors with the live CLR surface.
/// </summary>
public static class BuiltinUiCatalog
{
    public static UiControlCatalog Create()
    {
        var catalog = new UiControlCatalog();
        catalog.Add(Control(UiControlIds.Window, "Window", "Layout", true, [], []));
        catalog.Add(Control(UiControlIds.Panel, "PanelContainer", "Layout", true,
            [Prop("Visible", "Boolean", UiPropertyEditorKind.Boolean, "Behavior")], []));
        catalog.Add(Control(UiControlIds.BoxContainer, "BoxContainer", "Layout", true,
        [
            Prop("Orientation", "LayoutOrientation", UiPropertyEditorKind.Enum, "Layout", "Vertical", ["Vertical", "Horizontal"]),
            Prop("SeparationOverride", "Int32", UiPropertyEditorKind.Integer, "Layout"),
            ..Common()
        ], []));
        catalog.Add(Control(UiControlIds.GridContainer, "GridContainer", "Layout", true,
        [
            Prop("Columns", "Int32", UiPropertyEditorKind.Integer, "Layout", "1"),
            ..Common()
        ], []));
        catalog.Add(Control(UiControlIds.LayoutContainer, "LayoutContainer", "Layout", true, Common(), []));
        catalog.Add(Control(UiControlIds.ScrollContainer, "ScrollContainer", "Layout", true, Common(), []));
        catalog.Add(Control(UiControlIds.Button, "Button", "Input", true,
        [
            Prop("Text", "String", UiPropertyEditorKind.Text, "Content"),
            Prop("Disabled", "Boolean", UiPropertyEditorKind.Boolean, "Behavior", "false"),
            Prop("ToggleMode", "Boolean", UiPropertyEditorKind.Boolean, "Behavior", "false"),
            ..Common()
        ],
        [
            Event("OnPressed", "ButtonEventArgs", [])
        ]));
        catalog.Add(Control(UiControlIds.Label, "Label", "Display", false,
        [
            Prop("Text", "String", UiPropertyEditorKind.Text, "Content"),
            ..Common()
        ], []));
        catalog.Add(Control(UiControlIds.LineEdit, "LineEdit", "Input", false,
        [
            Prop("Text", "String", UiPropertyEditorKind.Text, "Content"),
            Prop("Editable", "Boolean", UiPropertyEditorKind.Boolean, "Behavior", "true"),
            ..Common()
        ],
        [
            Event("OnTextChanged", "LineEditEventArgs", [new UiEventPayloadField("Text", "String")]),
            Event("OnTextEntered", "LineEditEventArgs", [new UiEventPayloadField("Text", "String")])
        ]));
        catalog.Add(Control(UiControlIds.TextureRect, "TextureRect", "Display", false,
        [
            Prop("Texture", "Texture", UiPropertyEditorKind.Resource, "Content"),
            Prop("TextureScale", "Vector2", UiPropertyEditorKind.Vector2, "Content"),
            Prop("Stretch", "StretchMode", UiPropertyEditorKind.Enum, "Content", null, ["Keep", "KeepCentered", "KeepAspect", "KeepAspectCentered", "Scale"]),
            ..Common()
        ], []));
        catalog.Add(Control(UiControlIds.ItemList, "ItemList", "Display", false,
        [
            Prop("ItemSeparation", "Int32", UiPropertyEditorKind.Integer, "Layout", "0"),
            ..Common()
        ],
        [
            Event("OnItemSelected", "ItemListSelectedEventArgs", [new UiEventPayloadField("ItemIndex", "Int32")])
        ]));
        catalog.Add(Control(UiControlIds.ProgressBar, "ProgressBar", "Display", false,
        [
            Prop("Value", "Single", UiPropertyEditorKind.Float, "Content", "0"),
            Prop("MaxValue", "Single", UiPropertyEditorKind.Float, "Content", "1"),
            ..Common()
        ], []));

        foreach (var name in new[] { "AnchorLeft", "AnchorTop", "AnchorRight", "AnchorBottom", "MarginLeft", "MarginTop", "MarginRight", "MarginBottom" })
        {
            catalog.AddAttached(new UiAttachedPropertyDescriptor(name, "Single", UiControlIds.LayoutContainer, UiPropertyEditorKind.Float, "Layout"));
        }

        return catalog;
    }

    private static UiControlDescriptor Control(
        string typeId,
        string name,
        string category,
        bool children,
        IReadOnlyList<UiPropertyDescriptor> properties,
        IReadOnlyList<UiEventDescriptor> events) =>
        new(typeId, typeId, UiControlCatalog.DisplayName(name), category, children, properties, events);

    private static UiPropertyDescriptor[] Common() =>
    [
        Prop("Visible", "Boolean", UiPropertyEditorKind.Boolean, "Behavior", "true"),
        Prop("MinWidth", "Single", UiPropertyEditorKind.Float, "Layout"),
        Prop("MinHeight", "Single", UiPropertyEditorKind.Float, "Layout"),
        Prop("HorizontalExpand", "Boolean", UiPropertyEditorKind.Boolean, "Layout"),
        Prop("VerticalExpand", "Boolean", UiPropertyEditorKind.Boolean, "Layout"),
        Prop("HorizontalAlignment", "HAlignment", UiPropertyEditorKind.Enum, "Layout", null, ["Left", "Center", "Right", "Stretch"]),
        Prop("VerticalAlignment", "VAlignment", UiPropertyEditorKind.Enum, "Layout", null, ["Top", "Center", "Bottom", "Stretch"]),
        Prop("MouseFilter", "MouseFilterMode", UiPropertyEditorKind.Enum, "Behavior", null, ["Ignore", "Pass", "Stop"])
    ];

    private static UiPropertyDescriptor Prop(
        string name,
        string typeName,
        UiPropertyEditorKind kind,
        string category,
        string? defaultValue = null,
        IReadOnlyList<string>? enumValues = null) =>
        new(name, typeName, true, true, kind, category, null, defaultValue, enumValues);

    private static UiEventDescriptor Event(string name, string typeName, IReadOnlyList<UiEventPayloadField> payload) =>
        new(name, typeName, payload);
}
