using System.Text.Json.Serialization;

namespace AstraGraph.Core;

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSeverity>))]
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Standard error and warning codes for AstraGraph.
/// </summary>
public static class DiagnosticCodes
{
    // Syntax / Document validation (DOCxxx)
    public const string DuplicateNodeId = "DOC001";
    public const string DuplicatePinId = "DOC002";
    public const string InvalidConnection = "DOC003";
    public const string MissingPin = "DOC004";
    public const string MultipleInputConnectionsToExecutionPin = "DOC005";

    // Type checking & Semantic analysis (TYPxxx)
    public const string UnknownType = "TYP001";
    public const string TypeMismatch = "TYP002";
    public const string UnassignedVariable = "TYP003";
    public const string UnknownVariable = "TYP004";
    public const string UnsafeNullableDereference = "TYP005";
    public const string MissingRequiredField = "TYP006";

    // Side & Prediction policy (POLxxx)
    public const string ServerApiCalledOnClient = "POL001";
    public const string ClientApiCalledOnServer = "POL002";
    public const string NonDeterministicOperationInPrediction = "POL003";
    public const string UnauthorizedProfileAccess = "POL004";

    // Control flow & Graph invariants (FLOxxx)
    public const string MissingEntryPoint = "FLO001";
    public const string DeadCode = "FLO002";
    public const string InfinitePureDataLoop = "FLO003";
    public const string UnreachableResumePoint = "FLO004";
}

/// <summary>
/// Structured compiler and runtime diagnostic message.
/// </summary>
public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    NodeId? NodeId = null,
    PinId? PinId = null,
    SymbolId? SymbolId = null,
    string? SuggestedFix = null)
{
    public override string ToString()
    {
        var loc = NodeId.HasValue ? $" [Node: {NodeId.Value}]" : string.Empty;
        if (PinId.HasValue) loc += $" [Pin: {PinId.Value}]";
        return $"{Severity} {Code}: {Message}{loc}";
    }
}
