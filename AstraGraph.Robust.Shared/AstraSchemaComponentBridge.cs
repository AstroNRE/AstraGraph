using System.Reflection;
using System.Reflection.Emit;
using AstraGraph.Core;
using AstraGraph.State;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Robust component shell for a graph-defined schema. Gameplay values stay in the dynamic store.
/// </summary>
public abstract class AstraSchemaComponentProxy : Component
{
    protected AstraSchemaComponentProxy(string schemaName)
    {
        SchemaName = schemaName;
    }

    public string SchemaName { get; }

    public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Reads a prototype mapping onto a generated proxy. Data definitions are snapshotted at
/// serialization init, so emitted component types cannot use ordinary DataField metadata.
/// </summary>
public sealed class AstraSchemaMappingReader<T> :
    ITypeReader<T, MappingDataNode>,
    ITypeCopier<T>,
    ITypeWriter<T>
    where T : AstraSchemaComponentProxy, new()
{
    public T Read(
        ISerializationManager serializationManager,
        MappingDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<T>? instanceProvider = null)
    {
        var instance = instanceProvider != null ? instanceProvider() : new T();
        CopyFields(node, instance);
        return instance;
    }

    public ValidationNode Validate(
        ISerializationManager serializationManager,
        MappingDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        var schemaName = new T().SchemaName;
        if (AstraSchemaRuntime.Registry == null ||
            !AstraSchemaRuntime.Registry.TryGetSchema(schemaName, out var schema) ||
            schema is null)
        {
            return new ErrorNode(node, $"Unknown schema {schemaName}.");
        }

        ValidationNode? failure = null;
        foreach (var (key, child) in node.Children)
        {
            var raw = child is ValueDataNode value ? value.Value : child.ToString();
            var check = AstraSchemaRuntime.Registry.ValidateValue(schema, key, raw, AstraSchemaRuntime.PrototypeName);
            if (!check.Success)
            {
                failure = new ErrorNode(child, check.Diagnostics[0].Message);
            }
        }

        return failure ?? new ValidatedValueNode(node);
    }

    public void CopyTo(
        ISerializationManager serializationManager,
        T source,
        ref T target,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        target ??= new T();
        target.Fields.Clear();
        foreach (var (key, value) in source.Fields)
        {
            target.Fields[key] = value;
        }
    }

    public DataNode Write(
        ISerializationManager serializationManager,
        T value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        var mapping = new MappingDataNode();
        foreach (var (key, field) in value.Fields)
        {
            mapping.Add(key, new ValueDataNode(field));
        }

        return mapping;
    }

    private static void CopyFields(MappingDataNode node, T instance)
    {
        foreach (var (key, child) in node.Children)
        {
            if (child is ValueDataNode value)
            {
                instance.Fields[key] = value.Value;
            }
        }
    }
}

public static class AstraSchemaRuntime
{
    public static AstraSchemaRegistry? Registry { get; set; }

    public static string? PrototypeName { get; set; }
}

/// <summary>
/// Registers one runtime component type per schema so Robust prototype YAML can say <c>type: MothroachStrike</c>.
/// </summary>
public static class AstraSchemaComponentBridge
{
    private static readonly Dictionary<string, Type> ProxyTypes = new(StringComparer.Ordinal);
    private static readonly ModuleBuilder Module = CreateModule();

    public static Type RegisterProxy(IComponentFactory factory, SchemaType schema)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(schema);
        var type = GetOrCreateProxy(schema.Name);
        if (factory.GetComponentAvailability(schema.Name) == ComponentAvailability.Unknown)
        {
            factory.RegisterTypes(type);
        }

        return type;
    }

    public static void RegisterSerializer(ISerializationManager serialization, Type proxyType)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(proxyType);
        var readerType = typeof(AstraSchemaMappingReader<>).MakeGenericType(proxyType);
        var reader = Activator.CreateInstance(readerType)
            ?? throw new InvalidOperationException($"Cannot create serializer for {proxyType.Name}.");
        var provider = serialization.GetType()
            .GetField("_regularSerializerProvider", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(serialization)
            ?? throw new InvalidOperationException("Serialization provider is not available.");
        var register = provider.GetType().GetMethod("RegisterSerializer", [typeof(object)])
            ?? throw new InvalidOperationException("RegisterSerializer is not available.");
        register.Invoke(provider, [reader]);
    }

    public static bool ApplyProxy(
        SchemaComponentSource source,
        int entity,
        AstraSchemaComponentProxy proxy,
        string? prototypeName = null)
    {
        if (AstraSchemaRuntime.Registry == null ||
            !AstraSchemaRuntime.Registry.TryGetSchema(proxy.SchemaName, out var schema) ||
            schema is null)
        {
            return false;
        }

        return source.ApplyInitial(entity, schema, proxy.Fields, prototypeName, out _);
    }

    private static Type GetOrCreateProxy(string schemaName)
    {
        if (ProxyTypes.TryGetValue(schemaName, out var existing))
        {
            return existing;
        }

        var typeName = schemaName + "Component";
        var builder = Module.DefineType(
            "AstraGraph.Generated." + typeName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(AstraSchemaComponentProxy));
        var constructor = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, schemaName);
        var baseCtor = typeof(AstraSchemaComponentProxy).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(string)],
            null) ?? throw new InvalidOperationException("Proxy constructor is missing.");
        il.Emit(OpCodes.Call, baseCtor);
        il.Emit(OpCodes.Ret);
        var created = builder.CreateType() ?? throw new InvalidOperationException($"Cannot emit {typeName}.");
        ProxyTypes[schemaName] = created;
        return created;
    }

    private static ModuleBuilder CreateModule()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AstraGraph.SchemaProxies"), AssemblyBuilderAccess.Run);
        return assembly.DefineDynamicModule("schemas");
    }
}
