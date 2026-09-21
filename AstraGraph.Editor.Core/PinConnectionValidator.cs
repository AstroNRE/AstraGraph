using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core;

public sealed record ConnectionValidationResult(
    bool IsValid,
    string? ErrorReason = null)
{
    public static ConnectionValidationResult Success { get; } = new(true);
    public static ConnectionValidationResult Fail(string reason) => new(false, reason);
}

public static class PinConnectionValidator
{
    public static ConnectionValidationResult Validate(
        VisualPin pinA,
        VisualPin pinB,
        IReadOnlyList<VisualConnection> existingConnections)
    {
        ArgumentNullException.ThrowIfNull(pinA);
        ArgumentNullException.ThrowIfNull(pinB);
        ArgumentNullException.ThrowIfNull(existingConnections);

        // 1. Cannot connect to self
        if (pinA.Id == pinB.Id)
        {
            return ConnectionValidationResult.Fail("Cannot connect a pin to itself.");
        }

        // 2. Cannot connect pins belonging to the same node
        if (pinA.NodeId == pinB.NodeId)
        {
            return ConnectionValidationResult.Fail("Cannot connect pins on the same node.");
        }

        // 3. Must connect Output to Input
        if (pinA.Direction == pinB.Direction)
        {
            return ConnectionValidationResult.Fail(
                pinA.Direction == PinDirection.Input
                    ? "Cannot connect Input pin to another Input pin."
                    : "Cannot connect Output pin to another Output pin.");
        }

        var sourcePin = pinA.Direction == PinDirection.Output ? pinA : pinB;
        var targetPin = pinA.Direction == PinDirection.Input ? pinA : pinB;

        // 4. Pin kinds must match
        if (sourcePin.Kind != targetPin.Kind)
        {
            return ConnectionValidationResult.Fail(
                $"Cannot connect '{sourcePin.Kind}' pin to '{targetPin.Kind}' pin.");
        }

        // 5. Execution pins: only 1 incoming connection per input pin
        if (targetPin.Kind == PinKind.Execution)
        {
            if (existingConnections.Any(c => c.TargetPinId == targetPin.Id))
            {
                return ConnectionValidationResult.Fail("Execution input pin already has an incoming connection.");
            }
        }

        // 6. Data pins: check type compatibility
        if (sourcePin.Kind == PinKind.Data)
        {
            if (!AreTypesCompatible(sourcePin.DataType, targetPin.DataType))
            {
                return ConnectionValidationResult.Fail(
                    $"Type mismatch: cannot connect source type '{sourcePin.DataType}' to target type '{targetPin.DataType}'.");
            }
        }

        // 7. Prevent duplicate connections between the same pair of pins
        if (existingConnections.Any(c => c.SourcePinId == sourcePin.Id && c.TargetPinId == targetPin.Id))
        {
            return ConnectionValidationResult.Fail("Connection already exists between these pins.");
        }

        return ConnectionValidationResult.Success;
    }

    private static bool AreTypesCompatible(string sourceType, string targetType)
    {
        if (string.Equals(sourceType, targetType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // System.Object or generic Wildcard accepts any type
        if (string.Equals(targetType, "System.Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetType, "Any", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Coercion from Int32 to Int64 / Float64
        if (sourceType.Contains("Int32", StringComparison.OrdinalIgnoreCase))
        {
            return targetType.Contains("Int64", StringComparison.OrdinalIgnoreCase) ||
                   targetType.Contains("Double", StringComparison.OrdinalIgnoreCase) ||
                   targetType.Contains("Single", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
