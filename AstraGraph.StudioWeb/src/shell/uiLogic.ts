import { createGraph, emptyAttributes, type GraphDocument, type NodeDocument, type PinDocument } from "../documents/graph";
import type { UiDocumentModel, UiLogicStepModel } from "./uiTree";

function id() {
  return crypto.randomUUID();
}

function pin(name: string, direction: "input" | "output", kind: "execution" | "data", dataType = ""): PinDocument {
  return { id: id(), name, direction, kind, dataType };
}

function node(nodeType: string, name: string, properties: Record<string, string>, pins: PinDocument[]): NodeDocument {
  return { id: id(), name, nodeType, properties, pins };
}

function astraType(typeName?: string) {
  switch ((typeName ?? "string").toLowerCase()) {
    case "int":
    case "integer":
      return "int32";
    case "float":
    case "single":
      return "float32";
    case "bool":
    case "boolean":
      return "bool";
    default:
      return typeName || "string";
  }
}

function connect(graph: GraphDocument, from: NodeDocument, fromPin: string, to: NodeDocument, toPin: string) {
  const source = from.pins.find((item) => item.name === fromPin && item.direction === "output");
  const target = to.pins.find((item) => item.name === toPin && item.direction === "input");
  if (!source || !target) return;
  graph.connections.push({ fromNode: from.id, fromPin: source.id, toNode: to.id, toPin: target.id });
}

function place(graph: GraphDocument, item: NodeDocument) {
  graph.editorLayout.nodePositions[item.id] = { x: 80, y: 40 + graph.nodes.length * 120 };
}

function payloadVariable(document: UiDocumentModel, elementId?: string) {
  const onElement = (document.bindings ?? []).find((item) => item.elementId === elementId && item.direction === "TwoWay")?.stateVariable;
  return onElement
    ?? (document.bindings ?? []).find((item) => item.direction === "TwoWay")?.stateVariable
    ?? (document.stateVariables ?? []).find((item) => item.scope === "Local")?.name
    ?? "";
}

function addSend(graph: GraphDocument, elementId: string, eventName: string, action: string, stateVariable: string) {
  const entry = node("UI.OnEvent", `On ${elementId}.${eventName}`, { ElementId: elementId, EventName: eventName }, [pin("Out", "output", "execution")]);
  const call = node("Native.Call", `Send ${action}`, { Method: "Ui.SendAction", Action: action, StateVariable: stateVariable }, [
    pin("In", "input", "execution"),
    pin("Out", "output", "execution")
  ]);
  graph.nodes.push(entry, call);
  connect(graph, entry, "Out", call, "In");
  place(graph, entry);
  place(graph, call);
}

export function buildLogicGraphs(document: UiDocumentModel): { client: GraphDocument; server: GraphDocument } {
  const client = { ...createGraph(`${document.name} Client`), kind: "UI", side: "client", attributes: emptyAttributes() };
  const steps = document.logic ?? [];
  for (const step of steps.filter((item) => item.kind.toLowerCase() === "onevent")) {
    const send = steps.find((item) => item.kind.toLowerCase() === "sendaction" && item.elementId === step.elementId)
      ?? eventSend(document, step);
    if (!send?.actionName) continue;
    addSend(client, step.elementId ?? "", step.eventName ?? "OnPressed", send.actionName, send.stateVariable || payloadVariable(document, step.elementId));
  }
  if (client.nodes.length === 0) {
    for (const item of document.events ?? []) {
      addSend(client, item.elementId, item.eventName, item.targetAction, payloadVariable(document, item.elementId));
    }
  }
  client.variables = (document.stateVariables ?? []).map((item) => ({ id: item.id, name: item.name, typeName: astraType(item.typeName) }));

  const server = { ...createGraph(`${document.name} Server`), kind: "UI", side: "server", attributes: emptyAttributes() };
  const actions = uniqueActions(document);
  for (const action of actions) {
    const assign = steps.find((item) => item.kind.toLowerCase() === "setstate" && item.actionName === action);
    const stateName = assign?.stateVariable ?? (document.stateVariables ?? []).find((item) => item.scope === "Server")?.name ?? "result";
    const parameter = assign?.propertyName ?? "value";
    const typeName = astraType((document.stateVariables ?? []).find((item) => item.name === stateName)?.typeName);
    const entry = node("UI.OnAction", `On BUI ${action}`, { Action: action }, [
      pin("Out", "output", "execution"),
      pin("User", "output", "data", "string"),
      pin("Entity", "output", "data", "string"),
      pin(parameter, "output", "data", typeName)
    ]);
    const set = node("Core.VariableAssign", `Set ${stateName}`, { VariableName: stateName }, [
      pin("In", "input", "execution"),
      pin("Out", "output", "execution"),
      pin("Value", "input", "data", typeName)
    ]);
    server.nodes.push(entry, set);
    connect(server, entry, "Out", set, "In");
    connect(server, entry, parameter, set, "Value");
    place(server, entry);
    place(server, set);
    if (!server.variables.some((item) => item.name === stateName)) {
      server.variables.push({ id: id(), name: stateName, typeName });
    }
  }
  return { client, server };
}

function eventSend(document: UiDocumentModel, step: UiLogicStepModel): UiLogicStepModel | undefined {
  const match = (document.events ?? []).find((item) => item.elementId === step.elementId);
  if (!match) return undefined;
  return { id: match.subscriptionId, kind: "SendAction", elementId: match.elementId, actionName: match.targetAction, stateVariable: payloadVariable(document, match.elementId) };
}

function uniqueActions(document: UiDocumentModel) {
  const names = (document.events ?? []).map((item) => item.targetAction).filter(Boolean);
  return [...new Set(names)];
}
