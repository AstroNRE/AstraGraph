using AstraGraph.UI.Catalog;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Runtime;

public sealed record BuiSessionContext(string? User, string? Entity, string? BuiKey, string? BoundEntity);

public sealed record BuiNotice(string Name, IReadOnlyDictionary<string, object?> Payload);

/// <summary>
/// Server-side BUI session. The client can only submit declared actions; state changes happen here.
/// </summary>
public sealed class BuiAuthoritativeSession
{
    private readonly Dictionary<string, object?> _state;
    private readonly List<BuiNotice> _notifications = [];
    private int _revision;

    public BuiAuthoritativeSession(UiBuiContractDocument contract, int revision = 0)
    {
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _revision = revision;
        _state = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in contract.State)
        {
            _state[field.Name] = field.DefaultValue;
        }
    }

    public UiBuiContractDocument Contract { get; }

    public int Revision => _revision;

    public IReadOnlyDictionary<string, object?> State => _state;

    public bool SetState(string name, object? value, out string? error)
    {
        var field = Contract.State.FirstOrDefault(item => item.Name.Equals(name, StringComparison.Ordinal));
        if (field == null)
        {
            error = $"Unknown BUI state '{name}'.";
            return false;
        }

        if (!UiValueCoercion.TypesCompatible(field.TypeName, value?.GetType().Name ?? field.TypeName, converter: null) && value != null)
        {
            error = $"State '{name}' expects {field.TypeName}.";
            return false;
        }

        _state[name] = value;
        _revision++;
        error = null;
        return true;
    }

    public bool TryHandleAction(string actionName, IReadOnlyDictionary<string, object?> payload, int seenRevision, out string? error)
    {
        if (seenRevision != _revision)
        {
            error = seenRevision < _revision ? "Stale BUI revision." : "Future BUI revision.";
            return false;
        }

        var action = Contract.Actions.FirstOrDefault(item => item.Name.Equals(actionName, StringComparison.Ordinal));
        if (action == null)
        {
            error = $"Unknown BUI action '{actionName}'.";
            return false;
        }

        foreach (var parameter in action.Parameters)
        {
            if (!payload.TryGetValue(parameter.Name, out var value))
            {
                error = $"Action '{actionName}' is missing '{parameter.Name}'.";
                return false;
            }

            if (!UiValueCoercion.TypesCompatible(parameter.TypeName, value?.GetType().Name ?? "string", converter: null))
            {
                error = $"Action '{actionName}' parameter '{parameter.Name}' expects {parameter.TypeName}.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public BuiSessionContext Context { get; set; } = new(null, null, null, null);

    public IReadOnlyList<BuiNotice> Notifications => _notifications;

    public bool TryNotify(string name, IReadOnlyDictionary<string, object?> payload, out string? error)
    {
        var notification = Contract.Notifications.FirstOrDefault(item => item.Name.Equals(name, StringComparison.Ordinal));
        if (notification == null)
        {
            error = $"Unknown BUI notification '{name}'.";
            return false;
        }

        foreach (var parameter in notification.Parameters)
        {
            if (!payload.TryGetValue(parameter.Name, out var value))
            {
                error = $"Notification '{name}' is missing '{parameter.Name}'.";
                return false;
            }

            if (!UiValueCoercion.TypesCompatible(parameter.TypeName, value?.GetType().Name ?? "string", converter: null))
            {
                error = $"Notification '{name}' parameter '{parameter.Name}' expects {parameter.TypeName}.";
                return false;
            }
        }

        _notifications.Add(new BuiNotice(name, new Dictionary<string, object?>(payload, StringComparer.Ordinal)));
        error = null;
        return true;
    }

    public AstraBuiContract Snapshot() => new(
        new Dictionary<string, object?>(_state, StringComparer.Ordinal),
        Contract.Actions.Select(action => action.Name).ToArray(),
        Contract.Notifications.Select(item => item.Name).ToArray(),
        _revision,
        Contract.Id);
}
