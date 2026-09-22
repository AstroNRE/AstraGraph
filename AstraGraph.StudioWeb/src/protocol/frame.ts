export interface StudioMessage {
  kind: string;
  body: Record<string, unknown>;
}

export function frame(kind: string, body: Record<string, unknown>): Uint8Array {
  const payload = JSON.stringify({
    ...body,
    kind,
    messageId: crypto.randomUUID().replaceAll("-", ""),
    timestamp: new Date().toISOString()
  });
  const kindBytes = new TextEncoder().encode(kind);
  const payloadBytes = new TextEncoder().encode(payload);
  const buffer = new Uint8Array(4 + kindBytes.length + payloadBytes.length);
  new DataView(buffer.buffer).setInt32(0, kindBytes.length, false);
  buffer.set(kindBytes, 4);
  buffer.set(payloadBytes, 4 + kindBytes.length);
  return buffer;
}

export function unframe(buffer: Uint8Array): StudioMessage | null {
  if (buffer.length < 4) return null;
  const kindLength = new DataView(buffer.buffer, buffer.byteOffset, buffer.byteLength).getInt32(0, false);
  if (kindLength < 0 || 4 + kindLength > buffer.length) return null;
  const kind = new TextDecoder().decode(buffer.slice(4, 4 + kindLength));
  const payload = new TextDecoder().decode(buffer.slice(4 + kindLength));
  return { kind, body: JSON.parse(payload) as Record<string, unknown> };
}
