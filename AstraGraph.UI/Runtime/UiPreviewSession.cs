using AstraGraph.Core;
using AstraGraph.UI.Compiler;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Runtime;

/// <summary>
/// Live preview session. The same hot-reload path runs for a headless factory and for native Robust controls.
/// </summary>
public sealed class UiPreviewSession
{
    private readonly UiHotReloadHandler _handler;
    private UiSession? _session;

    public UiPreviewSession(IRobustUiControlFactory factory)
    {
        _handler = new UiHotReloadHandler(factory);
    }

    public bool IsOpen => _session != null;

    public UiSession? Session => _session;

    public bool Open(UiDocument document, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var compiled = UiCompiler.Compile(document);
        diagnostics = compiled.Diagnostics;
        if (!compiled.Success)
        {
            return false;
        }

        _session = _handler.InitializeSession(document);
        return true;
    }

    public bool Update(UiDocument document, out IReadOnlyList<Diagnostic> diagnostics)
    {
        if (_session == null)
        {
            return Open(document, out diagnostics);
        }

        var preserved = _session.ControlsById.Keys.ToHashSet(StringComparer.Ordinal);
        var ok = _handler.TryHotReload(_session, document, out diagnostics);
        PreservedIds = ok
            ? preserved.Where(id => _session.ControlsById.ContainsKey(id)).ToArray()
            : [];
        return ok;
    }

    public IReadOnlyList<string> PreservedIds { get; private set; } = [];

    public void Close() => _session = null;
}
