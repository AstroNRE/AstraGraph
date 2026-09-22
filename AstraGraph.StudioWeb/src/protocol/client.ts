import { frame, unframe, type StudioMessage } from "./frame";

type Waiter = { kind: string; resolve: (message: StudioMessage) => void; reject: (error: Error) => void };

export class AuthoringClient {
  private readonly waiters: Waiter[] = [];
  readonly inbound: StudioMessage[] = [];

  constructor(private readonly socket: WebSocket) {
    socket.addEventListener("close", () => {
      for (const waiter of this.waiters.splice(0)) waiter.reject(new Error("Authoring connection closed."));
    });
    socket.addEventListener("message", (event) => {
      void this.accept(event.data);
    });
  }

  private async accept(data: unknown) {
    let bytes: Uint8Array | null = null;
    try {
      if (data instanceof Blob) bytes = new Uint8Array(await data.arrayBuffer());
      else if (data instanceof ArrayBuffer) bytes = new Uint8Array(data);
      else if (ArrayBuffer.isView(data)) bytes = new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
    } catch {
      return;
    }
    if (!bytes) return;
    let message: StudioMessage | null = null;
    try {
      message = unframe(bytes);
    } catch {
      return;
    }
    if (!message) return;
    const waiter = this.waiters.find((item) => item.kind === message.kind);
    if (waiter) {
      this.waiters.splice(this.waiters.indexOf(waiter), 1);
      waiter.resolve(message);
      return;
    }
    this.inbound.push(message);
    this.onUnsolicited?.(message);
  }

  graphs = {
    list: () => this.roundtrip("graph.list.request", {}, "graph.list.response"),
    fetch: (graphId: string) => this.roundtrip("graph.fetch.request", { graphId }, "graph.fetch.response")
  };

  drafts = {
    save: (graphId: string, baseRevisionId: string, draftJson: string) =>
      this.roundtrip("draft.save.request", { graphId, baseRevisionId, draftJson, authorMessage: "Studio save" }, "draft.save.response"),
    compile: (graphId: string, draftJson: string) =>
      this.roundtrip("draft.compile.request", { graphId, draftJson }, "draft.compile.response")
  };

  publish(graphId: string, baseRevisionId: string, draftJson: string) {
    return this.roundtrip("draft.publish.request", { graphId, baseRevisionId, draftJson, publishMessage: "Studio publish" }, "draft.publish.response");
  }

  history = {
    list: (graphId: string) => this.roundtrip("history.list.request", { graphId }, "history.list.response"),
    rollback: (graphId: string, targetRevisionId: string) =>
      this.roundtrip("history.rollback.request", { graphId, targetRevisionId }, "history.list.response")
  };

  catalog = {
    query: (searchFilter = "") => this.roundtrip("catalog.query.request", { searchFilter }, "catalog.query.response")
  };

  debug = {
    setBreakpoint: (graphId: string, targetNode: string) =>
      this.roundtrip("debugger.command.request", { graphId, action: 0, targetNode }, "debugger.command.response"),
    pause: (graphId: string) => this.roundtrip("debugger.command.request", { graphId, action: 2 }, "debugger.command.response")
  };

  handshake(authorToken: string, authorName: string) {
    return this.roundtrip("auth.handshake.request", { clientVersion: "1.0.0", authorToken, authorName }, "auth.handshake.response");
  }

  sessionId = "";
  onUnsolicited: ((message: StudioMessage) => void) | null = null;

  private roundtrip(kind: string, body: Record<string, unknown>, responseKind: string) {
    const payload = this.sessionId ? { ...body, sessionId: this.sessionId } : body;
    return new Promise<StudioMessage>((resolve, reject) => {
      let fail: (error: Error) => void = () => {};
      const timer = setTimeout(() => {
        const index = this.waiters.findIndex((item) => item.reject === fail);
        if (index >= 0) this.waiters.splice(index, 1);
        reject(new Error(`No response for ${responseKind}. DevHost on port 5173 did not answer.`));
      }, 8000);
      const finish = (message: StudioMessage) => {
        clearTimeout(timer);
        resolve(message);
      };
      fail = (error: Error) => {
        clearTimeout(timer);
        reject(error);
      };
      this.waiters.push({ kind: responseKind, resolve: finish, reject: fail });
      this.socket.send(frame(kind, payload));
    });
  }
}
