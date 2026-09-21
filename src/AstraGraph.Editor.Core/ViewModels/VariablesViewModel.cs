using System;
using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core.ViewModels;

public sealed class VariableItem
{
    public SymbolId Id { get; }
    public string Name { get; set; }
    public string TypeName { get; set; }
    public string DefaultValue { get; set; }
    public bool IsPersistent { get; set; }
    public bool IsReplicated { get; set; }

    public VariableItem(SymbolId id, string name, string typeName, string defaultValue = "", bool isPersistent = false, bool isReplicated = false)
    {
        Id = id;
        Name = name;
        TypeName = typeName;
        DefaultValue = defaultValue;
        IsPersistent = isPersistent;
        IsReplicated = isReplicated;
    }
}

public sealed class VariablesViewModel
{
    private readonly List<VariableItem> _variables = [];

    public IReadOnlyList<VariableItem> Variables => _variables;

    public event Action? OnChanged;

    public static readonly IReadOnlyList<string> SupportedTypes =
    [
        "System.Int32",
        "System.Int64",
        "System.Single",
        "System.Double",
        "System.Boolean",
        "System.String",
        "System.Numerics.Vector2",
        "Robust.Shared.GameObjects.EntityUid"
    ];

    public bool TryAddVariable(string name, string typeName, string defaultValue, bool persistent, bool replicated, out string? error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Variable name cannot be empty.";
            return false;
        }

        var cleanName = name.Trim();
        if (_variables.Any(v => string.Equals(v.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"A variable named '{cleanName}' already exists.";
            return false;
        }

        var item = new VariableItem(SymbolId.New(), cleanName, typeName, defaultValue, persistent, replicated);
        _variables.Add(item);
        error = null;
        OnChanged?.Invoke();
        return true;
    }

    public bool RemoveVariable(SymbolId id)
    {
        var removed = _variables.RemoveAll(v => v.Id == id) > 0;
        if (removed)
        {
            OnChanged?.Invoke();
        }
        return removed;
    }

    public bool TryUpdateVariable(SymbolId id, string name, string typeName, string defaultValue, bool persistent, bool replicated, out string? error)
    {
        var existing = _variables.FirstOrDefault(v => v.Id == id);
        if (existing == null)
        {
            error = "Variable not found.";
            return false;
        }

        var cleanName = name.Trim();
        if (_variables.Any(v => v.Id != id && string.Equals(v.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Another variable named '{cleanName}' already exists.";
            return false;
        }

        existing.Name = cleanName;
        existing.TypeName = typeName;
        existing.DefaultValue = defaultValue;
        existing.IsPersistent = persistent;
        existing.IsReplicated = replicated;
        error = null;
        OnChanged?.Invoke();
        return true;
    }

    public List<GraphVariableDocument> ToDocuments()
    {
        return _variables.Select(v => new GraphVariableDocument
        {
            Id = v.Id,
            Name = v.Name,
            TypeName = v.TypeName,
            DefaultValue = v.DefaultValue,
            IsPersistent = v.IsPersistent,
            IsReplicated = v.IsReplicated
        }).ToList();
    }

    public void LoadFromDocuments(IEnumerable<GraphVariableDocument> docs)
    {
        _variables.Clear();
        foreach (var doc in docs)
        {
            _variables.Add(new VariableItem(
                doc.Id,
                doc.Name,
                doc.TypeName,
                doc.DefaultValue ?? "",
                doc.IsPersistent,
                doc.IsReplicated));
        }
        OnChanged?.Invoke();
    }
}
