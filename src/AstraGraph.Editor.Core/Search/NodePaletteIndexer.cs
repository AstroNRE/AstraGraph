using System;
using System.Collections.Generic;
using System.Linq;
using AstraGraph.Binding;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core.Search;

public sealed record SearchResult(
    NodePaletteItem Item,
    int Score);

public sealed class NodePaletteIndexer
{
    private readonly List<NodePaletteItem> _items = [];
    private readonly object _lock = new();

    public IReadOnlyList<NodePaletteItem> AllItems
    {
        get
        {
            lock (_lock)
            {
                return _items.ToList();
            }
        }
    }

    public NodePaletteIndexer()
    {
        RegisterBuiltInNodes();
    }

    public void RegisterItem(NodePaletteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_lock)
        {
            _items.Add(item);
        }
    }

    public void IndexBindingCatalog(BindingCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        lock (_lock)
        {
            foreach (var desc in catalog.Search(string.Empty))
            {
                var pins = new List<VisualPinDefinition>();

                // Execution pins for non-pure methods
                if (!desc.IsPure)
                {
                    pins.Add(new VisualPinDefinition("In", PinDirection.Input, PinKind.Execution, "Flow"));
                    pins.Add(new VisualPinDefinition("Out", PinDirection.Output, PinKind.Execution, "Flow"));
                }

                // If instance method, add target pin
                if (!desc.Method.IsStatic)
                {
                    pins.Add(new VisualPinDefinition("Target", PinDirection.Input, PinKind.Data, desc.DeclaringTypeName));
                }

                // Method parameters
                foreach (var param in desc.Parameters)
                {
                    pins.Add(new VisualPinDefinition(param.Name, PinDirection.Input, PinKind.Data, param.Type.TypeName));
                }

                // Return value
                if (desc.Method.ReturnType != typeof(void))
                {
                    pins.Add(new VisualPinDefinition("Result", PinDirection.Output, PinKind.Data, desc.ReturnType.TypeName));
                }

                var item = new NodePaletteItem(
                    id: $"Native:{desc.Descriptor}",
                    displayName: $"{desc.DeclaringTypeName}.{desc.Name}",
                    category: $"Native / {desc.DeclaringTypeName}",
                    nodeType: "Native.Call",
                    pins: pins,
                    isPure: desc.IsPure,
                    isPredictionSafe: desc.IsDeterministic,
                    side: desc.Side,
                    requiredProfile: desc.RequiredProfile,
                    estimatedCost: desc.Cost,
                    documentation: $"Calls {desc.DeclaringTypeName}.{desc.Name}",
                    defaultProperties: new Dictionary<string, string>
                    {
                        ["MethodDescriptor"] = desc.Descriptor
                    });

                _items.Add(item);
            }
        }
    }

    public List<SearchResult> Search(string query, GraphSide? sideFilter = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return _items
                    .Where(item => MatchesSide(item, sideFilter))
                    .Select(item => new SearchResult(item, 100))
                    .OrderBy(r => r.Item.Category)
                    .ThenBy(r => r.Item.DisplayName)
                    .ToList();
            }

            var cleanQuery = query.Trim();
            var tokens = cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var results = new List<SearchResult>();

            foreach (var item in _items)
            {
                if (!MatchesSide(item, sideFilter)) continue;

                var score = ComputeMatchScore(item, cleanQuery, tokens);
                if (score > 0)
                {
                    results.Add(new SearchResult(item, score));
                }
            }

            return results
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Item.DisplayName)
                .ToList();
        }
    }

    private static bool MatchesSide(NodePaletteItem item, GraphSide? sideFilter)
    {
        if (!sideFilter.HasValue) return true;
        return item.Side == GraphSide.Shared || item.Side == sideFilter.Value;
    }

    private static int ComputeMatchScore(NodePaletteItem item, string fullQuery, string[] tokens)
    {
        var name = item.DisplayName;
        var category = item.Category;

        // Exact match
        if (string.Equals(name, fullQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        // Starts with query
        if (name.StartsWith(fullQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 500;
        }

        // Contains full query
        if (name.Contains(fullQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 300;
        }

        // Acronym match (e.g. "sr" for "Set Rotation")
        if (IsAcronymMatch(name, fullQuery))
        {
            return 250;
        }

        // All tokens present
        var allTokensMatch = tokens.All(t =>
            name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            category.Contains(t, StringComparison.OrdinalIgnoreCase));

        if (allTokensMatch)
        {
            return 100 + (tokens.Length * 10);
        }

        return 0;
    }

    private static bool IsAcronymMatch(string name, string query)
    {
        if (query.Length < 2) return false;

        var capitalsOrWordStarts = name
            .Where((c, idx) => char.IsUpper(c) || idx == 0 || (idx > 0 && (name[idx - 1] == '.' || name[idx - 1] == ' ')))
            .Select(char.ToLowerInvariant)
            .ToArray();

        var queryChars = query.ToLowerInvariant();
        var qi = 0;

        foreach (var c in capitalsOrWordStarts)
        {
            if (qi < queryChars.Length && c == queryChars[qi])
            {
                qi++;
            }
        }

        return qi == queryChars.Length;
    }

    private void RegisterBuiltInNodes()
    {
        // Flow Control
        _items.Add(new NodePaletteItem(
            "Core.Branch",
            "Branch",
            "Control Flow",
            "Core.Branch",
            [
                new("In", PinDirection.Input, PinKind.Execution, "Flow"),
                new("Condition", PinDirection.Input, PinKind.Data, "System.Boolean"),
                new("True", PinDirection.Output, PinKind.Execution, "Flow"),
                new("False", PinDirection.Output, PinKind.Execution, "Flow")
            ],
            isPure: false,
            documentation: "Branches execution based on a boolean condition"));

        _items.Add(new NodePaletteItem(
            "Astra.EntryPoint",
            "Entry Point / Event",
            "Control Flow",
            "Astra.EntryPoint",
            [
                new("Out", PinDirection.Output, PinKind.Execution, "Flow"),
                new("EventEntity", PinDirection.Output, PinKind.Data, "Robust.Shared.GameObjects.EntityUid")
            ],
            isPure: false,
            documentation: "Subscribes to an ECS event and initiates graph execution"));

        _items.Add(new NodePaletteItem(
            "Core.Return",
            "Return",
            "Control Flow",
            "Core.Return",
            [
                new("In", PinDirection.Input, PinKind.Execution, "Flow"),
                new("Value", PinDirection.Input, PinKind.Data, "System.Object")
            ],
            isPure: false,
            documentation: "Terminates execution and returns a value"));

        // Latent Operations
        _items.Add(new NodePaletteItem(
            "Latent.Delay",
            "Delay",
            "Latent",
            "Latent.Delay",
            [
                new("In", PinDirection.Input, PinKind.Execution, "Flow"),
                new("Seconds", PinDirection.Input, PinKind.Data, "System.Single"),
                new("Completed", PinDirection.Output, PinKind.Execution, "Flow")
            ],
            isPure: false,
            documentation: "Suspends graph execution for specified seconds"));

        _items.Add(new NodePaletteItem(
            "Latent.DoAfter",
            "DoAfter",
            "Latent",
            "Latent.DoAfter",
            [
                new("In", PinDirection.Input, PinKind.Execution, "Flow"),
                new("User", PinDirection.Input, PinKind.Data, "Robust.Shared.GameObjects.EntityUid"),
                new("Target", PinDirection.Input, PinKind.Data, "Robust.Shared.GameObjects.EntityUid"),
                new("DelaySeconds", PinDirection.Input, PinKind.Data, "System.Single"),
                new("Success", PinDirection.Output, PinKind.Execution, "Flow"),
                new("Cancelled", PinDirection.Output, PinKind.Execution, "Flow")
            ],
            isPure: false,
            documentation: "Performs an interactive DoAfter delay with cancellation checks"));

        // Math & Logic
        _items.Add(new NodePaletteItem(
            "Math.Add",
            "Add",
            "Math",
            "Math.Add",
            [
                new("A", PinDirection.Input, PinKind.Data, "System.Int64"),
                new("B", PinDirection.Input, PinKind.Data, "System.Int64"),
                new("Result", PinDirection.Output, PinKind.Data, "System.Int64")
            ],
            isPure: true,
            documentation: "Adds two numbers"));

        _items.Add(new NodePaletteItem(
            "Math.Multiply",
            "Multiply",
            "Math",
            "Math.Mul",
            [
                new("A", PinDirection.Input, PinKind.Data, "System.Int64"),
                new("B", PinDirection.Input, PinKind.Data, "System.Int64"),
                new("Result", PinDirection.Output, PinKind.Data, "System.Int64")
            ],
            isPure: true,
            documentation: "Multiplies two numbers"));

        _items.Add(new NodePaletteItem(
            "Cmp.GreaterThan",
            "Greater Than",
            "Comparison",
            "Cmp.GreaterThan",
            [
                new("A", PinDirection.Input, PinKind.Data, "System.Int64"),
                new("B", PinDirection.Input, PinKind.Data, "System.Int64"),
                new("Result", PinDirection.Output, PinKind.Data, "System.Boolean")
            ],
            isPure: true,
            documentation: "Returns true if A > B"));

        // Dynamic Component Operations
        _items.Add(new NodePaletteItem(
            "Component.Get",
            "Get Dynamic Component",
            "ECS / Dynamic Component",
            "Component.Get",
            [
                new("Entity", PinDirection.Input, PinKind.Data, "Robust.Shared.GameObjects.EntityUid"),
                new("Value", PinDirection.Output, PinKind.Data, "System.Object")
            ],
            isPure: true,
            documentation: "Reads field from dynamic component on entity"));

        _items.Add(new NodePaletteItem(
            "Component.Set",
            "Set Dynamic Component",
            "ECS / Dynamic Component",
            "Component.Set",
            [
                new("In", PinDirection.Input, PinKind.Execution, "Flow"),
                new("Entity", PinDirection.Input, PinKind.Data, "Robust.Shared.GameObjects.EntityUid"),
                new("Value", PinDirection.Input, PinKind.Data, "System.Object"),
                new("Out", PinDirection.Output, PinKind.Execution, "Flow")
            ],
            isPure: false,
            documentation: "Writes field value into dynamic component"));
    }
}
