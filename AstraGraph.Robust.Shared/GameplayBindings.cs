using System.Collections;
using System.Globalization;
using System.Reflection;
using Robust.Shared.Log;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Robust.Client;
using AstraGraph.UI.Html;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Gameplay-profile operations a graph may call. Flow.DoAfter yields in the VM.
/// When Content is present, the host draws that wait as the engine progress bar.
/// </summary>
public static class GameplayBindings
{
    private static IEntityManager? _entities;
    private static readonly Dictionary<int, (EntityUid User, int Index)> _bars = new();
    private static int _nextBar;
    private static bool _doAfterLookedUp;
    private static object? _doAfterSystem;
    private static MethodInfo? _tryStart;
    private static ConstructorInfo? _doAfterArgs;
    private static Type? _awaitedEvent;
    private static Type? _doAfterComponent;

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
        Register(catalog, nameof(Popup), "Popup.Entity", deterministic: false);
        Register(catalog, nameof(Invoke), "System.Invoke", deterministic: false);
        Register(catalog, nameof(SetComponentField), "Component.SetField", deterministic: false);
        Register(catalog, nameof(SetDescription), "Meta.SetDescription", deterministic: false);
        Register(catalog, nameof(WithNumber), "Text.WithNumber");
    }

    public static string WithNumber(string? label, double number) =>
        string.Create(CultureInfo.InvariantCulture, $"{label}: {number:0.0}");

    public static bool SetDescription(int entity, string? text)
    {
        var uid = new EntityUid(entity);
        if (!Entities.EntityExists(uid))
        {
            return false;
        }

        Entities.EntitySysManager.GetEntitySystem<MetaDataSystem>().SetEntityDescription(uid, text ?? "");
        return true;
    }

    public static bool SetComponentField(int entity, string? componentName, string? fieldName, double value)
    {
        if (string.IsNullOrWhiteSpace(componentName) || string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        var uid = new EntityUid(entity);
        var componentType = ResolveComponentType(componentName);
        if (componentType == null || !Entities.EntityExists(uid) || !Entities.TryGetComponent(uid, componentType, out var component) || component == null)
        {
            return false;
        }

        var field = componentType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        if (field == null)
        {
            return false;
        }

        field.SetValue(component, Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture));
        if (component is IComponentDelta delta)
        {
            Entities.DirtyField(uid, delta, fieldName);
        }

        return true;
    }

    /// <summary>
    /// Calls one public entity-system method that takes the entity.
    /// The graph names the system and the method. The host resolves the live system.
    /// </summary>
    public static bool Invoke(string systemName, string methodName, int entity)
    {
        if (string.IsNullOrWhiteSpace(systemName) || string.IsNullOrWhiteSpace(methodName) || entity == 0)
        {
            return false;
        }

        var system = FindLiveSystem(systemName);
        if (system == null)
        {
            return false;
        }

        var method = FindInvokeMethod(system.GetType(), methodName);
        var uid = new EntityUid(entity);
        if (method == null || !Entities.EntityExists(uid) || !TryMakeArgument(method.GetParameters()[0].ParameterType, uid, out var argument))
        {
            return false;
        }

        try
        {
            method.Invoke(system, [argument]);
            return true;
        }
        catch (TargetInvocationException ex)
        {
            Logger.GetSawmill("astra").Error($"System.Invoke {systemName}.{methodName} failed: {ex.InnerException ?? ex}");
            return false;
        }
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

        if (wrote && string.Equals(name, "Reason", StringComparison.Ordinal))
        {
            Popup(UiActor(owner) ?? owner, value, "Small");
        }

        return wrote;
    }

    /// <summary>
    /// Floats a message above an entity. An empty recipient shows it to everyone who can see that entity.
    /// Kind is a popup size name: Small, Medium, Large, and the Caution forms of each.
    /// </summary>
    public static bool Popup(int entity, string? text, string? kind = "Small", int recipient = 0)
    {
        if (entity == 0 || string.IsNullOrEmpty(text))
        {
            return false;
        }

        var uid = new EntityUid(entity);
        if (!Entities.EntityExists(uid))
        {
            return false;
        }

        var system = FindLiveSystem("SharedPopupSystem");
        if (system == null)
        {
            return false;
        }

        var withRecipient = recipient != 0;
        var method = FindPopupEntityMethod(system.GetType(), withRecipient);
        if (method == null)
        {
            return false;
        }

        var style = PopupKind(method, kind);
        try
        {
            if (withRecipient)
            {
                var who = new EntityUid(recipient);
                var recipientType = method.GetParameters()[2].ParameterType;
                object recipientArg = Nullable.GetUnderlyingType(recipientType) != null ? (EntityUid?)who : who;
                method.Invoke(system, [text, uid, recipientArg, style]);
            }
            else
            {
                method.Invoke(system, [text, uid, style]);
            }

            return true;
        }
        catch (TargetInvocationException ex)
        {
            Logger.GetSawmill("astra").Error($"Popup.Entity failed: {ex.InnerException ?? ex}");
            return false;
        }
    }

    public static MethodInfo? FindPopupEntityMethod(Type type, bool recipient)
    {
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!method.Name.Equals("PopupEntity", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (!recipient &&
                parameters.Length == 3 &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType == typeof(EntityUid) &&
                parameters[2].ParameterType.IsEnum)
            {
                return method;
            }

            if (recipient &&
                parameters.Length == 4 &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType == typeof(EntityUid) &&
                IsEntityUid(parameters[2].ParameterType) &&
                parameters[3].ParameterType.IsEnum)
            {
                return method;
            }
        }

        return null;
    }

    public static object PopupKind(MethodInfo method, string? kind)
    {
        var enumType = method.GetParameters()[^1].ParameterType;
        var name = string.IsNullOrWhiteSpace(kind) ? "Small" : kind.Trim();
        return Enum.TryParse(enumType, name, ignoreCase: true, out var value) && value != null
            ? value
            : Enum.Parse(enumType, "Small");
    }

    private static bool IsEntityUid(Type type) =>
        type == typeof(EntityUid) || Nullable.GetUnderlyingType(type) == typeof(EntityUid);

    /// <summary>
    /// Shows the engine progress bar above the player who has this entity's UI open.
    /// The token is polled with <see cref="ReadDoAfterBar"/>.
    /// </summary>
    public static int? BeginDoAfterBar(int owner, double seconds)
    {
        if (_entities == null || owner == 0 || seconds <= 0)
        {
            return null;
        }

        try
        {
            EnsureDoAfter();
            if (_tryStart == null || _doAfterArgs == null || _awaitedEvent == null)
            {
                return null;
            }

            var userId = UiActor(owner) ?? owner;
            var user = new EntityUid(userId);
            if (!Entities.EntityExists(user))
            {
                return null;
            }

            var evt = Activator.CreateInstance(_awaitedEvent);
            var args = _doAfterArgs.Invoke([Entities, user, (float)seconds, evt, null, null, null]);
            if (args == null)
            {
                return null;
            }

            var argsType = args.GetType();
            argsType.GetField("BreakOnMove")?.SetValue(args, true);
            argsType.GetField("NeedHand")?.SetValue(args, false);
            argsType.GetField("RequireCanInteract")?.SetValue(args, false);
            argsType.GetField("BlockDuplicate")?.SetValue(args, false);
            argsType.GetField("CancelDuplicate")?.SetValue(args, false);
            argsType.GetField("Hidden")?.SetValue(args, false);

            var call = new object?[] { args, null, null };
            if (_tryStart.Invoke(_doAfterSystem, call) is not true || call[1] == null)
            {
                Logger.GetSawmill("astra").Warning($"Do-after bar did not start for entity {userId}.");
                return null;
            }

            var index = Convert.ToInt32(call[1].GetType().GetProperty("Index")?.GetValue(call[1]));
            var token = ++_nextBar;
            _bars[token] = (user, index);
            return token;
        }
        catch (Exception ex)
        {
            Logger.GetSawmill("astra").Warning($"Do-after bar failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>0 running, 1 finished, 2 cancelled.</summary>
    public static int ReadDoAfterBar(int token)
    {
        if (!_bars.TryGetValue(token, out var bar))
        {
            return 2;
        }

        var state = PollDoAfter(bar.User, bar.Index);
        if (state != 0)
        {
            _bars.Remove(token);
        }

        return state;
    }

    private static int? UiActor(int owner)
    {
        var ui = UserInterface(owner);
        if (ui == null)
        {
            return null;
        }

        foreach (var actors in ui.Actors.Values)
        {
            foreach (var actor in actors)
            {
                if (Entities.EntityExists(actor))
                {
                    return (int)actor;
                }
            }
        }

        return null;
    }

    private static void EnsureDoAfter()
    {
        if (_doAfterLookedUp)
        {
            return;
        }

        _doAfterLookedUp = true;
        _doAfterSystem = FindLiveSystem("SharedDoAfterSystem");
        _awaitedEvent = FindNamedType("AwaitedDoAfterEvent");
        _doAfterComponent = FindNamedType("DoAfterComponent");
        var argsType = FindNamedType("DoAfterArgs");
        if (_doAfterSystem == null || _awaitedEvent == null || argsType == null)
        {
            return;
        }

        foreach (var method in _doAfterSystem.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!method.Name.Equals("TryStartDoAfter", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 3 && parameters[1].IsOut)
            {
                _tryStart = method;
                break;
            }
        }

        foreach (var ctor in argsType.GetConstructors())
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length == 7 && parameters[2].ParameterType == typeof(float))
            {
                _doAfterArgs = ctor;
                break;
            }
        }
    }

    private static int PollDoAfter(EntityUid user, int index)
    {
        var component = DoAfterState(user);
        var doAfters = component?.GetType().GetField("DoAfters")?.GetValue(component) as IDictionary;
        if (doAfters == null)
        {
            return 2;
        }

        foreach (DictionaryEntry entry in doAfters)
        {
            if (entry.Value == null || Convert.ToInt32(entry.Key) != index)
            {
                continue;
            }

            var cancelled = entry.Value.GetType().GetField("CancelledTime")?.GetValue(entry.Value);
            if (cancelled != null)
            {
                return 2;
            }

            return entry.Value.GetType().GetField("Completed")?.GetValue(entry.Value) is true ? 1 : 0;
        }

        return 2;
    }

    private static object? DoAfterState(EntityUid user)
    {
        if (_doAfterComponent != null && Entities.TryGetComponent(user, _doAfterComponent, out var component))
        {
            return component;
        }

        foreach (var component in Entities.GetComponents(user))
        {
            if (component.GetType().Name == "DoAfterComponent")
            {
                return component;
            }
        }

        return null;
    }

    private static Type? FindNamedType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null).Cast<Type>().ToArray();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type.Name == name && !type.IsAbstract)
                {
                    return type;
                }
            }
        }

        return null;
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

    private static Type? ResolveComponentType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null).Cast<Type>().ToArray();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type.Name == name && typeof(IComponent).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static object? FindLiveSystem(string name)
    {
        object? inherited = null;
        foreach (var type in Entities.EntitySysManager.GetEntitySystemTypes())
        {
            if (!NameOrBaseName(type, name) || !Entities.EntitySysManager.TryGetEntitySystem(type, out var system) || system == null)
            {
                continue;
            }

            if (type.Name == name)
            {
                return system;
            }

            inherited ??= system;
        }

        return inherited;
    }

    private static bool NameOrBaseName(Type type, string name)
    {
        for (var cursor = type; cursor != null && cursor != typeof(object); cursor = cursor.BaseType)
        {
            if (cursor.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    private static MethodInfo? FindInvokeMethod(Type systemType, string methodName)
    {
        var matches = new List<MethodInfo>();
        foreach (var method in systemType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!method.Name.Equals(methodName, StringComparison.Ordinal) || method.IsGenericMethodDefinition)
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].IsOut || parameters[0].ParameterType.IsByRef)
            {
                continue;
            }

            matches.Add(method);
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        MethodInfo? entityArgument = null;
        foreach (var method in matches)
        {
            if (!IsEntityArgument(method.GetParameters()[0].ParameterType))
            {
                continue;
            }

            if (entityArgument != null)
            {
                return null;
            }

            entityArgument = method;
        }

        return entityArgument;
    }

    private static bool IsEntityArgument(Type type) =>
        type == typeof(EntityUid) ||
        type == typeof(int) ||
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Entity<>);

    private static bool TryMakeArgument(Type parameterType, EntityUid uid, out object? argument)
    {
        argument = null;
        if (parameterType == typeof(EntityUid))
        {
            argument = uid;
            return true;
        }

        if (parameterType == typeof(int))
        {
            argument = (int)uid;
            return true;
        }

        if (!parameterType.IsValueType || !parameterType.IsGenericType || parameterType.GetGenericTypeDefinition() != typeof(Entity<>))
        {
            return false;
        }

        var componentType = parameterType.GetGenericArguments()[0];
        if (!Entities.TryGetComponent(uid, componentType, out var component))
        {
            return false;
        }

        argument = Activator.CreateInstance(parameterType, uid, component);
        return argument != null;
    }

    private static IEntityManager Entities =>
        _entities ?? throw new InvalidOperationException("Gameplay bindings are not attached to an entity manager.");

    private static void Register(BindingCatalog catalog, string name, string descriptor, bool deterministic = true)
    {
        var method = typeof(GameplayBindings).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing gameplay method {name}.");
        catalog.RegisterMethod(method, customDescriptor: descriptor, isDeterministic: deterministic, profile: SecurityProfile.Gameplay);
    }
}
