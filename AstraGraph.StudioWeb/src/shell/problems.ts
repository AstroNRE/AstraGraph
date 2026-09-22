import { insertNode, removeNodes, setNodeProperty, type GraphDocument } from "../documents/graph";

export type ProblemPanel = "Errors" | "Warnings" | "Security" | "Prediction" | "Performance" | "Migration";

export function problemGroup(code: string, severity = "error"): ProblemPanel {
  if (code === "POL003") return "Prediction";
  if (code.startsWith("POL")) return "Security";
  if (code.startsWith("PRF") || code.startsWith("PERF")) return "Performance";
  if (code.startsWith("MIG")) return "Migration";
  if (severity === "warning" || severity === "info") return "Warnings";
  return "Errors";
}

export function suggestedFixFor(code: string, explicit?: string): string | undefined {
  if (explicit) return explicit;
  if (code === "DOC001" || code === "DOC002") return "remove-node";
  return undefined;
}

export function applyQuickFix(document: GraphDocument, problem: { suggestedFix?: string; nodeId?: string; pinId?: string }): GraphDocument {
  const fix = problem.suggestedFix;
  if (fix === "remove-node" && problem.nodeId) return removeNodes(document, [problem.nodeId]);
  if (fix === "remove-connection" && problem.pinId) {
    return { ...document, connections: document.connections.filter((wire) => wire.fromPin !== problem.pinId && wire.toPin !== problem.pinId) };
  }
  if (fix === "set-side:Server") return { ...document, side: "Server" };
  if (fix === "set-deterministic" && problem.nodeId) return setNodeProperty(document, problem.nodeId, "deterministic", "true");
  if (fix === "add-entry") {
    const id = crypto.randomUUID();
    return insertNode(document, {
      id,
      name: "Update",
      nodeType: "Event.Update",
      properties: {},
      pins: [{ id: crypto.randomUUID(), name: "Then", direction: "output", kind: "execution" }]
    });
  }
  return document;
}
