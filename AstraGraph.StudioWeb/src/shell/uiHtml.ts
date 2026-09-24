import { nodeType, shortType, type UiDocumentModel, type UiNode } from "./uiTree";

function encode(value: string | undefined): string {
  return (value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[char] ?? char));
}

export function elementStyle(node: UiNode): string {
  const rules: string[] = [];
  if (node.minWidth) rules.push(`min-width:${node.minWidth}px`);
  if (node.minHeight) rules.push(`min-height:${node.minHeight}px`);
  const color = node.properties?.FontColorOverride;
  if (color) rules.push(`color:${color}`);
  if (node.properties?.HorizontalExpand === "true") rules.push("flex-grow:1");
  if (node.properties?.VerticalExpand === "true") rules.push("align-self:stretch");
  const width = node.properties?.["editor.width"];
  const height = node.properties?.["editor.height"];
  const type = shortType(nodeType(node));
  if (type === "GridContainer") {
    rules.push("width:100%", "height:auto", "align-content:start");
    if (width) rules.push(`min-width:${Number(width)}px`);
    if (height) rules.push(`min-height:${Number(height)}px`);
  } else if (type === "LineEdit" && !width) {
    rules.push("width:100%");
  } else {
    if (width) rules.push(`width:${Number(width)}px`);
    if (height) rules.push(`height:${Number(height)}px`);
  }
  const x = node.properties?.["editor.x"];
  const y = node.properties?.["editor.y"];
  if ((x != null && x !== "") || (y != null && y !== "")) {
    rules.push("position:absolute", `left:${Number(x) || 0}px`, `top:${Number(y) || 0}px`);
  }
  const gap = node.properties?.Gap;
  if (gap) rules.push(`gap:${Number(gap) || 0}px`);
  const pin = node.properties?.Pin;
  if (pin === "right") rules.push("margin-left:auto");
  if (pin === "bottom") rules.push("margin-top:auto");
  if (pin === "stretch") rules.push("align-self:stretch");
  const authored = node.properties?.Style?.trim();
  if (authored) rules.push(authored.replace(/;?\s*$/, ""));
  return rules.join(";");
}

function writeNode(node: UiNode, document: UiDocumentModel): string {
  if (node.visible === false) return "";
  const type = shortType(nodeType(node));
  const classes = (node.styleClasses ?? []).filter(Boolean).map(encode).join(" ");
  const action = (document.events ?? []).find((item) => item.elementId === node.id)?.targetAction;
  const actionAttr = action ? ` data-astra-action="${encode(action)}"` : "";
  const style = elementStyle(node);
  const styleAttr = style ? ` style="${style}"` : "";
  const classAttr = classes ? ` class="${classes}"` : "";
  const text = encode(node.text ?? node.properties?.Text ?? node.name ?? "");
  if (type === "Button") {
    const open = node.properties?.OpenPage ? ` data-astra-open="${encode(node.properties.OpenPage)}"` : "";
    return `<button type="button" data-astra-id="${encode(node.id)}"${classAttr}${styleAttr}${actionAttr}${open}${node.enabled === false ? " disabled" : ""}>${text || "Button"}</button>`;
  }
  if (type === "LineEdit") return `<input data-astra-id="${encode(node.id)}"${classAttr}${styleAttr}${actionAttr} value="${text}"${node.enabled === false ? " disabled" : ""} />`;
  if (type === "Label") return `<label class="astra-label${classes ? " " + classes : ""}" data-astra-id="${encode(node.id)}"${styleAttr}>${text || "Label"}</label>`;
  if (type === "ProgressBar") {
    const value = Number(node.properties?.Value ?? 0) || 0;
    return `<progress data-astra-id="${encode(node.id)}"${classAttr}${styleAttr} value="${value}" max="100"></progress>`;
  }
  if (type === "TextureRect") {
    const texture = encode(node.properties?.Texture ?? "");
    const state = encode(node.properties?.State ?? "");
    const src = encode(spriteSource(node));
    const open = node.properties?.OpenPage ? ` data-astra-open="${encode(node.properties.OpenPage)}"` : "";
    return `<img data-astra-id="${encode(node.id)}" data-texture="${texture}" data-state="${state}"${classAttr}${styleAttr}${open} src="${src}" alt="${state || "sprite"}" />`;
  }
  const layout = type === "GridContainer" ? "astra-grid" : node.orientation === "Horizontal" ? "astra-row" : "astra-col";
  const root = node.id === document.root.id ? "astra-window " : "";
  const columns = Number(node.properties?.Columns ?? 0);
  const grid = type === "GridContainer" && columns > 0 ? `grid-template-columns: repeat(${columns}, minmax(0, 1fr))` : "";
  const known = ["BoxContainer", "GridContainer", "LayoutContainer", "ScrollContainer", "PanelContainer", "Panel", "Window", "Control"].includes(type);
  const once = node.children.map((child) => writeNode(child, document)).join("");
  const times = Math.min(12, Math.max(1, Number(node.properties?.Repeat ?? 1) || 1));
  const children = once.repeat(times);
  const merged = [grid, style].filter(Boolean).join(";");
  return `<div data-astra-id="${encode(node.id)}" class="${root}${layout}${known ? "" : " astra-missing"}${classes ? " " + classes : ""}"${merged ? ` style="${merged}"` : ""}>${known ? "" : encode(type)}${children}</div>`;
}

function spriteSource(node: UiNode): string {
  const uploaded = node.properties?.SpriteUrl ?? "";
  if (uploaded.startsWith("data:")) return uploaded;
  const texture = node.properties?.Texture ?? "";
  if (texture.startsWith("/Textures/")) return `astra-ui://texture?path=${encodeURIComponent(texture)}`;
  return uploaded;
}

export function renderPage(document: UiDocumentModel): string {
  const bindings = JSON.stringify((document.bindings ?? []).map((item) => ({ elementId: item.elementId, property: item.targetProperty, state: item.stateVariable })));
  const tokens = (document.tokens ?? []).map((token) => `--astra-${token.name.replace(/[^a-z0-9_-]/gi, "")}:${token.value}`).join(";");
  const pages = document.pages?.length
    ? document.pages
    : [{ id: document.id, name: document.name, width: document.width, height: document.height, root: document.root }];
  const active = document.activePageId ?? pages[0]?.id;
  const frames = pages.map((page) => {
    const root = page.id === active ? document.root : page.root;
    const hidden = page.id === active ? "" : " hidden";
    return `<section data-astra-page="${encode(page.id)}" class="astra-page"${hidden} style="width:${page.width}px;min-height:${page.height}px">${writeNode(root, document)}</section>`;
  }).join("");
  return `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>${encode(document.name)}</title>
<style>
html, body { margin: 0; height: 100%; background: #10191d; color: #e7ecf5; font: 13px/1.4 "Noto Sans", sans-serif; ${tokens} }
.astra-window, .astra-row, .astra-col, .astra-grid { position: relative; }
.astra-page[hidden] { display: none; }
img { image-rendering: pixelated; object-fit: contain; }
.astra-window { box-sizing: border-box; min-height: 100%; padding: 10px; display: flex; flex-direction: column; gap: 8px; }
.astra-row { display: flex; flex-direction: row; gap: 8px; align-items: center; }
.astra-col { display: flex; flex-direction: column; gap: 8px; }
.astra-grid { display: grid; gap: 8px; width: 100%; height: auto; min-height: 48px; align-content: start; grid-auto-rows: minmax(32px, auto); box-sizing: border-box; }
input, .astra-field { width: 100%; box-sizing: border-box; }
button, input, progress { font: inherit; color: inherit; }
button { background: #3d4454; color: #f2f2f2; border: 1px solid #6d7690; padding: 6px 10px; cursor: pointer; }
button:hover { background: #556070; border-color: #d7deea; }
button:active, button.is-pressed { background: #243044; border-color: #edbc63; }
button:disabled, button:disabled:hover { background: #3d4454; border-color: #6d7690; cursor: default; }
input { background: #0c0c0c; border: 1px solid #555; padding: 6px; }
label.astra-label { display: block; }
progress { width: 100%; height: 16px; }
.astra-missing { outline: 1px dashed #a55; padding: 4px; }
${document.css ?? ""}
</style>
</head>
<body>
${frames}
<script>
const astraBindings = ${bindings};
function astraApplyState(state) {
  if (!state) return;
  for (const binding of astraBindings) {
    const node = document.querySelector('[data-astra-id="' + binding.elementId + '"]');
    if (!node || state[binding.state] == null) continue;
    const value = String(state[binding.state]);
    if (binding.property === "Text" && "value" in node) node.value = value;
    else if (binding.property === "Text") node.textContent = value;
    else if (binding.property === "Value" && node instanceof HTMLProgressElement) node.value = Number(value);
  }
}
function astraSend(name, payload) {
  const url = "astra-bui://action?name=" + encodeURIComponent(name) + "&payload=" + encodeURIComponent(JSON.stringify(payload || {}));
  if (window.parent && window.parent !== window) {
    window.parent.postMessage({ type: "astra-bui", url: url }, "*");
    return;
  }
  fetch(url, { method: "GET", cache: "no-store" }).catch(function () {});
}
document.addEventListener("click", (event) => {
  const opener = event.target.closest("[data-astra-open]");
  if (opener) {
    const page = opener.getAttribute("data-astra-open");
    document.querySelectorAll("[data-astra-page]").forEach((section) => { section.hidden = section.getAttribute("data-astra-page") !== page; });
  }
  const node = event.target.closest("[data-astra-action]");
  if (!node) return;
  if (node.tagName === "BUTTON") {
    node.classList.add("is-pressed");
    window.setTimeout(function () { node.classList.remove("is-pressed"); }, 280);
  }
  astraSend(node.getAttribute("data-astra-action"), {});
});
document.addEventListener("change", (event) => {
  const node = event.target;
  if (!(node instanceof HTMLInputElement)) return;
  const action = node.getAttribute("data-astra-action");
  if (action) astraSend(action, { text: node.value });
});
${document.script ?? ""}
</script>
</body>
</html>`;
}
