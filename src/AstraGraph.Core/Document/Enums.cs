using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Defines the architectural role of a graph.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GraphKind
{
    System,
    Behavior,
    Function,
    Library,
    UI,
    Schema
}

/// <summary>
/// Execution side and authority for a graph.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GraphSide
{
    Server,
    Client,
    Shared,
    SharedPredicted
}

/// <summary>
/// Direction of data or execution flow through a pin.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PinDirection
{
    Input,
    Output
}

/// <summary>
/// Kind of pin: Execution (control flow) or Data (value flow).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PinKind
{
    Execution,
    Data
}
