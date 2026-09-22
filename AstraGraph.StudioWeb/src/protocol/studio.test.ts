import { describe, expect, it } from "vitest";
import { frame, unframe } from "./frame";
import { canCompile, canPublish, permissionBits } from "../permissions/gate";
import { addNode, createGraph, edit, redo, undo } from "../documents/graph";

describe("protocol frame", () => {
  it("roundtrips kind and payload", () => {
    const bytes = frame("draft.publish.request", { graphId: "abc" });
    const message = unframe(bytes);
    expect(message?.kind).toBe("draft.publish.request");
    expect(message?.body.graphId).toBe("abc");
    expect(String(message?.body.kind)).not.toContain("fetch(\"/api/status\")");
  });
});

describe("permissions", () => {
  it("gates publish separately from compile", () => {
    const developer = permissionBits(1 | 2 | 4 | 32 | 64);
    expect(canCompile(developer, true)).toBe(true);
    expect(canPublish(developer, true)).toBe(false);
    expect(canPublish(developer | 8, true)).toBe(true);
    expect(canPublish(developer | 8, false)).toBe(false);
  });
});

describe("documents", () => {
  it("undo restores the previous graph", () => {
    const created = { past: [], present: createGraph("One"), future: [] };
    const added = edit(created, addNode(created.present, "Event.Start", "On Start"));
    expect(added.present.nodes).toHaveLength(1);
    expect(undo(added).present.nodes).toHaveLength(0);
    expect(redo(undo(added)).present.nodes).toHaveLength(1);
  });
});
