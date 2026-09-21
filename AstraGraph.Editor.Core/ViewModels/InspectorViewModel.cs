using System;
using System.Collections.Generic;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core.ViewModels;

public sealed class InspectorViewModel
{
    private VisualNode? _selectedNode;

    public VisualNode? SelectedNode => _selectedNode;
    public bool HasSelectedNode => _selectedNode != null;

    public string GraphName { get; set; } = "UntitledGraph";
    public GraphKind GraphKind { get; set; } = GraphKind.System;
    public GraphSide GraphSide { get; set; } = GraphSide.Server;
    public string Author { get; set; } = "Author";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";

    public event Action? OnChanged;

    public void SelectNode(VisualNode? node)
    {
        _selectedNode = node;
        OnChanged?.Invoke();
    }

    public void RenameSelectedNode(string newName)
    {
        if (_selectedNode == null || string.IsNullOrWhiteSpace(newName)) return;
        _selectedNode.Name = newName.Trim();
        OnChanged?.Invoke();
    }

    public void SetProperty(string key, string value)
    {
        if (_selectedNode == null || string.IsNullOrWhiteSpace(key)) return;
        _selectedNode.Properties[key.Trim()] = value;
        OnChanged?.Invoke();
    }

    public void RemoveProperty(string key)
    {
        if (_selectedNode == null) return;
        if (_selectedNode.Properties.Remove(key))
        {
            OnChanged?.Invoke();
        }
    }

    public void UpdateGraphMetadata(string name, GraphKind kind, GraphSide side, string author, string description, string version)
    {
        GraphName = name;
        GraphKind = kind;
        GraphSide = side;
        Author = author;
        Description = description;
        Version = version;
        OnChanged?.Invoke();
    }
}
