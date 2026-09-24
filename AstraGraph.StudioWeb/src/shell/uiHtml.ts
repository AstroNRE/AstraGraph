import { nodeType, shortType, type UiDocumentModel, type UiNode } from "./uiTree";

function encode(value: string | undefined): string {
  return (value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[char] ?? char));
}

function writeNode(node: UiNode, document: UiDocumentModel): string {
  if (node.visible === false) return "";
  const type = shortType(nodeType(node));
  const classes = (node.styleClasses ?? []).filter(Boolean).map(encode).join(" ");
  const action = (document.events ?? []).find((item) => item.elementId === node.id)?.targetAction;
  const actionAttr = action ? ` data-astra-action="${encode(action)}"` : "";
  const classAttr = classes ? ` class="${classes}"` : "";
  const text = encode(node.text ?? node.properties?.Text ?? node.name ?? "");
  if (type === "Button") return `<button type="button" data-astra-id="${encode(node.id)}"${classAttr}${actionAttr}${node.enabled === false ? " disabled" : ""}>${text || "Button"}</button>`;
  if (type === "LineEdit") return `<input data-astra-id="${encode(node.id)}"${classAttr}${actionAttr} value="${text}"${node.enabled === false ? " disabled" : ""} />`;
  if (type === "Label") return `<label class="astra-label${classes ? " " + classes : ""}" data-astra-id="${encode(node.id)}">${text || "Label"}</label>`;
  if (type === "ProgressBar") {
    const value = Number(node.properties?.Value ?? 0) || 0;
    return `<progress data-astra-id="${encode(node.id)}"${classAttr} value="${value}" max="100"></progress>`;
  }
  const layout = type === "GridContainer" ? "astra-grid" : node.orientation === "Horizontal" ? "astra-row" : "astra-col";
  const root = node.id === document.root.id ? "astra-window " : "";
  const columns = Number(node.properties?.Columns ?? 0);
  const style = type === "GridContainer" && columns > 0 ? ` style="grid-template-columns: repeat(${columns}, minmax(0, 1fr))"` : "";
  const known = ["BoxContainer", "GridContainer", "LayoutContainer", "ScrollContainer", "PanelContainer", "Panel", "Window", "Control"].includes(type);
  const children = node.children.map((child) => writeNode(child, document)).join("");
  return `<div data-astra-id="${encode(node.id)}" class="${root}${layout}${known ? "" : " astra-missing"}${classes ? " " + classes : ""}"${style}>${known ? "" : encode(type)}${children}</div>`;
}

export function renderPage(document: UiDocumentModel): string {
  const bindings = JSON.stringify((document.bindings ?? []).map((item) => ({ elementId: item.elementId, property: item.targetProperty, state: item.stateVariable })));
  return `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>${encode(document.name)}</title>
<style>
html, body { margin: 0; height: 100%; background: #10191d; color: #e7ecf5; font: 13px/1.4 "Noto Sans", sans-serif; }
.astra-window { box-sizing: border-box; min-height: 100%; padding: 10px; display: flex; flex-direction: column; gap: 8px; }
.astra-row { display: flex; flex-direction: row; gap: 8px; align-items: center; }
.astra-col { display: flex; flex-direction: column; gap: 8px; }
.astra-grid { display: grid; gap: 8px; }
button, input, progress { font: inherit; color: inherit; }
button { background: #3d4454; color: #f2f2f2; border: 1px solid #6d7690; padding: 6px 10px; }
input { background: #0c0c0c; border: 1px solid #555; padding: 6px; }
label.astra-label { display: block; }
progress { width: 100%; height: 16px; }
.astra-missing { outline: 1px dashed #a55; padding: 4px; }
${document.css ?? ""}
</style>
</head>
<body>
${writeNode(document.root, document)}
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
  location.href = url;
}
document.addEventListener("click", (event) => {
  const node = event.target.closest("[data-astra-action]");
  if (!node) return;
  astraSend(node.getAttribute("data-astra-action"), {});
});
document.addEventListener("change", (event) => {
  const node = event.target;
  if (!(node instanceof HTMLInputElement)) return;
  const action = node.getAttribute("data-astra-action");
  if (action) astraSend(action, { text: node.value });
});
</script>
</body>
</html>`;
}
