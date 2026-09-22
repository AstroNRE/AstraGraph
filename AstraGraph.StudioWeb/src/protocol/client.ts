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
    fetch: (graphId: string) => this.roundtrip("graph.fetch.request", { graphId }, "graph.fetch.response"),
    create: (name: string, kind = "System", side = "Server") =>
      this.roundtrip("graph.create.request", { name, documentKind: kind, side }, "graph.create.response"),
    rename: (graphId: string, name: string) =>
      this.roundtrip("graph.rename.request", { graphId, name }, "graph.rename.response"),
    delete: (graphId: string) => this.roundtrip("graph.delete.request", { graphId }, "graph.delete.response"),
    disable: (graphId: string, disabled = true) =>
      this.roundtrip("graph.disable.request", { graphId, disabled }, "graph.disable.response")
  };

  drafts = {
    save: (graphId: string, baseRevisionId: string, draftJson: string) =>
      this.roundtrip("draft.save.request", { graphId, baseRevisionId, draftJson, authorMessage: "Studio save" }, "draft.save.response"),
    compile: (graphId: string, draftJson: string) =>
      this.roundtrip("draft.compile.request", { graphId, draftJson }, "draft.compile.response"),
    discard: (graphId: string) => this.roundtrip("draft.discard.request", { graphId }, "draft.discard.response"),
    rebase: (graphId: string) => this.roundtrip("draft.rebase.request", { graphId }, "draft.rebase.response"),
    merge: (graphId: string, otherDraftJson: string) => this.roundtrip("draft.merge.request", { graphId, otherDraftJson }, "draft.merge.response")
  };

  publish(graphId: string, baseRevisionId: string, draftJson: string, publishMessage = "Studio publish") {
    return this.roundtrip("draft.publish.request", { graphId, baseRevisionId, draftJson, publishMessage }, "draft.publish.response");
  }

  history = {
    list: (graphId: string) => this.roundtrip("history.list.request", { graphId }, "history.list.response"),
    rollback: (graphId: string, targetRevisionId: string) =>
      this.roundtrip("history.rollback.request", { graphId, targetRevisionId }, "history.rollback.response"),
    lkg: (graphId: string) => this.roundtrip("history.lkg.request", { graphId }, "history.lkg.response"),
    diff: (graphId: string, revisionId: string, draftJson: string) =>
      this.roundtrip("history.diff.request", { graphId, revisionId, draftJson }, "history.diff.response")
  };

  catalog = {
    query: (searchFilter = "") => this.roundtrip("catalog.query.request", { searchFilter }, "catalog.query.response")
  };

  bindings = {
    materialize: (bindingId: string) => this.roundtrip("binding.materialize.request", { bindingId }, "binding.materialize.response")
  };

  connections = {
    validate: (source: Record<string, string>, target: Record<string, string>, connections: { sourcePinId: string; targetPinId: string }[]) =>
      this.roundtrip("connection.validate.request", { source, target, connections }, "connection.validate.response"),
    compatible: (source: Record<string, string>, candidates: Record<string, string>[], connections: { sourcePinId: string; targetPinId: string }[]) =>
      this.roundtrip("connection.compatible.request", { source, candidates, connections }, "connection.compatible.response"),
    suggest: (pin: Record<string, string>, query = "") =>
      this.roundtrip("connection.suggest.request", { pin, query }, "connection.suggest.response")
  };

  debug = {
    setBreakpoint: (graphId: string, targetNode: string, condition = "") =>
      this.roundtrip("debugger.command.request", { graphId, action: 0, targetNode, condition }, "debugger.command.response"),
    pause: (graphId: string) => this.roundtrip("debugger.command.request", { graphId, action: 2 }, "debugger.command.response"),
    resume: (graphId: string) => this.roundtrip("debugger.command.request", { graphId, action: 3 }, "debugger.command.response"),
    stepOver: (graphId: string) => this.roundtrip("debugger.command.request", { graphId, action: 4 }, "debugger.command.response"),
    stepInto: (graphId: string) => this.roundtrip("debugger.command.request", { graphId, action: 5 }, "debugger.command.response"),
    stepOut: (graphId: string) => this.roundtrip("debugger.command.request", { graphId, action: 6 }, "debugger.command.response"),
    removeBreakpoint: (graphId: string, targetNode: string) =>
      this.roundtrip("debugger.command.request", { graphId, action: 1, targetNode }, "debugger.command.response"),
    inspect: (graphId: string) => this.roundtrip("debugger.inspect.request", { graphId }, "debugger.inspect.response"),
    watch: (name: string, expression: string, remove = false) =>
      this.roundtrip("debugger.watch.request", { name, expression, remove }, "debugger.watch.response")
  };

  profiler = {
    snapshot: (graphId: string, reset = false) => this.roundtrip("profiler.snapshot.request", { graphId, reset }, "profiler.snapshot.response"),
    subscribe: (graphId: string, unsubscribe = false) => this.roundtrip("profiler.subscribe.request", { graphId, unsubscribe }, "profiler.subscribe.response")
  };

  tests = {
    run: (draftJson: string) => this.roundtrip("test.run.request", { draftJson }, "test.run.response")
  };

  audit = {
    query: () => this.roundtrip("audit.query.request", {}, "audit.query.response")
  };

  ui = {
    list: () => this.roundtrip("ui.list.request", {}, "ui.list.response"),
    save: (document: { id: string; name: string; width: number; height: number; root: unknown }) =>
      this.roundtrip("ui.save.request", { document }, "ui.save.response"),
    compile: (document: { id: string; name: string; width: number; height: number; root: unknown }) =>
      this.roundtrip("ui.compile.request", { document }, "ui.compile.response")
  };

  sandbox = {
    run: (draftJson: string) => this.roundtrip("sandbox.run.request", { draftJson }, "sandbox.run.response")
  };

  runtime = {
    status: () => this.roundtrip("runtime.status.request", {}, "runtime.status.response")
  };

  schemas = {
    list: () => this.roundtrip("schema.list.request", {}, "schema.list.response"),
    save: (schema: { id: string; name: string; isComponent: boolean; fields: { id: string; name: string; typeName: string; defaultValue?: string; persistent: boolean; replicated: boolean }[] }) =>
      this.roundtrip("schema.save.request", { schema }, "schema.save.response")
  };

  handshake(authorToken: string, authorName: string) {
    return this.roundtrip("auth.handshake.request", { clientVersion: "1.0.0", protocolVersion: 1, authorToken, authorName }, "auth.handshake.response");
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
