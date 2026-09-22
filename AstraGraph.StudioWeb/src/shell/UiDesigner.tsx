import { useEffect, useState } from "react";

export interface UiNode {
  id: string;
  elementType: string;
  name?: string;
  text?: string;
  children: UiNode[];
  visible?: boolean;
  enabled?: boolean;
  orientation?: string;
  minWidth?: number | null;
  minHeight?: number | null;
  styleClasses?: string[];
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

export interface UiDocumentModel {
  id: string;
  name: string;
  width: number;
  height: number;
  root: UiNode;
  bindings?: UiBindingModel[];
  events?: UiEventModel[];
  localState?: Record<string, string>;
}

const controls = ["Panel", "BoxContainer", "Button", "Label", "LineEdit", "ProgressBar"];
const tabs = ["Design", "Logic", "State", "Contract", "Styles", "Preview", "Diagnostics"] as const;

function updateNode(node: UiNode, id: string, patch: Partial<UiNode>): UiNode {
  if (node.id === id) return { ...node, ...patch };
  return { ...node, children: node.children.map((child) => updateNode(child, id, patch)) };
}

function insertChild(node: UiNode, parentId: string, child: UiNode): UiNode {
  if (node.id === parentId) return { ...node, children: [...node.children, child] };
  return { ...node, children: node.children.map((item) => insertChild(item, parentId, child)) };
}

function Tree(props: { node: UiNode; selected: string; onSelect: (id: string) => void }) {
  return (
    <li>
      <button type="button" className={props.node.id === props.selected ? "active" : ""} onClick={() => props.onSelect(props.node.id)}>
        {props.node.elementType} {props.node.text ? `· ${props.node.text}` : ""}
      </button>
      {props.node.children.length > 0 ? (
        <ul className="list">
          {props.node.children.map((child) => <Tree key={child.id} node={child} selected={props.selected} onSelect={props.onSelect} />)}
        </ul>
      ) : null}
    </li>
  );
}

function findNode(node: UiNode, id: string): UiNode | undefined {
  if (node.id === id) return node;
  for (const child of node.children) {
    const found = findNode(child, id);
    if (found) return found;
  }
  return undefined;
}

function boundText(node: UiNode, local: Record<string, string>, bindings: UiBindingModel[]): string {
  const binding = bindings.find((item) => item.elementId === node.id && item.targetProperty === "Text");
  if (binding && local[binding.stateVariable]) return local[binding.stateVariable];
  return node.text ?? "";
}

function ControlNode(props: { node: UiNode; local: Record<string, string>; bindings: UiBindingModel[] }) {
  const text = boundText(props.node, props.local, props.bindings);
  const style = { minWidth: props.node.minWidth ?? undefined, opacity: props.node.visible === false ? 0.45 : 1, margin: 4 };
  const disabled = props.node.enabled === false;
  const children = props.node.children.map((child) => <ControlNode key={child.id} node={child} local={props.local} bindings={props.bindings} />);
  if (props.node.elementType === "Button") return <button type="button" disabled={disabled} style={style}>{text}</button>;
  if (props.node.elementType === "LineEdit") return <input aria-label={props.node.name || "LineEdit"} defaultValue={text} disabled={disabled} style={style} />;
  if (props.node.elementType === "Label") return <span style={style}>{text}</span>;
  if (props.node.elementType === "ProgressBar") return <progress aria-label={props.node.name || "ProgressBar"} value={40} max={100} style={style} />;
  const direction = props.node.orientation === "Horizontal" ? "row" : "column";
  return <div style={{ ...style, display: "flex", flexDirection: direction }}><span className="muted">{props.node.elementType}</span>{children}</div>;
}

function ControlPreview(props: { document: UiDocumentModel }) {
  const [scale, setScale] = useState(1);
  const [locale, setLocale] = useState("en");
  const [theme, setTheme] = useState("dark");
  return (
    <div>
      <label className="field">Scale
        <input type="number" min={0.5} max={2} step={0.25} value={scale} onChange={(event) => setScale(Number(event.target.value) || 1)} />
      </label>
      <label className="field">Locale
        <select value={locale} onChange={(event) => setLocale(event.target.value)}>
          <option value="en">en</option>
          <option value="ru">ru</option>
        </select>
      </label>
      <label className="field">Theme
        <select value={theme} onChange={(event) => setTheme(event.target.value)}>
          <option value="dark">dark</option>
          <option value="light">light</option>
        </select>
      </label>
      <p className="muted">{locale} · browser mock of the control catalog. A connected Robust client mounts the native controls.</p>
      <div style={{ width: props.document.width, transform: `scale(${scale})`, transformOrigin: "top left", background: theme === "light" ? "#f3f4f6" : "#171a22", color: theme === "light" ? "#111" : "#e7e9ee", padding: 8 }}>
        <ControlNode node={props.document.root} local={props.document.localState ?? {}} bindings={props.document.bindings ?? []} />
      </div>
    </div>
  );
}

export function UiDesigner(props: { documents: UiDocumentModel[]; onSave: (document: UiDocumentModel) => void; onDiagnose?: (document: UiDocumentModel) => void }) {
  const [draft, setDraft] = useState<UiDocumentModel | null>(props.documents[0] ?? null);
  const [selected, setSelected] = useState("");
  const [tab, setTab] = useState<(typeof tabs)[number]>("Design");

  useEffect(() => {
    setDraft(props.documents[0] ?? null);
  }, [props.documents]);

  if (!draft) {
    return (
      <button type="button" onClick={() => {
        const rootId = crypto.randomUUID();
        setSelected(rootId);
        setDraft({
          id: crypto.randomUUID(),
          name: "Window",
          width: 480,
          height: 320,
          bindings: [],
          events: [],
          localState: {},
          root: { id: rootId, elementType: "Window", name: "Window", text: "Window", children: [], visible: true, enabled: true, orientation: "Vertical", styleClasses: [] }
        });
      }}>New UI</button>
    );
  }

  const current = findNode(draft.root, selected) ?? draft.root;
  const bindings = draft.bindings ?? [];
  const events = draft.events ?? [];
  const localState = draft.localState ?? {};

  return (
    <div>
      <div className="dock-bar">
        {tabs.map((name) => <button key={name} type="button" className={name === tab ? "active" : ""} onClick={() => setTab(name)}>{name}</button>)}
      </div>
      {tab === "Design" ? (
        <>
          <ul className="list">
            <Tree node={draft.root} selected={selected || draft.root.id} onSelect={setSelected} />
          </ul>
          <label className="field">Text
            <input value={current.text ?? ""} onChange={(event) => setDraft({ ...draft, root: updateNode(draft.root, current.id, { text: event.target.value }) })} />
          </label>
          <div className="section-label">Add control</div>
          {controls.map((elementType) => (
            <button key={elementType} type="button" onClick={() => setDraft({
              ...draft,
              root: insertChild(draft.root, current.id, { id: crypto.randomUUID(), elementType, name: elementType, text: elementType, children: [], visible: true, enabled: true, styleClasses: [] })
            })}>{elementType}</button>
          ))}
        </>
      ) : null}
      {tab === "Logic" ? (
        <>
          <p className="muted">Events call a named action. The action graph is published separately.</p>
          <ul className="list">
            {events.filter((item) => item.elementId === current.id).map((item) => <li key={item.subscriptionId}>{item.eventName} → {item.targetAction}</li>)}
          </ul>
          <button type="button" onClick={() => setDraft({
            ...draft,
            events: [...events, { subscriptionId: crypto.randomUUID(), elementId: current.id, eventName: "OnPressed", targetAction: "Action" }]
          })}>Add OnPressed</button>
        </>
      ) : null}
      {tab === "State" ? (
        <>
          <ul className="list">
            {Object.entries(localState).map(([key, value]) => <li key={key}>{key}: {value}</li>)}
          </ul>
          <button type="button" onClick={() => setDraft({ ...draft, localState: { ...localState, DoorState: localState.DoorState ?? "" } })}>Add DoorState</button>
          <ul className="list">
            {bindings.filter((item) => item.elementId === current.id).map((item) => <li key={item.bindingId}>{item.targetProperty} ← {item.stateVariable}</li>)}
          </ul>
          <button type="button" onClick={() => setDraft({
            ...draft,
            bindings: [...bindings, { bindingId: crypto.randomUUID(), elementId: current.id, targetProperty: "Text", stateVariable: "DoorState", direction: "OneWay" }]
          })}>Bind Text to DoorState</button>
        </>
      ) : null}
      {tab === "Contract" ? (
        <>
          <label className="field">Name
            <input value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value, root: draft.root.elementType === "Window" ? { ...draft.root, text: event.target.value } : draft.root })} />
          </label>
          <label className="field">Width
            <input type="number" value={draft.width} onChange={(event) => setDraft({ ...draft, width: Number(event.target.value) })} />
          </label>
          <label className="field">Height
            <input type="number" value={draft.height} onChange={(event) => setDraft({ ...draft, height: Number(event.target.value) })} />
          </label>
        </>
      ) : null}
      {tab === "Styles" ? (
        <>
          <label className="field">Visible
            <input type="checkbox" checked={current.visible !== false} onChange={(event) => setDraft({ ...draft, root: updateNode(draft.root, current.id, { visible: event.target.checked }) })} />
          </label>
          <label className="field">Enabled
            <input type="checkbox" checked={current.enabled !== false} onChange={(event) => setDraft({ ...draft, root: updateNode(draft.root, current.id, { enabled: event.target.checked }) })} />
          </label>
          <label className="field">Orientation
            <select value={current.orientation ?? "Vertical"} onChange={(event) => setDraft({ ...draft, root: updateNode(draft.root, current.id, { orientation: event.target.value }) })}>
              <option>Vertical</option>
              <option>Horizontal</option>
            </select>
          </label>
          <label className="field">Min width
            <input type="number" value={current.minWidth ?? 0} onChange={(event) => setDraft({ ...draft, root: updateNode(draft.root, current.id, { minWidth: Number(event.target.value) }) })} />
          </label>
          <label className="field">Style class
            <input value={(current.styleClasses ?? []).join(" ")} onChange={(event) => setDraft({ ...draft, root: updateNode(draft.root, current.id, { styleClasses: event.target.value.split(/\s+/).filter(Boolean) }) })} />
          </label>
        </>
      ) : null}
      {tab === "Diagnostics" ? (
        <div>
          <p className="muted">Runs the UI compiler against this document.</p>
          <button type="button" onClick={() => props.onDiagnose?.(draft)}>Compile UI</button>
        </div>
      ) : null}
      {tab === "Preview" ? <ControlPreview document={draft} /> : null}
      <button type="button" onClick={() => props.onSave(draft)}>Save UI</button>
    </div>
  );
}
