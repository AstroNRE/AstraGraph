using AstraGraph.Binding;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core;

/// <summary>
/// Turns a catalog binding into a graph node whose pins match the C# signature.
/// </summary>
public static class BindingNodeFactory
{
    public static NodeDocument Create(NativeMethodDescriptor method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var pins = new List<PinDocument>();
        var isEvent = IsEvent(method);
        var isPure = method.IsPure || method.Name.StartsWith("op_", StringComparison.Ordinal);

        if (!isPure && !isEvent)
        {
            pins.Add(Execution("In", PinDirection.Input));
        }

        if (!isPure)
        {
            pins.Add(Execution(isEvent ? "Then" : "Out", PinDirection.Output));
        }

        foreach (var parameter in method.Parameters)
        {
            pins.Add(Data(
                string.IsNullOrWhiteSpace(parameter.Name) ? "value" : parameter.Name,
                isEvent ? PinDirection.Output : PinDirection.Input,
                parameter.Type.TypeName));
        }

        if (!IsVoid(method.ReturnType))
        {
            pins.Add(Data("Result", PinDirection.Output, method.ReturnType.TypeName));
        }

        return new NodeDocument
        {
            Id = NodeId.New(),
            Name = method.Name,
            NodeType = method.Descriptor,
            Pins = pins,
            Properties = new Dictionary<string, string>
            {
                ["bindingId"] = method.Descriptor,
                ["pure"] = isPure ? "true" : "false",
                ["side"] = method.Side.ToString(),
                ["deterministic"] = method.IsDeterministic ? "true" : "false",
                ["cost"] = method.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
    }

    private static bool IsEvent(NativeMethodDescriptor method) =>
        method.Name.StartsWith("On", StringComparison.Ordinal) && IsVoid(method.ReturnType) && !method.IsPure;

    private static bool IsVoid(AstraType type) =>
        type.TypeName is "void" or "System.Void";

    private static PinDocument Execution(string name, PinDirection direction) => new()
    {
        Id = PinId.New(),
        Name = name,
        Direction = direction,
        Kind = PinKind.Execution,
        DataType = "exec"
    };

    private static PinDocument Data(string name, PinDirection direction, string dataType) => new()
    {
        Id = PinId.New(),
        Name = name,
        Direction = direction,
        Kind = PinKind.Data,
        DataType = dataType
    };
}
