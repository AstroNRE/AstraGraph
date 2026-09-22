export function chooseRecovery(serverJson: string, localJson: string | null): "server" | "local" {
  if (!localJson || localJson === serverJson) return "server";
  return "local";
}

function openDb(): Promise<IDBDatabase | null> {
  if (typeof indexedDB === "undefined") return Promise.resolve(null);
  return new Promise((resolve) => {
    const request = indexedDB.open("astra-studio", 1);
    request.onupgradeneeded = () => request.result.createObjectStore("drafts");
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => resolve(null);
  });
}

export async function rememberDraft(id: string, json: string): Promise<void> {
  const db = await openDb();
  if (!db) return;
  await new Promise<void>((resolve) => {
    const tx = db.transaction("drafts", "readwrite");
    tx.objectStore("drafts").put(json, id);
    tx.oncomplete = () => resolve();
    tx.onerror = () => resolve();
  });
  db.close();
}

export async function recallDraft(id: string): Promise<string | null> {
  const db = await openDb();
  if (!db) return null;
  const value = await new Promise<string | null>((resolve) => {
    const tx = db.transaction("drafts", "readonly");
    const request = tx.objectStore("drafts").get(id);
    request.onsuccess = () => resolve(typeof request.result === "string" ? request.result : null);
    request.onerror = () => resolve(null);
  });
  db.close();
  return value;
}
