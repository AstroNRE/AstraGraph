using System;
using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core.Search;

public sealed record ContextCompletionSuggestion(
    NodePaletteItem Item,
    VisualPinDefinition TargetPinToConnect,
    int Score);

public static class ContextCompletionEngine
{
    public static List<ContextCompletionSuggestion> SuggestForPin(
        VisualPin draggedPin,
        NodePaletteIndexer indexer,
        string? query = null)
    {
        ArgumentNullException.ThrowIfNull(draggedPin);
        ArgumentNullException.ThrowIfNull(indexer);

        var neededDirection = draggedPin.Direction == PinDirection.Output
            ? PinDirection.Input
            : PinDirection.Output;

        var neededKind = draggedPin.Kind;
        var draggedType = draggedPin.DataType;

        var allItems = string.IsNullOrWhiteSpace(query)
            ? indexer.AllItems.Select(item => new SearchResult(item, 100)).ToList()
            : indexer.Search(query);

        var suggestions = new List<ContextCompletionSuggestion>();

        foreach (var (item, searchScore) in allItems)
        {
            // Find all matching pins on this candidate node
            var matchingPins = item.Pins
                .Where(p => p.Direction == neededDirection && p.Kind == neededKind)
                .ToList();

            if (matchingPins.Count == 0) continue;

            if (neededKind == PinKind.Execution)
            {
                // Execution pin matches
                suggestions.Add(new ContextCompletionSuggestion(item, matchingPins[0], searchScore + 200));
            }
            else
            {
                // Data pin: check type match
                foreach (var pin in matchingPins)
                {
                    var isExact = string.Equals(pin.DataType, draggedType, StringComparison.OrdinalIgnoreCase);
                    var isCompatible = isExact || IsTypeCompatible(draggedType, pin.DataType, neededDirection);

                    if (isCompatible)
                    {
                        var scoreBonus = isExact ? 300 : 150;
                        suggestions.Add(new ContextCompletionSuggestion(item, pin, searchScore + scoreBonus));
                        break; // Connect to primary matching pin
                    }
                }
            }
        }

        return suggestions
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Item.DisplayName)
            .ToList();
    }

    private static bool IsTypeCompatible(string fromType, string toType, PinDirection targetDirection)
    {
        // Target direction is the direction on the suggested node.
        // If suggested node has Input pin, dragged pin is Output pin: fromType -> toType.
        var src = targetDirection == PinDirection.Input ? fromType : toType;
        var dst = targetDirection == PinDirection.Input ? toType : fromType;

        if (string.Equals(dst, "System.Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dst, "Any", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (src.Contains("Int32", StringComparison.OrdinalIgnoreCase) &&
            (dst.Contains("Int64", StringComparison.OrdinalIgnoreCase) || dst.Contains("Double", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}
