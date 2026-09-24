using System.Text.Json.Serialization;

namespace AstraGraph.UI.Model;

/// <summary>
/// Supported visual element types for Astra UI in SS14 / RobustToolbox.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<UiElementType>))]
public enum UiElementType
{
    Window,
    Panel,
    BoxContainer,
    Button,
    Label,
    LineEdit,
    TextureRect,
    ProgressBar,
    ScrollContainer,
    GridContainer,
    LayoutContainer,
    ItemList,
    Custom
}

/// <summary>
/// Orientation for containers (horizontal vs vertical).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<UiOrientation>))]
public enum UiOrientation
{
    Vertical,
    Horizontal
}

/// <summary>
/// Direction for reactive data bindings between UI controls and state variables.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BindingDirection>))]
public enum BindingDirection
{
    OneWay,          // State -> Control
    TwoWay,          // State <-> Control
    OneWayToSource   // Control -> State
}
