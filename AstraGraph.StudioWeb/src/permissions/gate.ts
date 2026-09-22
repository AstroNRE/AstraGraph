export const Permission = {
  ViewGraphs: 1,
  EditDrafts: 2,
  Compile: 4,
  PublishServer: 8,
  PublishShared: 16,
  Rollback: 32,
  Debug: 64
} as const;

export function permissionBits(value: unknown): number {
  const bits = typeof value === "number" ? value : Number(value);
  return Number.isFinite(bits) ? bits : 0;
}

export function canCompile(bits: number, capability: boolean | undefined) {
  return (bits & Permission.Compile) !== 0 && capability !== false;
}

export function canPublish(bits: number, capability: boolean | undefined) {
  const allowed = (bits & Permission.PublishServer) !== 0 || (bits & Permission.PublishShared) !== 0;
  return allowed && capability !== false;
}

export function canRollback(bits: number) {
  return (bits & Permission.Rollback) !== 0;
}

export function canDebug(bits: number, capability: boolean | undefined) {
  return (bits & Permission.Debug) !== 0 && capability !== false;
}
