import { useEffect, useMemo, useRef, useState } from "react";
import { AuthoringClient } from "../protocol/client";
import { canCompile, canDebug, canPublish, canRollback, permissionBits } from "../permissions/gate";
import {
  addNode,
  connectPins,
  createGraph,
  duplicateNodes,
  edit,
  insertNode,
  emptyRevision,
  nudgeNodes,
  parseGraph,
  redo,
  removeNodes,
  serializeGraph,
  setNodeProperty,
  undo,
  type GraphDocument,
  type PinDocument,
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
interface Problem { severity: string; code: string; message: string; nodeId?: string; pinId?: string }
interface RevisionRecord { revisionId: string; author: string; timestamp: string; message: string; semanticHash: string }
interface Suggestion { bindingId: string; nodeType: string; displayName: string; pinName: string }
const graphKinds = ["System", "Behavior", "Function", "Library", "Schema", "UI"] as const;
const graphSides = ["Server", "Client", "Shared", "SharedPredicted"] as const;

export function App() {
  const [mode, setMode] = useState("offline");
  const [target, setTarget] = useState("Astra Studio");
  const [status, setStatus] = useState("offline");
  const [problems, setProblems] = useState<Problem[]>([]);
  const [graphs, setGraphs] = useState<GraphSummary[]>([]);
  const [catalog, setCatalog] = useState<CatalogEntry[]>([]);
  const [catalogStatus, setCatalogStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");
  const [catalogError, setCatalogError] = useState("");
  const [revisions, setRevisions] = useState<RevisionRecord[]>([]);
  const [bits, setBits] = useState(0);
  const [capabilities, setCapabilities] = useState({ compile: false, publish: false, debug: false });
  const [client, setClient] = useState<AuthoringClient | null>(null);
  const [baseRevision, setBaseRevision] = useState(emptyRevision);
  const [stack, setStack] = useState<UndoStack<GraphDocument>>({ past: [], present: createGraph("Untitled"), future: [] });
  const [selected, setSelected] = useState("");
  const [focusToken, setFocusToken] = useState(0);
  const [alertPin, setAlertPin] = useState("");
  const [pinMarks, setPinMarks] = useState<Record<string, "compatible" | "incompatible">>({});
  const [pinReasons, setPinReasons] = useState<Record<string, string>>({});
  const [createOpen, setCreateOpen] = useState(false);
  const [createName, setCreateName] = useState("New graph");
  const [createKind, setCreateKind] = useState<(typeof graphKinds)[number]>("System");
  const [createSide, setCreateSide] = useState<(typeof graphSides)[number]>("Server");
  const [publishOpen, setPublishOpen] = useState(false);
  const [publishMessage, setPublishMessage] = useState("Studio publish");
  const [suggestOpen, setSuggestOpen] = useState(false);
  const [suggestQuery, setSuggestQuery] = useState("");
  const [suggestions, setSuggestions] = useState<Suggestion[]>([]);
  const [wireDrop, setWireDrop] = useState<{ nodeId: string; pinId: string } | null>(null);
  const [diffLines, setDiffLines] = useState<{ kind: string; detail: string }[]>([]);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [dockOpen, setDockOpen] = useState(false);
  const [protocolError, setProtocolError] = useState("");
  const graph = stack.present;
  const preview = mode === "preview";
  const graphRef = useRef(graph);
  const selectedRef = useRef(selected);
  const clipboardRef = useRef<string[]>([]);
  graphRef.current = graph;
  selectedRef.current = selected;

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const typing = target?.closest("input, textarea, select");
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setPaletteOpen(true);
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z" && !typing) {
        event.preventDefault();
        setStack((current) => event.shiftKey ? redo(current) : undo(current));
      }
      if (typing) return;
      const id = selectedRef.current;
      if (!id) return;
      if (event.key === "Delete" || event.key === "Backspace") {
        event.preventDefault();
        setStack((current) => edit(current, removeNodes(current.present, [id])));
        setSelected("");
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "d") {
        event.preventDefault();
        setStack((current) => edit(current, duplicateNodes(current.present, [id])));
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "c") clipboardRef.current = [id];
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "v" && clipboardRef.current.length > 0) {
        event.preventDefault();
        setStack((current) => edit(current, duplicateNodes(current.present, clipboardRef.current)));
      }
      const step = event.shiftKey ? 16 : 1;
      if (event.key.startsWith("Arrow")) event.preventDefault();
      if (event.key === "ArrowLeft") setStack((current) => edit(current, nudgeNodes(current.present, [id], -step, 0)));
      if (event.key === "ArrowRight") setStack((current) => edit(current, nudgeNodes(current.present, [id], step, 0)));
      if (event.key === "ArrowUp") setStack((current) => edit(current, nudgeNodes(current.present, [id], 0, -step)));
      if (event.key === "ArrowDown") setStack((current) => edit(current, nudgeNodes(current.present, [id], 0, step)));
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
          if (Number(handshake.body.status) === 6) {
            setProtocolError(String(handshake.body.errorMessage ?? "This Studio build cannot speak the server protocol."));
            return;
          }
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
      const fetched = await authoring.graphs.fetch(summary.id);
      setBaseRevision(String(fetched.body.baseRevisionId || summary.activeRevision || emptyRevision));
      const source = String(fetched.body.draftJson ?? "");
      const next = source
        ? parseGraph(source)
        : { ...createGraph(summary.name), id: summary.id };
      setStack({ past: [], present: next, future: [] });
      setSelected("");
      const history = await authoring.history.list(summary.id);
      setRevisions(readRevisions(history.body));
    }
  }, []);

  useEffect(() => {
    if (problems.length > 0 || revisions.length > 0) setDockOpen(true);
  }, [problems.length, revisions.length]);

  const publishAllowed = canPublish(bits, capabilities.publish) && !preview;
  const compileAllowed = canCompile(bits, capabilities.compile) && !preview;
  const rollbackAllowed = canRollback(bits) && !preview;
  const debugAllowed = canDebug(bits, capabilities.debug) && !preview;
  const json = useMemo(() => serializeGraph(graph), [graph]);

  async function save() {
    if (!client) return;
    const response = await client.drafts.save(graph.id, baseRevision, json);
    setProblems(response.body.hasConflict
      ? [{ severity: "error", code: "CONFLICT", message: "OUT OF DATE" }]
      : [{ severity: "info", code: "SAVE", message: "Draft saved" }]);
  }

  async function compile() {
    if (!client) return;
    const response = await client.drafts.compile(graph.id, json);
    setProblems(readProblems(response.body.diagnostics, response.body.errorMessage));
  }

  async function publish() {
    if (!client) return;
    setPublishOpen(false);
    const response = await client.publish(graph.id, baseRevision, json, publishMessage);
    if (response.body.publishedRevision) setBaseRevision(String(response.body.publishedRevision));
    setProblems(readProblems(response.body.diagnostics, response.body.errorMessage));
    const listed = await client.graphs.list();
    setGraphs((listed.body.graphs as GraphSummary[] | undefined) ?? []);
  }

  async function compareRevision(revisionId: string) {
    if (!client) return;
    const response = await client.history.diff(graph.id, revisionId, json);
    setDiffLines(Array.isArray(response.body.changes) ? response.body.changes as { kind: string; detail: string }[] : []);
    setDockOpen(true);
  }

  async function rollback(revision: string) {
    if (!client) return;
    const target = revision;
    const response = await client.history.rollback(graph.id, target);
    if (Number(response.body.status) !== 0) {
      setProblems([{ severity: "error", code: "ROLLBACK", message: String(response.body.errorMessage ?? "Rollback failed") }]);
      return;
    }
    if (response.body.activeRevision) setBaseRevision(String(response.body.activeRevision));
    const fetched = await client.graphs.fetch(graph.id);
    const source = String(fetched.body.draftJson ?? "");
    if (source) setStack({ past: [], present: parseGraph(source), future: [] });
    const history = await client.history.list(graph.id);
    setRevisions(readRevisions(history.body));
  }

  async function createOnServer() {
    if (!client) {
      setProblems([{ severity: "error", code: "OFFLINE", message: "Authoring backend unavailable" }]);
      return;
    }
    setCreateOpen(false);
    const response = await client.graphs.create(createName || "New graph", createKind, createSide);
    if (Number(response.body.status) !== 0) {
      setProblems([{ severity: "error", code: "CREATE", message: String(response.body.errorMessage ?? "Graph was not created") }]);
      return;
    }
    const created = response.body.graph as GraphSummary | undefined;
    if (!created?.id) return;
    setGraphs((current) => [...current.filter((item) => item.id !== created.id), created]);
    setBaseRevision(String(created.activeRevision || emptyRevision));
    const source = String(response.body.sourceJson ?? "");
    setStack({ past: [], present: source ? parseGraph(source) : { ...createGraph(created.name || "New graph"), id: created.id }, future: [] });
    setSelected("");
  }

  async function deleteGraph(id: string) {
    if (!client || !window.confirm("Delete this graph?")) return;
    const response = await client.graphs.delete(id);
    if (Number(response.body.status) !== 0) {
      setProblems([{ severity: "error", code: "DELETE", message: String(response.body.errorMessage ?? "Graph was not deleted") }]);
      return;
    }
    setGraphs((current) => current.filter((item) => item.id !== id));
    if (graph.id === id) setStack({ past: [], present: createGraph("Untitled"), future: [] });
  }

  async function persistName(name: string) {
    if (!client || !graphs.some((item) => item.id === graph.id)) return;
    const response = await client.graphs.rename(graph.id, name);
    const renamed = response.body.graph as GraphSummary | undefined;
    if (Number(response.body.status) === 0 && renamed) {
      setGraphs((current) => current.map((item) => item.id === renamed.id ? { ...item, name: renamed.name } : item));
    }
  }

  async function openListed(item: GraphSummary) {
    if (!client) return;
    const fetched = await client.graphs.fetch(item.id);
    setBaseRevision(String(fetched.body.baseRevisionId || item.activeRevision || emptyRevision));
    const source = String(fetched.body.draftJson ?? "");
    setStack({ past: [], present: source ? parseGraph(source) : { ...createGraph(item.name), id: item.id }, future: [] });
    setSelected("");
    const history = await client.history.list(item.id);
    setRevisions(readRevisions(history.body));
  }

  const selectedNode = graph.nodes.find((node) => node.id === selected);

  async function insertBinding(entry: { bindingId?: string; signature: string; documentation: string }) {
    if (!client || !entry.bindingId) {
      insert(entry.signature, entry.documentation || entry.signature);
      return;
    }
    const response = await client.bindings.materialize(entry.bindingId);
    const shell = String(response.body.nodeJson ?? "");
    const node = shell ? parseGraph(shell).nodes[0] : undefined;
    if (Number(response.body.status) !== 0 || !node) {
      setProblems([{ severity: "error", code: "BINDING", message: String(response.body.errorMessage ?? "Binding did not become a node") }]);
      return;
    }
    setStack((current) => edit(current, insertNode(current.present, node)));
    setSelected(node.id);
    setPaletteOpen(false);
  }

  function insert(nodeType: string, name: string) {
    setStack((current) => edit(current, addNode(current.present, nodeType, name)));
    setPaletteOpen(false);
  }

  function pinRecord(nodeId: string, pin: PinDocument) {
    return { id: pin.id, nodeId, name: pin.name, direction: pin.direction, kind: pin.kind, dataType: pin.dataType ?? "" };
  }

  async function markCompatible(nodeId: string, pinId: string) {
    const current = graphRef.current;
    const node = current.nodes.find((item) => item.id === nodeId);
    const pin = node?.pins.find((item) => item.id === pinId);
    if (!client || !pin) return;
    const candidates = current.nodes.flatMap((item) => item.pins.filter((candidate) => candidate.id !== pinId).map((candidate) => pinRecord(item.id, candidate)));
    const response = await client.connections.compatible(
      pinRecord(nodeId, pin),
      candidates,
      current.connections.map((wire) => ({ sourcePinId: wire.fromPin, targetPinId: wire.toPin })));
    const pins = Array.isArray(response.body.pins) ? response.body.pins as { pinId?: string; valid?: boolean; reason?: string }[] : [];
    const marks: Record<string, "compatible" | "incompatible"> = {};
    const reasons: Record<string, string> = {};
    for (const item of pins) {
      if (!item.pinId) continue;
      marks[item.pinId] = item.valid ? "compatible" : "incompatible";
      if (item.reason) reasons[item.pinId] = item.reason;
    }
    setPinMarks(marks);
    setPinReasons(reasons);
  }

  async function openSuggestions(nodeId: string, pinId: string, query = suggestQuery) {
    setPinMarks({});
    setPinReasons({});
    const current = graphRef.current;
    const node = current.nodes.find((item) => item.id === nodeId);
    const pin = node?.pins.find((item) => item.id === pinId);
    if (!client || !pin) return;
    setWireDrop({ nodeId, pinId });
    const response = await client.connections.suggest(pinRecord(nodeId, pin), query);
    setSuggestions(Array.isArray(response.body.suggestions) ? response.body.suggestions as Suggestion[] : []);
    setSuggestOpen(true);
  }

  async function acceptSuggestion(item: Suggestion) {
    const drop = wireDrop;
    setSuggestOpen(false);
    if (!drop || !client) return;
    const current = graphRef.current;
    const origin = current.nodes.find((node) => node.id === drop.nodeId);
    const originPin = origin?.pins.find((pin) => pin.id === drop.pinId);
    if (!origin || !originPin) return;
    let next = current;
    const shell = item.bindingId ? String((await client.bindings.materialize(item.bindingId)).body.nodeJson ?? "") : "";
    let created = shell ? parseGraph(shell).nodes[0] : undefined;
    if (item.bindingId) {
      if (!created) return;
      next = insertNode(next, created);
    } else {
      next = addNode(next, item.nodeType, item.displayName);
      created = next.nodes.at(-1);
    }
    const targetPin = created?.pins.find((pin) => pin.name === item.pinName);
    if (created && targetPin) {
      const forward = originPin.direction === "output";
      const source = forward ? pinRecord(origin.id, originPin) : pinRecord(created.id, targetPin);
      const target = forward ? pinRecord(created.id, targetPin) : pinRecord(origin.id, originPin);
      const verdict = await client.connections.validate(source, target, next.connections.map((wire) => ({ sourcePinId: wire.fromPin, targetPinId: wire.toPin })));
      if (verdict.body.valid === true) next = connectPins(next, source.nodeId, source.id, target.nodeId, target.id);
      else setProblems([{ severity: "error", code: "WIRE", message: String(verdict.body.reason ?? "Connection refused") }]);
    }
    setStack((stackNow) => edit(stackNow, next));
    if (created) setSelected(created.id);
  }

  async function toggleBreakpoint() {
    if (!client || !selectedNode) return;
    const enabled = selectedNode.properties.breakpoint === "true";
    setStack((current) => edit(current, setNodeProperty(current.present, selectedNode.id, "breakpoint", enabled ? "false" : "true")));
    if (enabled) await client.debug.removeBreakpoint(graph.id, selectedNode.id);
    else await client.debug.setBreakpoint(graph.id, selectedNode.id);
  }

  const statusLabel = preview ? "Preview" : status === "bridge" ? "Connected" : "Offline";

  return (
    <div className="shell">
      {protocolError ? (
        <div className="blocked">
          <div>
            <div className="section-label">Incompatible protocol</div>
            <p>{protocolError}</p>
          </div>
        </div>
      ) : null}
      <header className="topbar">
        <div className="title">
          <strong>{graph.name || "Untitled"}</strong>
          <span className="muted" id="target">{target}</span>
        </div>
        <span className="grow" />
        <button onClick={() => setStack((current) => undo(current))}>Undo</button>
        <button onClick={() => setStack((current) => redo(current))}>Redo</button>
        <button onClick={() => setPaletteOpen(true)}>Add node</button>
        <button disabled={!client} onClick={() => void save()}>Save</button>
        <button disabled={!compileAllowed} onClick={() => void compile()}>Compile</button>
        <button className="primary" disabled={!publishAllowed} onClick={() => setPublishOpen(true)}>Publish</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.pause(graph.id)}>Pause</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.resume(graph.id)}>Continue</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.stepOver(graph.id)}>Step Over</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.stepInto(graph.id)}>Step Into</button>
        <div className="menu">
          <button aria-expanded={menuOpen} onClick={() => setMenuOpen((open) => !open)}>Theme</button>
          {menuOpen ? (
            <div className="menu-pop">
              {(["dark", "light", "contrast"] as const).map((theme) => (
                <button key={theme} onClick={() => {
                  window.document.documentElement.dataset.theme = theme;
                  localStorage.setItem("astra-theme", theme);
                  setMenuOpen(false);
                }}>{theme === "contrast" ? "High contrast" : theme[0].toUpperCase() + theme.slice(1)}</button>
              ))}
            </div>
          ) : null}
        </div>
      </header>
      <div className="workspace">
        <aside className="panel">
          <div className="section-label">Graphs</div>
          <ul className="list">
            {graphs.map((item) => (
              <li key={item.id} className="graph-row">
                <button className={item.id === graph.id ? "active" : ""} onClick={() => void openListed(item)}>{item.name}</button>
                <button onClick={() => void deleteGraph(item.id)}>Delete</button>
              </li>
            ))}
          </ul>
          {graphs.length === 0 ? <p className="muted">No graphs yet</p> : null}
          <button onClick={() => setCreateOpen(true)}>New graph</button>
          <div className="section-label">Bindings</div>
          <BindingBrowser
            entries={catalog}
            status={catalogStatus}
            error={catalogError}
            onInsert={(entry) => void insertBinding(entry)}
          />
        </aside>
        <main className="canvas">
          {preview ? <div className="banner"><strong>Preview Mode</strong><span>No authoring backend connected</span></div> : null}
          <GraphCanvas
            document={graph}
            selectedId={selected}
            focusToken={focusToken}
            alertPin={alertPin}
            pinMarks={pinMarks}
            pinReasons={pinReasons}
            onSelect={(id) => { setSelected(id); setAlertPin(""); }}
            onChange={(next) => { setPinMarks({}); setStack((current) => edit(current, next)); }}
            onWireStart={(nodeId, pinId) => void markCompatible(nodeId, pinId)}
            onWireDrop={(nodeId, pinId) => void openSuggestions(nodeId, pinId)}
            onReject={(reason) => { setPinMarks({}); setProblems([{ severity: "error", code: "WIRE", message: reason }]); }}
            authorizeConnection={async (source, target, existing) => {
              if (!client) return { ok: false, reason: "Authoring backend unavailable" };
              const response = await client.connections.validate(
                { id: source.id, nodeId: source.nodeId, name: source.name, direction: source.direction, kind: source.kind, dataType: source.dataType ?? "" },
                { id: target.id, nodeId: target.nodeId, name: target.name, direction: target.direction, kind: target.kind, dataType: target.dataType ?? "" },
                existing.map((wire) => ({ sourcePinId: wire.fromPin, targetPinId: wire.toPin })));
              return { ok: response.body.valid === true, reason: String(response.body.reason ?? "") };
            }}
          />
        </main>
        <aside className="panel">
          <div className="section-label">Inspector</div>
          <label className="field">Name
            <input value={graph.name} onChange={(event) => setStack((current) => edit(current, { ...current.present, name: event.target.value }))} onBlur={(event) => void persistName(event.target.value)} />
          </label>
          <label className="field">Node
            <select value={selected} onChange={(event) => setSelected(event.target.value)}>
              <option value="">None</option>
              {graph.nodes.map((node) => <option key={node.id} value={node.id}>{node.name}</option>)}
            </select>
          </label>
          {selectedNode ? (
            <div className="pin-list">
              <p className="muted">{selectedNode.nodeType}</p>
              <p className="muted">{selectedNode.properties.pure === "true" ? "Pure" : "Impure"} · {selectedNode.properties.side || graph.side}</p>
              {selectedNode.properties.bindingId ? <p className="muted">{selectedNode.properties.bindingId}</p> : null}
              <ul className="list">
                {selectedNode.pins.map((pin) => <li key={pin.id} className={alertPin === pin.id ? "problem error" : ""}>{pin.direction} {pin.kind} {pin.name}{pin.dataType ? `: ${pin.dataType}` : ""}</li>)}
              </ul>
              {Object.entries(selectedNode.properties).filter(([key]) => key !== "bindingId").map(([key, value]) => (
                <label className="field" key={key}>{key}
                  {value === "true" || value === "false"
                    ? <input type="checkbox" checked={value === "true"} onChange={(event) => setStack((current) => edit(current, setNodeProperty(current.present, selectedNode.id, key, event.target.checked ? "true" : "false")))} />
                    : <input value={value} onChange={(event) => setStack((current) => edit(current, setNodeProperty(current.present, selectedNode.id, key, event.target.value)))} />}
                </label>
              ))}
              <button disabled={!debugAllowed} onClick={() => void toggleBreakpoint()}>{selectedNode.properties.breakpoint === "true" ? "Clear breakpoint" : "Breakpoint"}</button>
            </div>
          ) : (
            <>
              <label className="field">Kind
                <select value={graph.kind} onChange={(event) => setStack((current) => edit(current, { ...current.present, kind: event.target.value }))}>
                  {graphKinds.map((kind) => <option key={kind}>{kind}</option>)}
                </select>
              </label>
              <label className="field">Side
                <select value={graph.side} onChange={(event) => setStack((current) => edit(current, { ...current.present, side: event.target.value }))}>
                  {graphSides.map((side) => <option key={side}>{side}</option>)}
                </select>
              </label>
            </>
          )}
          <div className="section-label">Variables</div>
          <VariableEditor
            variables={graph.variables}
            onChange={(variables) => setStack((current) => edit(current, { ...current.present, variables }))}
          />
        </aside>
      </div>
      <footer className="dock">
        <button className="dock-bar" onClick={() => setDockOpen((open) => !open)}>
          <span>Problems {problems.length}</span>
          <span>History {revisions.length}</span>
          <span className="grow" />
          <span className="muted">{statusLabel}</span>
        </button>
        {dockOpen ? (
          <div className="dock-body">
            <section>
              <ul className="list">
                {problems.map((problem, index) => (
                  <li key={`${problem.code}-${index}`}>
                    <button className={`problem ${problem.severity}`} onClick={() => {
                      if (!problem.nodeId) return;
                      setSelected(problem.nodeId);
                      setAlertPin(problem.pinId ?? "");
                      setFocusToken((token) => token + 1);
                    }}>{problem.code}: {problem.message}</button>
                  </li>
                ))}
                {problems.length === 0 ? <li className="muted">No problems</li> : null}
              </ul>
            </section>
            <section>
              <ul className="list">
                {revisions.map((revision) => (
                  <li key={revision.revisionId}>
                    <span className="mono">{revision.message || revision.revisionId}</span>
                    <span className="muted">{revision.author}</span>
                    <button onClick={() => void compareRevision(revision.revisionId)}>Diff</button>
                    <button disabled={!rollbackAllowed} onClick={() => void rollback(revision.revisionId)}>Rollback</button>
                  </li>
                ))}
                {revisions.length === 0 ? <li className="muted">No revisions</li> : null}
                {diffLines.map((line, index) => <li key={`${line.kind}-${index}`} className="muted">{line.kind}: {line.detail}</li>)}
              </ul>
            </section>
          </div>
        ) : null}
      </footer>
      {createOpen ? (
        <div className="dialog-back" onClick={() => setCreateOpen(false)}>
          <form className="dialog" onClick={(event) => event.stopPropagation()} onSubmit={(event) => { event.preventDefault(); void createOnServer(); }}>
            <h2>New graph</h2>
            <label className="field">Name<input value={createName} onChange={(event) => setCreateName(event.target.value)} /></label>
            <label className="field">Kind
              <select value={createKind} onChange={(event) => setCreateKind(event.target.value as (typeof graphKinds)[number])}>
                {graphKinds.map((kind) => <option key={kind}>{kind}</option>)}
              </select>
            </label>
            <label className="field">Side
              <select value={createSide} onChange={(event) => setCreateSide(event.target.value as (typeof graphSides)[number])}>
                {graphSides.map((side) => <option key={side}>{side}</option>)}
              </select>
            </label>
            <div className="dialog-actions">
              <button type="button" onClick={() => setCreateOpen(false)}>Cancel</button>
              <button className="primary" type="submit">Create</button>
            </div>
          </form>
        </div>
      ) : null}
      {publishOpen ? (
        <div className="dialog-back" onClick={() => setPublishOpen(false)}>
          <form className="dialog" onClick={(event) => event.stopPropagation()} onSubmit={(event) => { event.preventDefault(); void publish(); }}>
            <h2>Publish</h2>
            <p className="muted">{graph.side} · base {baseRevision} · {graph.nodes.length} nodes</p>
            <label className="field">Message<input value={publishMessage} onChange={(event) => setPublishMessage(event.target.value)} /></label>
            <div className="dialog-actions">
              <button type="button" onClick={() => setPublishOpen(false)}>Cancel</button>
              <button className="primary" type="submit">Publish</button>
            </div>
          </form>
        </div>
      ) : null}
      {suggestOpen ? (
        <div className="dialog-back" onClick={() => setSuggestOpen(false)}>
          <form className="dialog" onClick={(event) => event.stopPropagation()} onSubmit={(event) => { event.preventDefault(); if (wireDrop) void openSuggestions(wireDrop.nodeId, wireDrop.pinId, suggestQuery); }}>
            <h2>Connect to</h2>
            <input value={suggestQuery} placeholder="Filter by type" onChange={(event) => setSuggestQuery(event.target.value)} />
            <ul className="list">
              {suggestions.map((item) => (
                <li key={`${item.bindingId}-${item.nodeType}-${item.pinName}`}>
                  <button type="button" onClick={() => void acceptSuggestion(item)}>{item.displayName} <span className="muted">{item.pinName}</span></button>
                </li>
              ))}
              {suggestions.length === 0 ? <li className="muted">No compatible nodes</li> : null}
            </ul>
          </form>
        </div>
      ) : null}
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
  const diagnostics = Array.isArray(value) ? value as { severity?: unknown; code?: unknown; message?: unknown; nodeId?: unknown; pinId?: unknown }[] : [];
  if (diagnostics.length > 0) return diagnostics.map((item) => ({
    severity: String(item.severity ?? "error"),
    code: String(item.code ?? "DIAG"),
    message: String(item.message ?? ""),
    nodeId: item.nodeId ? String(item.nodeId) : undefined,
    pinId: item.pinId ? String(item.pinId) : undefined
  }));
  if (error) return [{ severity: "error", code: "ERROR", message: String(error) }];
  return [{ severity: "info", code: "OK", message: "Completed" }];
}

function readRevisions(body: Record<string, unknown>): RevisionRecord[] {
  const records = body.records;
  if (Array.isArray(records) && records.length > 0) return records as RevisionRecord[];
  const lines = Array.isArray(body.revisions) ? body.revisions as string[] : [];
  return lines.map((line) => {
    const [revisionId, message] = line.split("|");
    return { revisionId, author: "", timestamp: "", message: message ?? "", semanticHash: "" };
  });
}
