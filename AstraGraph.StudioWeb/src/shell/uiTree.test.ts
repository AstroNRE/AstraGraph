import { describe, expect, it } from "vitest";
import { alignmentGuides, boxesInRect, builtinControls, canParent, findNode, insertChild, layoutTree, remapIds, removeNode, reorder, shortType, snapValue, type UiNode } from "./uiTree";

const root: UiNode = {
  id: "root",
  elementType: "BoxContainer",
  controlTypeId: "Robust.Client.UserInterface.Controls.BoxContainer",
  children: [
    { id: "a", elementType: "Label", children: [], text: "A" },
    { id: "b", elementType: "Button", children: [], text: "B" }
  ]
};

describe("ui tree", () => {
  it("inserts, reorders, and remaps ids", () => {
    const inserted = insertChild(root, "root", { id: "c", elementType: "LineEdit", children: [] }, 1);
    expect(inserted.children.map((child) => child.id)).toEqual(["a", "c", "b"]);
    const moved = reorder(inserted, "root", 2, 0);
    expect(moved.children.map((child) => child.id)).toEqual(["b", "a", "c"]);
    const copy = remapIds(findNode(moved, "b")!, () => "fresh");
    expect(copy.id).toBe("fresh");
    expect(removeNode(moved, "a").children.map((child) => child.id)).toEqual(["b", "c"]);
  });

  it("uses the catalog instead of a hardcoded palette", () => {
    expect(builtinControls.some((item) => shortType(item.typeId) === "Button")).toBe(true);
    expect(canParent(builtinControls, { id: "label", elementType: "Label", controlTypeId: "Robust.Client.UserInterface.Controls.Label", children: [] })).toBe(false);
    expect(canParent(builtinControls, root)).toBe(true);
  });

  it("snaps layout boxes and selects a marquee", () => {
    const layout: UiNode = {
      id: "layout",
      elementType: "LayoutContainer",
      controlTypeId: "Robust.Client.UserInterface.Controls.LayoutContainer",
      children: [
        { id: "a", elementType: "Button", children: [], properties: { "editor.x": "10", "editor.y": "6" } },
        { id: "b", elementType: "Label", children: [], properties: { "editor.x": "40", "editor.y": "40" } }
      ]
    };
    expect(snapValue(10, 8)).toBe(8);
    const boxes = layoutTree(layout, 0, 0, 200, 8);
    expect(boxes.find((box) => box.id === "a")?.x).toBe(8);
    expect(boxesInRect(boxes, { x: 0, y: 0, w: 30, h: 30 })).toContain("a");
    expect(alignmentGuides(boxes, "b", 8, 40).x).toBe(8);
  });
});
