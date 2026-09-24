using System.Diagnostics;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Runtime;

public sealed record UiBindingWatch(string ElementId, string Property, string StateVariable, object? Value);

public sealed record UiDebugSnapshot(
    string DocumentName,
    IReadOnlyDictionary<string, object?> State,
    IReadOnlyList<UiBindingWatch> Bindings,
    string? LastEvent,
    string? LastAction);

public static class UiDebug
{
    public static UiDebugSnapshot Capture(UiSession session, string? lastEvent = null, string? lastAction = null)
    {
        var watches = new List<UiBindingWatch>();
        foreach (var binding in session.Document.Bindings)
        {
            watches.Add(new UiBindingWatch(
                binding.ElementId,
                binding.TargetProperty,
                binding.StateVariable,
                session.StateManager.GetVariable(binding.StateVariable)));
        }

        return new UiDebugSnapshot(
            session.Document.Name,
            session.StateManager.GetAllVariables(),
            watches,
            lastEvent,
            lastAction);
    }
}

public sealed class UiProfiler
{
    private readonly Stopwatch _reconcile = new();
    private long _controlCount;
    private int _stateUpdates;
    private int _graphExecutions;

    public long LastReconcileMicroseconds { get; private set; }

    public long ControlCount => _controlCount;

    public int StateUpdates => _stateUpdates;

    public int GraphExecutions => _graphExecutions;

    public void BeginReconcile() => _reconcile.Restart();

    public void EndReconcile(int controlCount)
    {
        _reconcile.Stop();
        LastReconcileMicroseconds = (long)(_reconcile.Elapsed.TotalMilliseconds * 1000);
        _controlCount = controlCount;
    }

    public void StateUpdated() => _stateUpdates++;

    public void GraphExecuted() => _graphExecutions++;
}

public static class UiLocalization
{
    public static void Apply(UiDocument document, string locale, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tables)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!tables.TryGetValue(locale, out var table))
        {
            return;
        }

        foreach (var element in document.AllElements())
        {
            if (element.TryGetProperty("LocId", out var key) && key is string id && table.TryGetValue(id, out var text))
            {
                element.Text = text;
            }
        }
    }
}
