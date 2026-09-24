using AstraGraph.Core;

namespace AstraGraph.UI.Model;

/// <summary>
/// Top-level declarative Astra UI Document representing an in-game window or interface.
/// </summary>
public sealed class UiDocument
{
    public required GraphId Id { get; init; }
    public required string Name { get; set; }
    public GraphKind Kind { get; init; } = GraphKind.UI;
    public GraphSide Side { get; init; } = GraphSide.Client;
    public string? Title { get; set; }
    public int DefaultWidth { get; set; } = 400;
    public int DefaultHeight { get; set; } = 300;

    /// <summary>
    /// Root visual container (usually Window or Panel).
    /// </summary>
    public required UiElementNode Root { get; set; }

    /// <summary>
    /// Reactive bindings connecting UI elements to state variables.
    /// </summary>
    public List<UiBindingDefinition> Bindings { get; set; } = [];

    /// <summary>
    /// Event subscriptions mapping control interactions to actions.
    /// </summary>
    public List<UiEventSubscription> Events { get; set; } = [];

    /// <summary>
    /// Default values for local UI state variables.
    /// </summary>
    public Dictionary<string, object?> LocalStateDefaults { get; set; } = [];

    /// <summary>
    /// Typed state schema. Empty documents keep using <see cref="LocalStateDefaults"/> only.
    /// </summary>
    public List<UiStateVariable> StateVariables { get; set; } = [];

    /// <summary>
    /// Client or server logic steps authored from control events and BUI actions.
    /// </summary>
    public List<UiLogicStep> Logic { get; set; } = [];

    /// <summary>
    /// Typed BUI contract. Ordinary UI documents leave this empty.
    /// </summary>
    public UiBuiContractDocument? Contract { get; set; }

    public List<UiComponentDefinition> Components { get; set; } = [];

    public string DocumentKind { get; set; } = "UI";

    /// <summary>
    /// Traverses all elements in the view tree recursively.
    /// </summary>
    public IEnumerable<UiElementNode> AllElements()
    {
        var stack = new Stack<UiElementNode>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var curr = stack.Pop();
            yield return curr;
            for (int i = curr.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(curr.Children[i]);
            }
        }
    }

    /// <summary>
    /// Finds an element by ID within the view tree.
    /// </summary>
    public UiElementNode? FindElement(string elementId)
    {
        return AllElements().FirstOrDefault(e => string.Equals(e.Id, elementId, StringComparison.Ordinal));
    }
}
