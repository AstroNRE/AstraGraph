using AstraGraph.Core;

namespace AstraGraph.Runtime;

public sealed class NativeEntityQueryBridge : IEcsQueryBridge
{
    private readonly INativeEntityAccess _access;

    public NativeEntityQueryBridge(INativeEntityAccess access)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    public bool HasNativeComponent(AstraEntityId entityUid, Type clrComponentType) =>
        _access.HasComponent(entityUid, clrComponentType);

    public object? GetNativeComponent(AstraEntityId entityUid, Type clrComponentType) =>
        _access.GetComponent(entityUid, clrComponentType);

    public IReadOnlyList<AstraEntityId> GetEntitiesWithNativeComponent(Type clrComponentType) =>
        _access.WithComponent(clrComponentType);
}

public sealed class FixedPhaseScheduleHook : IEngineScheduleHook
{
    private readonly List<(string Name, Action<double, int> Update)> _updates = [];

    public List<string> ApproximateOrderNotes { get; } = [];

    public void Register(string systemName, IReadOnlyList<string> before, IReadOnlyList<string> after, Action<double, int> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (before.Count > 0 || after.Count > 0)
        {
            ApproximateOrderNotes.Add(
                $"{systemName} requested Before [{string.Join(", ", before)}] After [{string.Join(", ", after)}] and is running in the fixed Astra host phase.");
        }

        _updates.Add((systemName, update));
    }

    public void Run(double timeSeconds, int tick)
    {
        foreach (var (_, update) in _updates)
        {
            try
            {
                update(timeSeconds, tick);
            }
            catch (Exception ex)
            {
                LastError = ex;
            }
        }
    }

    public Exception? LastError { get; private set; }
}

public sealed class DynamicScheduleHook : IEngineScheduleHook
{
    public sealed record Request(string SystemName, IReadOnlyList<string> Before, IReadOnlyList<string> After, Action<double, int> Update);

    private readonly List<Request> _requests = [];

    public IReadOnlyList<Request> Requests => _requests;

    public void Register(string systemName, IReadOnlyList<string> before, IReadOnlyList<string> after, Action<double, int> update)
    {
        _requests.Add(new Request(systemName, before, after, update));
    }

    public void Run(double timeSeconds, int tick)
    {
        foreach (var request in _requests)
        {
            request.Update(timeSeconds, tick);
        }
    }
}
