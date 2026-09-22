using AstraGraph.Core;

namespace AstraGraph.VM;

/// <summary>
/// Bridge interface for host environment services during VM execution (variables, native methods, ECS).
/// </summary>
public interface IVmHostServices
{
    AstraValue GetVariable(SymbolId variableId, string name);

    void SetVariable(SymbolId variableId, string name, AstraValue value);

    AstraValue CallNative(string methodDescriptor, IReadOnlyList<AstraValue> arguments);

    AstraValue GetComponent(AstraEntityId entityUid, string componentTypeName);

    bool HasComponent(AstraEntityId entityUid, string componentTypeName);

    void SetComponentField(AstraEntityId entityUid, string schemaIdAndFieldId, AstraValue value);
}

/// <summary>
/// Default in-memory host services for standalone VM testing and headless execution.
/// </summary>
public sealed class DefaultVmHostServices : IVmHostServices
{
    private readonly Dictionary<string, AstraValue> _variables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IReadOnlyList<AstraValue>, AstraValue>> _nativeHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<(AstraEntityId Entity, string Comp), AstraValue> _components = [];

    public void RegisterNativeMethod(string descriptor, Func<IReadOnlyList<AstraValue>, AstraValue> handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        _nativeHandlers[descriptor] = handler;
    }

    public void SetVariableDirect(string name, AstraValue value) => _variables[name] = value;

    public AstraValue GetVariable(SymbolId variableId, string name) =>
        _variables.GetValueOrDefault(name, AstraValue.Null);

    public void SetVariable(SymbolId variableId, string name, AstraValue value) =>
        _variables[name] = value;

    public AstraValue CallNative(string methodDescriptor, IReadOnlyList<AstraValue> arguments)
    {
        if (_nativeHandlers.TryGetValue(methodDescriptor, out var handler))
        {
            return handler(arguments);
        }
        throw new MissingMethodException($"Native method '{methodDescriptor}' is not registered in VM host services.");
    }

    public AstraValue GetComponent(AstraEntityId entityUid, string componentTypeName) =>
        _components.GetValueOrDefault((entityUid, componentTypeName), AstraValue.Null);

    public bool HasComponent(AstraEntityId entityUid, string componentTypeName) =>
        _components.ContainsKey((entityUid, componentTypeName));

    public void SetComponent(AstraEntityId entityUid, string componentTypeName, AstraValue component) =>
        _components[(entityUid, componentTypeName)] = component;

    public void SetComponentField(AstraEntityId entityUid, string schemaIdAndFieldId, AstraValue value)
    {
        // Standalone in-memory slot
        _components[(entityUid, schemaIdAndFieldId)] = value;
    }
}
