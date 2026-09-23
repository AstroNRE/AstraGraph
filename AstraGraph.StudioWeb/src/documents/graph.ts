export interface PinDocument {
  id: string;
  name: string;
  direction: "input" | "output";
  kind: "execution" | "data";
  dataType?: string;
  defaultValue?: string;
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

export interface CommentBox {
  id: string;
  title: string;
  text: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface GraphAttributes {
  author: string;
  schedule: string;
  securityProfile: string;
  hotReload: string;
  budget: string;
  generics: string;
  overrideOf: string;
  expected: string;
  prototype: string;
  entity: string;
  enumType: string;
  bookmarks: string;
}

export interface GraphSchemaDocument {
  id: string;
  name: string;
  kind?: string;
  isComponent?: boolean;
  fields: { id: string; name: string; typeName: string; defaultValue?: string }[];
}

export interface GraphDocument {
  formatVersion: string;
  id: string;
  name: string;
  kind: string;
  side: string;
  nodes: NodeDocument[];
  connections: ConnectionDocument[];
  description: string;
  tags: string;
  version: string;
  attributes: GraphAttributes;
  variables: { id: string; name: string; typeName: string; defaultValue?: string; persistent?: boolean; replicated?: boolean; parameter?: boolean }[];
  schemas?: GraphSchemaDocument[];
  editorLayout: {
    nodePositions: Record<string, { x: number; y: number }>;
    comments: CommentBox[];
    viewportX: number;
    viewportY: number;
    zoom: number;
  };
}

export interface FlowNode {
  id: string;
  position: { x: number; y: number };
  data: { label: string; nodeType: string; inputs: PinDocument[]; outputs: PinDocument[]; pure?: boolean; side?: string; comment?: boolean };
}

export interface FlowEdge {
  id: string;
  source: string;
  target: string;
  sourceHandle: string;
  targetHandle: string;
}

export const emptyRevision = "00000000-0000-0000-0000-000000000000";

export function emptyAttributes(): GraphAttributes {
  return { author: "", schedule: "Update", securityProfile: "Default", hotReload: "Automatic", budget: "256", generics: "", overrideOf: "", expected: "", prototype: "", entity: "", enumType: "", bookmarks: "[]" };
}

export function serializeGraph(document: GraphDocument): string {
  return JSON.stringify({
    formatVersion: document.formatVersion,
    id: document.id,
    name: document.name,
    kind: document.kind,
    side: document.side,
    metadata: {
      author: document.attributes?.author ?? "",
      description: document.description ?? "",
      version: document.version || "1.0.0",
      tags: (document.tags ?? "").split(",").map((tag) => tag.trim()).filter(Boolean),
      customAttributes: { ...(document.attributes ?? emptyAttributes()) }
    },
    variables: document.variables.map((variable) => ({
      id: variable.id,
      name: variable.name,
      typeName: variable.typeName,
      defaultValue: variable.defaultValue ?? "",
      isPersistent: variable.persistent === true,
      isReplicated: variable.replicated === true,
      isParameter: variable.parameter === true
    })),
    schemas: document.schemas ?? [],
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
        dataType: pin.dataType ?? "",
        defaultValue: pin.defaultValue ?? ""
      }))
    })),
    connections: document.connections,
    editorLayout: document.editorLayout
  });
}

export function parseGraph(json: string): GraphDocument {
  const raw = JSON.parse(json) as GraphDocument & { metadata?: { author?: string; description?: string; version?: string; tags?: string[]; customAttributes?: Partial<GraphAttributes> } };
  const stored = raw.metadata?.customAttributes ?? {};
  return {
    ...createGraph(raw.name || "Untitled"),
    ...raw,
    description: raw.metadata?.description ?? raw.description ?? "",
    tags: raw.metadata?.tags?.join(", ") ?? raw.tags ?? "",
    version: raw.metadata?.version ?? raw.version ?? "1.0.0",
    attributes: { ...emptyAttributes(), ...stored, author: raw.metadata?.author ?? stored.author ?? "" },
    nodes: (raw.nodes ?? []).map((node) => ({
      ...node,
      pins: (node.pins ?? []).map(normalizePin)
    })),
    connections: raw.connections ?? [],
    schemas: raw.schemas ?? [],
    variables: (raw.variables ?? []).map((variable) => ({
      id: variable.id,
      name: variable.name,
      typeName: variable.typeName,
      defaultValue: variable.defaultValue,
      persistent: variable.persistent === true || (variable as { isPersistent?: boolean }).isPersistent === true,
      replicated: variable.replicated === true || (variable as { isReplicated?: boolean }).isReplicated === true,
      parameter: variable.parameter === true || (variable as { isParameter?: boolean }).isParameter === true
    })),
    editorLayout: {
      ...(raw.editorLayout ?? createGraph("").editorLayout),
      comments: raw.editorLayout?.comments ?? [],
      nodePositions: raw.editorLayout?.nodePositions ?? {}
    }
  };
}

export function createGraph(name: string): GraphDocument {
  return {
    formatVersion: "1.0",
    id: crypto.randomUUID(),
    name,
    kind: "system",
    side: "server",
    description: "",
    tags: "",
    version: "1.0.0",
    attributes: emptyAttributes(),
    nodes: [],
    connections: [],
    variables: [],
    schemas: [],
    editorLayout: { nodePositions: {}, comments: [], viewportX: 0, viewportY: 0, zoom: 1 }
  };
}

export function normalizePin(pin: PinDocument): PinDocument {
  const direction = String(pin.direction).toLowerCase() === "output" ? "output" : "input";
  const kind = String(pin.kind).toLowerCase() === "data" ? "data" : "execution";
  return { ...pin, direction, kind };
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

export function insertNode(document: GraphDocument, node: NodeDocument): GraphDocument {
  const count = document.nodes.length;
  return {
    ...document,
    nodes: [...document.nodes, node],
    editorLayout: {
      ...document.editorLayout,
      nodePositions: {
        ...document.editorLayout.nodePositions,
        [node.id]: { x: 96 + (count % 4) * 40, y: 72 + count * 24 }
      }
    }
  };
}

export function upsertSchema(document: GraphDocument, schema: GraphSchemaDocument): GraphDocument {
  const schemas = document.schemas ?? [];
  return { ...document, schemas: [...schemas.filter((item) => item.id !== schema.id), schema] };
}

const gameplayPins: Record<string, { properties?: Record<string, string>; pins: { name: string; direction: "input" | "output"; kind: "execution" | "data"; dataType?: string; defaultValue?: string }[] }> = {
  "Event.Native": {
    properties: { eventType: "", componentType: "" },
    pins: [
      { name: "Out", direction: "output", kind: "execution" },
      { name: "Entity", direction: "output", kind: "data", dataType: "EntityUid" },
      { name: "Component", direction: "output", kind: "data", dataType: "object" },
      { name: "Event", direction: "output", kind: "data", dataType: "object" }
    ]
  },
  "Entity.TryGetComponent": {
    properties: { ComponentType: "" },
    pins: [
      { name: "In", direction: "input", kind: "execution" },
      { name: "Out", direction: "output", kind: "execution" },
      { name: "Entity", direction: "input", kind: "data", dataType: "EntityUid" },
      { name: "Found", direction: "output", kind: "data", dataType: "bool" },
      { name: "Component", direction: "output", kind: "data", dataType: "component" }
    ]
  },
  "Schema.GetField": {
    properties: { Schema: "", Field: "" },
    pins: [
      { name: "Component", direction: "input", kind: "data", dataType: "component" },
      { name: "Value", direction: "output", kind: "data", dataType: "int32" }
    ]
  },
  "Native.GetMember": {
    properties: { Member: "" },
    pins: [
      { name: "Target", direction: "input", kind: "data", dataType: "object" },
      { name: "Value", direction: "output", kind: "data", dataType: "object" }
    ]
  },
  "Flow.For": {
    pins: [
      { name: "In", direction: "input", kind: "execution" },
      { name: "Out", direction: "output", kind: "execution" },
      { name: "Body", direction: "output", kind: "execution" },
      { name: "Start", direction: "input", kind: "data", dataType: "int32", defaultValue: "0" },
      { name: "Count", direction: "input", kind: "data", dataType: "int32" },
      { name: "Index", direction: "output", kind: "data", dataType: "int32" }
    ]
  },
  "Flow.ForEach": {
    pins: [
      { name: "In", direction: "input", kind: "execution" },
      { name: "Out", direction: "output", kind: "execution" },
      { name: "Body", direction: "output", kind: "execution" },
      { name: "Collection", direction: "input", kind: "data", dataType: "List<EntityUid>" },
      { name: "Current", direction: "output", kind: "data", dataType: "EntityUid" }
    ]
  },
  "Nullable.HasValue": {
    pins: [
      { name: "Value", direction: "input", kind: "data", dataType: "EntityUid?" },
      { name: "HasValue", direction: "output", kind: "data", dataType: "bool" }
    ]
  },
  "Nullable.GetValue": {
    pins: [
      { name: "Value", direction: "input", kind: "data", dataType: "EntityUid?" },
      { name: "ValueOut", direction: "output", kind: "data", dataType: "EntityUid" }
    ]
  },
  "Graph.Call": {
    properties: { Function: "ReplaceEntity" },
    pins: [
      { name: "In", direction: "input", kind: "execution" },
      { name: "Out", direction: "output", kind: "execution" },
      { name: "Target", direction: "input", kind: "data", dataType: "EntityUid" },
      { name: "Prototype", direction: "input", kind: "data", dataType: "EntProtoId" },
      { name: "Count", direction: "input", kind: "data", dataType: "int32" }
    ]
  },
  "Native.Call": {
    properties: { Method: "" },
    pins: [
      { name: "In", direction: "input", kind: "execution" },
      { name: "Out", direction: "output", kind: "execution" }
    ]
  }
};

export function addNode(document: GraphDocument, nodeType: string, name: string): GraphDocument {
  const spec = gameplayPins[nodeType];
  const id = crypto.randomUUID();
  const count = document.nodes.length;
  const pins = spec
    ? spec.pins.map((pin) => ({ id: crypto.randomUUID(), ...pin }))
    : [
        { id: crypto.randomUUID(), name: "In", direction: "input" as const, kind: "execution" as const },
        { id: crypto.randomUUID(), name: "Out", direction: "output" as const, kind: "execution" as const }
      ];
  return {
    ...document,
    nodes: [...document.nodes, {
      id,
      name,
      nodeType,
      properties: spec?.properties ? { ...spec.properties } : {},
      pins
    }],
    editorLayout: {
      ...document.editorLayout,
      nodePositions: { ...document.editorLayout.nodePositions, [id]: { x: 80 + count * 28, y: 80 + count * 20 } }
    }
  };
}

export function toFlow(document: GraphDocument): { nodes: FlowNode[]; edges: FlowEdge[] } {
  return {
    nodes: [
      ...document.nodes.map((node) => ({
        id: node.id,
        position: document.editorLayout.nodePositions[node.id] ?? {
          x: 80 + (document.nodes.indexOf(node) % 6) * 280,
          y: 80 + Math.floor(document.nodes.indexOf(node) / 6) * 180
        },
        data: {
          label: node.name,
          nodeType: node.nodeType,
          inputs: node.pins.filter((pin) => pin.direction === "input"),
          outputs: node.pins.filter((pin) => pin.direction === "output"),
          pure: node.properties.pure === "true",
          side: node.properties.side ?? ""
        }
      })),
      ...document.editorLayout.comments.map((comment) => ({
        id: comment.id,
        position: { x: comment.x, y: comment.y },
        data: { label: comment.title || "Comment", nodeType: comment.text, inputs: [], outputs: [], comment: true }
      }))
    ],
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
  const comments = document.editorLayout.comments.map((comment) => {
    const moved = nodes.find((node) => node.id === comment.id);
    return moved ? { ...comment, x: moved.position.x, y: moved.position.y } : comment;
  });
  for (const node of nodes) {
    if (document.nodes.some((item) => item.id === node.id)) positions[node.id] = node.position;
  }
  return {
    ...document,
    connections: edges.filter((edge) => document.nodes.some((node) => node.id === edge.source)).map((edge) => ({
      fromNode: edge.source,
      fromPin: edge.sourceHandle,
      toNode: edge.target,
      toPin: edge.targetHandle
    })),
    editorLayout: { ...document.editorLayout, nodePositions: positions, comments }
  };
}

export function alignNodes(document: GraphDocument, ids: string[], axis: "left" | "top"): GraphDocument {
  const selected = ids.map((id) => document.editorLayout.nodePositions[id]).filter(Boolean);
  if (selected.length < 2) return document;
  const edge = axis === "left" ? Math.min(...selected.map((item) => item.x)) : Math.min(...selected.map((item) => item.y));
  const nodePositions = { ...document.editorLayout.nodePositions };
  for (const id of ids) {
    const position = nodePositions[id];
    if (!position) continue;
    nodePositions[id] = axis === "left" ? { ...position, x: edge } : { ...position, y: edge };
  }
  return { ...document, editorLayout: { ...document.editorLayout, nodePositions } };
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

export function connectPins(document: GraphDocument, fromNode: string, fromPin: string, toNode: string, toPin: string): GraphDocument {
  if (document.connections.some((wire) => wire.fromPin === fromPin && wire.toPin === toPin)) return document;
  return { ...document, connections: [...document.connections, { fromNode, fromPin, toNode, toPin }] };
}

export function removeNodes(document: GraphDocument, ids: string[]): GraphDocument {
  const idSet = new Set(ids);
  const positions = { ...document.editorLayout.nodePositions };
  for (const id of ids) delete positions[id];
  return {
    ...document,
    nodes: document.nodes.filter((node) => !idSet.has(node.id)),
    connections: document.connections.filter((wire) => !idSet.has(wire.fromNode) && !idSet.has(wire.toNode)),
    editorLayout: { ...document.editorLayout, nodePositions: positions }
  };
}

export function duplicateNodes(document: GraphDocument, ids: string[]): GraphDocument {
  const idSet = new Set(ids);
  const nodeMap = new Map<string, string>();
  const pinMap = new Map<string, string>();
  const clones: NodeDocument[] = [];
  const positions = { ...document.editorLayout.nodePositions };
  for (const node of document.nodes) {
    if (!idSet.has(node.id)) continue;
    const nextId = crypto.randomUUID();
    nodeMap.set(node.id, nextId);
    const pins = node.pins.map((pin) => {
      const pinId = crypto.randomUUID();
      pinMap.set(pin.id, pinId);
      return { ...pin, id: pinId };
    });
    clones.push({ ...node, id: nextId, name: node.name, pins, properties: { ...node.properties } });
    const position = positions[node.id] ?? { x: 80, y: 80 };
    positions[nextId] = { x: position.x + 32, y: position.y + 32 };
  }
  const copied = document.connections
    .filter((wire) => idSet.has(wire.fromNode) && idSet.has(wire.toNode))
    .map((wire) => ({
      fromNode: nodeMap.get(wire.fromNode) ?? wire.fromNode,
      toNode: nodeMap.get(wire.toNode) ?? wire.toNode,
      fromPin: pinMap.get(wire.fromPin) ?? wire.fromPin,
      toPin: pinMap.get(wire.toPin) ?? wire.toPin
    }));
  return {
    ...document,
    nodes: [...document.nodes, ...clones],
    connections: [...document.connections, ...copied],
    editorLayout: { ...document.editorLayout, nodePositions: positions }
  };
}

export function nudgeNodes(document: GraphDocument, ids: string[], dx: number, dy: number): GraphDocument {
  const positions = { ...document.editorLayout.nodePositions };
  for (const id of ids) {
    const position = positions[id] ?? { x: 80, y: 80 };
    positions[id] = { x: position.x + dx, y: position.y + dy };
  }
  return { ...document, editorLayout: { ...document.editorLayout, nodePositions: positions } };
}

export function findVariableUses(document: GraphDocument, variable: { id: string; name: string }): { nodeId: string; message: string }[] {
  const needle = variable.name.toLowerCase();
  return document.nodes.flatMap((node) => {
    const hit = node.pins.some((pin) => (pin.dataType ?? "").toLowerCase().includes(needle))
      || Object.values(node.properties).some((value) => value.toLowerCase().includes(needle) || value === variable.id);
    return hit ? [{ nodeId: node.id, message: `${node.name} references ${variable.name}` }] : [];
  });
}

export function setSchemaFieldDefault(document: GraphDocument, schemaName: string, fieldName: string, value: string): GraphDocument {
  return {
    ...document,
    schemas: (document.schemas ?? []).map((schema) => schema.name !== schemaName ? schema : {
      ...schema,
      fields: schema.fields.map((field) => field.name === fieldName ? { ...field, defaultValue: value } : field)
    })
  };
}

export function setPinDefault(document: GraphDocument, nodeId: string, pinId: string, value: string): GraphDocument {
  return {
    ...document,
    nodes: document.nodes.map((node) => node.id !== nodeId ? node : {
      ...node,
      pins: node.pins.map((pin) => pin.id === pinId ? { ...pin, defaultValue: value } : pin)
    })
  };
}

export function setNodeProperty(document: GraphDocument, nodeId: string, key: string, value: string): GraphDocument {
  return {
    ...document,
    nodes: document.nodes.map((node) => node.id === nodeId
      ? { ...node, properties: { ...node.properties, [key]: value } }
      : node)
  };
}
