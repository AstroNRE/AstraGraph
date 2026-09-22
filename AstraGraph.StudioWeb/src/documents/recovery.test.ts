import { describe, expect, it } from "vitest";
import { chooseRecovery } from "./recovery";

describe("draft recovery", () => {
  it("offers the local copy only when it differs from the server", () => {
    expect(chooseRecovery("{\"name\":\"Live\"}", null)).toBe("server");
    expect(chooseRecovery("{\"name\":\"Live\"}", "{\"name\":\"Live\"}")).toBe("server");
    expect(chooseRecovery("{\"name\":\"Live\"}", "{\"name\":\"Dirty\"}")).toBe("local");
  });
});
