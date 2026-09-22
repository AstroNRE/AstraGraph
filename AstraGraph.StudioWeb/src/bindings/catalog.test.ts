import { describe, expect, it } from "vitest";
import { filterBindings, isAuthoringBinding, readCatalog } from "./catalog";
import { nextVariableName } from "../documents/graph";

describe("binding catalog", () => {
  it("drops generated record members", () => {
    const entries = readCatalog([
      { signature: "ClientReadyMessage.Equals(ClientReadyMessage)", category: "ClientReadyMessage" },
      { signature: "ClientReadyMessage.<Clone>$()", category: "ClientReadyMessage" },
      { signature: "AstraNetworkSyncService.CreateReadyMessage()", category: "AstraNetworkSyncService", side: 0, isPure: true }
    ]);
    expect(entries.map((entry) => entry.signature)).toEqual(["AstraNetworkSyncService.CreateReadyMessage()"]);
    expect(entries[0]?.side).toBe("server");
    expect(isAuthoringBinding("JoinHandshakeMessage.ToString()")).toBe(false);
  });

  it("filters by the typed query", () => {
    const entries = readCatalog([
      { signature: "GraphScheduler.Update()", category: "GraphScheduler" },
      { signature: "BindingCatalog.Search(string)", category: "BindingCatalog" }
    ]);
    expect(filterBindings(entries, "search")).toHaveLength(1);
  });
});

describe("variables", () => {
  it("does not repeat the same name", () => {
    expect(nextVariableName([])).toBe("Value");
    expect(nextVariableName([{ name: "Value" }, { name: "Value2" }])).toBe("Value3");
  });
});
