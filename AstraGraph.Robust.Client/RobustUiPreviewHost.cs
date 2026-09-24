using AstraGraph.Core;
using AstraGraph.UI.Model;
using AstraGraph.UI.Runtime;
using Robust.Client.UserInterface.CustomControls;

namespace AstraGraph.Robust.Client;

/// <summary>
/// Opens the compiled UI in a real Robust window when the client UI manager exists.
/// The same session hot-reloads in place, so LineEdit text and focus stay on the control.
/// </summary>
public sealed class RobustUiPreviewHost : IDisposable
{
    private readonly UiPreviewSession _session;
    private DefaultWindow? _window;

    public RobustUiPreviewHost(RobustUiControlFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _session = new UiPreviewSession(factory);
    }

    public string Mode { get; private set; } = "headless";

    public bool IsWindowOpen => _window?.IsOpen == true;

    public UiSession? Session => _session.Session;

    public IReadOnlyList<string> PreservedIds => _session.PreservedIds;

    public bool Update(UiDocument document, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var ok = _session.Update(document, out diagnostics);
        if (!ok)
        {
            return false;
        }

        if (!RobustUiControlFactory.IsNativeUiAvailable() || _session.Session?.RootControl is not NativeRobustUiControlWrapper root)
        {
            Mode = "headless";
            return true;
        }

        try
        {
            EnsureWindow(document.Name, root);
            Mode = IsWindowOpen ? "robust" : "headless";
        }
        catch (Exception)
        {
            Mode = "headless";
        }

        return true;
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_window?.IsOpen == true)
        {
            _window.Close();
        }

        if (_window is IDisposable window)
        {
            window.Dispose();
        }

        _window = null;
        _session.Close();
        Mode = "headless";
    }

    private void EnsureWindow(string title, NativeRobustUiControlWrapper root)
    {
        if (_window == null)
        {
            _window = new DefaultWindow { Title = title };
            if (root.NativeControl.Parent == null)
            {
                _window.Contents.AddChild(root.NativeControl);
            }

            _window.OpenCentered();
            return;
        }

        _window.Title = title;
        if (root.NativeControl.Parent == null)
        {
            _window.Contents.AddChild(root.NativeControl);
        }

        if (!_window.IsOpen)
        {
            _window.OpenCentered();
        }
    }
}
