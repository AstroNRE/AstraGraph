using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Html;

/// <summary>
/// Renders an Astra UI document as one HTML page. The game shows that page in
/// <c>WebViewControl</c>. Studio shows the same markup.
/// </summary>
public static class UiHtmlPage
{
    public const string ActionPrefix = "astra-bui://action";

    public static string Render(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var bindings = document.Bindings.Select(binding => new
        {
            elementId = binding.ElementId,
            property = binding.TargetProperty,
            state = binding.StateVariable
        });
        var bindingJson = JsonSerializer.Serialize(bindings);
        var body = new StringBuilder();
        WriteElement(body, document.Root, document);
        var page = new StringBuilder();
        page.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>")
            .Append(Encode(document.Name))
            .Append("</title><style>")
            .Append("html, body { margin: 0; height: 100%; background: #10191d; color: #e7ecf5; font: 13px/1.4 \"Noto Sans\", sans-serif; }")
            .Append(".astra-window, .astra-row, .astra-col, .astra-grid { position: relative; }")
            .Append("img { image-rendering: pixelated; object-fit: contain; }")
            .Append(".astra-window { box-sizing: border-box; min-height: 100%; padding: 10px; display: flex; flex-direction: column; gap: 8px; }")
            .Append(".astra-row { display: flex; flex-direction: row; gap: 8px; align-items: center; }")
            .Append(".astra-col { display: flex; flex-direction: column; gap: 8px; }")
            .Append(".astra-grid { display: grid; gap: 8px; }")
            .Append("button, input, progress { font: inherit; color: inherit; }")
            .Append("button { background: #3d4454; color: #f2f2f2; border: 1px solid #6d7690; padding: 6px 10px; }")
            .Append("input { background: #0c0c0c; border: 1px solid #555; padding: 6px; }")
            .Append("label.astra-label { display: block; }")
            .Append("progress { width: 100%; height: 16px; }")
            .Append(".astra-list { display: flex; flex-direction: column; gap: 4px; overflow: auto; }")
            .Append(".astra-list-row { text-align: left; }")
            .Append(".astra-list-row.is-selected { outline: 1px solid #edbc63; }")
            .Append(".astra-list-row:disabled { opacity: 0.45; }")
            .Append(".astra-missing { outline: 1px dashed #a55; padding: 4px; }")
            .Append(document.Css ?? "")
            .Append("</style></head><body>")
            .Append(body)
            .Append("<script>")
            .Append("const astraBindings = ").Append(bindingJson).Append(';')
            .Append("function astraText(value) {")
            .Append("const text = String(value);")
            .Append("const split = text.indexOf(\":\");")
            .Append("const kind = split > 0 ? text.slice(0, split) : \"\";")
            .Append("return kind === \"string\" || kind === \"int\" || kind === \"bool\" || kind === \"float\" ? text.slice(split + 1) : text;")
            .Append('}')
            .Append("function astraApplyState(state) {")
            .Append("if (!state) return;")
            .Append("for (const binding of astraBindings) {")
            .Append("const node = document.querySelector('[data-astra-id=\"' + binding.elementId + '\"]');")
            .Append("if (!node || state[binding.state] == null) continue;")
            .Append("const value = astraText(state[binding.state]);")
            .Append("if (binding.property === \"Items\" && node.hasAttribute(\"data-astra-list\")) astraFillList(node, value);")
            .Append("else if (binding.property === \"Text\" && \"value\" in node) node.value = value;")
            .Append("else if (binding.property === \"Text\") node.textContent = value;")
            .Append("else if (binding.property === \"Value\" && node instanceof HTMLProgressElement) node.value = Number(value);")
            .Append("}}")
            .Append("function astraFillList(node, json) {")
            .Append("let rows = [];")
            .Append("try { rows = JSON.parse(json); } catch (error) { rows = []; }")
            .Append("if (!Array.isArray(rows)) rows = [];")
            .Append("const selected = node.getAttribute(\"data-selected-id\") || \"\";")
            .Append("node.replaceChildren();")
            .Append("let kept = false;")
            .Append("for (const row of rows) {")
            .Append("if (!row || typeof row !== \"object\") continue;")
            .Append("const id = String(row.id ?? \"\");")
            .Append("const button = document.createElement(\"button\");")
            .Append("button.type = \"button\";")
            .Append("button.className = \"astra-list-row\";")
            .Append("button.textContent = String(row.text ?? id);")
            .Append("button.dataset.id = id;")
            .Append("const disabled = row.disabled === true || row.disabled === \"true\";")
            .Append("if (disabled) button.disabled = true;")
            .Append("if (!disabled && id === selected) { button.classList.add(\"is-selected\"); kept = true; }")
            .Append("node.appendChild(button);")
            .Append('}')
            .Append("if (!kept) node.removeAttribute(\"data-selected-id\");")
            .Append('}')
            .Append("function astraSend(name, payload) {")
            .Append("const url = \"astra-bui://action?name=\" + encodeURIComponent(name) + \"&payload=\" + encodeURIComponent(JSON.stringify(payload || {}));")
            .Append("if (window.parent && window.parent !== window) { window.parent.postMessage({ type: \"astra-bui\", url: url }, \"*\"); return; }")
            .Append("fetch(url, { method: \"GET\", cache: \"no-store\" }).catch(function () {});")
            .Append('}')
            .Append("function astraSelected(node) {")
            .Append("const listId = node.getAttribute(\"data-astra-selection\");")
            .Append("if (!listId) return \"\";")
            .Append("const list = document.querySelector('[data-astra-id=\"' + listId + '\"]');")
            .Append("return list ? (list.getAttribute(\"data-selected-id\") || \"\") : \"\";")
            .Append('}')
            .Append("document.addEventListener(\"click\", (event) => {")
            .Append("const row = event.target.closest(\".astra-list-row\");")
            .Append("if (row) {")
            .Append("if (row.disabled) return;")
            .Append("const list = row.closest(\"[data-astra-list]\");")
            .Append("if (!list) return;")
            .Append("list.setAttribute(\"data-selected-id\", row.dataset.id || \"\");")
            .Append("for (const item of list.querySelectorAll(\".astra-list-row\")) item.classList.toggle(\"is-selected\", item === row);")
            .Append("const action = list.getAttribute(\"data-astra-action\");")
            .Append("if (action) astraSend(action, { id: row.dataset.id || \"\" });")
            .Append("return;")
            .Append('}')
            .Append("const node = event.target.closest(\"[data-astra-action]\");")
            .Append("if (!node) return;")
            .Append("const id = astraSelected(node);")
            .Append("astraSend(node.getAttribute(\"data-astra-action\"), id ? { id: id } : {});")
            .Append("});")
            .Append("document.addEventListener(\"change\", (event) => {")
            .Append("const node = event.target;")
            .Append("if (!(node instanceof HTMLInputElement)) return;")
            .Append("const action = node.getAttribute(\"data-astra-action\");")
            .Append("if (action) astraSend(action, { text: node.value });")
            .Append("});")
            .Append("</script></body></html>");
        return page.ToString();
    }

    public static bool TryReadAction(string? url, out string name, out IReadOnlyDictionary<string, string> payload)
    {
        name = "";
        payload = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(ActionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0 || queryIndex == url.Length - 1)
        {
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in url[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.IndexOf('=');
            var key = Uri.UnescapeDataString(split < 0 ? part : part[..split]);
            var value = split < 0 ? "" : Uri.UnescapeDataString(part[(split + 1)..]);
            fields[key] = value;
        }

        if (!fields.TryGetValue("name", out var action) || string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        name = action;
        if (fields.TryGetValue("payload", out var json) && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed != null)
                {
                    payload = parsed;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteElement(StringBuilder body, UiElementNode node, UiDocument document)
    {
        if (!node.Visible)
        {
            return;
        }

        var type = ShortType(node.ControlTypeId);
        var classes = string.Join(' ', node.StyleClasses.Where(item => !string.IsNullOrWhiteSpace(item)).Select(Encode));
        var id = Encode(node.Id);
        var action = document.Events.FirstOrDefault(item => item.ElementId == node.Id)?.TargetAction;
        var actionAttr = string.IsNullOrWhiteSpace(action) ? "" : " data-astra-action=\"" + Encode(action) + "\"";
        var selection = node.Properties.TryGetValue("Selection", out var selectedList) ? selectedList?.ToString() : null;
        var selectionAttr = string.IsNullOrWhiteSpace(selection) ? "" : " data-astra-selection=\"" + Encode(selection) + "\"";
        var classAttr = classes.Length == 0 ? "" : " class=\"" + classes + "\"";
        var text = Encode(node.Text ?? node.Name ?? "");
        switch (type)
        {
            case "ItemList":
                body.Append("<div data-astra-id=\"").Append(id).Append("\" data-astra-list=\"1\" class=\"astra-list");
                if (classes.Length > 0) body.Append(' ').Append(classes);
                body.Append('"').Append(actionAttr).Append("></div>");
                return;
            case "Button":
                body.Append("<button type=\"button\" data-astra-id=\"").Append(id).Append('"').Append(classAttr).Append(actionAttr).Append(selectionAttr);
                if (!node.Enabled) body.Append(" disabled");
                body.Append('>').Append(text.Length == 0 ? "Button" : text).Append("</button>");
                return;
            case "LineEdit":
                body.Append("<input data-astra-id=\"").Append(id).Append('"').Append(classAttr).Append(actionAttr);
                body.Append(" value=\"").Append(text).Append('"');
                if (!node.Enabled) body.Append(" disabled");
                body.Append(" />");
                return;
            case "Label":
                body.Append("<label class=\"astra-label");
                if (classes.Length > 0) body.Append(' ').Append(classes);
                body.Append("\" data-astra-id=\"").Append(id).Append("\">").Append(text.Length == 0 ? "Label" : text).Append("</label>");
                return;
            case "ProgressBar":
                var value = node.Properties.TryGetValue("Value", out var raw) ? raw?.ToString() : "0";
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) number = 0;
                body.Append("<progress data-astra-id=\"").Append(id).Append('"').Append(classAttr);
                body.Append(" value=\"").Append(number.ToString(CultureInfo.InvariantCulture)).Append("\" max=\"100\"></progress>");
                return;
            case "TextureRect":
                var texture = node.Properties.TryGetValue("Texture", out var textureValue) ? textureValue?.ToString() ?? "" : "";
                var state = node.Properties.TryGetValue("State", out var stateValue) ? stateValue?.ToString() ?? "" : "";
                var uploaded = node.Properties.TryGetValue("SpriteUrl", out var spriteUrl) ? spriteUrl?.ToString() ?? "" : "";
                var src = texture.StartsWith("/Textures/", StringComparison.Ordinal)
                    ? "astra-ui://texture?path=" + Uri.EscapeDataString(texture)
                    : uploaded;
                body.Append("<img data-astra-id=\"").Append(id)
                    .Append("\" data-texture=\"").Append(Encode(texture))
                    .Append("\" data-state=\"").Append(Encode(state))
                    .Append("\" alt=\"").Append(Encode(state))
                    .Append("\" src=\"").Append(Encode(src))
                    .Append("\" style=\"image-rendering:pixelated;object-fit:contain\" />");
                return;
            default:
                var layout = type == "GridContainer" ? "astra-grid" : node.Orientation == UiOrientation.Horizontal ? "astra-row" : "astra-col";
                if (node.Id == document.Root.Id) layout = "astra-window " + layout;
                var columns = node.Properties.TryGetValue("Columns", out var cols) ? cols?.ToString() : null;
                var style = type == "GridContainer" && int.TryParse(columns, out var count) && count > 0
                    ? " style=\"grid-template-columns: repeat(" + count.ToString(CultureInfo.InvariantCulture) + ", minmax(0, 1fr))\""
                    : "";
                var missing = type is "BoxContainer" or "GridContainer" or "LayoutContainer" or "ScrollContainer" or "PanelContainer" or "Panel" or "Window" or "Control"
                    ? ""
                    : " astra-missing";
                body.Append("<div data-astra-id=\"").Append(id).Append("\" class=\"").Append(layout).Append(missing);
                if (classes.Length > 0) body.Append(' ').Append(classes);
                body.Append('"').Append(style).Append('>');
                if (missing.Length > 0) body.Append(Encode(type));
                foreach (var child in node.Children) WriteElement(body, child, document);
                body.Append("</div>");
                return;
        }
    }

    private static string ShortType(string typeId)
    {
        var slash = typeId.LastIndexOf('.');
        return slash >= 0 ? typeId[(slash + 1)..] : typeId;
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");
}
