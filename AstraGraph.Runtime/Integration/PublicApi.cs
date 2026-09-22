using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Runtime.Security;
using AstraGraph.State;

namespace AstraGraph.Runtime;

public interface IAstraRuntime
{
    BytecodeProgram? GetProgram(GraphId graphId);

    void RegisterProgram(BytecodeProgram program);

    void UnregisterProgram(GraphId graphId);
}

public interface IAstraTypeRegistry
{
    TypeRegistry Registry { get; }
}

public interface IAstraBindingRegistry
{
    BindingCatalog Catalog { get; }

    void IndexAssembly(System.Reflection.Assembly assembly, Func<Type, bool>? filter = null);
}

public interface IAstraStateStore
{
    AstraStateStore Store { get; }
}

public interface IAstraComponentStore
{
    DynamicComponentStore Components { get; }

    void RemoveEntity(AstraEntityId entityId);
}

public interface IAstraEventRouter
{
    GraphEventRouter Router { get; }

    bool Dispatch(object? component, object eventObject);
}

public interface IAstraHotReload
{
    PublishOutcome Publish(GraphDocument draft, string author, string message, IReadOnlyList<SchemaType>? schemas = null);

    bool Rollback(GraphId graphId);
}

public readonly record struct PublishOutcome(bool Success, RevisionId? RevisionId, string? Error);

public interface IAstraGraphManager
{
    void Initialize();

    void Update(double timeSeconds, int tick);

    void Shutdown();

    IAstraRuntime Runtime { get; }

    IAstraTypeRegistry Types { get; }

    IAstraBindingRegistry Bindings { get; }

    IAstraStateStore State { get; }

    IAstraComponentStore Components { get; }

    IAstraEventRouter Events { get; }

    IAstraHotReload HotReload { get; }

    Security.IAstraPermissionProvider Permissions { get; }
}

public interface INativeEntityAccess
{
    bool Exists(AstraEntityId entityId);

    bool HasComponent(AstraEntityId entityId, Type componentType);

    object? GetComponent(AstraEntityId entityId, Type componentType);

    IReadOnlyList<AstraEntityId> WithComponent(Type componentType);

    void Delete(AstraEntityId entityId);
}

public interface IEngineScheduleHook
{
    void Register(string systemName, IReadOnlyList<string> before, IReadOnlyList<string> after, Action<double, int> update);

    void Run(double timeSeconds, int tick);
}
