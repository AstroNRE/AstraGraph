using System.Collections.Concurrent;
using AstraGraph.Core;

namespace AstraGraph.State;

/// <summary>
/// High-performance dynamic component store for schema-based Astra components on entities.
/// Provides O(1) presence verification and sparse-set indexing for mixed ECS queries.
/// </summary>
public sealed class DynamicComponentStore
{
    private sealed class SchemaPool
    {
        public SchemaType Schema { get; set; }
        public SparseSet Presence { get; } = new();
        public Dictionary<int, PackedFieldStorage> Instances { get; } = [];

        public SchemaPool(SchemaType schema)
        {
            Schema = schema;
        }
    }

    private readonly ConcurrentDictionary<SchemaId, SchemaPool> _poolsBySchema = new();
    private readonly ConcurrentDictionary<int, HashSet<SchemaId>> _schemasByEntity = new();
    private readonly Lock _lock = new();

    public PackedFieldStorage AddComponent(int entityUid, SchemaType schema, AstraValue[]? initialValues = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (entityUid < 0) throw new ArgumentOutOfRangeException(nameof(entityUid), "EntityUid must be non-negative.");

        lock (_lock)
        {
            var pool = _poolsBySchema.GetOrAdd(schema.Id, id => new SchemaPool(schema));
            pool.Schema = schema;

            var storage = new PackedFieldStorage(schema, initialValues);
            pool.Instances[entityUid] = storage;
            pool.Presence.Add(entityUid);

            var entitySchemas = _schemasByEntity.GetOrAdd(entityUid, _ => []);
            entitySchemas.Add(schema.Id);

            return storage;
        }
    }

    public bool RemoveComponent(int entityUid, SchemaId schemaId)
    {
        lock (_lock)
        {
            if (_poolsBySchema.TryGetValue(schemaId, out var pool))
            {
                if (pool.Instances.Remove(entityUid))
                {
                    pool.Presence.Remove(entityUid);

                    if (_schemasByEntity.TryGetValue(entityUid, out var set))
                    {
                        set.Remove(schemaId);
                    }
                    return true;
                }
            }
            return false;
        }
    }

    public bool HasComponent(int entityUid, SchemaId schemaId)
    {
        lock (_lock)
        {
            if (_poolsBySchema.TryGetValue(schemaId, out var pool))
            {
                return pool.Presence.Contains(entityUid);
            }
            return false;
        }
    }

    public bool TryGetComponent(int entityUid, SchemaId schemaId, out PackedFieldStorage? storage)
    {
        lock (_lock)
        {
            if (_poolsBySchema.TryGetValue(schemaId, out var pool))
            {
                return pool.Instances.TryGetValue(entityUid, out storage);
            }
            storage = null;
            return false;
        }
    }

    public PackedFieldStorage GetComponent(int entityUid, SchemaId schemaId)
    {
        if (TryGetComponent(entityUid, schemaId, out var storage) && storage is not null)
        {
            return storage;
        }
        throw new KeyNotFoundException($"Component with SchemaId '{schemaId}' not found on Entity {entityUid}.");
    }

    public IReadOnlyList<int> GetEntitiesWithComponent(SchemaId schemaId)
    {
        lock (_lock)
        {
            if (_poolsBySchema.TryGetValue(schemaId, out var pool))
            {
                return pool.Presence.Dense.ToArray();
            }
            return [];
        }
    }

    public IReadOnlyList<SchemaId> GetAllSchemas()
    {
        lock (_lock)
        {
            return _poolsBySchema.Keys.ToArray();
        }
    }

    /// <summary>
    /// Deterministically cleans up all dynamic components when an entity is deleted from the world.
    /// </summary>
    public void ClearEntity(int entityUid)
    {
        lock (_lock)
        {
            if (_schemasByEntity.TryRemove(entityUid, out var schemas))
            {
                foreach (var schemaId in schemas)
                {
                    if (_poolsBySchema.TryGetValue(schemaId, out var pool))
                    {
                        pool.Instances.Remove(entityUid);
                        pool.Presence.Remove(entityUid);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Migrates all instances of a schema in-place according to an executable migration function.
    /// </summary>
    public void MigrateSchema(SchemaId schemaId, SchemaType targetSchema, Func<PackedFieldStorage, PackedFieldStorage> migrationFunc)
    {
        ArgumentNullException.ThrowIfNull(targetSchema);
        ArgumentNullException.ThrowIfNull(migrationFunc);

        lock (_lock)
        {
            if (_poolsBySchema.TryGetValue(schemaId, out var pool))
            {
                pool.Schema = targetSchema;
                foreach (var (entityUid, oldStorage) in pool.Instances.ToArray())
                {
                    pool.Instances[entityUid] = migrationFunc(oldStorage);
                }
            }
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _poolsBySchema.Clear();
            _schemasByEntity.Clear();
        }
    }
}
