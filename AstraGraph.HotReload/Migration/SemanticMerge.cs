using AstraGraph.Core;

namespace AstraGraph.HotReload;

public sealed record MergeResult(GraphDocument? Document, IReadOnlyList<string> Conflicts);

/// <summary>
/// Three-way merge of a graph. A change wins when the other side still matches the base.
/// Both sides editing the same node, connection, or variable is a conflict.
/// </summary>
public static class SemanticMerge
{
    public static MergeResult Merge(GraphDocument baseDocument, GraphDocument ours, GraphDocument theirs)
    {
        ArgumentNullException.ThrowIfNull(baseDocument);
        ArgumentNullException.ThrowIfNull(ours);
        ArgumentNullException.ThrowIfNull(theirs);
        var conflicts = new List<string>();
        var nodes = MergeById(baseDocument.Nodes, ours.Nodes, theirs.Nodes, node => node.Id.ToString(), node => node.Name, conflicts, "node");
        var variables = MergeById(baseDocument.Variables, ours.Variables, theirs.Variables, variable => variable.Id.ToString(), variable => variable.Name, conflicts, "variable");
        var connections = MergeConnections(baseDocument.Connections, ours.Connections, theirs.Connections, conflicts);
        if (conflicts.Count > 0) return new MergeResult(null, conflicts);
        return new MergeResult(new GraphDocument
        {
            Id = ours.Id,
            Name = ours.Name,
            Kind = ours.Kind,
            Side = ours.Side,
            Metadata = ours.Metadata,
            Variables = variables,
            Nodes = nodes,
            Connections = connections,
            EditorLayout = ours.EditorLayout
        }, []);
    }

    private static List<T> MergeById<T>(
        IReadOnlyList<T> baseItems,
        IReadOnlyList<T> ourItems,
        IReadOnlyList<T> theirItems,
        Func<T, string> id,
        Func<T, string> label,
        List<string> conflicts,
        string kind) where T : class
    {
        var merged = new List<T>();
        foreach (var key in baseItems.Select(id).Union(ourItems.Select(id)).Union(theirItems.Select(id)))
        {
            var baseline = baseItems.FirstOrDefault(item => id(item) == key);
            var our = ourItems.FirstOrDefault(item => id(item) == key);
            var their = theirItems.FirstOrDefault(item => id(item) == key);
            var chosen = Choose(baseline, our, their, label, conflicts, kind);
            if (chosen != null) merged.Add(chosen);
        }

        return merged;
    }

    private static T? Choose<T>(T? baseline, T? ours, T? theirs, Func<T, string> label, List<string> conflicts, string kind) where T : class
    {
        if (ours == null && theirs == null) return null;
        if (ours == null) return Deleted(baseline, theirs, label, conflicts, kind);
        if (theirs == null) return Deleted(baseline, ours, label, conflicts, kind);
        if (ours.Equals(theirs)) return ours;
        if (baseline != null && ours.Equals(baseline)) return theirs;
        if (baseline != null && theirs.Equals(baseline)) return ours;
        conflicts.Add($"{kind} {label(ours)} changed on both sides");
        return null;
    }

    private static T? Deleted<T>(T? baseline, T? survivor, Func<T, string> label, List<string> conflicts, string kind) where T : class
    {
        if (survivor == null) return null;
        if (baseline == null || survivor.Equals(baseline)) return baseline == null ? survivor : null;
        conflicts.Add($"{kind} {label(survivor)} was deleted on one side and edited on the other");
        return null;
    }

    private static List<ConnectionDocument> MergeConnections(
        IReadOnlyList<ConnectionDocument> baseline,
        IReadOnlyList<ConnectionDocument> ours,
        IReadOnlyList<ConnectionDocument> theirs,
        List<string> conflicts)
    {
        var merged = new List<ConnectionDocument>();
        foreach (var key in baseline.Select(Key).Union(ours.Select(Key)).Union(theirs.Select(Key)))
        {
            var inBase = baseline.Any(item => Key(item) == key);
            var inOurs = ours.FirstOrDefault(item => Key(item) == key);
            var inTheirs = theirs.FirstOrDefault(item => Key(item) == key);
            if (inOurs == null && inTheirs == null) continue;
            if (inOurs == null)
            {
                if (!inBase) merged.Add(inTheirs!);
                continue;
            }

            if (inTheirs == null)
            {
                if (!inBase) merged.Add(inOurs);
                continue;
            }

            merged.Add(inOurs);
        }

        return merged;
    }

    private static string Key(ConnectionDocument connection) => $"{connection.FromPin}:{connection.ToPin}";
}
