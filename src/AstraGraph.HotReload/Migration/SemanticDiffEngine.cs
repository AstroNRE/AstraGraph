using AstraGraph.Core;

namespace AstraGraph.HotReload;

public sealed record SemanticDiff(
    IReadOnlyList<NodeDocument> NodesAdded,
    IReadOnlyList<NodeDocument> NodesRemoved,
    IReadOnlyList<(NodeDocument Old, NodeDocument New)> NodesModified,
    IReadOnlyList<ConnectionDocument> ConnectionsAdded,
    IReadOnlyList<ConnectionDocument> ConnectionsRemoved,
    IReadOnlyList<GraphVariableDocument> VariablesAdded,
    IReadOnlyList<GraphVariableDocument> VariablesRemoved,
    IReadOnlyList<(GraphVariableDocument Old, GraphVariableDocument New)> VariablesModified)
{
    public bool HasSemanticChanges =>
        NodesAdded.Count > 0 || NodesRemoved.Count > 0 || NodesModified.Count > 0 ||
        ConnectionsAdded.Count > 0 || ConnectionsRemoved.Count > 0 ||
        VariablesAdded.Count > 0 || VariablesRemoved.Count > 0 || VariablesModified.Count > 0;
}

/// <summary>
/// Compares two graph document revisions and produces a structured semantic difference.
/// </summary>
public static class SemanticDiffEngine
{
    public static SemanticDiff Diff(GraphDocument oldDoc, GraphDocument newDoc)
    {
        ArgumentNullException.ThrowIfNull(oldDoc);
        ArgumentNullException.ThrowIfNull(newDoc);

        var oldNodes = oldDoc.Nodes.ToDictionary(n => n.Id);
        var newNodes = newDoc.Nodes.ToDictionary(n => n.Id);

        var nodesAdded = newNodes.Values.Where(n => !oldNodes.ContainsKey(n.Id)).ToList();
        var nodesRemoved = oldNodes.Values.Where(n => !newNodes.ContainsKey(n.Id)).ToList();
        var nodesModified = new List<(NodeDocument Old, NodeDocument New)>();

        foreach (var (id, newNode) in newNodes)
        {
            if (oldNodes.TryGetValue(id, out var oldNode) && !newNode.Equals(oldNode))
            {
                nodesModified.Add((oldNode, newNode));
            }
        }

        // Connections
        var oldConns = oldDoc.Connections.ToHashSet();
        var newConns = newDoc.Connections.ToHashSet();

        var connsAdded = newConns.Where(c => !oldConns.Contains(c)).ToList();
        var connsRemoved = oldConns.Where(c => !newConns.Contains(c)).ToList();

        // Variables
        var oldVars = oldDoc.Variables.ToDictionary(v => v.Id);
        var newVars = newDoc.Variables.ToDictionary(v => v.Id);

        var varsAdded = newVars.Values.Where(v => !oldVars.ContainsKey(v.Id)).ToList();
        var varsRemoved = oldVars.Values.Where(v => !newVars.ContainsKey(v.Id)).ToList();
        var varsModified = new List<(GraphVariableDocument Old, GraphVariableDocument New)>();

        foreach (var (id, newVar) in newVars)
        {
            if (oldVars.TryGetValue(id, out var oldVar) && !newVar.Equals(oldVar))
            {
                varsModified.Add((oldVar, newVar));
            }
        }

        return new SemanticDiff(
            nodesAdded,
            nodesRemoved,
            nodesModified,
            connsAdded,
            connsRemoved,
            varsAdded,
            varsRemoved,
            varsModified);
    }
}
