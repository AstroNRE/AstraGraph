import { useEffect, useState } from "react";

export interface UiNode {
  id: string;
  elementType: string;
  name?: string;
  text?: string;
  children: UiNode[];
}

export interface UiDocumentModel {
  id: string;
  name: string;
  width: number;
  height: number;
  root: UiNode;
}

const controls = ["Panel", "BoxContainer", "Button", "Label", "LineEdit", "ProgressBar"];

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

export function UiDesigner(props: { documents: UiDocumentModel[]; onSave: (document: UiDocumentModel) => void }) {
  const [draft, setDraft] = useState<UiDocumentModel | null>(props.documents[0] ?? null);
  const [selected, setSelected] = useState("");

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
          root: { id: rootId, elementType: "Window", name: "Window", text: "Window", children: [] }
        });
      }}>New UI</button>
    );
  }

  const current = findNode(draft.root, selected) ?? draft.root;

  return (
    <div>
      <label className="field">Window
        <input value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value, root: { ...draft.root, text: event.target.value } })} />
      </label>
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
          root: insertChild(draft.root, current.id, { id: crypto.randomUUID(), elementType, name: elementType, text: elementType, children: [] })
        })}>{elementType}</button>
      ))}
      <button type="button" onClick={() => props.onSave(draft)}>Save UI</button>
    </div>
  );
}
