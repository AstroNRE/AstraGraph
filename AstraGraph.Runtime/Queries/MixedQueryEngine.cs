using AstraGraph.Core;
using AstraGraph.State;

namespace AstraGraph.Runtime;

public sealed record MixedQueryResult(
    AstraEntityId EntityUid,
    IReadOnlyDictionary<SchemaId, PackedFieldStorage> AstraComponents);

/// <summary>
/// Optimizing mixed query planner and engine intersecting native CLR ECS components
/// and dynamic Astra schemas without full entity world scans.
/// </summary>
public sealed class MixedQueryEngine
{
    private readonly DynamicComponentStore _componentStore;
    private readonly IEcsQueryBridge _ecsBridge;

    public MixedQueryEngine(DynamicComponentStore componentStore, IEcsQueryBridge ecsBridge)
    {
        _componentStore = componentStore ?? throw new ArgumentNullException(nameof(componentStore));
        _ecsBridge = ecsBridge ?? throw new ArgumentNullException(nameof(ecsBridge));
    }

    public List<MixedQueryResult> Execute(QueryDescriptor query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.RequiredAstraSchemas.Count == 0 && query.RequiredNativeTypes.Count == 0)
        {
            // Empty query: protect against full world scan
            return [];
        }

        // 1. Select the most selective index (smallest candidate count)
        IReadOnlyList<AstraEntityId>? primaryCandidates = null;
        var minCount = int.MaxValue;

        foreach (var schemaId in query.RequiredAstraSchemas)
        {
            var entities = _componentStore.GetEntitiesWithComponent(schemaId);
            if (entities.Count < minCount)
            {
                minCount = entities.Count;
                primaryCandidates = entities;
            }
        }

        foreach (var nativeType in query.RequiredNativeTypes)
        {
            var entities = _ecsBridge.GetEntitiesWithNativeComponent(nativeType);
            if (entities.Count < minCount)
            {
                minCount = entities.Count;
                primaryCandidates = entities;
            }
        }

        if (primaryCandidates is null || primaryCandidates.Count == 0)
        {
            return [];
        }

        // 2. Filter candidates against remaining constraints
        var results = new List<MixedQueryResult>(primaryCandidates.Count);

        foreach (var entity in primaryCandidates)
        {
            var match = true;

            // Check excluded Astra schemas
            foreach (var excludedSchema in query.ExcludedAstraSchemas)
            {
                if (_componentStore.HasComponent(entity, excludedSchema))
                {
                    match = false;
                    break;
                }
            }
            if (!match) continue;

            // Check excluded native components
            foreach (var excludedNative in query.ExcludedNativeTypes)
            {
                if (_ecsBridge.HasNativeComponent(entity, excludedNative))
                {
                    match = false;
                    break;
                }
            }
            if (!match) continue;

            // Check remaining required Astra schemas
            var astraComponents = new Dictionary<SchemaId, PackedFieldStorage>();
            foreach (var requiredSchema in query.RequiredAstraSchemas)
            {
                if (!_componentStore.TryGetComponent(entity, requiredSchema, out var storage) || storage is null)
                {
                    match = false;
                    break;
                }
                astraComponents[requiredSchema] = storage;
            }
            if (!match) continue;

            // Check remaining required native components
            foreach (var requiredNative in query.RequiredNativeTypes)
            {
                if (!_ecsBridge.HasNativeComponent(entity, requiredNative))
                {
                    match = false;
                    break;
                }
            }
            if (!match) continue;

            results.Add(new MixedQueryResult(entity, astraComponents));
        }

        return results;
    }
}
