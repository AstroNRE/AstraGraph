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

    void PushEventContext(AstraEventInvocationContext context)
    {
    }

    void PopEventContext()
    {
    }

    AstraEventInvocationContext? PeekEventContext() => null;

    void UseSchemaComponents(ISchemaComponentSource source)
    {
    }
}

/// <summary>
/// Default in-memory host services for standalone VM testing and headless execution.
/// </summary>
public sealed class DefaultVmHostServices : IVmHostServices
{
    private readonly Dictionary<string, AstraValue> _variables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IReadOnlyList<AstraValue>, AstraValue>> _nativeHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<(AstraEntityId Entity, string Comp), AstraValue> _components = [];
    private readonly Stack<AstraEventInvocationContext> _events = [];
    private readonly Dictionary<string, (BytecodeProgram Program, BytecodeFunction Function)> _functions = new(StringComparer.Ordinal);
    private ISchemaComponentSource? _schemas;

    public void UseSchemaComponents(ISchemaComponentSource source) => _schemas = source;

    public void RegisterGraphFunction(string name, BytecodeProgram program, BytecodeFunction function)
    {
        _functions[name] = (program, function);
    }

    public void PushEventContext(AstraEventInvocationContext context) => _events.Push(context);

    public void PopEventContext()
    {
        if (_events.Count > 0)
        {
            _events.Pop();
        }
    }

    public AstraEventInvocationContext? PeekEventContext() => _events.Count == 0 ? null : _events.Peek();

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
        if (methodDescriptor == "Graph.Call" && arguments.Count > 0)
        {
            var name = arguments[0].AsString() ?? string.Empty;
            if (_functions.TryGetValue(name, out var function))
            {
                SetVariable(SymbolId.Empty, "Target", arguments.Count > 1 ? arguments[1] : AstraValue.Null);
                SetVariable(SymbolId.Empty, "Prototype", arguments.Count > 2 ? arguments[2] : AstraValue.Null);
                SetVariable(SymbolId.Empty, "Count", arguments.Count > 3 ? arguments[3] : AstraValue.FromInt64(0));
                var vm = new AstraVm();
                var result = vm.Execute(function.Program, function.Function, hostServices: this);
                if (result.Exception != null)
                {
                    throw result.Exception;
                }

                if (result.Status != VmExecutionStatus.Completed)
                {
                    throw new InvalidOperationException($"Graph function '{name}' ended with {result.Status}.");
                }

                return result.ReturnValue;
            }
        }

        if (methodDescriptor == "Entity.AddComponent" && _schemas != null && arguments.Count >= 2)
        {
            return AstraValue.FromBool(_schemas.Add(arguments[0].AsEntityUid(), arguments[1].AsString() ?? string.Empty));
        }

        if (methodDescriptor == "Entity.RemoveComponent" && _schemas != null && arguments.Count >= 2)
        {
            return AstraValue.FromBool(_schemas.Remove(arguments[0].AsEntityUid(), arguments[1].AsString() ?? string.Empty));
        }

        if (_nativeHandlers.TryGetValue(methodDescriptor, out var handler))
        {
            return handler(arguments);
        }
        throw new MissingMethodException($"Native method '{methodDescriptor}' is not registered in VM host services.");
    }

    public AstraValue GetComponent(AstraEntityId entityUid, string componentTypeName)
    {
        if (_schemas != null && _schemas.Has(entityUid, componentTypeName))
        {
            return AstraValue.FromObject(_schemas.TryGet(entityUid, componentTypeName));
        }

        return _components.GetValueOrDefault((entityUid, componentTypeName), AstraValue.Null);
    }

    public bool HasComponent(AstraEntityId entityUid, string componentTypeName) =>
        (_schemas != null && _schemas.Has(entityUid, componentTypeName)) ||
        _components.ContainsKey((entityUid, componentTypeName));

    public void SetComponent(AstraEntityId entityUid, string componentTypeName, AstraValue component) =>
        _components[(entityUid, componentTypeName)] = component;

    public void SetComponentField(AstraEntityId entityUid, string schemaIdAndFieldId, AstraValue value)
    {
        // Standalone in-memory slot
        _components[(entityUid, schemaIdAndFieldId)] = value;
    }
}
