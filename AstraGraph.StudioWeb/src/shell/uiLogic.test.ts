import { describe, expect, it } from "vitest";
import { buildLogicGraphs } from "./uiLogic";
import type { UiDocumentModel } from "./uiTree";

const document: UiDocumentModel = {
  id: "doc",
  name: "Mothroach Converter",
  width: 480,
  height: 320,
  documentKind: "BUI",
  root: {
    id: "root",
    elementType: "BoxContainer",
    children: [
      { id: "result", elementType: "Label", children: [] },
      { id: "input", elementType: "LineEdit", children: [] },
      { id: "submit", elementType: "Button", children: [], text: "Submit" }
    ]
  },
  bindings: [
    { bindingId: "b1", elementId: "input", targetProperty: "Text", stateVariable: "inputText", direction: "TwoWay" },
    { bindingId: "b2", elementId: "result", targetProperty: "Text", stateVariable: "result", direction: "OneWay" }
  ],
  events: [{ subscriptionId: "e1", elementId: "submit", eventName: "OnPressed", targetAction: "Submit" }],
  stateVariables: [
    { id: "inputText", name: "inputText", typeName: "string", scope: "Local" },
    { id: "result", name: "result", typeName: "string", scope: "Server" }
  ],
  logic: [
    { id: "l1", kind: "OnEvent", elementId: "submit", eventName: "OnPressed" },
    { id: "l2", kind: "SendAction", elementId: "submit", actionName: "Submit", stateVariable: "inputText" },
    { id: "l3", kind: "SetState", actionName: "Submit", stateVariable: "result", propertyName: "text" }
  ]
};

describe("ui logic graphs", () => {
  it("builds client and server graphs for a BUI", () => {
    const graphs = buildLogicGraphs(document);
    expect(graphs.client.side).toBe("client");
    expect(graphs.client.nodes.some((node) => node.nodeType === "UI.OnEvent")).toBe(true);
    expect(graphs.client.nodes.some((node) => node.properties.Method === "Ui.SendAction" && node.properties.Action === "Submit")).toBe(true);
    expect(graphs.server.nodes.some((node) => node.nodeType === "UI.OnAction" && node.properties.Action === "Submit")).toBe(true);
    expect(graphs.server.nodes.some((node) => node.nodeType === "Core.VariableAssign" && node.properties.VariableName === "result")).toBe(true);
    expect(graphs.client.connections.length).toBeGreaterThan(0);
    expect(graphs.server.connections.length).toBeGreaterThan(0);
  });
});
