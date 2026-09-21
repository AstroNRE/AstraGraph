using System.Collections;

namespace AstraGraph.Core;

/// <summary>
/// Thread-safe diagnostic accumulator for compiling and validating AstraGraph programs.
/// </summary>
public sealed class DiagnosticBag : IReadOnlyList<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock) return _diagnostics.Count;
        }
    }

    public bool HasErrors
    {
        get
        {
            lock (_lock) return _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        }
    }

    public bool HasWarnings
    {
        get
        {
            lock (_lock) return _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);
        }
    }

    public Diagnostic this[int index]
    {
        get
        {
            lock (_lock) return _diagnostics[index];
        }
    }

    public void Report(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        lock (_lock)
        {
            _diagnostics.Add(diagnostic);
        }
    }

    public void ReportError(string code, string message, NodeId? nodeId = null, PinId? pinId = null, SymbolId? symbolId = null, string? suggestedFix = null)
    {
        Report(new Diagnostic(code, DiagnosticSeverity.Error, message, nodeId, pinId, symbolId, suggestedFix));
    }

    public void ReportWarning(string code, string message, NodeId? nodeId = null, PinId? pinId = null, SymbolId? symbolId = null, string? suggestedFix = null)
    {
        Report(new Diagnostic(code, DiagnosticSeverity.Warning, message, nodeId, pinId, symbolId, suggestedFix));
    }

    public void ReportInfo(string code, string message, NodeId? nodeId = null, PinId? pinId = null, SymbolId? symbolId = null, string? suggestedFix = null)
    {
        Report(new Diagnostic(code, DiagnosticSeverity.Info, message, nodeId, pinId, symbolId, suggestedFix));
    }

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        lock (_lock)
        {
            _diagnostics.AddRange(diagnostics);
        }
    }

    public IEnumerator<Diagnostic> GetEnumerator()
    {
        List<Diagnostic> copy;
        lock (_lock)
        {
            copy = new List<Diagnostic>(_diagnostics);
        }
        return copy.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        lock (_lock)
        {
            return string.Join(Environment.NewLine, _diagnostics);
        }
    }
}
