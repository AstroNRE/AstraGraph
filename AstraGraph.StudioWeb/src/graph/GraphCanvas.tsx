import { useEffect, useRef, useState } from "react";
import {
  Background,
  ConnectionLineType,
  Controls,
  Handle,
  MiniMap,
  Panel,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState,
  useReactFlow,
  type Connection,
  type Node,
  type NodeProps
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import "./canvas.css";
import { alignNodes, applyFlow, emptyAttributes, setNodeProperty, setPinDefault, setSchemaFieldDefault, toFlow, type FlowEdge, type GraphDocument, type GraphSchemaDocument, type PinDocument } from "../documents/graph";

type PinMark = "compatible" | "incompatible";

type AstraData = {
  label: string;
  nodeType: string;
  inputs: PinDocument[];
  outputs: PinDocument[];
  properties?: Record<string, string>;
  wired?: string[];
  schemas?: GraphSchemaDocument[];
  pure?: boolean;
  side?: string;
  heat?: number;
  marks?: Record<string, PinMark>;
  reasons?: Record<string, string>;
  alertPin?: string;
  comment?: boolean;
  onProperty?: (nodeId: string, key: string, value: string) => void;
  onPinDefault?: (nodeId: string, pinId: string, value: string) => void;
  onSchemaDefault?: (schemaName: string, fieldName: string, value: string) => void;
};

const inlinePropertyKeys = ["eventType", "componentType", "ComponentType", "Schema", "Field", "Member", "Method", "Function"];

function nodeTone(nodeType: string): string {
  if (nodeType.startsWith("Event")) return "event";
  if (nodeType.startsWith("Flow") || nodeType.startsWith("Core.Branch") || nodeType === "Branch") return "flow";
  if (nodeType.startsWith("Schema")) return "schema";
  if (nodeType.startsWith("Entity")) return "entity";
  if (nodeType.startsWith("Function")) return "function";
  return "call";
}

function shortType(nodeType: string): string {
  const dot = nodeType.lastIndexOf(".");
  return dot >= 0 ? nodeType.slice(dot + 1) : nodeType;
}

function edgeTone(pin: PinDocument | undefined): "exec" | "bool" | "data" {
  if (!pin || pin.kind === "execution") return "exec";
  const type = (pin.dataType ?? "").toLowerCase();
  if (type === "bool" || type === "boolean") return "bool";
  return "data";
}

const handleInRow = { position: "relative", top: "auto", right: "auto", bottom: "auto", left: "auto", transform: "none" } as const;

function pinShape(pin: { name: string; kind: string }) {
  if (pin.kind === "data") return "data";
  return pin.name === "Then" ? "event" : "execution";
}

function PinValue({ pin, onChange }: { pin: PinDocument; onChange: (value: string) => void }) {
  const type = (pin.dataType ?? "").toLowerCase();
  const value = pin.defaultValue ?? "";
  if (type === "bool" || type === "boolean") {
    return <input className="nodrag nopan node-check" type="checkbox" checked={value === "true"} onChange={(event) => onChange(event.target.checked ? "true" : "false")} />;
  }
  if (type.includes("int") || type.includes("float") || type.includes("double")) {
    return <input className="nodrag nopan node-value" type="number" value={value} onChange={(event) => onChange(event.target.value)} />;
  }
  if (type.includes("string") || type.includes("proto")) {
    return <input className="nodrag nopan node-value" value={value} placeholder={pin.name} onChange={(event) => onChange(event.target.value)} />;
  }
  return null;
}

function AstraNodeView({ id, data }: NodeProps<Node<AstraData, "astra">>) {
  if (data.comment) return <div className="astra-comment"><strong>{data.label}</strong><div>{data.nodeType}</div></div>;
  const wired = new Set(data.wired ?? []);
  const properties = data.properties ?? {};
  const schemaName = properties.Schema ?? "";
  const fieldName = properties.Field ?? "";
  const schemaField = data.nodeType === "Schema.GetField"
    ? (data.schemas ?? []).find((schema) => schema.name === schemaName)?.fields.find((field) => field.name === fieldName)
    : undefined;
  const prototypeField = (schemaField?.typeName ?? "").includes("EntProtoId");
  return (
    <div className={`astra-node tone-${nodeTone(data.nodeType)}`}>
      <header className="astra-head">
        <span className="astra-node-title">{data.label}</span>
        <span className="astra-node-type">{shortType(data.nodeType)}</span>
        {data.pure ? <span className="node-badge">pure</span> : null}
        {data.heat ? <span className="node-badge">{data.heat}</span> : null}
      </header>
      <div className="astra-body">
        {inlinePropertyKeys.filter((key) => key in properties).map((key) => (
          <label className="node-field" key={key}>
            <span>{key}</span>
            <input className="nodrag nopan node-value" value={properties[key] ?? ""} onChange={(event) => data.onProperty?.(id, key, event.target.value)} />
          </label>
        ))}
        {schemaField ? (
          <label className="node-field">
            <span>{prototypeField ? "Prototype" : "Default"}</span>
            <input className="nodrag nopan node-value" value={schemaField.defaultValue ?? ""} onChange={(event) => data.onSchemaDefault?.(schemaName, fieldName, event.target.value)} />
          </label>
        ) : null}
        {data.inputs.map((pin) => (
          <div className="pin-row in" key={pin.id}>
            <Handle
              id={pin.id}
              className={`pin ${pinShape(pin)} ${data.marks?.[pin.id] ?? ""} ${data.alertPin === pin.id ? "alert" : ""}`}
              type="target"
              position={Position.Left}
              role="button"
              aria-label={`Input ${pin.name}`}
              title={data.reasons?.[pin.id] || `${pin.name}: ${pin.dataType || pin.kind}`}
              style={handleInRow}
            />
            <span className="pin-name">{pin.name}</span>
            {pin.kind === "data" && !wired.has(pin.id) ? <PinValue pin={pin} onChange={(value) => data.onPinDefault?.(id, pin.id, value)} /> : null}
          </div>
        ))}
        {data.outputs.map((pin) => (
          <div className="pin-row out" key={pin.id}>
            <span className="pin-name">{pin.name}</span>
            <Handle
              id={pin.id}
              className={`pin ${pinShape(pin)} ${data.marks?.[pin.id] ?? ""} ${data.alertPin === pin.id ? "alert" : ""}`}
              type="source"
              position={Position.Right}
              role="button"
              aria-label={`Output ${pin.name}`}
              title={data.reasons?.[pin.id] || `${pin.name}: ${pin.dataType || pin.kind}`}
              style={handleInRow}
            />
          </div>
        ))}
      </div>
    </div>
  );
}

const nodeTypes = { astra: AstraNodeView };

function readBookmarks(value: string | undefined): { id: string; name: string }[] {
  try {
    const parsed = JSON.parse(value || "[]") as { id?: string; name?: string }[];
    return Array.isArray(parsed) ? parsed.filter((item) => item.id).map((item) => ({ id: String(item.id), name: String(item.name || "Node") })) : [];
  } catch {
    return [];
  }
}

function useFlowColorMode(): "dark" | "light" {
  const [mode, setMode] = useState<"dark" | "light">(() => document.documentElement.dataset.theme === "light" ? "light" : "dark");
  useEffect(() => {
    const root = document.documentElement;
    const sync = () => setMode(root.dataset.theme === "light" ? "light" : "dark");
    const observer = new MutationObserver(sync);
    observer.observe(root, { attributes: true, attributeFilter: ["data-theme"] });
    return () => observer.disconnect();
  }, []);
  return mode;
}

export function GraphCanvas(props: {
  document: GraphDocument;
  selectedId?: string;
  focusToken?: number;
  alertPin?: string;
  heat?: Record<string, number>;
  pinMarks?: Record<string, PinMark>;
  pinReasons?: Record<string, string>;
  onChange: (next: GraphDocument) => void;
  onSelect?: (id: string) => void;
  onReject?: (reason: string) => void;
  onWireStart?: (nodeId: string, pinId: string) => void;
  onWireDrop?: (nodeId: string, pinId: string) => void;
  authorizeConnection?: (source: PinDocument & { nodeId: string }, target: PinDocument & { nodeId: string }, existing: GraphDocument["connections"]) => Promise<{ ok: boolean; reason?: string }>;
}) {
  return (
    <ReactFlowProvider>
      <CanvasSurface {...props} />
    </ReactFlowProvider>
  );
}

function CanvasSurface(props: {
  document: GraphDocument;
  selectedId?: string;
  focusToken?: number;
  alertPin?: string;
  heat?: Record<string, number>;
  pinMarks?: Record<string, PinMark>;
  pinReasons?: Record<string, string>;
  onChange: (next: GraphDocument) => void;
  onSelect?: (id: string) => void;
  onReject?: (reason: string) => void;
  onWireStart?: (nodeId: string, pinId: string) => void;
  onWireDrop?: (nodeId: string, pinId: string) => void;
  authorizeConnection?: (source: PinDocument & { nodeId: string }, target: PinDocument & { nodeId: string }, existing: GraphDocument["connections"]) => Promise<{ ok: boolean; reason?: string }>;
}) {
  const colorMode = useFlowColorMode();
  const { fitView } = useReactFlow();
  const [nodes, setNodes, onNodesChange] = useNodesState<Node<AstraData, "astra">>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<FlowEdge>([]);
  const [picked, setPicked] = useState<string[]>([]);
  const frameRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const flow = toFlow(props.document);
    setNodes(flow.nodes.map((node) => ({
      ...node,
      type: "astra" as const,
      selected: node.id === props.selectedId,
      data: {
        ...node.data,
        heat: props.heat?.[node.id],
        marks: props.pinMarks,
        reasons: props.pinReasons,
        alertPin: props.alertPin,
        properties: props.document.nodes.find((item) => item.id === node.id)?.properties,
        wired: props.document.connections.filter((wire) => wire.toNode === node.id).map((wire) => wire.toPin),
        schemas: props.document.schemas,
        onProperty: (nodeId, key, value) => props.onChange(setNodeProperty(props.document, nodeId, key, value)),
        onPinDefault: (nodeId, pinId, value) => props.onChange(setPinDefault(props.document, nodeId, pinId, value)),
        onSchemaDefault: (schemaName, fieldName, value) => props.onChange(setSchemaFieldDefault(props.document, schemaName, fieldName, value))
      }
    })));
    setEdges(flow.edges.map((edge) => {
      const source = props.document.nodes.find((node) => node.id === edge.source);
      const pin = source?.pins.find((item) => item.id === edge.sourceHandle);
      const alert = props.alertPin && (edge.sourceHandle === props.alertPin || edge.targetHandle === props.alertPin);
      return { ...edge, type: "default", className: alert ? "edge-alert" : `edge-${edgeTone(pin)}`, interactionWidth: 20 };
    }));
  }, [props.alertPin, props.document, props.heat, props.pinMarks, props.pinReasons, props.selectedId, setEdges, setNodes]);

  useEffect(() => {
    if (!props.focusToken || !props.selectedId) return;
    void fitView({ nodes: [{ id: props.selectedId }], duration: 180, padding: 0.45 });
  }, [fitView, props.focusToken, props.selectedId]);

  function commit(nextNodes: { id: string; position: { x: number; y: number } }[], nextEdges: FlowEdge[]) {
    props.onChange(applyFlow(props.document, nextNodes, nextEdges));
  }

  async function onConnect(connection: Connection) {
    if (!connection.source || !connection.target || !connection.sourceHandle || !connection.targetHandle) return;
    const sourceNode = props.document.nodes.find((node) => node.id === connection.source);
    const targetNode = props.document.nodes.find((node) => node.id === connection.target);
    const sourcePin = sourceNode?.pins.find((pin) => pin.id === connection.sourceHandle);
    const targetPin = targetNode?.pins.find((pin) => pin.id === connection.targetHandle);
    if (props.authorizeConnection && sourcePin && targetPin) {
      const verdict = await props.authorizeConnection(
        { ...sourcePin, nodeId: connection.source },
        { ...targetPin, nodeId: connection.target },
        props.document.connections);
      if (!verdict.ok) {
        props.onReject?.(verdict.reason || "Connection refused");
        return;
      }
    }
    const next = [...edges, {
      id: `${connection.sourceHandle}-${connection.targetHandle}`,
      source: connection.source,
      target: connection.target,
      sourceHandle: connection.sourceHandle,
      targetHandle: connection.targetHandle
    }];
    setEdges(next);
    commit(nodes, next);
  }

  return (
    <div ref={frameRef} className="canvas-frame">
    <ReactFlow
      colorMode={colorMode}
      nodes={nodes}
      edges={edges}
      nodeTypes={nodeTypes}
      defaultEdgeOptions={{ type: "default", interactionWidth: 20 }}
      connectionLineType={ConnectionLineType.Bezier}
      connectionLineStyle={{ strokeWidth: 2.5 }}
      onNodesChange={onNodesChange}
      onEdgesChange={onEdgesChange}
      onConnect={onConnect}
      onConnectStart={(_event, params) => {
        if (params.nodeId && params.handleId) props.onWireStart?.(params.nodeId, params.handleId);
      }}
      onConnectEnd={(_event, state) => {
        if (state.toNode || !state.fromNode?.id || !state.fromHandle?.id) return;
        props.onWireDrop?.(state.fromNode.id, state.fromHandle.id);
      }}
      isValidConnection={(connection) => {
        const handle = connection.targetHandle ?? connection.sourceHandle;
        if (!handle || !props.pinMarks || Object.keys(props.pinMarks).length === 0) return true;
        return props.pinMarks[handle] !== "incompatible";
      }}
      onNodeClick={(_event, node) => props.onSelect?.(node.id)}
      onPaneClick={() => props.onSelect?.("")}
      onNodeDragStop={(_, node) => {
        const snapped = { x: Math.round(node.position.x / 16) * 16, y: Math.round(node.position.y / 16) * 16 };
        commit(nodes.map((item) => item.id === node.id ? { ...item, position: snapped } : item), edges);
      }}
      deleteKeyCode={null}
      selectionOnDrag
      multiSelectionKeyCode="Shift"
      onlyRenderVisibleElements
      snapToGrid
      snapGrid={[16, 16]}
      fitView
      onMove={(_, viewport) => frameRef.current?.classList.toggle("semantic-far", viewport.zoom < 0.6)}
      onSelectionChange={({ nodes: selected }) => {
        const ids = selected.map((node) => node.id);
        setPicked((current) => current.length === ids.length && current.every((id, index) => id === ids[index]) ? current : ids);
      }}
    >
      <Panel position="top-left" className="canvas-tools">
        <button type="button" onClick={() => props.onChange(alignNodes(props.document, picked, "left"))}>Align left</button>
        <button type="button" onClick={() => props.onChange(alignNodes(props.document, picked, "top"))}>Align top</button>
        <button type="button" onClick={() => props.onChange({
          ...props.document,
          editorLayout: {
            ...props.document.editorLayout,
            comments: [...props.document.editorLayout.comments, { id: crypto.randomUUID(), title: "Comment", text: "", x: 48, y: 48, width: 180, height: 80 }]
          }
        })}>Comment</button>
        <button type="button" onClick={() => {
          if (!props.selectedId) return;
          const bookmarks = readBookmarks(props.document.attributes?.bookmarks);
          const node = props.document.nodes.find((item) => item.id === props.selectedId);
          bookmarks.push({ id: props.selectedId, name: node?.name || "Node" });
          props.onChange({ ...props.document, attributes: { ...emptyAttributes(), ...props.document.attributes, bookmarks: JSON.stringify(bookmarks) } });
        }}>Bookmark</button>
        {readBookmarks(props.document.attributes?.bookmarks).map((bookmark) => (
          <button key={bookmark.id} type="button" onClick={() => props.onSelect?.(bookmark.id)}>{bookmark.name}</button>
        ))}
      </Panel>
      <Background gap={18} size={1} />
      <Controls showInteractive={false} />
      <MiniMap pannable zoomable style={{ width: 132, height: 88 }} />
    </ReactFlow>
    </div>
  );
}
