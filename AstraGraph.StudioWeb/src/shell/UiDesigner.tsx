import { useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { elementStyle } from "./uiHtml";
import { alignmentGuides, boxesInRect, builtinControls, canParent, findNode, findParent, insertChild, layoutTree, nodeType, remapIds, remapSubtree, removeNode, reorder, shortType, snapValue, updateNode, type UiControlInfo, type UiDocumentModel, type UiNode, type UiPageModel, type UiPropertyInfo, type UiTokenModel } from "./uiTree";
import { buildLogicGraphs } from "./uiLogic";

const tabs = ["Design", "Logic", "State", "Contract", "Styles", "Preview", "Diagnostics"] as const;

function clampSize(value: number, fallback: number) {
  if (!Number.isFinite(value)) return fallback;
  return Math.min(4096, Math.max(64, Math.round(value)));
}

function newId() {
  return crypto.randomUUID();
}

function leaf(typeId: string, text?: string): UiNode {
  const name = shortType(typeId);
  return {
    id: newId(),
    elementType: name === "PanelContainer" ? "Panel" : name,
    controlTypeId: typeId,
    name,
    text,
    children: [],
    visible: true,
    enabled: true,
    styleClasses: [],
    properties: {},
    valueSource: "Constant"
  };
}

function emptyDocument(kind: "UI" | "BUI"): UiDocumentModel {
  const root = leaf("Robust.Client.UserInterface.Controls.BoxContainer");
  root.name = kind === "BUI" ? "Mothroach" : "Window";
  root.orientation = "Vertical";
  const label = leaf("Robust.Client.UserInterface.Controls.Label", kind === "BUI" ? "Result" : "Window");
  const input = leaf("Robust.Client.UserInterface.Controls.LineEdit", "");
  const button = leaf("Robust.Client.UserInterface.Controls.Button", kind === "BUI" ? "Submit" : "Button");
  root.children = kind === "BUI" ? [label, input, button] : [label];
  return {
    id: newId(),
    name: kind === "BUI" ? "Mothroach Converter" : "Window",
    width: 480,
    height: 320,
    documentKind: kind,
    bindings: kind === "BUI"
      ? [
          { bindingId: newId(), elementId: input.id, targetProperty: "Text", stateVariable: "inputText", direction: "TwoWay" },
          { bindingId: newId(), elementId: label.id, targetProperty: "Text", stateVariable: "result", direction: "OneWay" }
        ]
      : [],
    events: kind === "BUI" ? [{ subscriptionId: newId(), elementId: button.id, eventName: "OnPressed", targetAction: "Submit" }] : [],
    logic: kind === "BUI"
      ? [
          { id: newId(), kind: "OnEvent", elementId: button.id, eventName: "OnPressed" },
          { id: newId(), kind: "SendAction", elementId: button.id, actionName: "Submit", stateVariable: "inputText" },
          { id: newId(), kind: "SetState", actionName: "Submit", stateVariable: "result", propertyName: "text" }
        ]
      : [],
    localState: kind === "BUI" ? { inputText: "", result: "" } : {},
    stateVariables: kind === "BUI"
      ? [
          { id: newId(), name: "inputText", typeName: "string", scope: "Local", defaultValue: "" },
          { id: newId(), name: "result", typeName: "string", scope: "Server", defaultValue: "" }
        ]
      : [],
    root
  };
}

function templateDocument(kind: "mothroach" | "vehicle" | "surgery" | "form" | "inventory"): UiDocumentModel {
  const base = emptyDocument(kind === "form" ? "UI" : "BUI");
  if (kind === "mothroach") {
    base.name = "Mothroach Converter";
    base.root.children[0].text = "Spawn count";
    return base;
  }
  if (kind === "vehicle") {
    base.name = "Vehicle Maintenance";
    base.root.controlTypeId = "Robust.Client.UserInterface.Controls.GridContainer";
    base.root.elementType = "GridContainer";
    base.root.properties = { Columns: "2" };
    return base;
  }
  if (kind === "surgery") {
    base.name = "Surgery";
    base.root.children.push(leaf("Robust.Client.UserInterface.Controls.ProgressBar"));
    base.root.children.push(leaf("Robust.Client.UserInterface.Controls.ItemList"));
    return base;
  }
  if (kind === "inventory") {
    base.name = "Inventory";
    base.root.children = [leaf("Robust.Client.UserInterface.Controls.ItemList")];
    return base;
  }
  base.name = "Form";
  return base;
}

function boundText(node: UiNode, local: Record<string, string>, bindings: UiDocumentModel["bindings"]): string {
  const binding = (bindings ?? []).find((item) => item.elementId === node.id && item.targetProperty === "Text");
  if (binding && local[binding.stateVariable] != null && local[binding.stateVariable] !== "") return local[binding.stateVariable];
  return node.text ?? node.properties?.Text ?? "";
}

function ControlView(props: {
  node: UiNode;
  document: UiDocumentModel;
  selected: string;
  onSelect: (id: string) => void;
  onDrop: (parentId: string, index: number, typeId?: string, elementId?: string) => void;
}) {
  const text = boundText(props.node, props.document.localState ?? {}, props.document.bindings);
  const type = shortType(nodeType(props.node));
  const selected = props.node.id === props.selected;
  const horizontal = props.node.orientation === "Horizontal";
  const columns = Number(props.node.properties?.Columns ?? 1) || 1;
  const style: CSSProperties = {
    minWidth: props.node.minWidth ?? undefined,
    minHeight: props.node.minHeight ?? undefined,
    opacity: props.node.visible === false ? 0.4 : 1,
    outline: selected ? "1px solid var(--accent)" : "1px dashed transparent",
    margin: 4,
    padding: type.includes("Container") || type === "BoxContainer" ? 6 : 0
  };
  const accept = (event: React.DragEvent, index: number) => {
    event.preventDefault();
    event.stopPropagation();
    props.onDrop(props.node.id, index, event.dataTransfer.getData("application/x-astra-control"), event.dataTransfer.getData("application/x-astra-element"));
  };
  const children = props.node.children.map((child, index) => (
    <div key={child.id} className="ui-slot" onDragOver={(event) => event.preventDefault()} onDrop={(event) => accept(event, index)}>
      <ControlView node={child} document={props.document} selected={props.selected} onSelect={props.onSelect} onDrop={props.onDrop} />
    </div>
  ));
  const body = (
    <div
      className={selected ? "ui-node selected" : "ui-node"}
      style={style}
      onClick={(event) => { event.stopPropagation(); props.onSelect(props.node.id); }}
      draggable
      onDragStart={(event) => event.dataTransfer.setData("application/x-astra-element", props.node.id)}
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => accept(event, props.node.children.length)}
    >
      {type === "Button" ? <button type="button" disabled={props.node.enabled === false}>{text}</button> : null}
      {type === "LineEdit" ? <input aria-label={props.node.name || "LineEdit"} defaultValue={text} disabled={props.node.enabled === false} /> : null}
      {type === "Label" ? <span>{text}</span> : null}
      {type === "ProgressBar" ? <progress aria-label={props.node.name || "ProgressBar"} value={Number(props.node.properties?.Value ?? 40)} max={100} /> : null}
      {type !== "Button" && type !== "LineEdit" && type !== "Label" && type !== "ProgressBar" ? (
        <div className={type === "GridContainer" ? "ui-grid" : "ui-box"} style={{ display: "grid", gridTemplateColumns: type === "GridContainer" ? `repeat(${columns}, minmax(0, 1fr))` : undefined, flexDirection: horizontal ? "row" : "column", gap: 4 }}>
          <span className="muted">{type}{horizontal ? " →" : ""}</span>
          {children}
          <div className="ui-drop">drop here</div>
        </div>
      ) : null}
    </div>
  );
  return body;
}

export function UiDesigner(props: {
  documents: UiDocumentModel[];
  layout?: string;
  catalog?: UiControlInfo[];
  onSave: (document: UiDocumentModel) => void;
  onDiagnose?: (document: UiDocumentModel) => void;
  styles?: string[];
  assets?: string[];
  onPatch?: (documentId: string, operations: { kind: string; elementId: string; propertyName?: string; value?: string }[]) => void;
  onPreview?: (document: UiDocumentModel) => void | Promise<{ mode?: string; xaml?: string; framePng?: string } | void>;
  onOpenGraph?: (graph: ReturnType<typeof buildLogicGraphs>["client"]) => void;
}) {
  const catalog = props.catalog && props.catalog.length > 0 ? props.catalog : builtinControls;
  const [draft, setDraft] = useState<UiDocumentModel | null>(props.documents[0] ?? null);
  const [past, setPast] = useState<UiDocumentModel[]>([]);
  const [future, setFuture] = useState<UiDocumentModel[]>([]);
  const [selected, setSelected] = useState("");
  const [tab, setTab] = useState<(typeof tabs)[number]>("Design");
  const [query, setQuery] = useState("");
  const [layerQuery, setLayerQuery] = useState("");
  const [zoom, setZoom] = useState(1);
  const [locale, setLocale] = useState("en");
  const [previewNote, setPreviewNote] = useState("");
  const [snap] = useState(8);
  const [marquee, setMarquee] = useState<{ x: number; y: number; w: number; h: number } | null>(null);
  const [guides, setGuides] = useState<{ x?: number; y?: number }>({});
  const [clipboard, setClipboard] = useState<UiNode | null>(null);
  const [selection, setSelection] = useState<string[]>([]);
  const [surface, setSurface] = useState<{ mode: string; framePng: string }>({ mode: "", framePng: "" });
  const [library, setLibrary] = useState<"controls" | "sprites" | "frames">("controls");
  const [inspectTab, setInspectTab] = useState<"properties" | "code">("properties");
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const liveDraft = useRef<UiDocumentModel | null>(null);
  const spacePan = useRef(false);

  useEffect(() => {
    setDraft(props.documents[0] ?? null);
  }, [props.documents]);

  async function refreshSurface(document: UiDocumentModel) {
    try {
      const surfaceResult = await props.onPreview?.(document);
      if (!surfaceResult) return;
      setSurface({ mode: surfaceResult.mode ?? "", framePng: surfaceResult.framePng ?? "" });
    } catch {
      setSurface({ mode: "offline", framePng: "" });
    }
  }

  useEffect(() => {
    if (!draft) return;
    void refreshSurface(draft);
  }, [draft?.id]);

  function syncActive(document: UiDocumentModel): UiDocumentModel {
    if (!document.pages?.length) return document;
    const id = document.activePageId ?? document.pages[0].id;
    return {
      ...document,
      activePageId: id,
      pages: document.pages.map((page) => page.id === id
        ? { ...page, name: document.name, width: document.width, height: document.height, root: document.root }
        : page)
    };
  }

  function commit(next: UiDocumentModel, baseline: UiDocumentModel | null = draft) {
    if (!baseline) return;
    const synced = syncActive(next);
    setPast((items) => [...items.slice(-49), baseline]);
    setFuture([]);
    setDraft(synced);
    void refreshSurface(synced);
  }

  function undo() {
    setPast((items) => {
      const previous = items[items.length - 1];
      if (!previous || !draft) return items;
      setFuture((queue) => [draft, ...queue]);
      setDraft(previous);
      return items.slice(0, -1);
    });
  }

  function redo() {
    setFuture((items) => {
      const next = items[0];
      if (!next || !draft) return items;
      setPast((queue) => [...queue, draft]);
      setDraft(next);
      return items.slice(1);
    });
  }

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.code === "Space") {
        const tag = (event.target as HTMLElement | null)?.tagName;
        if (tag !== "INPUT" && tag !== "TEXTAREA") event.preventDefault();
        spacePan.current = event.type === "keydown";
      }
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "z") {
        event.preventDefault();
        if (event.shiftKey) redo();
        else undo();
      }
    }
    window.addEventListener("keydown", onKey);
    window.addEventListener("keyup", onKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("keyup", onKey);
    };
  });

  const grouped = useMemo(() => {
    const groups = new Map<string, UiControlInfo[]>();
    for (const control of catalog) {
      if (query && !control.displayName.toLowerCase().includes(query.toLowerCase()) && !shortType(control.typeId).toLowerCase().includes(query.toLowerCase())) continue;
      const list = groups.get(control.category) ?? [];
      list.push(control);
      groups.set(control.category, list);
    }
    return [...groups.entries()];
  }, [catalog, query]);

  if (!draft) {
    return (
      <div className="ui-designer studio-viewport">
        <div className="studio-empty">
          <strong>Design</strong>
          <p>Pick a template or an empty frame. The canvas, layers, and inspector open beside it.</p>
          <div className="studio-empty-actions">
            <button type="button" className="primary" onClick={() => { const created = emptyDocument("UI"); setSelected(created.root.id); setDraft(created); }}>New UI</button>
            <button type="button" onClick={() => { const created = emptyDocument("BUI"); setSelected(created.root.id); setDraft(created); }}>New BUI</button>
            <button type="button" onClick={() => { const created = templateDocument("mothroach"); setSelected(created.root.id); setDraft(created); }}>Mothroach</button>
            <button type="button" onClick={() => { const created = templateDocument("vehicle"); setSelected(created.root.id); setDraft(created); }}>Vehicle</button>
            <button type="button" onClick={() => { const created = templateDocument("surgery"); setSelected(created.root.id); setDraft(created); }}>Surgery</button>
            <button type="button" onClick={() => { const created = templateDocument("form"); setSelected(created.root.id); setDraft(created); }}>Form</button>
            <button type="button" onClick={() => { const created = templateDocument("inventory"); setSelected(created.root.id); setDraft(created); }}>Inventory</button>
          </div>
        </div>
      </div>
    );
  }

  const current = findNode(draft.root, selected) ?? draft.root;
  const info = catalog.find((item) => item.typeId === nodeType(current) || shortType(item.typeId) === current.elementType);
  const bindings = draft.bindings ?? [];
  const events = draft.events ?? [];
  const variables = draft.stateVariables ?? [];

  function place(typeId: string, parentId = current.id, index?: number, point?: { x: number; y: number }) {
    const parent = findNode(draft!.root, parentId);
    if (!parent || parent.locked || !canParent(catalog, parent)) return;
    const child = leaf(typeId, shortType(typeId));
    const free = parent.children.some((item) => item.properties?.["editor.x"] != null && item.properties["editor.x"] !== "");
    if (point && free) {
      child.properties = { "editor.x": String(Math.round(point.x)), "editor.y": String(Math.round(point.y)) };
    }
    commit({ ...draft!, root: insertChild(draft!.root, parentId, child, index) });
    setSelected(child.id);
  }

  function placeSprite(sprite: { rsi: string; state: string; file?: string; url: string }, parentId?: string, index?: number) {
    if (!draft) return;
    const hinted = parentId ? findNode(draft.root, parentId) : undefined;
    const parent = hinted && canParent(catalog, hinted) ? hinted : canParent(catalog, current) ? current : findParent(draft.root, current.id) ?? draft.root;
    if (!canParent(catalog, parent)) return;
    const fileName = sprite.file?.split("/").pop() ?? `${sprite.state}.png`;
    const texture = sprite.url.startsWith("data:") ? sprite.rsi : `${sprite.rsi.replace(/\/$/, "")}/${fileName}`;
    const child = leaf("Robust.Client.UserInterface.Controls.TextureRect", sprite.state);
    child.properties = { Texture: texture, State: sprite.state, SpriteUrl: sprite.url, "editor.width": "48", "editor.height": "48" };
    child.minWidth = 48;
    child.minHeight = 48;
    commit({ ...draft, root: insertChild(draft.root, parent.id, child, index) });
    setSelected(child.id);
  }

  function addPage() {
    if (!draft) return;
    const existing = draft.pages?.length
      ? syncActive(draft).pages ?? []
      : [{ id: draft.id, name: draft.name, width: draft.width, height: draft.height, root: draft.root }];
    const root = leaf("Robust.Client.UserInterface.Controls.BoxContainer");
    root.name = "Window";
    root.orientation = "Vertical";
    const page: UiPageModel = { id: newId(), name: `Frame ${existing.length + 1}`, width: draft.width, height: draft.height, root };
    commit({ ...draft, pages: [...existing, page], activePageId: page.id, name: page.name, root });
    setSelected(root.id);
  }

  function openPage(id: string) {
    if (!draft) return;
    const synced = syncActive(draft);
    const page = synced.pages?.find((item) => item.id === id);
    if (!page) return;
    commit({ ...synced, activePageId: page.id, name: page.name, width: page.width, height: page.height, root: page.root });
    setSelected(page.root.id);
  }

  function alignSelection(axis: "x" | "y") {
    if (!draft) return;
    const ids = selection.length > 1 ? selection : [current.id];
    const key = axis === "x" ? "editor.x" : "editor.y";
    const values = ids.map((id) => Number(findNode(draft.root, id)?.properties?.[key] ?? NaN)).filter((value) => Number.isFinite(value));
    if (values.length === 0) return;
    const target = Math.min(...values);
    let root = draft.root;
    for (const id of ids) {
      const node = findNode(root, id);
      if (!node || node.id === draft.root.id) continue;
      root = updateNode(root, id, { properties: { ...(node.properties ?? {}), [key]: String(target) } });
    }
    commit({ ...draft, root });
  }

  function onDrop(parentId: string, index: number, typeId?: string, elementId?: string, point?: { x: number; y: number }) {
    if (!draft) return;
    const parent = findNode(draft.root, parentId);
    if (!parent || !canParent(catalog, parent)) return;
    if (typeId) {
      place(typeId, parentId, index, point);
      return;
    }
    if (!elementId || elementId === parentId) return;
    const moving = findNode(draft.root, elementId);
    if (!moving) return;
    const owner = findParent(draft.root, elementId);
    if (!owner) return;
    const from = owner.children.findIndex((child) => child.id === elementId);
    let next = removeNode(draft.root, elementId);
    next = insertChild(next, parentId, moving, index);
    if (owner.id === parentId && from < index) {
      next = reorder(draft.root, parentId, from, Math.max(0, index - 1));
    }
    commit({ ...draft, root: next });
  }

  function patch(partial: Partial<UiNode>) {
    commit({ ...draft, root: updateNode(draft.root, current.id, partial) });
  }

  function setProperty(name: string, value: string) {
    const properties = { ...(current.properties ?? {}) };
    if (value === "") delete properties[name];
    else properties[name] = value;
    const partial: Partial<UiNode> = { properties };
    if (name === "Text") partial.text = value;
    if (name === "Visible") partial.visible = value !== "false";
    if (name === "MinWidth") partial.minWidth = value === "" ? null : Number(value);
    if (name === "MinHeight") partial.minHeight = value === "" ? null : Number(value);
    if (name === "Orientation") partial.orientation = value;
    patch(partial);
  }

  return (
    <div className="ui-designer" tabIndex={0} onKeyDown={(event) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "d") {
        event.preventDefault();
        const copy = remapIds(current, newId);
        const parent = findParent(draft.root, current.id) ?? draft.root;
        commit({ ...draft, root: insertChild(draft.root, parent.id, copy) });
      }
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "c") {
        event.preventDefault();
        setClipboard(current);
      }
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "v" && clipboard) {
        event.preventDefault();
        const copied = remapSubtree(draft, clipboard, newId);
        const parent = findParent(draft.root, current.id) ?? draft.root;
        commit({ ...draft, root: insertChild(draft.root, parent.id, copied.node), bindings: [...bindings, ...copied.bindings], events: [...events, ...copied.events] });
        setSelected(copied.node.id);
      }
      const tag = (event.target as HTMLElement).tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;
      if (!event.key.startsWith("Arrow") || current.id === draft.root.id) return;
      event.preventDefault();
      const step = event.shiftKey ? snap : 1;
      const parent = findParent(draft.root, current.id);
      const hasBox = current.properties?.["editor.x"] != null && current.properties["editor.x"] !== "";
      if (!hasBox && parent && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
        const from = parent.children.findIndex((child) => child.id === current.id);
        const to = event.key === "ArrowUp" ? from - 1 : from + 1;
        if (from >= 0 && to >= 0 && to < parent.children.length) commit({ ...draft, root: reorder(draft.root, parent.id, from, to) });
        return;
      }
      if (!hasBox) return;
      const dx = event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0;
      const dy = event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0;
      patch({
        properties: {
          ...(current.properties ?? {}),
          "editor.x": String(Math.round(Number(current.properties?.["editor.x"]) + dx)),
          "editor.y": String(Math.round(Number(current.properties?.["editor.y"] ?? 0) + dy))
        }
      });
    }}>
      <div className="dock-bar studio-modes">
        {tabs.map((name) => <button key={name} type="button" className={name === tab ? "active" : ""} onClick={() => setTab(name)}>{name}</button>)}
        <span className="grow" />
        <button type="button" onClick={undo}>Undo</button>
        <button type="button" onClick={redo}>Redo</button>
        <button type="button" onClick={() => props.onSave(draft)}>Save</button>
      </div>
      {tab === "Design" ? (
        <div className="ui-body">
          <aside className="studio-side">
            <div className="library-switch">
              <button type="button" className={library === "controls" ? "active" : ""} onClick={() => setLibrary("controls")}>Controls</button>
              <button type="button" className={library === "sprites" ? "active" : ""} onClick={() => setLibrary("sprites")}>Sprites</button>
              <button type="button" className={library === "frames" ? "active" : ""} onClick={() => setLibrary("frames")}>Frames</button>
            </div>
            {library === "controls" ? <input className="studio-search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search controls" /> : null}
            {library === "sprites" ? <SpriteBrowser onPlace={(sprite) => placeSprite(sprite)} /> : null}
            {library === "frames" ? (
              <div className="studio-assets">
                {(draft.pages?.length ? draft.pages : [{ id: draft.id, name: draft.name, width: draft.width, height: draft.height, root: draft.root }]).map((page) => (
                  <button key={page.id} type="button" className={page.id === (draft.activePageId ?? draft.id) ? "studio-asset active" : "studio-asset"} onClick={() => openPage(page.id)}>{page.name}</button>
                ))}
                <button type="button" className="studio-asset" onClick={addPage}>New frame</button>
                <div className="studio-section">Components</div>
                {(draft.components ?? []).map((component) => (
                  <button key={component.id} type="button" className="studio-asset" onClick={() => {
                    const copy = remapIds(component.root, newId);
                    const parent = canParent(catalog, current) ? current : findParent(draft.root, current.id) ?? draft.root;
                    commit({ ...draft, root: insertChild(draft.root, parent.id, copy) });
                    setSelected(copy.id);
                  }}>{component.name}</button>
                ))}
                {(draft.components ?? []).length === 0 ? <p className="muted">Select an element and save it as a component.</p> : null}
              </div>
            ) : null}
            {library === "controls" ? <div className="studio-assets">
              {grouped.map(([category, controls]) => (
                <div key={category}>
                  <div className="studio-section">{category}</div>
                  {controls.map((control) => (
                    <button
                      key={control.typeId}
                      type="button"
                      className="studio-asset"
                      draggable
                      onDragStart={(event) => {
                        event.dataTransfer.setData("application/x-astra-control", control.typeId);
                        event.dataTransfer.setData("text/plain", control.typeId);
                        event.dataTransfer.effectAllowed = "copy";
                      }}
                      onClick={() => place(control.typeId)}
                    >{control.displayName}</button>
                  ))}
                </div>
              ))}
            </div> : null}
            <div className="studio-section">Layers</div>
            <input className="studio-search" value={layerQuery} onChange={(event) => setLayerQuery(event.target.value)} placeholder="Search layers" />
            <div className="studio-layers">
              <Layer node={draft.root} selected={selected || draft.root.id} query={layerQuery} depth={0} onSelect={setSelected} onToggle={(id, field) => {
                const node = findNode(draft.root, id);
                if (!node) return;
                commit({ ...draft, root: updateNode(draft.root, id, field === "visible" ? { visible: node.visible === false } : { locked: !node.locked }) });
              }} onDrop={(targetId, typeId, elementId) => {
                const target = findNode(draft.root, targetId) ?? draft.root;
                const container = canParent(catalog, target) ? target : findParent(draft.root, target.id) ?? draft.root;
                const index = container.id === target.id ? container.children.length : Math.max(0, container.children.findIndex((child) => child.id === target.id) + 1);
                onDrop(container.id, index, typeId, elementId);
              }} />
            </div>
          </aside>
          <main className="studio-viewport" onDragOver={(event) => { event.preventDefault(); event.dataTransfer.dropEffect = "copy"; }} onPointerDown={(event) => {
            if (event.button !== 1 && !spacePan.current) return;
            event.preventDefault();
            const origin = { x: event.clientX, y: event.clientY, pan };
            const move = (ev: PointerEvent) => setPan({ x: origin.pan.x + ev.clientX - origin.x, y: origin.pan.y + ev.clientY - origin.y });
            const up = () => {
              window.removeEventListener("pointermove", move);
              window.removeEventListener("pointerup", up);
            };
            window.addEventListener("pointermove", move);
            window.addEventListener("pointerup", up);
          }}>
            <div className="studio-crumbs">
              <span>{breadcrumb(draft.root, current.id).join("  /  ")}</span>
              <span className="grow" />
              {(draft.pages ?? [{ id: draft.id, name: draft.name, width: draft.width, height: draft.height, root: draft.root }]).map((page) => (
                <button key={page.id} type="button" className={page.id === (draft.activePageId ?? draft.id) ? "page-chip active" : "page-chip"} onClick={() => openPage(page.id)}>{page.name}</button>
              ))}
              <button type="button" className="page-chip" onClick={addPage}>+</button>
            </div>
            <div className="studio-world">
              <div className="studio-frame-wrap" style={{ transform: `translate(${pan.x}px, ${pan.y}px)` }}>
                <div className={current.id === draft.root.id ? "studio-frame-label selected" : "studio-frame-label"}>{draft.name}</div>
            <DesignCanvas
              document={draft}
              selected={selection.length > 0 ? selection : [selected || draft.root.id]}
              zoom={zoom}
              snap={snap}
              marquee={marquee}
              guides={guides}
              onSelect={(ids) => { setSelection(ids); setSelected(ids[0] ?? draft.root.id); }}
              onMarquee={setMarquee}
              onGuides={setGuides}
              onMove={(id, x, y, done, baseline, pins) => {
                const source = liveDraft.current ?? (done ? draft : baseline);
                let root = source.root;
                if (pins) {
                  for (const pin of pins.nodes) {
                    const pinned = findNode(root, pin.id);
                    if (!pinned) continue;
                    const moving = pin.id === id;
                    root = updateNode(root, pin.id, {
                      properties: {
                        ...(pinned.properties ?? {}),
                        "editor.x": String(Math.round(moving ? x : pin.x)),
                        "editor.y": String(Math.round(moving ? y : pin.y)),
                        "editor.width": String(Math.max(16, Math.round(pin.w))),
                        "editor.height": String(Math.max(16, Math.round(pin.h)))
                      }
                    });
                  }
                  const parent = findNode(root, pins.parentId);
                  if (parent) {
                    root = updateNode(root, parent.id, {
                      minWidth: Math.max(parent.minWidth ?? 0, Math.round(pins.parentWidth)),
                      minHeight: Math.max(parent.minHeight ?? 0, Math.round(pins.parentHeight))
                    });
                  }
                }
                const node = findNode(root, id);
                if (!node) return;
                root = updateNode(root, id, {
                  properties: { ...(node.properties ?? {}), "editor.x": String(Math.round(x)), "editor.y": String(Math.round(y)) }
                });
                const next = { ...source, root };
                liveDraft.current = next;
                if (done) {
                  liveDraft.current = null;
                  commit(next, baseline);
                } else setDraft(next);
              }}
              onDrop={onDrop}
              onResizeNode={(id, x, y, width, height, done, baseline) => {
                const source = liveDraft.current ?? baseline;
                const node = findNode(source.root, id);
                if (!node) return;
                const properties = {
                  ...(node.properties ?? {}),
                  "editor.x": String(Math.round(x)),
                  "editor.y": String(Math.round(y)),
                  "editor.width": String(Math.max(16, Math.round(width))),
                  "editor.height": String(Math.max(16, Math.round(height)))
                };
                const next = { ...source, root: updateNode(source.root, id, { properties, minWidth: Number(properties["editor.width"]), minHeight: Number(properties["editor.height"]) }) };
                if (done) {
                  liveDraft.current = null;
                  commit(next, baseline);
                } else {
                  liveDraft.current = next;
                  setDraft(next);
                }
              }}
              onResize={(width, height, done, baseline) => {
                const next = { ...baseline, width, height };
                if (done) commit(next, baseline);
                else setDraft(next);
              }}
              onSprite={placeSprite}
              onReorder={(id, parentId, index) => {
                const owner = findParent(draft.root, id);
                if (!owner || owner.id !== parentId) return;
                const from = owner.children.findIndex((child) => child.id === id);
                if (from < 0) return;
                const to = Math.max(0, from < index ? index - 1 : index);
                if (to === from) return;
                commit({ ...draft, root: reorder(draft.root, parentId, from, to) });
              }}
              onDuplicate={(id) => {
                const node = findNode(draft.root, id);
                const parent = findParent(draft.root, id);
                if (!node || !parent) return;
                const copy = remapIds(node, newId);
                if (node.properties?.["editor.x"]) {
                  copy.properties = { ...(copy.properties ?? {}), "editor.x": String(Number(node.properties["editor.x"]) + 16), "editor.y": String(Number(node.properties["editor.y"] ?? 0) + 16) };
                }
                commit({ ...draft, root: insertChild(draft.root, parent.id, copy) });
                setSelected(copy.id);
              }}
              framePng={surface.framePng}
              mode={surface.mode}
              knownTypes={new Set(catalog.flatMap((item) => [item.typeId, shortType(item.typeId), item.displayName]))}
            />
              </div>
            </div>
            <CanvasMinimap document={draft} zoom={zoom} />
            <div className="studio-zoom" onClick={(event) => event.stopPropagation()}>
              <button type="button" onClick={() => setZoom((value) => Math.max(0.25, Math.round((value - 0.25) * 4) / 4))}>−</button>
              <span>{Math.round(zoom * 100)}%</span>
              <button type="button" onClick={() => setZoom((value) => Math.min(2, Math.round((value + 0.25) * 4) / 4))}>+</button>
              <button type="button" onClick={() => { setZoom(1); setPan({ x: 0, y: 0 }); }}>Fit frame</button>
              <button type="button" onClick={() => alignSelection("x")}>Align left</button>
              <button type="button" onClick={() => alignSelection("y")}>Align top</button>
              <label>W <input type="number" min={64} max={4096} defaultValue={draft.width} key={`w-${draft.width}`} onBlur={(event) => commit({ ...draft, width: clampSize(Number(event.target.value), draft.width) })} /></label>
              <label>H <input type="number" min={64} max={4096} defaultValue={draft.height} key={`h-${draft.height}`} onBlur={(event) => commit({ ...draft, height: clampSize(Number(event.target.value), draft.height) })} /></label>
            </div>
          </main>
          <aside className="studio-side studio-inspect">
            <div className="inspect-switch">
              <button type="button" className={inspectTab === "properties" ? "active" : ""} onClick={() => setInspectTab("properties")}>Properties</button>
              <button type="button" className={inspectTab === "code" ? "active" : ""} onClick={() => setInspectTab("code")}>Code</button>
            </div>
            <div className="studio-inspect-title">{current.name || info?.displayName || shortType(nodeType(current))}</div>
            <div className="studio-inspect-type">{info?.displayName ?? shortType(nodeType(current))}</div>
            {inspectTab === "code" ? (
              <div className="studio-code">
                <label>Element CSS
                  <textarea rows={6} placeholder="color: #edbc63; font-size: 18px;" value={current.properties?.Style ?? ""} onChange={(event) => setProperty("Style", event.target.value)} />
                </label>
                <label>Page CSS
                  <textarea rows={6} placeholder=".danger { color: #e55; }" value={draft.css ?? ""} onChange={(event) => commit({ ...draft, css: event.target.value })} />
                </label>
                <label>JavaScript
                  <textarea rows={8} placeholder={"astraSend('Submit', {});"} value={draft.script ?? ""} onChange={(event) => commit({ ...draft, script: event.target.value })} />
                </label>
                <p className="muted">The script is included in the window page. Record an action here, or open the graph separately.</p>
                <button type="button" onClick={() => {
                  const eventName = info?.events[0]?.name ?? "OnPressed";
                  if (events.some((item) => item.elementId === current.id)) return;
                  commit({
                    ...draft,
                    events: [...events, { subscriptionId: newId(), elementId: current.id, eventName, targetAction: "Submit" }],
                    logic: [...(draft.logic ?? []), { id: newId(), kind: "OnEvent", elementId: current.id, eventName }, { id: newId(), kind: "SendAction", elementId: current.id, actionName: "Submit" }]
                  });
                }}>Record action</button>
                <button type="button" onClick={() => {
                  const eventName = info?.events[0]?.name ?? "OnPressed";
                  const exists = events.some((item) => item.elementId === current.id);
                  const next = exists ? draft : {
                    ...draft,
                    events: [...events, { subscriptionId: newId(), elementId: current.id, eventName, targetAction: "Submit" }],
                    logic: [...(draft.logic ?? []), { id: newId(), kind: "OnEvent", elementId: current.id, eventName }, { id: newId(), kind: "SendAction", elementId: current.id, actionName: "Submit" }]
                  };
                  if (!exists) commit(next);
                  props.onOpenGraph?.(buildLogicGraphs(next).client);
                }}>Open logic graph</button>
              </div>
            ) : null}
            {inspectTab === "properties" ? (<>
            <label className="studio-prop">Name
              <input value={current.name ?? ""} onChange={(event) => patch({ name: event.target.value })} />
            </label>
            {[...groupProperties(info?.properties ?? [])].map(([category, properties]) => (
              <div key={category}>
                <div className="studio-section">{category}</div>
                {properties.map((property) => (
                  <label key={property.name} className="studio-prop">{property.name}
                    {property.editorKind === "Boolean" ? (
                      <input type="checkbox" checked={(current.properties?.[property.name] ?? property.defaultValue ?? "false") === "true" || (property.name === "Visible" && current.visible !== false)} onChange={(event) => setProperty(property.name, event.target.checked ? "true" : "false")} />
                    ) : property.editorKind === "Enum" ? (
                      <select value={current.properties?.[property.name] ?? current.orientation ?? property.enumValues?.[0] ?? ""} onChange={(event) => setProperty(property.name, event.target.value)}>
                        {(property.enumValues ?? []).map((option) => <option key={option}>{option}</option>)}
                      </select>
                    ) : (
                      <input value={property.name === "Text" ? current.text ?? "" : current.properties?.[property.name] ?? ""} onChange={(event) => setProperty(property.name, event.target.value)} />
                    )}
                  </label>
                ))}
              </div>
            ))}
            {canParent(catalog, current) ? (
              <>
                <div className="studio-section">Layout</div>
                <label className="studio-prop">Mode
                  <select value={current.properties?.LayoutMode ?? "Free"} onChange={(event) => setProperty("LayoutMode", event.target.value)}>
                    <option value="Free">Free</option>
                    <option value="Auto">Auto</option>
                  </select>
                </label>
                <label className="studio-prop">Gap
                  <input value={current.properties?.Gap ?? ""} onChange={(event) => setProperty("Gap", event.target.value)} />
                </label>
                <label className="studio-prop">Repeat
                  <input type="number" min={1} max={12} value={current.properties?.Repeat ?? "1"} onChange={(event) => setProperty("Repeat", event.target.value)} />
                </label>
              </>
            ) : null}
            <label className="studio-prop">Pin
              <select value={current.properties?.Pin ?? ""} onChange={(event) => setProperty("Pin", event.target.value)}>
                <option value="">None</option>
                <option value="right">Right</option>
                <option value="bottom">Bottom</option>
                <option value="stretch">Stretch</option>
              </select>
            </label>
            <div className="studio-section">Variant</div>
            <div className="studio-actions">
              {["normal", "hover", "pressed", "disabled"].map((name) => (
                <button key={name} type="button" className={current.properties?.Variant === name ? "active" : ""} onClick={() => setProperty("Variant", name)}>{name}</button>
              ))}
            </div>
            <label className="studio-prop">Open frame
              <select value={current.properties?.OpenPage ?? ""} onChange={(event) => setProperty("OpenPage", event.target.value)}>
                <option value="">Stay</option>
                {(draft.pages ?? []).map((page) => <option key={page.id} value={page.id}>{page.name}</option>)}
              </select>
            </label>
            <div className="studio-section">Style</div>
            <div className="token-row">
              {(draft.tokens ?? []).map((token) => (
                <button key={token.name} type="button" className="token-chip" style={{ background: token.value }} onClick={() => setProperty("Style", `${current.properties?.Style ? `${current.properties.Style}; ` : ""}color: ${token.value}`)}>{token.name}</button>
              ))}
            </div>
            <TokenAdder tokens={draft.tokens ?? []} onAdd={(token) => commit({ ...draft, tokens: [...(draft.tokens ?? []), token] })} />
            <div className="ui-tags">
              {(current.styleClasses ?? []).map((name) => <button key={name} type="button" onClick={() => patch({ styleClasses: (current.styleClasses ?? []).filter((item) => item !== name) })}>{name} ×</button>)}
            </div>
            <StyleAdder classes={current.styleClasses ?? []} onAdd={(name) => patch({ styleClasses: [...(current.styleClasses ?? []), name] })} />
            <div className="studio-section">Events</div>
            {(info?.events ?? []).map((item) => <div key={item.name} className="studio-prop">{item.name}</div>)}
            <div className="studio-actions">
              <button type="button" onClick={() => { const parent = findParent(draft.root, current.id); if (!parent) return; const index = parent.children.findIndex((child) => child.id === current.id); if (index > 0) commit({ ...draft, root: reorder(draft.root, parent.id, index, index - 1) }); }}>Up</button>
              <button type="button" onClick={() => { const parent = findParent(draft.root, current.id); if (!parent) return; const index = parent.children.findIndex((child) => child.id === current.id); if (index >= 0 && index < parent.children.length - 1) commit({ ...draft, root: reorder(draft.root, parent.id, index, index + 1) }); }}>Down</button>
              <button type="button" onClick={() => commit({ ...draft, root: removeNode(draft.root, current.id) })}>Delete</button>
              <button type="button" onClick={() => patch({ locked: !current.locked })}>{current.locked ? "Unlock" : "Lock"}</button>
              <button type="button" onClick={() => commit({
                ...draft,
                components: [...(draft.components ?? []), { id: newId(), name: current.name || shortType(nodeType(current)), root: current }]
              })}>Save component</button>
            </div>
            {(draft.components ?? []).map((component) => (
              <button key={component.id} type="button" onClick={() => {
                const copy = remapIds(component.root, newId);
                const parent = findParent(draft.root, current.id) ?? draft.root;
                commit({ ...draft, root: insertChild(draft.root, parent.id, copy) });
              }}>{component.name}</button>
            ))}
            </>) : null}
          </aside>
        </div>
      ) : null}
      {tab === "Logic" ? (
        <div>
          <p className="muted">Events become graph entry points. Handlers are catalog events, not handwritten nodes.</p>
          <ul className="list">
            {events.filter((item) => item.elementId === current.id).map((item) => <li key={item.subscriptionId}>On {current.name || shortType(nodeType(current))}.{item.eventName} → Send {item.targetAction}</li>)}
          </ul>
          <button type="button" onClick={() => commit({
            ...draft,
            events: [...events, { subscriptionId: newId(), elementId: current.id, eventName: info?.events[0]?.name ?? "OnPressed", targetAction: "Submit" }],
            logic: [
              ...(draft.logic ?? []),
              { id: newId(), kind: "OnEvent", elementId: current.id, eventName: info?.events[0]?.name ?? "OnPressed" },
              { id: newId(), kind: "SendAction", elementId: current.id, actionName: "Submit", stateVariable: bindings.find((item) => item.direction === "TwoWay")?.stateVariable }
            ]
          })}>Create handler</button>
          <div className="ui-row">
            <button type="button" onClick={() => props.onOpenGraph?.(buildLogicGraphs(draft).client)}>Open Logic Graph</button>
            <button type="button" onClick={() => props.onOpenGraph?.(buildLogicGraphs(draft).server)}>Open Server Graph</button>
          </div>
        </div>
      ) : null}
      {tab === "State" ? (
        <StatePanel draft={draft} current={current} variables={variables} bindings={bindings} commit={commit} />
      ) : null}
      {tab === "Contract" ? (
        <div>
          <label className="field">Kind
            <select value={draft.documentKind ?? "UI"} onChange={(event) => commit({ ...draft, documentKind: event.target.value as "UI" | "BUI" })}>
              <option>UI</option>
              <option>BUI</option>
            </select>
          </label>
          <label className="field">Name
            <input value={draft.name} onChange={(event) => commit({ ...draft, name: event.target.value })} />
          </label>
          <p className="muted">Client actions: Submit(inputText). Server notifications stay on the contract. The client only sends the action.</p>
        </div>
      ) : null}
      {tab === "Styles" ? (
        <div>
          <p className="muted">This page CSS is copied into the window. A class on a control becomes a class in the HTML.</p>
          <textarea rows={8} value={draft.css ?? ""} onChange={(event) => commit({ ...draft, css: event.target.value })} />
          <div className="ui-tags">
            {(props.styles ?? ["danger", "windowTitle"]).map((name) => <button key={name} type="button" onClick={() => patch({ styleClasses: [...(current.styleClasses ?? []), name] })}>{name}</button>)}
          </div>
          <label className="field">Texture
            <select value={current.properties?.Texture ?? ""} onChange={(event) => setProperty("Texture", event.target.value)}>
              <option value="">Choose texture</option>
              {(props.assets ?? []).map((path) => <option key={path}>{path}</option>)}
            </select>
          </label>
        </div>
      ) : null}
      {tab === "Diagnostics" ? (
        <div>
          <p className="muted">Runs the UI compiler against this document. {props.layout}</p>
          <button type="button" onClick={() => props.onDiagnose?.(draft)}>Compile UI</button>
        </div>
      ) : null}
      {tab === "Preview" ? (
        <div>
          <p className="badge">Browser Approximation</p>
          <label className="field">Locale
            <select value={locale} onChange={(event) => setLocale(event.target.value)}>
              <option value="en">en</option>
              <option value="ru">ru</option>
              <option value="uk">uk</option>
            </select>
          </label>
          <p className="muted">{locale} · browser mock of the same control types. Robust Preview opens the real client controls.</p>
          <p className="muted">State {Object.entries(draft.localState ?? {}).map(([key, value]) => `${key}=${value || "∅"}`).join(" · ") || "empty"}</p>
          <div className="xaml-surface" style={{ width: Math.min(draft.width, 960), minHeight: 160 }}>
            {surface.framePng ? <img className="xaml-frame" alt="" src={surface.framePng.startsWith("data:") ? surface.framePng : `data:image/png;base64,${surface.framePng}`} /> : <p className="xaml-wait">The same frame as the canvas: a Robust window from XAML, shown here as HTML.</p>}
          </div>
          <button type="button" onClick={() => { props.onPreview?.(draft); setPreviewNote("Preview in Game requested. Headless hot reload runs when the game client is not attached."); }}>Preview in Game</button>
          <p className="muted">{previewNote}</p>
          <span className="badge">Robust Preview</span>
        </div>
      ) : null}
      {tab === "Design" ? null : <button type="button" onClick={() => props.onSave(draft)}>Save UI</button>}
    </div>
  );
}

function DesignCanvas(props: {
  document: UiDocumentModel;
  selected: string[];
  zoom: number;
  snap: number;
  marquee: { x: number; y: number; w: number; h: number } | null;
  guides: { x?: number; y?: number };
  onSelect: (ids: string[]) => void;
  onMarquee: (rect: { x: number; y: number; w: number; h: number } | null) => void;
  onGuides: (guides: { x?: number; y?: number }) => void;
  onMove: (id: string, x: number, y: number, done: boolean, baseline: UiDocumentModel, pins?: { parentId: string; parentWidth: number; parentHeight: number; nodes: { id: string; x: number; y: number; w: number; h: number }[] }) => void;
  onDrop: (parentId: string, index: number, typeId?: string, elementId?: string, point?: { x: number; y: number }) => void;
  onResizeNode: (id: string, x: number, y: number, width: number, height: number, done: boolean, baseline: UiDocumentModel) => void;
  onResize: (width: number, height: number, done: boolean, baseline: UiDocumentModel) => void;
  onSprite: (sprite: { rsi: string; state: string; file?: string; url: string }, parentId?: string, index?: number) => void;
  onReorder: (id: string, parentId: string, index: number) => void;
  onDuplicate: (id: string) => void;
  framePng: string;
  mode: string;
  knownTypes: Set<string>;
}) {
  const boxes = layoutTree(props.document.root, 0, 0, props.document.width, props.snap || 8);
  void props.knownTypes;
  void props.mode;
  const rootBox = boxes.find((box) => box.id === props.document.root.id);
  if (rootBox) {
    rootBox.x = 0;
    rootBox.y = 0;
    rootBox.w = props.document.width;
    rootBox.h = Math.max(props.document.height, rootBox.h);
  }
  const height = Math.max(props.document.height, ...boxes.map((box) => box.y + box.h));
  const stage = useRef<HTMLDivElement>(null);
  const start = useRef<{ x: number; y: number } | null>(null);
  const moveBase = useRef<UiDocumentModel | null>(null);
  function local(event: { clientX: number; clientY: number }) {
    const bounds = stage.current?.getBoundingClientRect();
    if (!bounds) return { x: 0, y: 0 };
    return { x: (event.clientX - bounds.left) / props.zoom, y: (event.clientY - bounds.top) / props.zoom };
  }
  function acceptDrop(event: React.DragEvent<HTMLDivElement>) {
    event.preventDefault();
    event.stopPropagation();
    const spriteRaw = event.dataTransfer.getData("application/x-astra-sprite");
    const typeId = event.dataTransfer.getData("application/x-astra-control") || event.dataTransfer.getData("text/plain");
    const elementId = event.dataTransfer.getData("application/x-astra-element");
    const point = local(event);
    const hit = [...boxes].reverse().find((box) => point.x >= box.x && point.x <= box.x + box.w && point.y >= box.y && point.y <= box.y + box.h);
    const node = hit ? findNode(props.document.root, hit.id) : props.document.root;
    const target = node && (shortType(nodeType(node)).endsWith("Container") || shortType(nodeType(node)) === "Panel")
      ? node
      : findParent(props.document.root, node?.id ?? props.document.root.id) ?? props.document.root;
    const index = target.children.findIndex((child) => child.id === node?.id);
    const at = index >= 0 ? index + 1 : target.children.length;
    if (spriteRaw) {
      try {
        props.onSprite(JSON.parse(spriteRaw) as { rsi: string; state: string; file?: string; url: string }, target.id, at);
      } catch {
        return;
      }
      return;
    }
    props.onDrop(target.id, at, typeId, elementId, { x: point.x, y: point.y });
  }
  return (
    <div
      ref={stage}
      className="ui-stage"
      style={{ transform: `scale(${props.zoom})`, transformOrigin: "top left", width: props.document.width, height, position: "relative" }}
      onMouseDown={(event) => {
        if (event.target !== event.currentTarget) return;
        start.current = local(event);
        props.onMarquee({ ...start.current, w: 0, h: 0 });
      }}
      onMouseMove={(event) => {
        if (!start.current) return;
        const point = local(event);
        props.onMarquee({ x: start.current.x, y: start.current.y, w: point.x - start.current.x, h: point.y - start.current.y });
      }}
      onMouseUp={() => {
        if (props.marquee && (Math.abs(props.marquee.w) > 4 || Math.abs(props.marquee.h) > 4)) props.onSelect(boxesInRect(boxes, props.marquee));
        start.current = null;
        props.onMarquee(null);
      }}
      onDragOver={(event) => { event.preventDefault(); event.dataTransfer.dropEffect = "copy"; }}
      onDrop={acceptDrop}
    >
      <div className="html-page">
        <style>{`.astra-selected { outline: 1px solid #0d99ff; outline-offset: 1px; } ${props.document.css ?? ""}`}</style>
        <HtmlNode
          node={props.document.root}
          document={props.document}
          selected={props.selected}
          zoom={props.zoom}
          onSelect={props.onSelect}
          onMove={(id, x, y, done, pins) => {
            const baseline = moveBase.current ?? props.document;
            if (!moveBase.current) moveBase.current = props.document;
            props.onMove(id, x, y, done, baseline, pins);
            if (done) moveBase.current = null;
          }}
          onResize={(id, x, y, width, height, done) => {
            const baseline = moveBase.current ?? props.document;
            if (!moveBase.current) moveBase.current = props.document;
            props.onResizeNode(id, x, y, width, height, done, baseline);
            if (done) moveBase.current = null;
          }}
          onReorder={props.onReorder}
          onDuplicate={props.onDuplicate}
        />
      </div>
      <button
        type="button"
        className="frame-resize"
        aria-label="Resize window"
        onMouseDown={(event) => {
          event.stopPropagation();
          const origin = { x: event.clientX, y: event.clientY, width: props.document.width, height: props.document.height, baseline: props.document };
          const move = (ev: PointerEvent) => {
            const width = clampSize(origin.width + (ev.clientX - origin.x) / props.zoom, origin.width);
            const height = clampSize(origin.height + (ev.clientY - origin.y) / props.zoom, origin.height);
            props.onResize(width, height, false, origin.baseline);
          };
          const up = (ev: PointerEvent) => {
            window.removeEventListener("pointermove", move);
            window.removeEventListener("pointerup", up);
            const width = clampSize(origin.width + (ev.clientX - origin.x) / props.zoom, origin.width);
            const height = clampSize(origin.height + (ev.clientY - origin.y) / props.zoom, origin.height);
            props.onResize(width, height, true, origin.baseline);
          };
          window.addEventListener("pointermove", move);
          window.addEventListener("pointerup", up);
        }}
      />
      {props.marquee ? <div className="ui-marquee" style={{ left: props.marquee.x, top: props.marquee.y, width: props.marquee.w, height: props.marquee.h }} /> : null}
      {props.guides.x != null ? <div className="ui-guide" style={{ left: props.guides.x }} /> : null}
      {props.guides.y != null ? <div className="ui-guide-y" style={{ top: props.guides.y }} /> : null}
    </div>
  );
}

function CanvasMinimap(props: { document: UiDocumentModel; zoom: number }) {
  const [boxes, setBoxes] = useState<{ id: string; x: number; y: number; w: number; h: number; kind: string }[]>([]);
  useLayoutEffect(() => {
    const stage = document.querySelector(".ui-stage");
    if (!stage) return;
    const origin = stage.getBoundingClientRect();
    const scale = props.zoom || 1;
    setBoxes([...stage.querySelectorAll("[data-astra-id]")].map((node) => {
      const rect = node.getBoundingClientRect();
      return {
        id: node.getAttribute("data-astra-id") ?? "",
        x: (rect.left - origin.left) / scale,
        y: (rect.top - origin.top) / scale,
        w: rect.width / scale,
        h: rect.height / scale,
        kind: node.tagName.toLowerCase()
      };
    }));
  }, [props.document, props.zoom]);
  const frameW = Math.max(props.document.width, 1);
  const frameH = Math.max(props.document.height, ...boxes.map((box) => box.y + box.h), 1);
  const mapW = 164;
  const mapH = 104;
  return (
    <div className="studio-minimap" aria-hidden="true">
      <div className="studio-minimap-frame">
        {boxes.map((box) => (
          <i key={box.id} className={`mm-${box.kind}`} style={{ left: (box.x / frameW) * mapW, top: (box.y / frameH) * mapH, width: Math.max(1, (box.w / frameW) * mapW), height: Math.max(1, (box.h / frameH) * mapH) }} />
        ))}
      </div>
    </div>
  );
}

function breadcrumb(node: UiNode, id: string, trail: string[] = []): string[] {
  const next = [...trail, node.name || shortType(nodeType(node))];
  if (node.id === id) return next;
  for (const child of node.children) {
    const found = breadcrumb(child, id, next);
    if (found.length > 0) return found;
  }
  return [];
}

function Layer(props: { node: UiNode; selected: string; query: string; depth: number; onSelect: (id: string) => void; onToggle: (id: string, field: "visible" | "locked") => void; onDrop: (targetId: string, typeId: string, elementId: string) => void }) {
  const type = shortType(nodeType(props.node));
  const label = props.node.name || type;
  if (props.query && !`${type} ${label}`.toLowerCase().includes(props.query.toLowerCase()) && props.node.children.length === 0) return null;
  return (
    <>
      <button
        type="button"
        draggable
        className={props.node.id === props.selected ? "studio-layer active" : "studio-layer"}
        style={{ paddingLeft: 8 + props.depth * 16 }}
        onClick={() => props.onSelect(props.node.id)}
        onDragStart={(event) => {
          event.stopPropagation();
          event.dataTransfer.setData("application/x-astra-element", props.node.id);
          event.dataTransfer.effectAllowed = "move";
        }}
        onDragOver={(event) => { event.preventDefault(); event.stopPropagation(); event.dataTransfer.dropEffect = "move"; }}
        onDrop={(event) => {
          event.preventDefault();
          event.stopPropagation();
          props.onDrop(props.node.id, event.dataTransfer.getData("application/x-astra-control") || event.dataTransfer.getData("text/plain"), event.dataTransfer.getData("application/x-astra-element"));
        }}
      >
        <span className="studio-layer-name">{label}</span>
        <span className="studio-layer-type">{type}</span>
        <span className="layer-tool" onClick={(event) => { event.stopPropagation(); props.onToggle(props.node.id, "visible"); }}>{props.node.visible === false ? "○" : "●"}</span>
        <span className="layer-tool" onClick={(event) => { event.stopPropagation(); props.onToggle(props.node.id, "locked"); }}>{props.node.locked ? "🔒" : "○"}</span>
      </button>
      {props.node.children.map((child) => <Layer key={child.id} node={child} selected={props.selected} query={props.query} depth={props.depth + 1} onSelect={props.onSelect} onToggle={props.onToggle} onDrop={props.onDrop} />)}
    </>
  );
}

function HtmlNode(props: {
  node: UiNode;
  document: UiDocumentModel;
  selected: string[];
  zoom: number;
  onSelect: (ids: string[]) => void;
  onMove: (id: string, x: number, y: number, done: boolean, pins?: { parentId: string; parentWidth: number; parentHeight: number; nodes: { id: string; x: number; y: number; w: number; h: number }[] }) => void;
  onResize: (id: string, x: number, y: number, width: number, height: number, done: boolean) => void;
  onReorder: (id: string, parentId: string, index: number) => void;
  onDuplicate: (id: string) => void;
}) {
  if (props.node.visible === false) return null;
  const type = shortType(nodeType(props.node));
  const classes = (props.node.styleClasses ?? []).filter(Boolean).join(" ");
  const text = props.node.text ?? props.node.properties?.Text ?? props.node.name ?? "";
  const selected = props.selected.includes(props.node.id);
  const style = cssStyle(elementStyle(props.node));
  const root = props.node.id === props.document.root.id;
  function beginMove(event: React.PointerEvent<HTMLElement>) {
    event.stopPropagation();
    event.preventDefault();
    props.onSelect([props.node.id]);
    if (root || props.node.locked) return;
    if (event.altKey) {
      props.onDuplicate(props.node.id);
      return;
    }
    const parentEl = event.currentTarget.parentElement;
    if (parentEl?.getAttribute("data-layout") === "Auto") {
      const origin = { x: event.clientX, y: event.clientY };
      const target = event.currentTarget;
      let moved = false;
      const move = (ev: PointerEvent) => {
        moved = true;
        target.style.transform = `translate(${ev.clientX - origin.x}px, ${ev.clientY - origin.y}px)`;
      };
      const up = (ev: PointerEvent) => {
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", up);
        target.style.transform = "";
        if (!moved) return;
        const siblings = [...parentEl.querySelectorAll(":scope > [data-astra-id]")];
        let index = siblings.length;
        for (let i = 0; i < siblings.length; i += 1) {
          const rect = siblings[i].getBoundingClientRect();
          if (ev.clientY < rect.top + rect.height / 2) {
            index = i;
            break;
          }
        }
        props.onReorder(props.node.id, parentEl.getAttribute("data-astra-id") ?? "", index);
      };
      window.addEventListener("pointermove", move);
      window.addEventListener("pointerup", up);
      return;
    }
    const parent = parentEl?.getBoundingClientRect();
    const box = event.currentTarget.getBoundingClientRect();
    if (!parentEl || !parent) return;
    const origin = {
      px: event.clientX,
      py: event.clientY,
      x: (box.left - parent.left) / props.zoom,
      y: (box.top - parent.top) / props.zoom
    };
    const pins = props.node.properties?.["editor.x"] ? undefined : {
      parentId: parentEl.getAttribute("data-astra-id") ?? "",
      parentWidth: parent.width / props.zoom,
      parentHeight: parent.height / props.zoom,
      nodes: [...parentEl.querySelectorAll(":scope > [data-astra-id]")].flatMap((child) => {
        const id = child.getAttribute("data-astra-id");
        if (!id) return [];
        const rect = child.getBoundingClientRect();
        return [{ id, x: (rect.left - parent.left) / props.zoom, y: (rect.top - parent.top) / props.zoom, w: rect.width / props.zoom, h: rect.height / props.zoom }];
      })
    };
    let sentPins = false;
    const emit = (x: number, y: number, done: boolean) => {
      props.onMove(props.node.id, x, y, done, sentPins ? undefined : pins);
      sentPins = true;
    };
    const move = (ev: PointerEvent) => emit(origin.x + (ev.clientX - origin.px) / props.zoom, origin.y + (ev.clientY - origin.py) / props.zoom, false);
    const up = (ev: PointerEvent) => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      if (!sentPins) return;
      emit(origin.x + (ev.clientX - origin.px) / props.zoom, origin.y + (ev.clientY - origin.py) / props.zoom, true);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  }
  function beginResize(edge: string, event: React.PointerEvent<HTMLElement>) {
    event.stopPropagation();
    event.preventDefault();
    const nodeEl = event.currentTarget.closest("[data-astra-id]") as HTMLElement | null;
    const parent = nodeEl?.parentElement?.getBoundingClientRect();
    const rect = nodeEl?.getBoundingClientRect();
    if (!nodeEl || !parent || !rect) return;
    if (!props.node.properties?.["editor.x"]) {
      const parentEl = nodeEl.parentElement;
      if (parentEl) {
        props.onMove(props.node.id, (rect.left - parent.left) / props.zoom, (rect.top - parent.top) / props.zoom, false, {
          parentId: parentEl.getAttribute("data-astra-id") ?? "",
          parentWidth: parent.width / props.zoom,
          parentHeight: parent.height / props.zoom,
          nodes: [...parentEl.querySelectorAll(":scope > [data-astra-id]")].flatMap((child) => {
            const id = child.getAttribute("data-astra-id");
            if (!id) return [];
            const item = child.getBoundingClientRect();
            return [{ id, x: (item.left - parent.left) / props.zoom, y: (item.top - parent.top) / props.zoom, w: item.width / props.zoom, h: item.height / props.zoom }];
          })
        });
      }
    }
    const origin = {
      px: event.clientX,
      py: event.clientY,
      x: (rect.left - parent.left) / props.zoom,
      y: (rect.top - parent.top) / props.zoom,
      w: rect.width / props.zoom,
      h: rect.height / props.zoom
    };
    const measure = (ev: PointerEvent) => {
      const dx = (ev.clientX - origin.px) / props.zoom;
      const dy = (ev.clientY - origin.py) / props.zoom;
      let { x, y, w, h } = origin;
      if (edge.includes("e")) w = Math.max(16, origin.w + dx);
      if (edge.includes("s")) h = Math.max(16, origin.h + dy);
      if (edge.includes("w")) { w = Math.max(16, origin.w - dx); x = origin.x + (origin.w - w); }
      if (edge.includes("n")) { h = Math.max(16, origin.h - dy); y = origin.y + (origin.h - h); }
      return { x, y, w, h };
    };
    const move = (ev: PointerEvent) => {
      const box = measure(ev);
      props.onResize(props.node.id, box.x, box.y, box.w, box.h, false);
    };
    const up = (ev: PointerEvent) => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      const box = measure(ev);
      props.onResize(props.node.id, box.x, box.y, box.w, box.h, true);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  }
  const handles = selected && !root ? (
    <span className="astra-handles">
      {["nw", "n", "ne", "e", "se", "s", "sw", "w"].map((edge) => <i key={edge} className={`h-${edge}`} onPointerDown={(event) => beginResize(edge, event)} />)}
    </span>
  ) : null;
  if (type === "TextureRect") {
    const url = props.node.properties?.SpriteUrl ?? "";
    return <span data-astra-id={props.node.id} className={`astra-sprite${selected ? " astra-selected" : ""}`} style={{ minWidth: 32, minHeight: 32, ...style }} onPointerDown={beginMove} onClick={(event) => event.stopPropagation()}>{url ? <img src={url} alt={props.node.properties?.State ?? "sprite"} draggable={false} /> : text || "Sprite"}{handles}</span>;
  }
  if (type === "Button") return <button type="button" data-astra-id={props.node.id} className={`${classes}${selected ? " astra-selected" : ""}${props.node.properties?.Variant ? ` is-${props.node.properties.Variant}` : ""}`} style={style} disabled={props.node.enabled === false || props.node.properties?.Variant === "disabled"} onPointerDown={beginMove} onClick={(event) => event.stopPropagation()}>{text || "Button"}{handles}</button>;
  if (type === "LineEdit") return <span data-astra-id={props.node.id} className={`astra-field${selected ? " astra-selected" : ""}`} style={style} onPointerDown={beginMove} onClick={(event) => event.stopPropagation()}><input className={classes} value={text} disabled={props.node.enabled === false} onChange={() => undefined} />{handles}</span>;
  if (type === "Label") return <label data-astra-id={props.node.id} className={`astra-label ${classes}${selected ? " astra-selected" : ""}`} style={style} onPointerDown={beginMove} onClick={(event) => event.stopPropagation()}>{text || "Label"}{handles}</label>;
  if (type === "ProgressBar") return <span data-astra-id={props.node.id} className={`astra-field${selected ? " astra-selected" : ""}`} style={style} onPointerDown={beginMove} onClick={(event) => event.stopPropagation()}><progress className={classes} value={Number(props.node.properties?.Value ?? 0) || 0} max={100} />{handles}</span>;
  const layout = type === "GridContainer" ? "astra-grid" : props.node.orientation === "Horizontal" ? "astra-row" : "astra-col";
  const columns = Number(props.node.properties?.Columns ?? 0);
  const known = ["BoxContainer", "GridContainer", "LayoutContainer", "ScrollContainer", "PanelContainer", "Panel", "Window", "Control"].includes(type);
  return (
    <div
      data-astra-id={props.node.id}
      data-layout={props.node.properties?.LayoutMode ?? "Free"}
      className={`${root ? "astra-window " : ""}${layout}${known ? "" : " astra-missing"}${classes ? " " + classes : ""}${selected ? " astra-selected" : ""}`}
      style={{ ...style, gridTemplateColumns: type === "GridContainer" && columns > 0 ? `repeat(${columns}, minmax(0, 1fr))` : undefined }}
      onPointerDown={beginMove}
      onClick={(event) => event.stopPropagation()}
    >
      {known ? null : type}
      {props.node.children.map((child) => <HtmlNode key={child.id} node={child} document={props.document} selected={props.selected} zoom={props.zoom} onSelect={props.onSelect} onMove={props.onMove} onResize={props.onResize} onReorder={props.onReorder} onDuplicate={props.onDuplicate} />)}
      {Array.from({ length: Math.max(0, Math.min(12, Number(props.node.properties?.Repeat ?? 1) || 1) - 1) }, (_, copy) => (
        <div key={`repeat-${copy}`} className="astra-repeat" aria-hidden="true">
          {props.node.children.map((child) => <span key={child.id}>{child.text || child.name || shortType(nodeType(child))}</span>)}
        </div>
      ))}
      {handles}
    </div>
  );
}

function cssStyle(value: string): CSSProperties {
  const style: Record<string, string> = {};
  for (const rule of value.split(";").filter(Boolean)) {
    const split = rule.indexOf(":");
    if (split < 0) continue;
    const key = rule.slice(0, split).trim().replace(/-([a-z])/g, (_, char: string) => char.toUpperCase());
    style[key] = rule.slice(split + 1).trim();
  }
  return style;
}

function groupProperties(properties: UiPropertyInfo[]) {
  const groups = new Map<string, UiPropertyInfo[]>();
  for (const property of properties) {
    const category = property.category || "Layout";
    const list = groups.get(category) ?? [];
    list.push(property);
    groups.set(category, list);
  }
  return groups;
}

function TokenAdder(props: { tokens: UiTokenModel[]; onAdd: (token: UiTokenModel) => void }) {
  const [name, setName] = useState("accent");
  const [value, setValue] = useState("#edbc63");
  return (
    <form className="token-form" onSubmit={(event) => {
      event.preventDefault();
      if (!name.trim() || props.tokens.some((token) => token.name === name.trim())) return;
      props.onAdd({ name: name.trim(), value });
    }}>
      <input value={name} onChange={(event) => setName(event.target.value)} aria-label="Token name" />
      <input value={value} onChange={(event) => setValue(event.target.value)} aria-label="Token value" />
      <button type="submit">Color</button>
    </form>
  );
}

function SpriteBrowser(props: { onPlace: (sprite: { rsi: string; state: string; file: string; url: string }) => void }) {
  const [query, setQuery] = useState("Interface");
  const [items, setItems] = useState<{ rsi: string; state: string; file: string; url: string }[]>([]);
  useEffect(() => {
    const handle = window.setTimeout(() => {
      void fetch(`/build-textures/index.json?q=${encodeURIComponent(query)}`)
        .then((response) => response.json())
        .then((body: { rsi: string; state: string; file: string; url: string }[]) => setItems(body))
        .catch(() => setItems([]));
    }, 180);
    return () => window.clearTimeout(handle);
  }, [query]);
  return (
    <div className="sprite-browser">
      <input className="studio-search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search sprites" />
      <label className="studio-asset">Image
        <input type="file" accept="image/*" onChange={(event) => {
          const file = event.target.files?.[0];
          if (!file) return;
          const reader = new FileReader();
          reader.onload = () => props.onPlace({ rsi: file.name, state: file.name, file: file.name, url: String(reader.result ?? "") });
          reader.readAsDataURL(file);
        }} />
      </label>
      <div className="sprite-grid">
        {items.map((item) => (
          <button
            key={`${item.file}`}
            type="button"
            className="sprite-card"
            draggable
            title={`${item.rsi}/${item.state}`}
            onDragStart={(event) => {
              event.dataTransfer.setData("application/x-astra-sprite", JSON.stringify(item));
              event.dataTransfer.effectAllowed = "copy";
            }}
            onClick={() => props.onPlace(item)}
          >
            <img src={item.url} alt={item.state} draggable={false} />
            <span>{item.state}</span>
          </button>
        ))}
      </div>
    </div>
  );
}

function StyleAdder(props: { classes: string[]; onAdd: (name: string) => void }) {
  const [name, setName] = useState("");
  return (
    <form onSubmit={(event) => { event.preventDefault(); if (!name.trim() || props.classes.includes(name.trim())) return; props.onAdd(name.trim()); setName(""); }}>
      <label className="field">Add class
        <input value={name} onChange={(event) => setName(event.target.value)} />
      </label>
    </form>
  );
}

function StatePanel(props: {
  draft: UiDocumentModel;
  current: UiNode;
  variables: NonNullable<UiDocumentModel["stateVariables"]>;
  bindings: NonNullable<UiDocumentModel["bindings"]>;
  commit: (document: UiDocumentModel) => void;
}) {
  const [name, setName] = useState("inputText");
  const [typeName, setTypeName] = useState("string");
  const [scope, setScope] = useState<"Local" | "Server" | "Derived">("Local");
  return (
    <div>
      <ul className="list">
        {props.variables.map((item) => <li key={item.id}>{item.name}: {item.typeName} · {item.scope}</li>)}
      </ul>
      <label className="field">Name <input value={name} onChange={(event) => setName(event.target.value)} /></label>
      <label className="field">Type
        <select value={typeName} onChange={(event) => setTypeName(event.target.value)}>
          <option>string</option><option>int</option><option>float</option><option>bool</option>
        </select>
      </label>
      <label className="field">Scope
        <select value={scope} onChange={(event) => setScope(event.target.value as typeof scope)}>
          <option>Local</option><option>Server</option><option>Derived</option>
        </select>
      </label>
      <button type="button" onClick={() => props.commit({
        ...props.draft,
        stateVariables: [...props.variables, { id: crypto.randomUUID(), name, typeName, scope }],
        localState: { ...(props.draft.localState ?? {}), [name]: "" }
      })}>Add state</button>
      <ul className="list">
        {props.bindings.filter((item) => item.elementId === props.current.id).map((item) => <li key={item.bindingId}>{item.targetProperty} {item.direction === "TwoWay" ? "↔" : "←"} {item.stateVariable}</li>)}
      </ul>
      <button type="button" onClick={() => props.commit({
        ...props.draft,
        bindings: [...props.bindings, { bindingId: crypto.randomUUID(), elementId: props.current.id, targetProperty: "Text", stateVariable: props.variables[0]?.name ?? "inputText", direction: "OneWay" }]
      })}>Bind Text</button>
    </div>
  );
}
