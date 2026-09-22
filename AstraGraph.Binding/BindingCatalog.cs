using System.Collections.Concurrent;
using System.Reflection;
using AstraGraph.Core;

namespace AstraGraph.Binding;

/// <summary>
/// Central registry of all native CLR types, methods, and properties indexed for AstraGraph authoring and execution.
/// </summary>
public sealed class BindingCatalog
{
    private readonly ConcurrentDictionary<string, NativeMethodDescriptor> _methodsByDescriptor = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Type, NativeTypeDescriptor> _types = new();
    private readonly ConcurrentDictionary<string, Type> _events = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private readonly TypeRegistry _typeRegistry;

    public BindingCatalog(TypeRegistry? typeRegistry = null)
    {
        _typeRegistry = typeRegistry ?? TypeRegistry.Default;
    }

    public static BindingCatalog Default { get; } = new();

    public void IndexAssembly(Assembly assembly, Func<Type, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetExportedTypes())
        {
            if (filter != null && !filter(type)) continue;
            IndexType(type);
        }
    }

    public void IndexType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.GetCustomAttribute<AstraHiddenAttribute>() != null) return;

        var typeDesc = _types.GetOrAdd(type, t => new NativeTypeDescriptor(t, t.Name));

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal)) continue;
            if (method.GetCustomAttribute<AstraHiddenAttribute>() != null) continue;

            try
            {
                var desc = CreateMethodDescriptor(method);
                _methodsByDescriptor[desc.Descriptor] = desc;
                typeDesc.Methods.Add(desc);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
            }
        }

        IndexMembers(type, typeDesc);
    }

    public void IndexEventType(Type eventType)
    {
        IndexSurface(eventType);
        _events[eventType.FullName ?? eventType.Name] = eventType;
    }

    /// <summary>
    /// Indexes public event and component types so a graph can name them without an assembly-qualified string.
    /// </summary>
    public void IndexGameplaySurface(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var type in ExportedTypes(assembly))
        {
            if (IsEventType(type))
            {
                IndexEventType(type);
            }
            else if (IsComponentType(type))
            {
                IndexSurface(type);
            }
        }
    }

    public bool TryGetNamedType(string name, out Type? type)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            type = null;
            return false;
        }

        if (_byName.TryGetValue(name, out type) || TryGetEvent(name, out type))
        {
            return type != null;
        }

        type = null;
        return false;
    }

    public static bool IsEventType(Type type) =>
        IsSurfaceCandidate(type) && type.Name.EndsWith("Event", StringComparison.Ordinal);

    public static bool IsComponentType(Type type) =>
        IsSurfaceCandidate(type) && type.Name.EndsWith("Component", StringComparison.Ordinal);

    private void IndexSurface(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var key = type.FullName ?? type.Name;
        if (!_byName.TryAdd(key, type))
        {
            return;
        }

        _byName.TryAdd(type.Name, type);
        var typeDesc = _types.GetOrAdd(type, t => new NativeTypeDescriptor(t, t.Name));
        IndexMembers(type, typeDesc);
    }

    private static bool IsSurfaceCandidate(Type type) =>
        type is { IsPublic: true, IsAbstract: false, IsGenericTypeDefinition: false, IsInterface: false, IsEnum: false }
        && type.DeclaringType == null;

    private static IEnumerable<Type> ExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    public IEnumerable<Type> Events => _events.Values;

    public bool TryGetEvent(string name, out Type? eventType)
    {
        if (_events.TryGetValue(name, out eventType))
        {
            return true;
        }

        eventType = _events.Values.FirstOrDefault(type => type.Name.Equals(name, StringComparison.Ordinal));
        return eventType != null;
    }

    public IEnumerable<NativePropertyDescriptor> SearchMembers(string query)
    {
        var members = _types.Values.SelectMany(type => type.Properties);
        if (string.IsNullOrWhiteSpace(query))
        {
            return members;
        }

        return members.Where(member =>
            member.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            member.DeclaringTypeName.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void IndexMembers(Type type, NativeTypeDescriptor typeDesc)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length != 0 || property.GetCustomAttribute<AstraHiddenAttribute>() != null)
            {
                continue;
            }

            try
            {
                var astraType = _typeRegistry.GetOrCreateNativeType(property.PropertyType);
                typeDesc.Properties.Add(new NativePropertyDescriptor(
                    property.Name,
                    property,
                    astraType,
                    property.CanRead ? target => ReadMember(target, property) : null,
                    property.CanWrite ? (target, value) => property.SetValue(target.AsObject(), value.AsObject()) : null)
                {
                    DeclaringTypeName = type.Name,
                    CanRead = property.CanRead,
                    CanWrite = property.CanWrite,
                    Side = GraphSide.Shared
                });
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
            }
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.IsSpecialName || field.GetCustomAttribute<AstraHiddenAttribute>() != null)
            {
                continue;
            }

            try
            {
                var astraType = _typeRegistry.GetOrCreateNativeType(field.FieldType);
                typeDesc.Properties.Add(new NativePropertyDescriptor(
                    field.Name,
                    null,
                    astraType,
                    target => AstraValueBox.Box(field.GetValue(target.AsObject())))
                {
                    DeclaringTypeName = type.Name,
                    CanRead = true,
                    CanWrite = !field.IsInitOnly,
                    Side = GraphSide.Shared,
                    IsField = true
                });
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
            }
        }
    }

    private static AstraValue ReadMember(AstraValue target, PropertyInfo property) =>
        AstraValueBox.Box(property.GetValue(target.AsObject()));

    public NativeMethodDescriptor RegisterMethod(
        MethodInfo method,
        string? customDescriptor = null,
        GraphSide? side = null,
        bool? isPure = null,
        bool? isDeterministic = null,
        SecurityProfile? profile = null,
        int cost = 1)
    {
        ArgumentNullException.ThrowIfNull(method);

        var desc = CreateMethodDescriptor(method, customDescriptor, side, isPure, isDeterministic, profile, cost);
        _methodsByDescriptor[desc.Descriptor] = desc;

        var typeDesc = _types.GetOrAdd(method.DeclaringType ?? typeof(object), t => new NativeTypeDescriptor(t, t.Name));
        typeDesc.Methods.Add(desc);

        return desc;
    }

    public NativeMethodDescriptor? FindMethod(string descriptor) =>
        _methodsByDescriptor.GetValueOrDefault(descriptor);

    public IEnumerable<NativeMethodDescriptor> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _methodsByDescriptor.Values;
        }

        return _methodsByDescriptor.Values.Where(m =>
            m.Descriptor.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            m.DeclaringTypeName.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private NativeMethodDescriptor CreateMethodDescriptor(
        MethodInfo method,
        string? customDescriptor = null,
        GraphSide? side = null,
        bool? isPure = null,
        bool? isDeterministic = null,
        SecurityProfile? profile = null,
        int cost = 1)
    {
        var declaringTypeName = method.DeclaringType?.Name ?? "Global";
        var paramDescs = new List<NativeParameterDescriptor>();

        foreach (var p in method.GetParameters())
        {
            var astraType = _typeRegistry.GetOrCreateNativeType(p.ParameterType);
            paramDescs.Add(new NativeParameterDescriptor(p.Name ?? "arg", p.ParameterType, astraType, p.IsOptional, p.DefaultValue));
        }

        var returnAstraType = method.ReturnType == typeof(void)
            ? (AstraType)PrimitiveType.Void
            : _typeRegistry.GetOrCreateNativeType(method.ReturnType);

        var descriptor = customDescriptor ?? BuildCanonicalDescriptor(declaringTypeName, method.Name, paramDescs);

        var resolvedSide = side ?? DetermineSide(method);
        var resolvedPure = isPure ?? (method.GetCustomAttribute<AstraPureAttribute>() != null);
        var resolvedDeterministic = isDeterministic ?? (method.GetCustomAttribute<AstraPredictedAttribute>() != null);
        var resolvedProfile = profile ?? SecurityProfile.Gameplay;

        var invoker = FastInvokerCompiler.Compile(method);

        return new NativeMethodDescriptor(
            method.Name,
            declaringTypeName,
            descriptor,
            method,
            paramDescs,
            method.ReturnType,
            returnAstraType,
            resolvedSide,
            resolvedPure,
            resolvedDeterministic,
            resolvedProfile,
            cost,
            invoker);
    }

    public static string BuildCanonicalDescriptor(string typeName, string methodName, IEnumerable<NativeParameterDescriptor> parameters)
    {
        var paramTypes = string.Join(", ", parameters.Select(p => p.Type.TypeName));
        return $"{typeName}.{methodName}({paramTypes})";
    }

    private static GraphSide DetermineSide(MethodInfo method)
    {
        if (method.GetCustomAttribute<AstraClientAttribute>() != null) return GraphSide.Client;
        if (method.GetCustomAttribute<AstraSharedAttribute>() != null) return GraphSide.Shared;
        if (method.GetCustomAttribute<AstraPredictedAttribute>() != null) return GraphSide.SharedPredicted;
        return GraphSide.Server;
    }
}
