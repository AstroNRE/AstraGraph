using AstraGraph.UI.Compiler;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Runtime;

/// <summary>
/// Connects a collection of UI controls to a UiStateManager via reactive bindings.
/// </summary>
public sealed class UiBindingEngine : IDisposable
{
    private readonly UiStateManager _stateManager;
    private readonly Dictionary<string, IRobustUiControl> _controls = new(StringComparer.Ordinal);
    private readonly List<RegisterBindingInstruction> _bindings = [];
    private bool _isUpdating;

    public UiBindingEngine(UiStateManager stateManager)
    {
        _stateManager = stateManager;
        _stateManager.OnVariableChanged += HandleVariableChanged;
    }

    public void RegisterControl(IRobustUiControl control)
    {
        _controls[control.Id] = control;
        control.OnEventTriggered += (evName, payload) => HandleControlEvent(control, evName, payload);
    }

    public void AddBinding(RegisterBindingInstruction binding)
    {
        _bindings.Add(binding);

        // Initial push from state to control for OneWay and TwoWay
        if (binding.Direction is BindingDirection.OneWay or BindingDirection.TwoWay)
        {
            if (_controls.TryGetValue(binding.ElementId, out var control))
            {
                var val = _stateManager.GetVariable(binding.StateVariable);
                control.SetProperty(binding.TargetProperty, val);
            }
        }
    }

    private void HandleVariableChanged(VariableChangedEventArgs args)
    {
        if (_isUpdating) return;

        try
        {
            _isUpdating = true;
            foreach (var b in _bindings)
            {
                if (string.Equals(b.StateVariable, args.Name, StringComparison.Ordinal) &&
                    b.Direction is BindingDirection.OneWay or BindingDirection.TwoWay)
                {
                    if (_controls.TryGetValue(b.ElementId, out var control))
                    {
                        control.SetProperty(b.TargetProperty, args.NewValue);
                    }
                }
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void HandleControlEvent(IRobustUiControl control, string eventName, object? payload)
    {
        if (_isUpdating) return;

        // When a control property changes (e.g. TextChanged), sync back to State for TwoWay / OneWayToSource
        if (string.Equals(eventName, "TextChanged", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _isUpdating = true;
                foreach (var b in _bindings)
                {
                    if (string.Equals(b.ElementId, control.Id, StringComparison.Ordinal) &&
                        string.Equals(b.TargetProperty, "Text", StringComparison.OrdinalIgnoreCase) &&
                        b.Direction is BindingDirection.TwoWay or BindingDirection.OneWayToSource)
                    {
                        _stateManager.SetVariable(b.StateVariable, control.Text);
                    }
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }
    }

    public void Dispose()
    {
        _stateManager.OnVariableChanged -= HandleVariableChanged;
    }
}
