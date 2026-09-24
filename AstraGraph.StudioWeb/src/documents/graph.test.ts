import { describe, expect, it } from "vitest";
import { addNode, connectPins, createGraph, duplicateNodes, findVariableUses, nudgeNodes, parseGraph, removeNodes, serializeGraph, setNodeProperty, setPinDefault, setSchemaFieldDefault } from "./graph";

describe("graph editing", () => {
  it("duplicates a node with fresh ids and keeps the original wire", () => {
    const first = addNode(createGraph("Demo"), "Flow.Branch", "Branch");
    const second = addNode(first, "Core.VariableAssign", "Assign");
    const wired = connectPins(second, second.nodes[0].id, second.nodes[0].pins[1].id, second.nodes[1].id, second.nodes[1].pins[0].id);
    const copy = duplicateNodes(wired, [wired.nodes[0].id]);
    expect(copy.nodes).toHaveLength(3);
    expect(copy.nodes[2].id).not.toBe(wired.nodes[0].id);
    expect(copy.connections).toHaveLength(1);
    expect(copy.connections[0].fromNode).toBe(wired.nodes[0].id);
  });

  it("removes a node together with its wires", () => {
    const graph = addNode(addNode(createGraph("Demo"), "Flow.Branch", "Branch"), "Core.VariableAssign", "Assign");
    const removed = removeNodes(graph, [graph.nodes[0].id]);
    expect(removed.nodes.map((node) => node.name)).toEqual(["Assign"]);
    expect(removed.editorLayout.nodePositions[graph.nodes[0].id]).toBeUndefined();
  });

  it("nudges layout without touching semantic nodes", () => {
    const graph = addNode(createGraph("Demo"), "Event.Tick", "On Tick");
    const moved = nudgeNodes(graph, [graph.nodes[0].id], 16, 0);
    expect(moved.nodes).toEqual(graph.nodes);
    expect(moved.editorLayout.nodePositions[graph.nodes[0].id].x).toBe(graph.editorLayout.nodePositions[graph.nodes[0].id].x + 16);
  });

  it("gives struct and list nodes their pins", () => {
    const graph = addNode(addNode(createGraph("Parts"), "Schema.SetField", "Set"), "List.Add", "Add");
    expect(graph.nodes[0].pins.map((pin) => pin.name)).toEqual(["In", "Out", "Target", "Value", "Result"]);
    expect(graph.nodes[1].pins.filter((pin) => pin.direction === "output").map((pin) => pin.name)).toEqual(["Out", "List"]);
    const identified = addNode(graph, "PersistentId.New", "Id");
    expect(identified.nodes[2].pins.map((pin) => pin.name)).toEqual(["In", "Out", "Id"]);
    expect(identified.nodes[2].properties.IsDeterministic).toBe("false");
  });

  it("keeps variable persistence across serialize and finds a reference", () => {
    const graph = addNode(createGraph("Demo"), "Core.VariableAssign", "Assign");
    graph.nodes[0].properties.variable = "Health";
    const withVariable = {
      ...graph,
      variables: [{ id: "var-1", name: "Health", typeName: "int32", defaultValue: "1", persistent: true, replicated: true }]
    };
    const parsed = parseGraph(serializeGraph(withVariable));
    expect(parsed.variables[0].persistent).toBe(true);
    expect(parsed.variables[0].replicated).toBe(true);
    expect(findVariableUses(parsed, parsed.variables[0]).map((use) => use.nodeId)).toEqual([parsed.nodes[0].id]);
  });

  it("writes a schema field default from the graph", () => {
    const graph = {
      ...createGraph("Weapon"),
      schemas: [{ id: "schema-1", name: "MothroachStrike", isComponent: true, fields: [
        { id: "field-1", name: "spawn", typeName: "EntProtoId<EntityPrototype>", defaultValue: "MobMothroach" },
        { id: "field-2", name: "count", typeName: "int32", defaultValue: "1" }
      ] }]
    };
    const next = setSchemaFieldDefault(graph, "MothroachStrike", "count", "5");
    expect(next.schemas?.[0].fields[1].defaultValue).toBe("5");
    expect(next.schemas?.[0].fields[0].defaultValue).toBe("MobMothroach");
  });

  it("writes an unwired pin default", () => {
    const graph = addNode(createGraph("Demo"), "Flow.For", "Loop");
    const pin = graph.nodes[0].pins.find((item) => item.name === "Start");
    expect(pin).toBeTruthy();
    const next = setPinDefault(graph, graph.nodes[0].id, pin!.id, "2");
    expect(next.nodes[0].pins.find((item) => item.id === pin!.id)?.defaultValue).toBe("2");
  });

  it("writes a node property in place", () => {
    const graph = addNode(createGraph("Demo"), "Flow.Branch", "Branch");
    const next = setNodeProperty(graph, graph.nodes[0].id, "pure", "true");
    expect(next.nodes[0].properties.pure).toBe("true");
  });
});
