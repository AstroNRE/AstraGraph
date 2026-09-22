using AstraGraph.Core;

namespace AstraGraph.Runtime.Events;

/// <summary>
/// Mutable frame abstraction representing an in-flight engine event.
/// For struct events, reads and mutations are applied directly to the underlying ref memory.
/// </summary>
public interface IAstraEventFrame
{
    Type EventType { get; }

    object? RawEventBoxed { get; }

    AstraValue GetFieldValue(string fieldName);

    void SetFieldValue(string fieldName, AstraValue value);

    bool TryGetFieldValue(string fieldName, out AstraValue value);

    bool TrySetFieldValue(string fieldName, AstraValue value);
}
