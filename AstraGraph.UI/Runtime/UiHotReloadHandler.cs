using AstraGraph.UI.Compiler;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Runtime;

public sealed class UiSession
{
    public required UiDocument Document { get; set; }
    public required UiIrProgram Program { get; set; }
    public required UiStateManager StateManager { get; init; }
    public required UiBindingEngine BindingEngine { get; set; }
    public required IRobustUiControl RootControl { get; set; }
    public required Dictionary<string, IRobustUiControl> ControlsById { get; set; }
}

/// <summary>
/// Orchestrates live hot reloading of Astra UI documents without closing windows or dropping input state.
/// </summary>
public sealed class UiHotReloadHandler
{
    private readonly IRobustUiControlFactory _controlFactory;

    public UiHotReloadHandler(IRobustUiControlFactory controlFactory)
    {
        _controlFactory = controlFactory;
    }

    public UiSession InitializeSession(UiDocument doc)
    {
        var compileResult = UiCompiler.Compile(doc);
        if (!compileResult.Success || compileResult.Program == null)
        {
            throw new InvalidOperationException($"Failed to compile UI document '{doc.Name}': {string.Join("; ", compileResult.Diagnostics.Select(d => d.Message))}");
        }

        var stateManager = new UiStateManager(compileResult.Program.InitialState);
        var reconciler = new RobustUiReconciler(_controlFactory);
        var result = reconciler.Reconcile(compileResult.Program);

        var bindingEngine = new UiBindingEngine(stateManager);
        foreach (var control in result.ControlsById.Values)
        {
            bindingEngine.RegisterControl(control);
        }

        foreach (var inst in compileResult.Program.Instructions)
        {
            if (inst is RegisterBindingInstruction b)
            {
                bindingEngine.AddBinding(b);
            }
        }

        return new UiSession
        {
            Document = doc,
            Program = compileResult.Program,
            StateManager = stateManager,
            BindingEngine = bindingEngine,
            RootControl = result.RootControl,
            ControlsById = new Dictionary<string, IRobustUiControl>(result.ControlsById, StringComparer.Ordinal)
        };
    }

    public bool TryHotReload(UiSession session, UiDocument newDoc, out IReadOnlyList<Core.Diagnostic> diagnostics)
    {
        var compileResult = UiCompiler.Compile(newDoc);
        diagnostics = compileResult.Diagnostics;
        if (!compileResult.Success || compileResult.Program == null)
        {
            return false;
        }

        // 1. Snapshot existing state
        var savedState = session.StateManager.GetAllVariables();

        // 2. Reconcile control tree in-place
        var reconciler = new RobustUiReconciler(_controlFactory);
        var result = reconciler.Reconcile(compileResult.Program, session.RootControl);

        // 3. Re-create binding engine with existing state
        session.BindingEngine.Dispose();
        var newBindingEngine = new UiBindingEngine(session.StateManager);

        foreach (var control in result.ControlsById.Values)
        {
            newBindingEngine.RegisterControl(control);
        }

        foreach (var inst in compileResult.Program.Instructions)
        {
            if (inst is RegisterBindingInstruction b)
            {
                newBindingEngine.AddBinding(b);
            }
        }

        // 4. Update session
        session.Document = newDoc;
        session.Program = compileResult.Program;
        session.BindingEngine = newBindingEngine;
        session.RootControl = result.RootControl;
        session.ControlsById = new Dictionary<string, IRobustUiControl>(result.ControlsById, StringComparer.Ordinal);

        // 5. Re-apply saved state so input fields are preserved
        foreach (var kv in savedState)
        {
            session.StateManager.SetVariable(kv.Key, kv.Value);
        }

        return true;
    }
}
