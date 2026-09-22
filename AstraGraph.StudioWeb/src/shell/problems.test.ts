import { describe, expect, it } from "vitest";
import { createGraph } from "../documents/graph";
import { applyQuickFix, problemGroup, suggestedFixFor } from "./problems";

describe("problem groups", () => {
  it("maps compiler codes onto panels", () => {
    expect(problemGroup("DOC003")).toBe("Errors");
    expect(problemGroup("TYP002", "warning")).toBe("Warnings");
    expect(problemGroup("POL001")).toBe("Security");
    expect(problemGroup("POL003")).toBe("Prediction");
    expect(problemGroup("PRF001")).toBe("Performance");
    expect(problemGroup("MIG001", "warning")).toBe("Migration");
    expect(suggestedFixFor("DOC001")).toBe("remove-node");
    expect(suggestedFixFor("DOC003", "remove-connection")).toBe("remove-connection");
    expect(suggestedFixFor("FLO001")).toBeUndefined();
  });

  it("applies a compiler connection fix", () => {
    const graph = createGraph("Door");
    const next = {
      ...graph,
      connections: [{ fromNode: "a", fromPin: "out", toNode: "b", toPin: "in" }]
    };
    const fixed = applyQuickFix(next, { suggestedFix: "remove-connection", pinId: "out" });
    expect(fixed.connections).toEqual([]);
    expect(applyQuickFix(graph, { suggestedFix: "set-side:Server" }).side).toBe("Server");
  });
});