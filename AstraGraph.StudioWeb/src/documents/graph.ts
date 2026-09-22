export interface PinDocument {
  id: string;
  name: string;
  direction: "input" | "output";
  kind: "execution" | "data";
  dataType?: string;
}

export interface NodeDocument {
  id: string;
  name: string;
  nodeType: string;
  pins: PinDocument[];
  properties: Record<string, string>;
}

export interface ConnectionDocument {
  fromNode: string;
  fromPin: string;
  toNode: string;
  toPin: string;
}

export interface GraphDocument {
  formatVersion: string;
  id: string;
  name: string;
  kind: string;
  side: string;
  nodes: NodeDocument[];
  connections: ConnectionDocument[];
  variables: { id: string; name: string; typeName: string }[];
  editorLayout: {
    nodePositions: Record<string, { x: number; y: number }>;
    comments: [];
    viewportX: number;
    viewportY: number;
    zoom: number;
  };
}

export interface FlowNode {
  id: string;
  position: { x: number; y: number };
  data: { label: string; nodeType: string; inputs: PinDocument[]; outputs: PinDocument[] };
}

export interface FlowEdge {
  id: string;
  source: string;
  target: string;
  sourceHandle: string;
  targetHandle: string;
}

export const emptyRevision = "00000000-0000-0000-0000-000000000000";

export function serializeGraph(document: GraphDocument): string {
  return JSON.stringify({
    formatVersion: document.formatVersion,
    id: document.id,
    name: document.name,
    kind: document.kind,
    side: document.side,
    metadata: { author: "", description: "", version: "1.0.0", tags: [], customAttributes: {} },
    variables: document.variables.map((variable) => ({
      id: variable.id,
      name: variable.name,
      typeName: variable.typeName,
      isPersistent: false,
      isReplicated: false
    })),
    nodes: document.nodes.map((node) => ({
      id: node.id,
      name: node.name,
      nodeType: node.nodeType,
      properties: node.properties,
      pins: node.pins.map((pin) => ({
        id: pin.id,
        name: pin.name,
        direction: pin.direction,
        kind: pin.kind,
        dataType: pin.dataType ?? ""
      }))
    })),
    connections: document.connections,
    editorLayout: document.editorLayout
  });
}

export function parseGraph(json: string): GraphDocument {
  const raw = JSON.parse(json) as GraphDocument;
  return {
    ...createGraph(raw.name || "Untitled"),
    ...raw,
    nodes: raw.nodes ?? [],
    connections: raw.connections ?? [],
    variables: raw.variables ?? [],
    editorLayout: raw.editorLayout ?? createGraph("").editorLayout
  };
}

export function createGraph(name: string): GraphDocument {
  return {
    formatVersion: "1.0",
    id: crypto.randomUUID(),
    name,
    kind: "system",
    side: "server",
    nodes: [],
    connections: [],
    variables: [],
    editorLayout: { nodePositions: {}, comments: [], viewportX: 0, viewportY: 0, zoom: 1 }
  };
}

export function nextVariableName(variables: { name: string }[]): string {
  const used = new Set(variables.map((variable) => variable.name));
  if (!used.has("Value")) return "Value";
  for (let index = 2; index < 100; index += 1) {
    const name = `Value${index}`;
    if (!used.has(name)) return name;
  }
  return `Value${variables.length + 1}`;
}

export function addNode(document: GraphDocument, nodeType: string, name: string): GraphDocument {
  const id = crypto.randomUUID();
  const input = crypto.randomUUID();
  const output = crypto.randomUUID();
  const count = document.nodes.length;
  return {
    ...document,
    nodes: [...document.nodes, {
      id,
      name,
      nodeType,
      properties: {},
      pins: [
        { id: input, name: "In", direction: "input", kind: "execution" },
        { id: output, name: "Out", direction: "output", kind: "execution" }
      ]
    }],
    editorLayout: {
      ...document.editorLayout,
      nodePositions: { ...document.editorLayout.nodePositions, [id]: { x: 80 + count * 28, y: 80 + count * 20 } }
    }
  };
}

export function toFlow(document: GraphDocument): { nodes: FlowNode[]; edges: FlowEdge[] } {
  return {
    nodes: document.nodes.map((node) => ({
      id: node.id,
      position: document.editorLayout.nodePositions[node.id] ?? { x: 80, y: 80 },
      data: {
        label: node.name,
        nodeType: node.nodeType,
        inputs: node.pins.filter((pin) => pin.direction === "input"),
        outputs: node.pins.filter((pin) => pin.direction === "output")
      }
    })),
    edges: document.connections.map((connection) => ({
      id: `${connection.fromPin}-${connection.toPin}`,
      source: connection.fromNode,
      target: connection.toNode,
      sourceHandle: connection.fromPin,
      targetHandle: connection.toPin
    }))
  };
}

export function applyFlow(
  document: GraphDocument,
  nodes: { id: string; position: { x: number; y: number } }[],
  edges: FlowEdge[]
): GraphDocument {
  const positions = { ...document.editorLayout.nodePositions };
  for (const node of nodes) positions[node.id] = node.position;
  return {
    ...document,
    connections: edges.map((edge) => ({
      fromNode: edge.source,
      fromPin: edge.sourceHandle,
      toNode: edge.target,
      toPin: edge.targetHandle
    })),
    editorLayout: { ...document.editorLayout, nodePositions: positions }
  };
}

export interface UndoStack<T> {
  past: T[];
  present: T;
  future: T[];
}

export function undo<T>(stack: UndoStack<T>): UndoStack<T> {
  const previous = stack.past.at(-1);
  if (!previous) return stack;
  return { past: stack.past.slice(0, -1), present: previous, future: [stack.present, ...stack.future] };
}

export function redo<T>(stack: UndoStack<T>): UndoStack<T> {
  const next = stack.future[0];
  if (!next) return stack;
  return { past: [...stack.past, stack.present], present: next, future: stack.future.slice(1) };
}

export function edit<T>(stack: UndoStack<T>, next: T): UndoStack<T> {
  return { past: [...stack.past, stack.present].slice(-50), present: next, future: [] };
}
