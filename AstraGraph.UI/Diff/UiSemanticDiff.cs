using AstraGraph.UI.Model;

namespace AstraGraph.UI.Diff;

public sealed record UiDiffEntry(string Kind, string Detail);

/// <summary>
/// Semantic UI diff. It describes controls and properties, not a JSON patch.
/// </summary>
public static class UiSemanticDiff
{
    public static IReadOnlyList<UiDiffEntry> Compare(UiDocument before, UiDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var entries = new List<UiDiffEntry>();
        var previous = before.AllElements().ToDictionary(node => node.Id, StringComparer.Ordinal);
        var next = after.AllElements().ToDictionary(node => node.Id, StringComparer.Ordinal);

        foreach (var node in next.Values)
        {
            if (!previous.TryGetValue(node.Id, out var old))
            {
                entries.Add(new UiDiffEntry("Added", $"{Label(node)}"));
                continue;
            }

            if (!string.Equals(old.ControlTypeId, node.ControlTypeId, StringComparison.Ordinal))
            {
                entries.Add(new UiDiffEntry("Type", $"{Label(node)} {old.ControlTypeId} → {node.ControlTypeId}"));
            }

            foreach (var pair in node.Properties)
            {
                previous.TryGetValue(node.Id, out _);
                old.Properties.TryGetValue(pair.Key, out var previousValue);
                if (!Equals(previousValue, pair.Value))
                {
                    entries.Add(new UiDiffEntry("Property", $"{Label(node)} {pair.Key}: {Format(previousValue)} → {Format(pair.Value)}"));
                }
            }
        }

        foreach (var node in previous.Values)
        {
            if (!next.ContainsKey(node.Id))
            {
                entries.Add(new UiDiffEntry("Removed", Label(node)));
            }
        }

        return entries;
    }

    private static string Label(UiElementNode node) =>
        string.IsNullOrWhiteSpace(node.Name) ? node.ElementType.ToString() : $"{node.ElementType} {node.Name}";

    private static string Format(object? value) => value?.ToString() ?? "∅";
}
