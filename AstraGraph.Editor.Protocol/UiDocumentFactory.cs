using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Html;
using AstraGraph.UI.Model;

namespace AstraGraph.Editor.Protocol;

/// <summary>
/// Builds a <see cref="UiDocument"/> from the authoring DTO. Studio and the game client share this path.
/// </summary>
public static class UiDocumentFactory
{
    public static bool TryRender(UiDocumentDto? document, out string html, out int width, out int height, out IReadOnlySet<string> actions, out string? error)
    {
        html = "";
        width = 400;
        height = 300;
        actions = new HashSet<string>(StringComparer.Ordinal);
        if (!TryCreate(document, out var built, out error))
        {
            return false;
        }

        html = UiHtmlPage.Render(built);
        width = built.DefaultWidth;
        height = built.DefaultHeight;
        actions = built.Events
            .Select(item => item.TargetAction)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.Ordinal);
        return true;
    }

    public static bool TryCreate(UiDocumentDto? document, out UiDocument built, out string? error)
    {
        built = null!;
        if (document?.Root == null || string.IsNullOrWhiteSpace(document.Name))
        {
            error = "UI document needs a name and a root control.";
            return false;
        }

        try
        {
            if (!GraphId.TryParse(document.Id, out var id) || id == GraphId.Empty)
            {
                id = GraphId.New();
            }

            built = new UiDocument
            {
                Id = id,
                Name = document.Name.Trim(),
                Title = document.Name.Trim(),
                Kind = GraphKind.UI,
                Side = GraphSide.Client,
                DefaultWidth = document.Width <= 0 ? 400 : document.Width,
                DefaultHeight = document.Height <= 0 ? 300 : document.Height,
                Root = BuildNode(document.Root, 0),
                Bindings = (document.Bindings ?? []).Select(binding => new UiBindingDefinition
                {
                    BindingId = string.IsNullOrWhiteSpace(binding.BindingId) ? Guid.NewGuid().ToString("D") : binding.BindingId,
                    ElementId = binding.ElementId,
                    TargetProperty = binding.TargetProperty,
                    StateVariable = binding.StateVariable,
                    Direction = Enum.TryParse<BindingDirection>(binding.Direction, ignoreCase: true, out var direction) ? direction : BindingDirection.OneWay
                }).ToList(),
                Events = (document.Events ?? []).Select(item => new UiEventSubscription
                {
                    SubscriptionId = string.IsNullOrWhiteSpace(item.SubscriptionId) ? Guid.NewGuid().ToString("D") : item.SubscriptionId,
                    ElementId = item.ElementId,
                    EventName = item.EventName,
                    TargetAction = item.TargetAction
                }).ToList(),
                LocalStateDefaults = (document.LocalState ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => (object?)pair.Value),
                DocumentKind = string.IsNullOrWhiteSpace(document.DocumentKind) ? "UI" : document.DocumentKind,
                Css = document.Css
            };
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static UiElementNode BuildNode(UiNodeDto dto, int depth)
    {
        if (depth > 32)
        {
            throw new InvalidOperationException("UI tree is too deep.");
        }

        if (!Enum.TryParse<UiOrientation>(dto.Orientation, ignoreCase: true, out var orientation))
        {
            orientation = UiOrientation.Vertical;
        }

        var node = new UiElementNode
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("D") : dto.Id,
            Name = dto.Name,
            Text = dto.Text,
            Visible = dto.Visible,
            Enabled = dto.Enabled,
            Orientation = orientation,
            MinWidth = dto.MinWidth,
            MinHeight = dto.MinHeight,
            StyleClasses = (dto.StyleClasses ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).ToList(),
            CustomProperties = new Dictionary<string, object?> { ["valueSource"] = string.IsNullOrWhiteSpace(dto.ValueSource) ? "Constant" : dto.ValueSource },
            Children = (dto.Children ?? []).Select(child => BuildNode(child, depth + 1)).ToList()
        };
        var typeToken = string.IsNullOrWhiteSpace(dto.ControlTypeId) ? dto.ElementType : dto.ControlTypeId;
        if (UiControlIds.TryParseLegacy(typeToken, out var elementType))
        {
            node.ElementType = elementType;
        }
        else if (!string.IsNullOrWhiteSpace(typeToken))
        {
            node.ControlTypeId = typeToken;
        }

        if (dto.Properties != null)
        {
            foreach (var pair in dto.Properties)
            {
                node.SetAuthoredProperty(pair.Key, pair.Value);
            }
        }

        return node;
    }
}
