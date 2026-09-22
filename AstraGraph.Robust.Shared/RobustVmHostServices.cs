using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.State;
using AstraGraph.VM;
using Robust.Shared.GameObjects;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// VM services backed by the live entity manager and the gameplay binding catalog.
/// Unknown calls fall back to the in-memory host so pure functions still work.
/// </summary>
public sealed class RobustVmHostServices : IVmHostServices
{
    private readonly IEntityManager _entities;
    private readonly DefaultVmHostServices _fallback = new();
    private BindingCatalog? _catalog;
    private ISchemaComponentSource? _schemas;

    public RobustVmHostServices(IEntityManager entities)
    {
        _entities = entities;
    }

    public void UseCatalog(BindingCatalog catalog) => _catalog = catalog;

    public void UseSchemaComponents(ISchemaComponentSource source)
    {
        _schemas = source;
        _fallback.UseSchemaComponents(source);
    }

    public void PushEventContext(AstraEventInvocationContext context) => _fallback.PushEventContext(context);

    public void PopEventContext() => _fallback.PopEventContext();

    public AstraEventInvocationContext? PeekEventContext() => _fallback.PeekEventContext();

    public AstraValue GetVariable(SymbolId variableId, string name) => _fallback.GetVariable(variableId, name);

    public void SetVariable(SymbolId variableId, string name, AstraValue value) =>
        _fallback.SetVariable(variableId, name, value);

    public AstraValue CallNative(string methodDescriptor, IReadOnlyList<AstraValue> arguments)
    {
        var found = _catalog?.FindMethod(methodDescriptor);
        if (found != null)
        {
            return found.Invoker(arguments is AstraValue[] array ? array : [.. arguments]);
        }

        return _fallback.CallNative(methodDescriptor, arguments);
    }

    public bool HasComponent(AstraEntityId entityUid, string componentTypeName)
    {
        if (_schemas != null && _schemas.Has(entityUid, componentTypeName))
        {
            return true;
        }

        var type = Type.GetType(componentTypeName);
        if (type != null && _entities.HasComponent(RobustEntityMap.ToUid(entityUid), type))
        {
            return true;
        }

        return _fallback.HasComponent(entityUid, componentTypeName);
    }

    public AstraValue GetComponent(AstraEntityId entityUid, string componentTypeName)
    {
        if (_schemas != null && _schemas.Has(entityUid, componentTypeName))
        {
            return AstraValue.FromObject(_schemas.TryGet(entityUid, componentTypeName));
        }

        var type = Type.GetType(componentTypeName);
        if (type != null && _entities.TryGetComponent(RobustEntityMap.ToUid(entityUid), type, out var component) && component != null)
        {
            return AstraValue.FromObject(component);
        }

        return _fallback.GetComponent(entityUid, componentTypeName);
    }

    public void SetComponentField(AstraEntityId entityUid, string schemaIdAndFieldId, AstraValue value) =>
        _fallback.SetComponentField(entityUid, schemaIdAndFieldId, value);
}
