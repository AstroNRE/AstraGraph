using System.Reflection;
using AstraGraph.Binding;
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
    }

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

    private static void Register(BindingCatalog catalog, string name, string descriptor)
    {
        var method = typeof(GameplayBindings).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing gameplay method {name}.");
        catalog.RegisterMethod(method, customDescriptor: descriptor, isDeterministic: true, profile: SecurityProfile.Gameplay);
    }
}
