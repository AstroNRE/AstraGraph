using System;
using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core.ViewModels;

public enum ProblemSeverity
{
    Error,
    Warning,
    Info
}

public enum ProblemCategory
{
    All,
    Errors,
    Warnings,
    Security,
    Prediction,
    Performance
}

public sealed record ProblemItem(
    Diagnostic Diagnostic,
    ProblemSeverity Severity,
    ProblemCategory Category,
    string Code,
    string Message,
    NodeId? RelatedNodeId);

public sealed class ProblemsViewModel
{
    private readonly List<ProblemItem> _allItems = [];
    private ProblemCategory _currentFilter = ProblemCategory.All;

    public ProblemCategory CurrentFilter
    {
        get => _currentFilter;
        set
        {
            _currentFilter = value;
            OnChanged?.Invoke();
        }
    }

    public ProblemItem? SelectedProblem { get; set; }

    public int ErrorCount => _allItems.Count(i => i.Severity == ProblemSeverity.Error);
    public int WarningCount => _allItems.Count(i => i.Severity == ProblemSeverity.Warning);
    public bool HasErrors => ErrorCount > 0;

    public event Action? OnChanged;

    public IReadOnlyList<ProblemItem> FilteredItems
    {
        get
        {
            if (_currentFilter == ProblemCategory.All) return _allItems;
            if (_currentFilter == ProblemCategory.Errors) return _allItems.Where(i => i.Severity == ProblemSeverity.Error).ToList();
            if (_currentFilter == ProblemCategory.Warnings) return _allItems.Where(i => i.Severity == ProblemSeverity.Warning).ToList();
            return _allItems.Where(i => i.Category == _currentFilter).ToList();
        }
    }

    public void UpdateDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        _allItems.Clear();
        foreach (var diag in diagnostics)
        {
            var severity = diag.Severity switch
            {
                DiagnosticSeverity.Error => ProblemSeverity.Error,
                DiagnosticSeverity.Warning => ProblemSeverity.Warning,
                _ => ProblemSeverity.Info
            };

            var category = ClassifyCategory(diag.Code);
            _allItems.Add(new ProblemItem(diag, severity, category, diag.Code, diag.Message, diag.NodeId));
        }

        SelectedProblem = null;
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        _allItems.Clear();
        SelectedProblem = null;
        OnChanged?.Invoke();
    }

    private static ProblemCategory ClassifyCategory(string code)
    {
        if (code.Contains("SEC", StringComparison.OrdinalIgnoreCase) || code.Contains("Security", StringComparison.OrdinalIgnoreCase))
        {
            return ProblemCategory.Security;
        }

        if (code.Contains("PRED", StringComparison.OrdinalIgnoreCase) || code.Contains("Prediction", StringComparison.OrdinalIgnoreCase))
        {
            return ProblemCategory.Prediction;
        }

        if (code.Contains("PERF", StringComparison.OrdinalIgnoreCase) || code.Contains("Budget", StringComparison.OrdinalIgnoreCase))
        {
            return ProblemCategory.Performance;
        }

        return ProblemCategory.Errors;
    }
}
