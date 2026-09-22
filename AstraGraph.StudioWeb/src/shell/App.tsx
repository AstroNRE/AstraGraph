import { useEffect, useMemo, useState } from "react";
import { AuthoringClient } from "../protocol/client";
import { canCompile, canDebug, canPublish, canRollback, permissionBits } from "../permissions/gate";
import {
  addNode,
  createGraph,
  edit,
  emptyRevision,
  parseGraph,
  redo,
  serializeGraph,
  undo,
  type GraphDocument,
  type UndoStack
} from "../documents/graph";
import { GraphCanvas } from "../graph/GraphCanvas";
import { readCatalog, type CatalogEntry } from "../bindings/catalog";
import { BindingBrowser } from "./BindingBrowser";
import { VariableEditor } from "./VariableEditor";

const palette = [
  ["Event.Tick", "On Tick"],
  ["Event.Start", "On Start"],
  ["Flow.Branch", "Branch"],
  ["Core.VariableAssign", "Assign"]
] as const;

interface GraphSummary { id: string; name: string; activeRevision?: string }
interface Problem { severity: string; code: string; message: string }

export function App() {
  const [mode, setMode] = useState("offline");
  const [target, setTarget] = useState("Astra Studio");
  const [status, setStatus] = useState("offline");
  const [problems, setProblems] = useState<Problem[]>([]);
  const [graphs, setGraphs] = useState<GraphSummary[]>([]);
  const [catalog, setCatalog] = useState<CatalogEntry[]>([]);
  const [catalogStatus, setCatalogStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");
  const [catalogError, setCatalogError] = useState("");
  const [revisions, setRevisions] = useState<string[]>([]);
  const [bits, setBits] = useState(0);
  const [capabilities, setCapabilities] = useState({ compile: false, publish: false, debug: false });
  const [client, setClient] = useState<AuthoringClient | null>(null);
  const [baseRevision, setBaseRevision] = useState(emptyRevision);
  const [stack, setStack] = useState<UndoStack<GraphDocument>>({ past: [], present: createGraph("Untitled"), future: [] });
  const [selected, setSelected] = useState("");
  const [paletteOpen, setPaletteOpen] = useState(false);
  const graph = stack.present;
  const preview = mode === "preview";

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setPaletteOpen(true);
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") {
        event.preventDefault();
        setStack((current) => event.shiftKey ? redo(current) : undo(current));
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  useEffect(() => {
    let disposed = false;
    void connect();
    return () => { disposed = true; };

    async function connect() {
      const params = new URLSearchParams(window.location.search);
      let config: { authoringAvailable?: boolean; targetLabel?: string; mode?: string } | null = null;
      try {
        const response = await fetch("/api/config");
        if (response.ok) config = await response.json();
      } catch {
        config = null;
      }
      if (disposed) return;
      if (config?.targetLabel) setTarget(config.targetLabel);
      if (config?.authoringAvailable === false) {
        setMode("preview");
        setStatus("PREVIEW");
        setProblems([{ severity: "warning", code: "PREVIEW", message: "Preview Mode" }, { severity: "warning", code: "PREVIEW", message: "No authoring backend connected" }]);
        return;
      }

      let nonce = params.get("nonce");
      if (!nonce && config?.authoringAvailable) {
        const response = await fetch("/api/session");
        if (response.ok) nonce = (await response.json()).nonce;
      }
      if (!nonce) {
        setProblems([{ severity: "error", code: "OFFLINE", message: "Authoring backend unavailable" }]);
        return;
      }

      const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
      const socket = new WebSocket(`${protocol}//${window.location.host}/ws?nonce=${encodeURIComponent(nonce)}`);
      socket.binaryType = "arraybuffer";
      const authoring = new AuthoringClient(socket);
      authoring.onUnsolicited = (message) => {
        if (message.kind === "session.updated") setBits(permissionBits(message.body.permissions));
        if (message.kind === "debug.stream.event") {
          setProblems((current) => [...current, { severity: "info", code: "DEBUG", message: `${message.body.action ?? "event"} ${message.body.currentNode ?? ""}` }]);
        }
      };
      socket.addEventListener("open", () => {
        if (disposed) return;
        setStatus("bridge");
        setClient(authoring);
        void authoring.handshake(params.get("token") ?? "", params.get("author") ?? "Studio").then(async (handshake) => {
          if (Number(handshake.body.status) !== 0) {
            setProblems([{ severity: "error", code: "AUTH", message: String(handshake.body.errorMessage ?? "Handshake failed") }]);
            return;
          }
          authoring.sessionId = String(handshake.body.sessionId ?? "");
          setBits(permissionBits(handshake.body.permissions));
          setCapabilities({
            compile: handshake.body.canCompile !== false,
            publish: handshake.body.canPublish !== false,
            debug: handshake.body.canDebug !== false
          });
          setMode(config?.mode ?? "attached");
          const listed = await authoring.graphs.list();
          const items = (listed.body.graphs as GraphSummary[] | undefined) ?? [];
          setGraphs(items);
          setCatalogStatus("loading");
          try {
            const catalogResponse = await authoring.catalog.query();
            if (disposed) return;
            setCatalog(readCatalog(catalogResponse.body.entries));
            setCatalogStatus("ready");
          } catch (error) {
            if (disposed) return;
            setCatalogStatus("error");
            setCatalogError(error instanceof Error ? error.message : "Catalog request failed.");
          }
          if (items[0]) await openGraph(authoring, items[0]);
        });
      });
    }

    async function openGraph(authoring: AuthoringClient, summary: GraphSummary) {
      setBaseRevision(summary.activeRevision || emptyRevision);
      const fetched = await authoring.graphs.fetch(summary.id);
      const next = fetched.body.draftJson
        ? parseGraph(String(fetched.body.draftJson))
        : { ...createGraph(summary.name), id: summary.id };
      setStack({ past: [], present: next, future: [] });
      setSelected("");
      const history = await authoring.history.list(summary.id);
      setRevisions((history.body.revisions as string[] | undefined) ?? []);
    }
  }, []);

  const publishAllowed = canPublish(bits, capabilities.publish) && !preview;
  const compileAllowed = canCompile(bits, capabilities.compile) && !preview;
  const rollbackAllowed = canRollback(bits) && !preview;
  const debugAllowed = canDebug(bits, capabilities.debug) && !preview;
  const json = useMemo(() => serializeGraph(graph), [graph]);

  async function save() {
    if (!client) return;
    const response = await client.drafts.save(graph.id, baseRevision, json);
    setProblems(response.body.hasConflict
      ? [{ severity: "error", code: "CONFLICT", message: String(response.body.errorMessage ?? "Conflict") }]
      : [{ severity: "info", code: "SAVE", message: "Draft saved" }]);
  }

  async function compile() {
    if (!client) return;
    const response = await client.drafts.compile(graph.id, json);
    setProblems(readProblems(response.body.diagnostics, response.body.errorMessage));
  }

  async function publish() {
    if (!client) return;
    const response = await client.publish(graph.id, baseRevision, json);
    if (response.body.publishedRevision) setBaseRevision(String(response.body.publishedRevision));
    setProblems(readProblems(response.body.diagnostics, response.body.errorMessage));
    const listed = await client.graphs.list();
    setGraphs((listed.body.graphs as GraphSummary[] | undefined) ?? []);
  }

  async function rollback(revision: string) {
    if (!client) return;
    const target = revision.split("|")[0];
    await client.history.rollback(graph.id, target);
    const history = await client.history.list(graph.id);
    setRevisions((history.body.revisions as string[] | undefined) ?? []);
  }

  function insert(nodeType: string, name: string) {
    setStack((current) => edit(current, addNode(current.present, nodeType, name)));
    setPaletteOpen(false);
  }

  return (
    <div className="shell">
      <header className="topbar">
        <span className="brand">Astra Studio</span>
        <span className="muted" id="target">{target}</span>
        <span className="grow" />
        <button onClick={() => setStack((current) => undo(current))}>Undo</button>
        <button onClick={() => setStack((current) => redo(current))}>Redo</button>
        <button onClick={() => setPaletteOpen(true)}>Add node</button>
        <button disabled={!client} onClick={() => void save()}>Save</button>
        <button disabled={!compileAllowed} onClick={() => void compile()}>Compile</button>
        <button disabled={!publishAllowed} onClick={() => void publish()}>Publish</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.pause(graph.id)}>Debug</button>
        <label className="muted">Theme
          <select defaultValue={localStorage.getItem("astra-theme") ?? "dark"} onChange={(event) => {
            window.document.documentElement.dataset.theme = event.target.value;
            localStorage.setItem("astra-theme", event.target.value);
          }}>
            <option value="dark">Dark</option>
            <option value="light">Light</option>
            <option value="contrast">High Contrast</option>
          </select>
        </label>
      </header>
      <div className="workspace">
        <aside className="panel">
          <h2>Graphs</h2>
          <ul className="list">
            {graphs.map((item) => (
              <li key={item.id}>
                <button className={item.id === graph.id ? "active" : ""} onClick={() => client && void client.graphs.fetch(item.id).then((fetched) => {
                  setBaseRevision(item.activeRevision || emptyRevision);
                  setStack({ past: [], present: fetched.body.draftJson ? parseGraph(String(fetched.body.draftJson)) : { ...createGraph(item.name), id: item.id }, future: [] });
                })}>{item.name}</button>
              </li>
            ))}
          </ul>
          <button onClick={() => setStack({ past: [], present: createGraph("Untitled"), future: [] })}>New graph</button>
          <h2>Bindings</h2>
          <BindingBrowser
            entries={catalog}
            status={catalogStatus}
            error={catalogError}
            onInsert={(entry) => insert(entry.signature, entry.documentation || entry.signature)}
          />
        </aside>
        <main className="canvas">
          {preview ? <div className="banner"><strong>Preview Mode</strong><div>No authoring backend connected</div></div> : null}
          <GraphCanvas document={graph} onChange={(next) => setStack((current) => edit(current, next))} />
        </main>
        <aside className="panel right">
          <h2>Inspector</h2>
          <label className="field">Name
            <input value={graph.name} onChange={(event) => setStack((current) => edit(current, { ...current.present, name: event.target.value }))} />
          </label>
          <label className="field">Node
            <select value={selected} onChange={(event) => setSelected(event.target.value)}>
              <option value="">None</option>
              {graph.nodes.map((node) => <option key={node.id} value={node.id}>{node.name}</option>)}
            </select>
          </label>
          <p className="muted">{graph.nodes.find((node) => node.id === selected)?.nodeType ?? "Select a node"}</p>
          <h2>Variables</h2>
          <VariableEditor
            variables={graph.variables}
            onChange={(variables) => setStack((current) => edit(current, { ...current.present, variables }))}
          />
        </aside>
      </div>
      <div className="bottom">
        <section>
          <h2>Problems</h2>
          <ul className="list">
            {problems.map((problem, index) => <li key={`${problem.code}-${index}`} className={`problem ${problem.severity}`}>{problem.code}: {problem.message}</li>)}
            {problems.length === 0 ? <li className="muted">No problems</li> : null}
          </ul>
        </section>
        <section>
          <h2>History</h2>
          <p className="muted">Status {status}</p>
          <ul className="list">
            {revisions.map((revision) => (
              <li key={revision}>
                <span className="mono">{revision}</span>
                <button disabled={!rollbackAllowed} onClick={() => void rollback(revision)}>Rollback</button>
              </li>
            ))}
          </ul>
        </section>
      </div>
      {paletteOpen ? (
        <div className="palette" onClick={() => setPaletteOpen(false)}>
          <form onClick={(event) => event.stopPropagation()}>
            <input autoFocus placeholder="Add node" onKeyDown={(event) => { if (event.key === "Escape") setPaletteOpen(false); }} />
            <ul className="list">
              {palette.map(([nodeType, name]) => (
                <li key={nodeType}><button type="button" onClick={() => insert(nodeType, name)}>{name} <span className="muted">{nodeType}</span></button></li>
              ))}
            </ul>
          </form>
        </div>
      ) : null}
    </div>
  );
}

function readProblems(value: unknown, error: unknown): Problem[] {
  const diagnostics = Array.isArray(value) ? value as Problem[] : [];
  if (diagnostics.length > 0) return diagnostics.map((item) => ({ severity: String(item.severity ?? "error"), code: String(item.code ?? "DIAG"), message: String(item.message ?? "") }));
  if (error) return [{ severity: "error", code: "ERROR", message: String(error) }];
  return [{ severity: "info", code: "OK", message: "Completed" }];
}
