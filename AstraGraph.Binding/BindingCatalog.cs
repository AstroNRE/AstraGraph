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

        // Index Methods
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            if (method.IsSpecialName) continue; // skip property getters/setters
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
    }

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
