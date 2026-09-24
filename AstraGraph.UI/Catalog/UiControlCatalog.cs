using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace AstraGraph.UI.Catalog;

/// <summary>
/// Indexes real CLR control types and the properties and events Studio may author.
/// Creation and property access go through compiled delegates cached after indexing.
/// </summary>
public sealed class UiControlCatalog
{
    private static readonly HashSet<string> SkippedProperties = new(StringComparer.Ordinal)
    {
        "Name", "Parent", "Children", "ChildCount", "Window", "Root", "UserData", "UIScale",
        "Size", "DesiredSize", "GlobalPosition", "GlobalRect", "Rect", "Position", "PixelPosition"
    };

    private static readonly HashSet<string> LeafNames = new(StringComparer.Ordinal)
    {
        "Label", "TextureRect", "ProgressBar", "LineEdit", "Slider", "SpinBox", "CheckBox", "RichTextLabel", "SpriteView"
    };

    private readonly ConcurrentDictionary<string, UiControlDescriptor> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _shortNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<object>> _factories = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PropertySlot> _properties = new(StringComparer.Ordinal);
    private readonly List<UiAttachedPropertyDescriptor> _attached = [];
    private readonly List<string> _styles = [];

    public IReadOnlyList<UiAttachedPropertyDescriptor> AttachedProperties => _attached;

    public IReadOnlyList<string> StyleClasses => _styles;

    public IReadOnlyList<UiControlDescriptor> Controls =>
        _byId.Values.OrderBy(control => control.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(control => control.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void Add(UiControlDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _byId[descriptor.TypeId] = descriptor;
        _shortNames.TryAdd(ShortName(descriptor.TypeId), descriptor.TypeId);
    }

    public void AddAttached(UiAttachedPropertyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _attached.RemoveAll(item => item.ParentTypeId == descriptor.ParentTypeId && item.Name == descriptor.Name);
        _attached.Add(descriptor);
    }

    public void RegisterStyleClass(string className)
    {
        if (string.IsNullOrWhiteSpace(className) || _styles.Contains(className, StringComparer.Ordinal))
        {
            return;
        }

        _styles.Add(className);
    }

    public bool TryGet(string typeId, out UiControlDescriptor? descriptor)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            descriptor = null;
            return false;
        }

        if (_byId.TryGetValue(typeId, out descriptor))
        {
            return true;
        }

        if (_shortNames.TryGetValue(typeId, out var full) && _byId.TryGetValue(full, out descriptor))
        {
            return true;
        }

        descriptor = _byId.Values.FirstOrDefault(item =>
            item.DisplayName.Equals(typeId, StringComparison.OrdinalIgnoreCase) ||
            ShortName(item.TypeId).Equals(typeId, StringComparison.OrdinalIgnoreCase));
        return descriptor != null;
    }

    public bool Contains(string typeId) => TryGet(typeId, out _);

    public void IndexAssembly(Assembly assembly, Type controlBaseType)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(controlBaseType);
        foreach (var type in ExportedTypes(assembly))
        {
            if (IsControl(type, controlBaseType))
            {
                IndexType(type, controlBaseType);
            }
        }
    }

    public void IndexType(Type type, Type controlBaseType)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(controlBaseType);
        if (!IsControl(type, controlBaseType) || IsHidden(type))
        {
            return;
        }

        var typeId = type.FullName ?? type.Name;
        var properties = new List<UiPropertyDescriptor>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var property in type.GetProperties(flags))
        {
            try
            {
                if (!IsAuthorableProperty(property, controlBaseType))
                {
                    continue;
                }

                properties.Add(DescribeProperty(property));
                CacheProperty(typeId, type, property);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or TypeLoadException)
            {
            }
        }

        var events = new List<UiEventDescriptor>();
        foreach (var uiEvent in type.GetEvents(flags))
        {
            try
            {
                if (IsHidden(uiEvent) || uiEvent.EventHandlerType == null)
                {
                    continue;
                }

                events.Add(DescribeEvent(uiEvent));
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or TypeLoadException)
            {
            }
        }

        Add(new UiControlDescriptor(
            typeId,
            typeId,
            DisplayName(type.Name),
            CategoryFor(type),
            CanHaveChildren(type),
            properties,
            events));

        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor != null)
        {
            var factory = Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor), typeof(object))).Compile();
            _factories[typeId] = factory;
        }
    }

    public bool TryCreate(string typeId, out object? instance)
    {
        instance = null;
        if (!TryGet(typeId, out var descriptor) || descriptor == null)
        {
            return false;
        }

        if (!_factories.TryGetValue(descriptor.TypeId, out var factory))
        {
            return false;
        }

        instance = factory();
        return instance != null;
    }

    public bool TryGetProperty(object target, string propertyName, out object? value)
    {
        value = null;
        if (target == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        if (TrySlot(target, propertyName, out var slot) && slot.Getter != null)
        {
            value = slot.Getter(target);
            return true;
        }

        return false;
    }

    public bool TrySetProperty(object target, string propertyName, object? value)
    {
        if (target == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        if (!TrySlot(target, propertyName, out var slot) || slot.Setter == null)
        {
            return false;
        }

        if (!UiValueCoercion.TryCoerce(value, slot.PropertyType, out var coerced))
        {
            return false;
        }

        slot.Setter(target, coerced);
        return true;
    }

    public IEnumerable<UiGraphBinding> GraphBindings(string typeId)
    {
        if (!TryGet(typeId, out var descriptor) || descriptor == null)
        {
            yield break;
        }

        foreach (var property in descriptor.Properties)
        {
            if (property.CanRead)
            {
                yield return new UiGraphBinding(descriptor.TypeId, "Get", property.Name, property.TypeName);
            }

            if (property.CanWrite)
            {
                yield return new UiGraphBinding(descriptor.TypeId, "Set", property.Name, property.TypeName);
            }
        }

        foreach (var uiEvent in descriptor.Events)
        {
            yield return new UiGraphBinding(descriptor.TypeId, "Event", uiEvent.Name, uiEvent.EventType);
        }
    }

    public static void WireEvents(object target, Action<string, object?> raise)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(raise);
        foreach (var uiEvent in target.GetType().GetEvents(BindingFlags.Public | BindingFlags.Instance))
        {
            var handlerType = uiEvent.EventHandlerType;
            var invoke = handlerType?.GetMethod("Invoke");
            if (handlerType == null || invoke == null || uiEvent.AddMethod == null)
            {
                continue;
            }

            try
            {
                var parameters = invoke.GetParameters();
                var args = parameters.Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
                Expression payload = args.Length == 0
                    ? Expression.Constant(null, typeof(object))
                    : Expression.Convert(args[0], typeof(object));
                var call = Expression.Invoke(
                    Expression.Constant(raise),
                    Expression.Constant(uiEvent.Name ?? "Event"),
                    payload);
                var lambda = Expression.Lambda(handlerType, call, args);
                uiEvent.AddEventHandler(target, lambda.Compile());
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
            }
        }
    }

    private bool TrySlot(object target, string propertyName, out PropertySlot slot)
    {
        var typeId = target.GetType().FullName ?? target.GetType().Name;
        if (_properties.TryGetValue(Key(typeId, propertyName), out slot))
        {
            return true;
        }

        if (TryGet(typeId, out var descriptor) && descriptor != null &&
            _properties.TryGetValue(Key(descriptor.TypeId, propertyName), out slot))
        {
            return true;
        }

        slot = default;
        return false;
    }

    private void CacheProperty(string typeId, Type declaringType, PropertyInfo property)
    {
        Func<object, object?>? getter = null;
        Action<object, object?>? setter = null;
        if (property.CanRead && property.GetMethod is { IsPublic: true })
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var read = Expression.Property(Expression.Convert(instance, declaringType), property);
            getter = Expression.Lambda<Func<object, object?>>(Expression.Convert(read, typeof(object)), instance).Compile();
        }

        if (property.CanWrite && property.SetMethod is { IsPublic: true })
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(object), "value");
            var assign = Expression.Assign(
                Expression.Property(Expression.Convert(instance, declaringType), property),
                Expression.Convert(value, property.PropertyType));
            setter = Expression.Lambda<Action<object, object?>>(assign, instance, value).Compile();
        }

        _properties[Key(typeId, property.Name)] = new PropertySlot(property.PropertyType, getter, setter);
    }

    private static bool IsControl(Type type, Type controlBaseType) =>
        type is { IsPublic: true, IsAbstract: false, IsGenericTypeDefinition: false, IsInterface: false }
        && type != controlBaseType
        && controlBaseType.IsAssignableFrom(type);

    private static bool IsAuthorableProperty(PropertyInfo property, Type controlBaseType)
    {
        if (property.GetIndexParameters().Length != 0 || IsHidden(property) || SkippedProperties.Contains(property.Name))
        {
            return false;
        }

        if (property.GetCustomAttribute<ObsoleteAttribute>() != null)
        {
            return false;
        }

        if (!property.CanWrite && !property.CanRead)
        {
            return false;
        }

        if (!property.CanWrite)
        {
            return false;
        }

        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (controlBaseType.IsAssignableFrom(propertyType))
        {
            return false;
        }

        return EditorKind(property) != UiPropertyEditorKind.Custom || property.Name.Equals("StyleClasses", StringComparison.Ordinal);
    }

    private static UiPropertyDescriptor DescribeProperty(PropertyInfo property)
    {
        var kind = EditorKind(property);
        var enumValues = kind == UiPropertyEditorKind.Enum
            ? Enum.GetNames(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType)
            : null;
        return new UiPropertyDescriptor(
            property.Name,
            (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).Name,
            property.CanRead,
            property.CanWrite,
            kind,
            PropertyCategory(property.Name),
            null,
            null,
            enumValues);
    }

    private static UiEventDescriptor DescribeEvent(EventInfo uiEvent)
    {
        var payload = new List<UiEventPayloadField>();
        var handler = uiEvent.EventHandlerType;
        var invoke = handler?.GetMethod("Invoke");
        var parameters = invoke?.GetParameters() ?? [];
        if (parameters.Length > 0)
        {
            var argType = parameters[^1].ParameterType;
            foreach (var property in argType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0 || !IsSimple(property.PropertyType))
                {
                    continue;
                }

                payload.Add(new UiEventPayloadField(property.Name, property.PropertyType.Name));
            }
        }

        return new UiEventDescriptor(uiEvent.Name ?? "Event", handler?.Name ?? "Event", payload);
    }

    public static UiPropertyEditorKind EditorKind(PropertyInfo property)
    {
        if (property.Name.Equals("StyleClasses", StringComparison.Ordinal))
        {
            return UiPropertyEditorKind.StyleClasses;
        }

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(bool)) return UiPropertyEditorKind.Boolean;
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return UiPropertyEditorKind.Integer;
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return UiPropertyEditorKind.Float;
        if (type == typeof(string))
        {
            if (property.Name.Contains("Texture", StringComparison.OrdinalIgnoreCase)) return UiPropertyEditorKind.Resource;
            if (property.Name.Contains("Proto", StringComparison.OrdinalIgnoreCase)) return UiPropertyEditorKind.Prototype;
            return UiPropertyEditorKind.Text;
        }

        if (type.IsEnum) return UiPropertyEditorKind.Enum;
        if (type.Name.Contains("Color", StringComparison.Ordinal)) return UiPropertyEditorKind.Color;
        if (type.Name is "Vector2" or "Vector2i") return UiPropertyEditorKind.Vector2;
        if (type.Name.Contains("ProtoId", StringComparison.Ordinal)) return UiPropertyEditorKind.Prototype;
        return UiPropertyEditorKind.Custom;
    }

    public static string DisplayName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return typeName;
        }

        var chars = new List<char>(typeName.Length + 8);
        for (var i = 0; i < typeName.Length; i++)
        {
            var current = typeName[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(typeName[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        return new string(chars.ToArray());
    }

    public static string CategoryFor(Type type)
    {
        var name = type.Name;
        if (name is "BoxContainer" or "GridContainer" or "LayoutContainer" or "ScrollContainer" or "PanelContainer" or "Window" or "SplitContainer")
        {
            return "Layout";
        }

        if (name is "Button" or "LineEdit" or "CheckBox" or "Slider" or "SpinBox" or "OptionButton" or "TextureButton")
        {
            return "Input";
        }

        if (name is "Label" or "TextureRect" or "ProgressBar" or "RichTextLabel")
        {
            return "Display";
        }

        if (name is "TabContainer")
        {
            return "Navigation";
        }

        var assemblyName = type.Assembly.GetName().Name ?? "Custom";
        if (assemblyName.StartsWith("Robust", StringComparison.Ordinal))
        {
            return "Controls";
        }

        return assemblyName;
    }

    public static bool CanHaveChildren(Type type) => !LeafNames.Contains(type.Name);

    private static string PropertyCategory(string name) => name switch
    {
        "Text" or "Texture" or "TextureScale" or "Stretch" => "Content",
        "Visible" or "Disabled" or "Editable" or "MouseFilter" or "ToggleMode" => "Behavior",
        "MinWidth" or "MinHeight" or "MinSize" or "MaxSize" or "Orientation" or "Columns" or "HorizontalExpand" or "VerticalExpand"
            or "HorizontalAlignment" or "VerticalAlignment" or "SeparationOverride" => "Layout",
        _ => "General"
    };

    private static bool IsSimple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string);
    }

    private static bool IsHidden(MemberInfo member) =>
        member.GetCustomAttributes(inherit: true).Any(attribute => attribute.GetType().Name is "AstraHiddenAttribute" or "UiHiddenAttribute");

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

    private static string ShortName(string typeId) =>
        typeId.Contains('.', StringComparison.Ordinal) ? typeId[(typeId.LastIndexOf('.') + 1)..] : typeId;

    private static string Key(string typeId, string propertyName) =>
        typeId + "\u001f" + propertyName.ToLower(CultureInfo.InvariantCulture);

    private readonly record struct PropertySlot(Type PropertyType, Func<object, object?>? Getter, Action<object, object?>? Setter);
}
