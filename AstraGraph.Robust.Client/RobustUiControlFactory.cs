using System.Numerics;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Model;
using AstraGraph.UI.Runtime;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;

namespace AstraGraph.Robust.Client;

/// <summary>
/// Wrapper adapting native RobustToolbox Control instances to AstraGraph's IRobustUiControl.
/// Enables two-way data bindings and zero-flicker reconciliation directly on the real Robust UI visual tree.
/// </summary>
public sealed class NativeRobustUiControlWrapper : IRobustUiControl
{
    private readonly Control _nativeControl;
    private readonly List<IRobustUiControl> _children = [];
    private readonly Dictionary<string, object?> _customProperties = new(StringComparer.OrdinalIgnoreCase);

    public Control NativeControl => _nativeControl;

    public string Id { get; }
    public UiElementType ElementType { get; }
    public string ControlTypeId { get; }
    public string? Name
    {
        get => _nativeControl.Name;
        set => _nativeControl.Name = value ?? string.Empty;
    }

    public string? Text
    {
        get => _nativeControl switch
        {
            Label lbl => lbl.Text,
            Button btn => btn.Text,
            LineEdit edit => edit.Text,
            _ => _customProperties.TryGetValue("Text", out var t) ? t?.ToString() : null
        };
        set
        {
            _customProperties["Text"] = value;
            switch (_nativeControl)
            {
                case Label lbl:
                    lbl.Text = value ?? string.Empty;
                    break;
                case Button btn:
                    btn.Text = value ?? string.Empty;
                    break;
                case LineEdit edit:
                    if (edit.Text != value)
                        edit.Text = value ?? string.Empty;
                    break;
            }
        }
    }

    public bool Visible
    {
        get => _nativeControl.Visible;
        set => _nativeControl.Visible = value;
    }

    public bool Enabled
    {
        get => _nativeControl switch
        {
            Button btn => !btn.Disabled,
            LineEdit edit => edit.Editable,
            _ => true
        };
        set
        {
            switch (_nativeControl)
            {
                case Button btn:
                    btn.Disabled = !value;
                    break;
                case LineEdit edit:
                    edit.Editable = value;
                    break;
            }
        }
    }

    public IRobustUiControl? Parent { get; set; }
    public IReadOnlyList<IRobustUiControl> Children => _children;

    public event Action<string, object?>? OnEventTriggered;

    public NativeRobustUiControlWrapper(string id, UiElementType type, Control nativeControl, string? name = null, string? controlTypeId = null)
    {
        Id = id;
        ElementType = type;
        ControlTypeId = string.IsNullOrWhiteSpace(controlTypeId) ? UiControlIds.FromLegacy(type) : controlTypeId;
        _nativeControl = nativeControl ?? throw new ArgumentNullException(nameof(nativeControl));
        Name = name;
        UiControlCatalog.WireEvents(_nativeControl, TriggerEvent);
    }

    public void AddChild(IRobustUiControl child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!_children.Contains(child))
        {
            _children.Add(child);
            child.Parent = this;
            if (child is NativeRobustUiControlWrapper nativeChild)
            {
                _nativeControl.AddChild(nativeChild.NativeControl);
            }
        }
    }

    public void RemoveChild(IRobustUiControl child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_children.Remove(child))
        {
            child.Parent = null;
            if (child is NativeRobustUiControlWrapper nativeChild)
            {
                _nativeControl.RemoveChild(nativeChild.NativeControl);
            }
        }
    }

    public void SetProperty(string propertyName, object? value)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        _customProperties[propertyName] = value;

        if (propertyName.Equals("StyleClasses", StringComparison.OrdinalIgnoreCase) && value is IEnumerable<string> classes)
        {
            foreach (var className in classes)
            {
                if (!string.IsNullOrWhiteSpace(className))
                {
                    _nativeControl.AddStyleClass(className);
                }
            }

            return;
        }

        if (UiCatalogRegistry.Shared.TrySetProperty(_nativeControl, propertyName, value))
        {
            return;
        }

        if (propertyName.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            Text = value?.ToString();
        }
        else if (propertyName.Equals("Visible", StringComparison.OrdinalIgnoreCase) && value is bool v)
        {
            Visible = v;
        }
        else if (propertyName.Equals("Enabled", StringComparison.OrdinalIgnoreCase) && value is bool e)
        {
            Enabled = e;
        }
        else if ((propertyName.Equals("MinWidth", StringComparison.OrdinalIgnoreCase) || propertyName.Equals("MinHeight", StringComparison.OrdinalIgnoreCase))
            && float.TryParse(value?.ToString(), out var size))
        {
            var current = _nativeControl.MinSize;
            _nativeControl.MinSize = propertyName.Equals("MinWidth", StringComparison.OrdinalIgnoreCase)
                ? new Vector2(size, current.Y)
                : new Vector2(current.X, size);
        }
    }

    public object? GetProperty(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        if (UiCatalogRegistry.Shared.TryGetProperty(_nativeControl, propertyName, out var value))
        {
            return value;
        }

        if (propertyName.Equals("Text", StringComparison.OrdinalIgnoreCase)) return Text;
        if (propertyName.Equals("Visible", StringComparison.OrdinalIgnoreCase)) return Visible;
        if (propertyName.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) return Enabled;
        return _customProperties.GetValueOrDefault(propertyName);
    }

    public void TriggerEvent(string eventName, object? payload = null)
    {
        OnEventTriggered?.Invoke(eventName, payload);
    }
}

/// <summary>
/// Factory creating real native RobustToolbox Control instances for AstraGraph UI documents.
/// </summary>
public sealed class RobustUiControlFactory : IRobustUiControlFactory
{
    public RobustUiControlFactory()
    {
        IndexAssembly(typeof(Control).Assembly);
    }

    public static UiControlCatalog Catalog => UiCatalogRegistry.Shared;

    public static void IndexAssembly(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        UiCatalogRegistry.IndexAssembly(assembly, typeof(Control));
    }

    public IRobustUiControl CreateControl(
        string id,
        UiElementType type,
        string? name,
        int? minWidth,
        int? minHeight,
        UiOrientation orientation) =>
        CreateControl(id, UiControlIds.FromLegacy(type), name, minWidth, minHeight, orientation);

    public IRobustUiControl CreateControl(
        string id,
        string controlTypeId,
        string? name,
        int? minWidth,
        int? minHeight,
        UiOrientation orientation)
    {
        var legacy = UiControlIds.ToLegacy(controlTypeId);
        if (!IsNativeUiAvailable())
        {
            return new MockRobustUiControl(id, legacy, name) { ControlTypeId = controlTypeId };
        }

        Control? control = null;
        if (Catalog.TryCreate(controlTypeId, out var created) && created is Control indexed)
        {
            control = indexed;
        }

        control ??= legacy switch
        {
            UiElementType.BoxContainer => new BoxContainer
            {
                Orientation = orientation == UiOrientation.Horizontal
                    ? BoxContainer.LayoutOrientation.Horizontal
                    : BoxContainer.LayoutOrientation.Vertical
            },
            UiElementType.Label => new Label(),
            UiElementType.Button => new Button(),
            UiElementType.LineEdit => new LineEdit(),
            UiElementType.TextureRect => new TextureRect(),
            UiElementType.Panel => new PanelContainer(),
            UiElementType.GridContainer => new GridContainer(),
            UiElementType.ScrollContainer => new ScrollContainer(),
            UiElementType.ProgressBar => new ProgressBar(),
            UiElementType.LayoutContainer => new LayoutContainer(),
            UiElementType.ItemList => new ItemList(),
            _ => new BoxContainer()
        };

        if (control is BoxContainer box)
        {
            box.Orientation = orientation == UiOrientation.Horizontal
                ? BoxContainer.LayoutOrientation.Horizontal
                : BoxContainer.LayoutOrientation.Vertical;
        }

        if (minWidth.HasValue || minHeight.HasValue)
        {
            control.MinSize = new Vector2(minWidth ?? 0, minHeight ?? 0);
        }

        return new NativeRobustUiControlWrapper(id, legacy, control, name, controlTypeId);
    }

    public static bool IsNativeUiAvailable()
    {
        try
        {
            return IoCManager.Instance != null && IoCManager.Resolve<IUserInterfaceManager>() != null;
        }
        catch
        {
            return false;
        }
    }
}
