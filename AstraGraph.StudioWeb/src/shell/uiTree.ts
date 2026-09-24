export interface UiNode {
  id: string;
  elementType: string;
  controlTypeId?: string;
  name?: string;
  text?: string;
  children: UiNode[];
  visible?: boolean;
  enabled?: boolean;
  orientation?: string;
  minWidth?: number | null;
  minHeight?: number | null;
  styleClasses?: string[];
  valueSource?: "Constant" | "Binding" | "Expression";
  properties?: Record<string, string>;
  locked?: boolean;
}

export interface UiBindingModel {
  bindingId: string;
  elementId: string;
  targetProperty: string;
  stateVariable: string;
  direction: string;
}

export interface UiEventModel {
  subscriptionId: string;
  elementId: string;
  eventName: string;
  targetAction: string;
}

export interface UiStateVariableModel {
  id: string;
  name: string;
  typeName: string;
  defaultValue?: string;
  scope: "Local" | "Server" | "Derived";
}

export interface UiDocumentModel {
  id: string;
  name: string;
  width: number;
  height: number;
  documentKind?: "UI" | "BUI";
  root: UiNode;
  bindings?: UiBindingModel[];
  events?: UiEventModel[];
  localState?: Record<string, string>;
  stateVariables?: UiStateVariableModel[];
  logic?: UiLogicStepModel[];
  components?: UiComponentModel[];
  css?: string;
}

export interface UiLogicStepModel {
  id: string;
  kind: string;
  elementId?: string;
  eventName?: string;
  actionName?: string;
  stateVariable?: string;
  propertyName?: string;
}

export interface UiComponentModel {
  id: string;
  name: string;
  root: UiNode;
}

export interface UiPropertyInfo {
  name: string;
  typeName: string;
  editorKind: string;
  canWrite: boolean;
  category?: string;
  defaultValue?: string;
  enumValues?: string[];
}

export interface UiEventInfo {
  name: string;
  eventType: string;
  payload: string[];
}

export interface UiControlInfo {
  typeId: string;
  displayName: string;
  category: string;
  canHaveChildren: boolean;
  properties: UiPropertyInfo[];
  events: UiEventInfo[];
}

export function shortType(typeId: string): string {
  const slash = typeId.lastIndexOf(".");
  return slash >= 0 ? typeId.slice(slash + 1) : typeId;
}

export function nodeType(node: UiNode): string {
  return node.controlTypeId || node.elementType;
}

export function updateNode(node: UiNode, id: string, patch: Partial<UiNode>): UiNode {
  if (node.id === id) return { ...node, ...patch, children: patch.children ?? node.children };
  return { ...node, children: node.children.map((child) => updateNode(child, id, patch)) };
}

export function insertChild(node: UiNode, parentId: string, child: UiNode, index?: number): UiNode {
  if (node.id === parentId) {
    const children = [...node.children];
    const at = index == null || index < 0 || index > children.length ? children.length : index;
    children.splice(at, 0, child);
    return { ...node, children };
  }
  return { ...node, children: node.children.map((item) => insertChild(item, parentId, child, index)) };
}

export function removeNode(node: UiNode, id: string): UiNode {
  return { ...node, children: node.children.filter((child) => child.id !== id).map((child) => removeNode(child, id)) };
}

export function findNode(node: UiNode, id: string): UiNode | undefined {
  if (node.id === id) return node;
  for (const child of node.children) {
    const found = findNode(child, id);
    if (found) return found;
  }
  return undefined;
}

export function findParent(node: UiNode, id: string, parent?: UiNode): UiNode | undefined {
  if (node.id === id) return parent;
  for (const child of node.children) {
    const found = findParent(child, id, node);
    if (found) return found;
  }
  return undefined;
}

export function remapIds(node: UiNode, ids: () => string): UiNode {
  return { ...node, id: ids(), children: node.children.map((child) => remapIds(child, ids)) };
}

export function remapSubtree(document: UiDocumentModel, node: UiNode, ids: () => string): { node: UiNode; bindings: NonNullable<UiDocumentModel["bindings"]>; events: NonNullable<UiDocumentModel["events"]> } {
  const map = new Map<string, string>();
  function walk(current: UiNode): UiNode {
    const id = ids();
    map.set(current.id, id);
    return { ...current, id, children: current.children.map(walk) };
  }
  const copy = walk(node);
  const bindings = (document.bindings ?? []).filter((item) => map.has(item.elementId)).map((item) => ({ ...item, bindingId: ids(), elementId: map.get(item.elementId) ?? item.elementId }));
  const events = (document.events ?? []).filter((item) => map.has(item.elementId)).map((item) => ({ ...item, subscriptionId: ids(), elementId: map.get(item.elementId) ?? item.elementId }));
  return { node: copy, bindings, events };
}

export function reorder(node: UiNode, parentId: string, from: number, to: number): UiNode {
  if (node.id === parentId) {
    const children = [...node.children];
    const [item] = children.splice(from, 1);
    if (!item) return node;
    children.splice(to, 0, item);
    return { ...node, children };
  }
  return { ...node, children: node.children.map((child) => reorder(child, parentId, from, to)) };
}

export function canParent(catalog: UiControlInfo[], parent: UiNode): boolean {
  const info = catalog.find((item) => item.typeId === nodeType(parent) || shortType(item.typeId) === parent.elementType);
  return info ? info.canHaveChildren : true;
}

export interface UiBox {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

export function snapValue(value: number, step = 8): number {
  if (!Number.isFinite(value) || step <= 0) return value;
  return Math.round(value / step) * step;
}

export function layoutTree(node: UiNode, x = 0, y = 0, width = 320, snap = 8): UiBox[] {
  const type = shortType(nodeType(node));
  const self: UiBox = { id: node.id, x: snapValue(x, snap), y: snapValue(y, snap), w: Math.max(80, snapValue(width, snap)), h: 40 };
  const boxes: UiBox[] = [self];
  if (node.children.length === 0) return boxes;
  if (type === "LayoutContainer") {
    let bottom = self.y + 28;
    for (const child of node.children) {
      const cx = self.x + snapValue(Number(child.properties?.["editor.x"] ?? 0), snap);
      const cy = self.y + snapValue(Number(child.properties?.["editor.y"] ?? 0), snap);
      const nested = layoutTree(child, cx, cy, Number(child.properties?.["editor.width"] ?? child.minWidth ?? 120), snap);
      boxes.push(...nested);
      bottom = Math.max(bottom, cy + (nested[0]?.h ?? 40));
    }
    self.h = Math.max(48, bottom - self.y);
    return boxes;
  }
  const horizontal = node.orientation === "Horizontal";
  const gap = snapValue(Number(node.properties?.SeparationOverride ?? 8), snap);
  if (type === "GridContainer") {
    const columns = Math.max(1, Number(node.properties?.Columns ?? 1) || 1);
    const cell = Math.max(80, Math.floor((self.w - gap) / columns));
    node.children.forEach((child, index) => {
      const col = index % columns;
      const row = Math.floor(index / columns);
      boxes.push(...layoutTree(child, self.x + col * (cell + gap), self.y + 28 + row * 48, cell, snap));
    });
    self.h = 28 + Math.ceil(node.children.length / columns) * 48;
    return boxes;
  }
  let cursorX = self.x + 8;
  let cursorY = self.y + 28;
  const childWidth = horizontal
    ? Math.max(80, Math.floor((self.w - 16 - gap * Math.max(node.children.length - 1, 0)) / Math.max(node.children.length, 1)))
    : self.w - 16;
  for (const child of node.children) {
    const nested = layoutTree(child, horizontal ? cursorX : self.x + 8, cursorY, childWidth, snap);
    boxes.push(...nested);
    const height = nested[0]?.h ?? 36;
    if (horizontal) cursorX += childWidth + gap;
    else cursorY += height + gap;
  }
  self.h = Math.max(48, horizontal ? 72 : cursorY - self.y);
  return boxes;
}

export function boxesInRect(boxes: UiBox[], rect: { x: number; y: number; w: number; h: number }): string[] {
  const left = Math.min(rect.x, rect.x + rect.w);
  const right = Math.max(rect.x, rect.x + rect.w);
  const top = Math.min(rect.y, rect.y + rect.h);
  const bottom = Math.max(rect.y, rect.y + rect.h);
  return boxes.filter((box) => box.x < right && box.x + box.w > left && box.y < bottom && box.y + box.h > top).map((box) => box.id);
}

export function alignmentGuides(boxes: UiBox[], movingId: string, x: number, y: number, threshold = 4): { x?: number; y?: number } {
  const guides: { x?: number; y?: number } = {};
  for (const box of boxes) {
    if (box.id === movingId) continue;
    if (Math.abs(box.x - x) <= threshold) guides.x = box.x;
    if (Math.abs(box.y - y) <= threshold) guides.y = box.y;
  }
  return guides;
}

export const builtinControls: UiControlInfo[] = [
  control("Robust.Client.UserInterface.Controls.BoxContainer", "Box Container", "Layout", true, [prop("Orientation", "Enum", ["Vertical", "Horizontal"]), prop("SeparationOverride", "Integer"), ...layoutProps()]),
  control("Robust.Client.UserInterface.Controls.GridContainer", "Grid Container", "Layout", true, [prop("Columns", "Integer"), ...layoutProps()]),
  control("Robust.Client.UserInterface.Controls.LayoutContainer", "Layout Container", "Layout", true, layoutProps()),
  control("Robust.Client.UserInterface.Controls.ScrollContainer", "Scroll Container", "Layout", true, layoutProps()),
  control("Robust.Client.UserInterface.Controls.PanelContainer", "Panel Container", "Layout", true, layoutProps()),
  control("Robust.Client.UserInterface.Controls.Button", "Button", "Input", true, [prop("Text", "Text"), prop("Disabled", "Boolean"), prop("ToggleMode", "Boolean"), ...layoutProps()], ["OnPressed"]),
  control("Robust.Client.UserInterface.Controls.LineEdit", "Line Edit", "Input", false, [prop("Text", "Text"), prop("Editable", "Boolean"), ...layoutProps()], ["OnTextChanged", "OnTextEntered"]),
  control("Robust.Client.UserInterface.Controls.Label", "Label", "Display", false, [prop("Text", "Text"), ...layoutProps()]),
  control("Robust.Client.UserInterface.Controls.TextureRect", "Texture Rect", "Display", false, [prop("Texture", "Resource"), ...layoutProps()]),
  control("Robust.Client.UserInterface.Controls.ProgressBar", "Progress Bar", "Display", false, [prop("Value", "Float"), prop("MaxValue", "Float"), ...layoutProps()]),
  control("Robust.Client.UserInterface.Controls.ItemList", "Item List", "Display", false, [prop("ItemSeparation", "Integer"), ...layoutProps()], ["OnItemSelected"])
];

function control(typeId: string, displayName: string, category: string, canHaveChildren: boolean, properties: UiPropertyInfo[], events: string[] = []): UiControlInfo {
  return {
    typeId,
    displayName,
    category,
    canHaveChildren,
    properties,
    events: events.map((name) => ({ name, eventType: name, payload: [] }))
  };
}

function prop(name: string, editorKind: string, enumValues?: string[]): UiPropertyInfo {
  return { name, typeName: editorKind, editorKind, canWrite: true, category: editorKind === "Text" ? "Content" : "Layout", enumValues };
}

function layoutProps(): UiPropertyInfo[] {
  return [prop("Visible", "Boolean"), prop("MinWidth", "Float"), prop("MinHeight", "Float"), prop("HorizontalExpand", "Boolean"), prop("VerticalExpand", "Boolean")];
}
