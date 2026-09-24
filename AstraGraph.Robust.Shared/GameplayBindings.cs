using System.Collections;
using System.Reflection;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Robust.Client;
using AstraGraph.UI.Html;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Gameplay-profile operations a graph may call. DoAfter lives in Content, so the
/// graph node Flow.DoAfter is the supported latent form on this engine commit.
/// </summary>
public static class GameplayBindings
{
    private static IEntityManager? _entities;

    public static void Bind(IEntityManager entities) => _entities = entities;

    public static int Spawn()
    {
        var uid = Entities.SpawnEntity(null, MapCoordinates.Nullspace);
        return (int)uid;
    }

    public static void Delete(int entity)
    {
        Entities.DeleteEntity(new EntityUid(entity));
    }

    public static void Index(BindingCatalog catalog, IEntityManager entities)
    {
        Bind(entities);
        Register(catalog, nameof(Spawn), "Entity.Spawn");
        Register(catalog, nameof(Delete), "Entity.Delete");
        Register(catalog, nameof(EntityExists), "Entity.Exists");
        Register(catalog, nameof(EntityTerminating), "Entity.Terminating");
        Register(catalog, nameof(GetCoordinates), "Entity.GetCoordinates");
        Register(catalog, nameof(SpawnEntity), "Entity.SpawnAt");
        Register(catalog, nameof(QueueDeleteEntity), "Entity.QueueDelete");
        Register(catalog, nameof(ContainerHas), "Container.Has");
        Register(catalog, nameof(ContainerInsert), "Container.Insert");
        Register(catalog, nameof(ContainerRemove), "Container.Remove");
        Register(catalog, nameof(ContainerContents), "Container.Contents");
        Register(catalog, nameof(InventoryFind), "Inventory.Find");
        Register(catalog, nameof(InventoryContains), "Inventory.Contains");
        Register(catalog, nameof(InventoryTryInsert), "Inventory.TryInsert");
        Register(catalog, nameof(InventoryTryRemove), "Inventory.TryRemove");
        Register(catalog, nameof(GetHeldItem), "Entity.GetHeldItem");
        Register(catalog, nameof(UiRows), "Ui.Rows");
        Register(catalog, nameof(BuiField), "Bui.Field");
        Register(catalog, nameof(BuiSet), "Bui.Set", deterministic: false);
    }

    public static string UiRows(AstraList? list, string? idField, string? textField, string? disabledField) =>
        UiListRows.Format(list, idField, textField, disabledField);

    public static string BuiField(string? payload, string? name) =>
        UiListRows.Field(payload, name);

    public static bool BuiSet(int owner, string name, string value)
    {
        var ui = UserInterface(owner);
        if (string.IsNullOrEmpty(name) || ui == null || InterfacesField?.GetValue(ui) is not IDictionary interfaces || interfaces.Count == 0)
        {
            return false;
        }

        var uid = new EntityUid(owner);
        var system = Entities.EntitySysManager.GetEntitySystem<SharedUserInterfaceSystem>();
        var wrote = false;
        foreach (DictionaryEntry entry in interfaces)
        {
            if (entry.Key is not Enum key || entry.Value is not InterfaceData data ||
                data.ClientType.IndexOf("AstraHtml", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            ui.States.TryGetValue(key, out var previous);
            system.SetUiState(new Entity<UserInterfaceComponent?>(uid, ui), key, AssignState(previous, name, value));
            wrote = true;
        }

        return wrote;
    }

    private static UserInterfaceComponent? UserInterface(int owner)
    {
        var uid = new EntityUid(owner);
        if (!Entities.EntityExists(uid) ||
            !Entities.ComponentFactory.TryGetRegistration<UserInterfaceComponent>(out _) ||
            !Entities.TryGetComponent(uid, out UserInterfaceComponent? ui))
        {
            return null;
        }

        return ui;
    }

    public static AstraBuiState AssignState(BoundUserInterfaceState? previous, string name, string value)
    {
        var source = previous as AstraBuiState;
        var state = new AstraBuiState
        {
            Revision = (source?.Revision ?? 0) + 1,
            ContractHash = source?.ContractHash ?? "",
            Values = source == null ? [] : new Dictionary<string, string>(source.Values, StringComparer.Ordinal),
            TypedValues = source == null ? [] : new Dictionary<string, string>(source.TypedValues, StringComparer.Ordinal),
            ClientActions = source == null ? [] : [.. source.ClientActions],
            ServerNotifications = source == null ? [] : [.. source.ServerNotifications]
        };
        state.Values[name] = value ?? "";
        state.TypedValues[name] = "string:" + (value ?? "");
        return state;
    }

    private static readonly FieldInfo? InterfacesField =
        typeof(UserInterfaceComponent).GetField("Interfaces", BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool ContainerHas(int owner, string containerId) =>
        TryGetContainer(owner, containerId, out _);

    public static bool ContainerInsert(int owner, string containerId, int item)
    {
        if (!TryGetContainer(owner, containerId, out var container))
        {
            return false;
        }

        return Containers.Insert(new EntityUid(item), container);
    }

    public static bool ContainerRemove(int owner, string containerId, int item)
    {
        if (!TryGetContainer(owner, containerId, out var container))
        {
            return false;
        }

        var uid = new EntityUid(item);
        return container.Contains(uid) && Containers.Remove(uid, container);
    }

    public static int[] ContainerContents(int owner, string containerId)
    {
        if (!TryGetContainer(owner, containerId, out var container))
        {
            return [];
        }

        var contained = container.ContainedEntities;
        var ids = new int[contained.Count];
        for (var i = 0; i < contained.Count; i++)
        {
            ids[i] = (int)contained[i];
        }

        return ids;
    }

    public static int InventoryFind(int owner, string prototypeId)
    {
        if (!Entities.TryGetComponent(new EntityUid(owner), out ContainerManagerComponent? manager) || manager is null)
        {
            return 0;
        }

        foreach (var container in manager.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (Entities.TryGetComponent(contained, out MetaDataComponent? metadata) &&
                    metadata?.EntityPrototype?.ID == prototypeId)
                {
                    return (int)contained;
                }
            }
        }

        return 0;
    }

    public static bool InventoryContains(int owner, int item)
    {
        if (!Entities.TryGetComponent(new EntityUid(owner), out ContainerManagerComponent? manager) || manager is null)
        {
            return false;
        }

        var uid = new EntityUid(item);
        foreach (var container in manager.Containers.Values)
        {
            if (container.Contains(uid))
            {
                return true;
            }
        }

        return false;
    }

    public static bool InventoryTryInsert(int owner, string containerId, int item) =>
        ContainerInsert(owner, containerId, item);

    public static bool InventoryTryRemove(int owner, int item)
    {
        if (!InventoryContains(owner, item))
        {
            return false;
        }

        return Containers.RemoveEntity(new EntityUid(owner), new EntityUid(item));
    }

    public static int GetHeldItem(int holder, string containerId)
    {
        if (!TryGetContainer(holder, containerId, out var container) || container.Count == 0)
        {
            return 0;
        }

        return (int)container.ContainedEntities[0];
    }

    private static bool TryGetContainer(int owner, string containerId, out BaseContainer container)
    {
        if (string.IsNullOrEmpty(containerId) ||
            !Entities.EntityExists(new EntityUid(owner)) ||
            !Containers.TryGetContainer(new EntityUid(owner), containerId, out var found) ||
            found is null)
        {
            container = null!;
            return false;
        }

        container = found;
        return true;
    }

    private static SharedContainerSystem Containers =>
        Entities.EntitySysManager.GetEntitySystem<SharedContainerSystem>();

    public static bool EntityExists(int entity) =>
        Entities.EntityExists(new EntityUid(entity));

    public static bool EntityTerminating(int entity)
    {
        var uid = new EntityUid(entity);
        return !Entities.TryGetComponent(uid, out MetaDataComponent? metadata) ||
               metadata == null ||
               metadata.EntityLifeStage >= EntityLifeStage.Terminating;
    }

    public static object? GetCoordinates(int entity)
    {
        var uid = new EntityUid(entity);
        return Entities.TryGetComponent(uid, out TransformComponent? transform) ? transform?.Coordinates : null;
    }

    public static int SpawnEntity(string prototype, object? coordinates)
    {
        var uid = coordinates switch
        {
            EntityCoordinates entityCoordinates => Entities.SpawnEntity(prototype, entityCoordinates),
            MapCoordinates mapCoordinates => Entities.SpawnEntity(prototype, mapCoordinates),
            _ => Entities.SpawnEntity(prototype, MapCoordinates.Nullspace)
        };
        return (int)uid;
    }

    public static void QueueDeleteEntity(int entity) =>
        Entities.QueueDeleteEntity(new EntityUid(entity));

    private static IEntityManager Entities =>
        _entities ?? throw new InvalidOperationException("Gameplay bindings are not attached to an entity manager.");

    private static void Register(BindingCatalog catalog, string name, string descriptor, bool deterministic = true)
    {
        var method = typeof(GameplayBindings).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing gameplay method {name}.");
        catalog.RegisterMethod(method, customDescriptor: descriptor, isDeterministic: deterministic, profile: SecurityProfile.Gameplay);
    }
}
