import { describe, expect, it } from "vitest";
import { fuzzyScore, rankTexts } from "./fuzzy";

describe("fuzzy ranking", () => {
  it("prefers a prefix over a scattered match", () => {
    expect(fuzzyScore("door", "DoorState")).toBeGreaterThan(fuzzyScore("door", "rendered"));
    const ranked = rankTexts("door", [
      { id: "a", text: "rendered order" },
      { id: "b", text: "Door" }
    ]);
    expect(ranked[0]?.id).toBe("b");
  });
});
