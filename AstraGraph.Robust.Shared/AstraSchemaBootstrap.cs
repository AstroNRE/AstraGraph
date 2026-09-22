using System.Text.Json;
using AstraGraph.Core;
using AstraGraph.State;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Registers graph-defined component proxies before Robust freezes component net ids,
/// then attaches their YAML readers after serialization init and before prototype load.
/// </summary>
public static class AstraSchemaBootstrap
{
    private static readonly object Gate = new();
    private static Type[] _proxyTypes = [];
    private static bool _serializersRegistered;

    public static IReadOnlyList<Type> RegisterProxies(IComponentFactory factory, params string[] graphDirectories)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var registry = new AstraSchemaRegistry();
        var types = new List<Type>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in graphDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.agraph", SearchOption.AllDirectories))
            {
                GraphDocument document;
                try
                {
                    document = GraphSerializer.Deserialize(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
                {
                    continue;
                }

                foreach (var schemaDocument in document.Schemas ?? [])
                {
                    if (!schemaDocument.IsComponent || string.IsNullOrWhiteSpace(schemaDocument.Name))
                    {
                        continue;
                    }

                    var schema = SchemaDocuments.ToSchema(schemaDocument, TypeRegistry.Default);
                    registry.RegisterSchema(schema);
                    if (seen.Add(schema.Name))
                    {
                        types.Add(AstraSchemaComponentBridge.RegisterProxy(factory, schema));
                    }
                }
            }
        }

        if (types.Count == 0)
        {
            return types;
        }

        lock (Gate)
        {
            AstraSchemaRuntime.Registry = registry;
            _proxyTypes = types.ToArray();
            _serializersRegistered = false;
        }

        return types;
    }

    public static void RegisterSerializers(ISerializationManager serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        Type[] types;
        lock (Gate)
        {
            if (_serializersRegistered || _proxyTypes.Length == 0)
            {
                return;
            }

            _serializersRegistered = true;
            types = _proxyTypes;
        }

        foreach (var type in types)
        {
            AstraSchemaComponentBridge.RegisterSerializer(serialization, type);
        }
    }
}
