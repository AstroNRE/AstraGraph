export type ProblemPanel = "Errors" | "Warnings" | "Security" | "Prediction" | "Performance" | "Migration";

export function problemGroup(code: string, severity = "error"): ProblemPanel {
  if (code === "POL003") return "Prediction";
  if (code.startsWith("POL")) return "Security";
  if (code.startsWith("PRF") || code.startsWith("PERF")) return "Performance";
  if (code.startsWith("MIG")) return "Migration";
  if (severity === "warning" || severity === "info") return "Warnings";
  return "Errors";
}

export function suggestedFixFor(code: string, explicit?: string): string | undefined {
  if (explicit) return explicit;
  if (code === "DOC001" || code === "DOC002") return "remove-node";
  return undefined;
}
