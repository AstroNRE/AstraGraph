import { useEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { alignmentGuides, boxesInRect, builtinControls, canParent, findNode, findParent, insertChild, layoutTree, nodeType, remapIds, remapSubtree, removeNode, reorder, shortType, snapValue, updateNode, type UiControlInfo, type UiDocumentModel, type UiNode, type UiPropertyInfo } from "./uiTree";
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

  function commit(next: UiDocumentModel, baseline: UiDocumentModel | null = draft) {
    if (!baseline) return;
    setPast((items) => [...items.slice(-49), baseline]);
    setFuture([]);
    setDraft(next);
    void refreshSurface(next);
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
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "z") {
        event.preventDefault();
        if (event.shiftKey) redo();
        else undo();
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
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
      <div className="ui-designer figma-viewport">
        <div className="figma-empty">
          <strong>Design</strong>
          <p>Выбери шаблон или пустой фрейм. Холст, слои и инспектор откроются как в Figma.</p>
          <div className="figma-empty-actions">
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

  function place(typeId: string, parentId = current.id, index?: number) {
    const parent = findNode(draft!.root, parentId);
    if (!parent || parent.locked || !canParent(catalog, parent)) return;
    const child = leaf(typeId, shortType(typeId));
    commit({ ...draft!, root: insertChild(draft!.root, parentId, child, index) });
    setSelected(child.id);
  }

  function onDrop(parentId: string, index: number, typeId?: string, elementId?: string) {
    if (!draft) return;
    const parent = findNode(draft.root, parentId);
    if (!parent || !canParent(catalog, parent)) return;
    if (typeId) {
      place(typeId, parentId, index);
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
    }}>
      <div className="dock-bar figma-modes">
        {tabs.map((name) => <button key={name} type="button" className={name === tab ? "active" : ""} onClick={() => setTab(name)}>{name}</button>)}
        <span className="grow" />
        <button type="button" onClick={undo}>Undo</button>
        <button type="button" onClick={redo}>Redo</button>
        <button type="button" onClick={() => props.onSave(draft)}>Save</button>
      </div>
      {tab === "Design" ? (
        <div className="ui-body">
          <aside className="figma-side">
            <div className="figma-section">Controls</div>
            <input className="figma-search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search controls" />
            <div className="figma-assets">
              {grouped.map(([category, controls]) => (
                <div key={category}>
                  <div className="figma-section">{category}</div>
                  {controls.map((control) => (
                    <button
                      key={control.typeId}
                      type="button"
                      className="figma-asset"
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
            </div>
            <div className="figma-section">Layers</div>
            <input className="figma-search" value={layerQuery} onChange={(event) => setLayerQuery(event.target.value)} placeholder="Search layers" />
            <div className="figma-layers">
              <Layer node={draft.root} selected={selected || draft.root.id} query={layerQuery} depth={0} onSelect={setSelected} />
            </div>
          </aside>
          <main className="figma-viewport" onClick={() => setSelected(draft.root.id)} onDragOver={(event) => { event.preventDefault(); event.dataTransfer.dropEffect = "copy"; }}>
            <div className="figma-crumbs">{breadcrumb(draft.root, current.id).join("  /  ")}</div>
            <div className="figma-world">
              <div className="figma-frame-wrap">
                <div className={current.id === draft.root.id ? "figma-frame-label selected" : "figma-frame-label"}>{draft.name}</div>
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
              onMove={(id, x, y) => {
                const node = findNode(draft.root, id);
                if (!node) return;
                const properties = { ...(node.properties ?? {}), "editor.x": String(snapValue(x, snap)), "editor.y": String(snapValue(y, snap)) };
                commit({ ...draft, root: updateNode(draft.root, id, { properties }) });
              }}
              onDrop={onDrop}
              onResize={(width, height, done, baseline) => {
                const next = { ...baseline, width, height };
                if (done) commit(next, baseline);
                else setDraft(next);
              }}
              framePng={surface.framePng}
              mode={surface.mode}
              knownTypes={new Set(catalog.flatMap((item) => [item.typeId, shortType(item.typeId), item.displayName]))}
            />
              </div>
            </div>
            <div className="figma-zoom" onClick={(event) => event.stopPropagation()}>
              <button type="button" onClick={() => setZoom((value) => Math.max(0.25, Math.round((value - 0.25) * 4) / 4))}>−</button>
              <span>{Math.round(zoom * 100)}%</span>
              <button type="button" onClick={() => setZoom((value) => Math.min(2, Math.round((value + 0.25) * 4) / 4))}>+</button>
              <label>W <input type="number" min={64} max={4096} defaultValue={draft.width} key={`w-${draft.width}`} onBlur={(event) => commit({ ...draft, width: clampSize(Number(event.target.value), draft.width) })} /></label>
              <label>H <input type="number" min={64} max={4096} defaultValue={draft.height} key={`h-${draft.height}`} onBlur={(event) => commit({ ...draft, height: clampSize(Number(event.target.value), draft.height) })} /></label>
            </div>
          </main>
          <aside className="figma-side figma-inspect">
            <div className="figma-inspect-title">{current.name || info?.displayName || shortType(nodeType(current))}</div>
            <div className="figma-inspect-type">{info?.displayName ?? shortType(nodeType(current))}</div>
            <label className="figma-prop">Name
              <input value={current.name ?? ""} onChange={(event) => patch({ name: event.target.value })} />
            </label>
            {[...groupProperties(info?.properties ?? [])].map(([category, properties]) => (
              <div key={category}>
                <div className="figma-section">{category}</div>
                {properties.map((property) => (
                  <label key={property.name} className="figma-prop">{property.name}
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
            <div className="figma-section">Style</div>
            <div className="ui-tags">
              {(current.styleClasses ?? []).map((name) => <button key={name} type="button" onClick={() => patch({ styleClasses: (current.styleClasses ?? []).filter((item) => item !== name) })}>{name} ×</button>)}
            </div>
            <StyleAdder classes={current.styleClasses ?? []} onAdd={(name) => patch({ styleClasses: [...(current.styleClasses ?? []), name] })} />
            <div className="figma-section">Events</div>
            {(info?.events ?? []).map((item) => <div key={item.name} className="figma-prop">{item.name}</div>)}
            <div className="figma-actions">
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
          <p className="muted">Style classes come from the consumer stylesheet.</p>
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
            {surface.framePng ? <img className="xaml-frame" alt="" src={surface.framePng.startsWith("data:") ? surface.framePng : `data:image/png;base64,${surface.framePng}`} /> : <p className="xaml-wait">Тот же кадр, что на холсте: окно Robust из XAML, не HTML.</p>}
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
  onMove: (id: string, x: number, y: number) => void;
  onDrop: (parentId: string, index: number, typeId?: string, elementId?: string) => void;
  onResize: (width: number, height: number, done: boolean, baseline: UiDocumentModel) => void;
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
  function local(event: { clientX: number; clientY: number }) {
    const bounds = stage.current?.getBoundingClientRect();
    if (!bounds) return { x: 0, y: 0 };
    return { x: (event.clientX - bounds.left) / props.zoom, y: (event.clientY - bounds.top) / props.zoom };
  }
  function acceptDrop(event: React.DragEvent<HTMLDivElement>) {
    event.preventDefault();
    event.stopPropagation();
    const typeId = event.dataTransfer.getData("application/x-astra-control") || event.dataTransfer.getData("text/plain");
    const elementId = event.dataTransfer.getData("application/x-astra-element");
    const point = local(event);
    const hit = [...boxes].reverse().find((box) => point.x >= box.x && point.x <= box.x + box.w && point.y >= box.y && point.y <= box.y + box.h);
    const node = hit ? findNode(props.document.root, hit.id) : props.document.root;
    const target = node && (shortType(nodeType(node)).endsWith("Container") || shortType(nodeType(node)) === "Panel")
      ? node
      : findParent(props.document.root, node?.id ?? props.document.root.id) ?? props.document.root;
    const index = target.children.findIndex((child) => child.id === node?.id);
    props.onDrop(target.id, index >= 0 ? index + 1 : target.children.length, typeId, elementId);
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
      {props.framePng ? <img className="xaml-frame" alt="" src={props.framePng.startsWith("data:") ? props.framePng : `data:image/png;base64,${props.framePng}`} /> : null}
      {boxes.map((box) => {
        const node = findNode(props.document.root, box.id);
        if (!node) return null;
        const type = shortType(nodeType(node));
        const text = boundText(node, props.document.localState ?? {}, props.document.bindings);
        const selected = props.selected.includes(box.id);
        const container = type.endsWith("Container") || type === "Panel";
        const color = node.properties?.FontColorOverride;
        return (
          <div
            key={box.id}
            className={`ui-node kind-${type}${selected ? " selected" : ""}${container ? " kind-container" : ""}`}
            style={{ position: "absolute", left: box.x, top: box.y, width: box.w, height: box.h, color }}
            onMouseDown={(event) => { event.stopPropagation(); props.onSelect([box.id]); }}
            onDragOver={(event) => { event.preventDefault(); event.stopPropagation(); }}
            onDrop={(event) => { event.stopPropagation(); acceptDrop(event); }}
          >
            {type === "Button" ? <span className="game-button">{text || node.name || "Button"}</span> : null}
            {type === "LineEdit" ? <span className="game-line">{text || "Text"}</span> : null}
            {type === "Label" ? <span className="game-label">{text || node.name || "Label"}</span> : null}
            {type === "ProgressBar" ? <span className="game-progress" /> : null}
            {container && node.id === props.document.root.id ? <span className="game-title">{props.document.name}</span> : null}
            {selected ? <span className="figma-handles" aria-hidden="true">{["nw", "n", "ne", "e", "se", "s", "sw", "w"].map((handle) => <i key={handle} className={`h-${handle}`} />)}</span> : null}
          </div>
        );
      })}
      <button
        type="button"
        className="frame-resize"
        aria-label="Resize window"
        onMouseDown={(event) => {
          event.stopPropagation();
          const origin = { x: event.clientX, y: event.clientY, width: props.document.width, height: props.document.height, baseline: props.document };
          resizeStart.current = origin;
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
            resizeStart.current = null;
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

function breadcrumb(node: UiNode, id: string, trail: string[] = []): string[] {
  const next = [...trail, node.name || shortType(nodeType(node))];
  if (node.id === id) return next;
  for (const child of node.children) {
    const found = breadcrumb(child, id, next);
    if (found.length > 0) return found;
  }
  return [];
}

function Layer(props: { node: UiNode; selected: string; query: string; depth: number; onSelect: (id: string) => void }) {
  const type = shortType(nodeType(props.node));
  const label = props.node.name || type;
  if (props.query && !`${type} ${label}`.toLowerCase().includes(props.query.toLowerCase()) && props.node.children.length === 0) return null;
  return (
    <>
      <button type="button" className={props.node.id === props.selected ? "figma-layer active" : "figma-layer"} style={{ paddingLeft: 8 + props.depth * 16 }} onClick={() => props.onSelect(props.node.id)}>
        <span className="figma-layer-name">{label}</span>
        <span className="figma-layer-type">{type}</span>
      </button>
      {props.node.children.map((child) => <Layer key={child.id} node={child} selected={props.selected} query={props.query} depth={props.depth + 1} onSelect={props.onSelect} />)}
    </>
  );
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
