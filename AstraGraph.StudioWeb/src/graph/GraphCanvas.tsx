import { useEffect, useState } from "react";
import {
  Background,
  Controls,
  Handle,
  MiniMap,
  Position,
  ReactFlow,
  useEdgesState,
  useNodesState,
  type Connection,
  type Node,
  type NodeProps
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import "./canvas.css";
import { applyFlow, toFlow, type FlowEdge, type GraphDocument } from "../documents/graph";

type AstraData = {
  label: string;
  nodeType: string;
  inputs: { id: string }[];
  outputs: { id: string }[];
};

function AstraNodeView({ data }: NodeProps<Node<AstraData, "astra">>) {
  return (
    <div className={data.nodeType.startsWith("Event") ? "astra-node event" : "astra-node"}>
      {data.inputs.map((pin, index) => (
        <Handle key={pin.id} id={pin.id} type="target" position={Position.Left} style={{ top: 28 + index * 16 }} />
      ))}
      <div className="astra-node-title">{data.label}</div>
      <div className="astra-node-type">{data.nodeType}</div>
      {data.outputs.map((pin, index) => (
        <Handle key={pin.id} id={pin.id} type="source" position={Position.Right} style={{ top: 28 + index * 16 }} />
      ))}
    </div>
  );
}

const nodeTypes = { astra: AstraNodeView };

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

export function GraphCanvas(props: { document: GraphDocument; onChange: (next: GraphDocument) => void }) {
  const colorMode = useFlowColorMode();
  const [nodes, setNodes, onNodesChange] = useNodesState<Node<AstraData, "astra">>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<FlowEdge>([]);

  useEffect(() => {
    const flow = toFlow(props.document);
    setNodes(flow.nodes.map((node) => ({ ...node, type: "astra" as const })));
    setEdges(flow.edges);
  }, [props.document, setEdges, setNodes]);

  function commit(nextNodes: { id: string; position: { x: number; y: number } }[], nextEdges: FlowEdge[]) {
    props.onChange(applyFlow(props.document, nextNodes, nextEdges));
  }

  function onConnect(connection: Connection) {
    if (!connection.source || !connection.target || !connection.sourceHandle || !connection.targetHandle) return;
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
    <ReactFlow
      colorMode={colorMode}
      nodes={nodes}
      edges={edges}
      nodeTypes={nodeTypes}
      onNodesChange={onNodesChange}
      onEdgesChange={onEdgesChange}
      onConnect={onConnect}
      onNodeDragStop={(_, node) => commit(nodes.map((item) => item.id === node.id ? { ...item, position: node.position } : item), edges)}
      fitView
    >
      <Background />
      <Controls />
      <MiniMap />
    </ReactFlow>
  );
}
