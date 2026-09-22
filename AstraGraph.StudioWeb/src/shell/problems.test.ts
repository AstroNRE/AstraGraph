import { describe, expect, it } from "vitest";
import { problemGroup, suggestedFixFor } from "./problems";

describe("problem groups", () => {
  it("maps compiler codes onto panels", () => {
    expect(problemGroup("DOC003")).toBe("Errors");
    expect(problemGroup("TYP002", "warning")).toBe("Warnings");
    expect(problemGroup("POL001")).toBe("Security");
    expect(problemGroup("POL003")).toBe("Prediction");
    expect(problemGroup("PRF001")).toBe("Performance");
    expect(problemGroup("MIG001", "warning")).toBe("Migration");
    expect(suggestedFixFor("DOC001")).toBe("remove-node");
    expect(suggestedFixFor("FLO001")).toBeUndefined();
  });
});