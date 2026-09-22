export interface CatalogEntry {
  signature: string;
  bindingId: string;
  category: string;
  isPure: boolean;
  isPredictionSafe: boolean;
  side: string;
  documentation: string;
  parameters: { name: string; typeName: string; direction: string }[];
  returnType: string;
}

const generated = new Set(["Equals", "GetHashCode", "ToString", "Deconstruct", "PrintMembers", "GetType", "MemberwiseClone"]);

const sides = ["server", "client", "shared", "sharedPredicted"];

export function methodName(signature: string): string {
  const head = signature.split("(")[0] ?? signature;
  const dot = head.lastIndexOf(".");
  return dot >= 0 ? head.slice(dot + 1) : head;
}

export function isAuthoringBinding(signature: string): boolean {
  const name = methodName(signature);
  if (!name || name.includes("<") || name.includes(">")) return false;
  return !generated.has(name);
}

export function sideLabel(value: unknown): string {
  if (typeof value === "number") return sides[value] ?? String(value);
  return String(value ?? "");
}

export function readCatalog(value: unknown): CatalogEntry[] {
  if (!Array.isArray(value)) return [];
  return value.flatMap((item) => {
    if (!item || typeof item !== "object") return [];
    const entry = item as Record<string, unknown>;
    const signature = String(entry.signature ?? "");
    if (!signature || !isAuthoringBinding(signature)) return [];
    const parameters = Array.isArray(entry.parameters)
      ? entry.parameters.flatMap((pin) => {
          if (!pin || typeof pin !== "object") return [];
          const row = pin as Record<string, unknown>;
          return [{ name: String(row.name ?? ""), typeName: String(row.typeName ?? ""), direction: String(row.direction ?? "") }];
        })
      : [];
    return [{
      signature,
      bindingId: String(entry.bindingId ?? signature),
      category: String(entry.category ?? "Bindings"),
      isPure: entry.isPure === true,
      isPredictionSafe: entry.isPredictionSafe === true,
      side: sideLabel(entry.side),
      documentation: String(entry.documentation ?? ""),
      parameters,
      returnType: String(entry.returnType ?? "")
    }];
  });
}

export function filterBindings(entries: CatalogEntry[], query: string): CatalogEntry[] {
  const needle = query.trim().toLowerCase();
  if (!needle) return entries;
  return entries.filter((entry) =>
    entry.signature.toLowerCase().includes(needle) || entry.category.toLowerCase().includes(needle));
}

export function groupBindings(entries: CatalogEntry[]): { category: string; items: CatalogEntry[] }[] {
  const groups = new Map<string, CatalogEntry[]>();
  for (const entry of entries) {
    const items = groups.get(entry.category) ?? [];
    items.push(entry);
    groups.set(entry.category, items);
  }
  return [...groups.entries()].map(([category, items]) => ({ category, items }));
}
