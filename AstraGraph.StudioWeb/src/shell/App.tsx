import { useEffect, useMemo, useRef, useState } from "react";
import { AuthoringClient } from "../protocol/client";
import { canCompile, canDebug, canEdit, canPublish, canRollback, permissionBits } from "../permissions/gate";
import {
  addNode,
  connectPins,
  createGraph,
  emptyAttributes,
  duplicateNodes,
  edit,
  insertNode,
  emptyRevision,
  nudgeNodes,
  parseGraph,
  redo,
  findVariableUses,
  removeNodes,
  serializeGraph,
  setNodeProperty,
  setSchemaFieldDefault,
  undo,
  upsertSchema,
  type GraphDocument,
  type NodeDocument,
  type PinDocument,
  type UndoStack
} from "../documents/graph";
import { GraphCanvas } from "../graph/GraphCanvas";
import { methodName, readCatalog, type CatalogEntry } from "../bindings/catalog";
import { BindingBrowser } from "./BindingBrowser";
import { VariableEditor } from "./VariableEditor";
import { SchemaEditor, type SchemaDocument } from "./SchemaEditor";
import { applyQuickFix, problemGroup, suggestedFixFor } from "./problems";
import { rankTexts } from "../search/fuzzy";
import { UiDesigner, type UiDocumentModel } from "./UiDesigner";
import type { UiControlInfo } from "./uiTree";
import { chooseRecovery, recallDraft, rememberDraft } from "../documents/recovery";

const palette = [
  ["Event.Native", "On event"],
  ["Entity.TryGetComponent", "Try Get Component"],
  ["Schema.GetField", "Get field"],
  ["Schema.Make", "Make struct"],
  ["Schema.Copy", "Copy"],
  ["Schema.SetField", "Set field"],
  ["List.Create", "Make list"],
  ["List.Add", "Add"],
  ["List.Get", "Get item"],
  ["PersistentId.New", "New id"],
  ["Container.Insert", "Insert"],
  ["Container.Contents", "Contents"],
  ["Inventory.Find", "Find item"],
  ["Entity.GetHeldItem", "Held item"],
  ["Native.GetMember", "Get member"],
  ["Native.Call", "Call"],
  ["Graph.Call", "Call function"],
  ["Flow.For", "For"],
  ["Flow.ForEach", "For each"],
  ["Nullable.HasValue", "Has value"],
  ["Nullable.GetValue", "Get value"],
  ["Flow.Branch", "Branch"],
  ["Core.VariableAssign", "Assign"]
] as const;

interface GraphSummary { id: string; name: string; activeRevision?: string; kind?: string; side?: string; status?: string; hasDraft?: boolean; owner?: string; tags?: string; overrideOf?: string }
type Activity = "explorer" | "bindings" | "types" | "design" | "runtime" | "audit" | "access";
type DockTab = "problems" | "watch" | "debug" | "profiler" | "history" | "output";
const activities: Activity[] = ["explorer", "bindings", "types", "design", "runtime", "audit", "access"];
const dockTabs: DockTab[] = ["problems", "watch", "debug", "profiler", "history", "output"];
interface Problem { severity: string; code: string; message: string; nodeId?: string; pinId?: string; relatedNodeId?: string; suggestedFix?: string }
interface RevisionRecord { revisionId: string; author: string; timestamp: string; message: string; semanticHash: string; parentRevisionId?: string; activationTick?: number }
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
  const [headRevision, setHeadRevision] = useState(emptyRevision);
  const [recoveryJson, setRecoveryJson] = useState("");
  const [uiDocuments, setUiDocuments] = useState<UiDocumentModel[]>([]);
  const [uiCatalog, setUiCatalog] = useState<UiControlInfo[]>([]);
  const [uiStyles, setUiStyles] = useState<string[]>([]);
  const [graphQuery, setGraphQuery] = useState("");
  const [sideFilter, setSideFilter] = useState("All");
  const [compileMs, setCompileMs] = useState(0);
  const [historyLimit, setHistoryLimit] = useState(40);
  const [publishConfirm, setPublishConfirm] = useState(false);
  const [activationTick, setActivationTick] = useState(0);
  const [ownerFilter, setOwnerFilter] = useState("");
  const [tagFilter, setTagFilter] = useState("");
  const [revisionFilter, setRevisionFilter] = useState("");
  const [clientCount, setClientCount] = useState(0);
  const [migrationSummary, setMigrationSummary] = useState("No schema migration");
  const [eventPayload, setEventPayload] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [uiLayout, setUiLayout] = useState("");
  const [otherDraft, setOtherDraft] = useState("");
  const [openTabs, setOpenTabs] = useState<GraphSummary[]>([]);
  const [bindingQuery, setBindingQuery] = useState("");
  const [reconnectToken, setReconnectToken] = useState(0);
  const [compileStage, setCompileStage] = useState("Parse → Type Check → Binding → Side → Verify");
  const [runtimeGraphs, setRuntimeGraphs] = useState<{ id: string; name: string; activeRevision: string }[]>([]);
  const [runtimeEntities, setRuntimeEntities] = useState<{ entityId: number; schema: string; fields: { name: string; value: string }[] }[]>([]);
  const [selectedEntity, setSelectedEntity] = useState<number | null>(null);
  const [sandboxResult, setSandboxResult] = useState("");
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
  const [activity, setActivity] = useState<Activity>(() => readStored(localStorage.getItem("astra-activity"), activities, "explorer"));
  const [dockTab, setDockTab] = useState<DockTab>(() => readStored(localStorage.getItem("astra-dock"), dockTabs, "problems"));
  const [schemas, setSchemas] = useState<SchemaDocument[]>([]);
  const [schemaNote, setSchemaNote] = useState("");
  const [auditEntries, setAuditEntries] = useState<{ action: string; author: string; message: string }[]>([]);
  const [locals, setLocals] = useState<{ name: string; value: string }[]>([]);
  const [watches, setWatches] = useState<{ name: string; expression: string; value: string }[]>([]);
  const [trace, setTrace] = useState<{ nodeId: string; instructionPointer: number }[]>([]);
  const [watchName, setWatchName] = useState("Value");
  const [watchExpr, setWatchExpr] = useState("r0");
  const [breakpointCondition, setBreakpointCondition] = useState("");
  const [profile, setProfile] = useState<{ invocations?: number; averageMicroseconds?: number; p95Microseconds?: number; budgetViolations?: number; allocatedBytes?: number; networkBytes?: number; queryIterations?: number; samples?: number[]; instructions?: number; nativeCalls?: number; yields?: number; hottest?: { nodeId: string; hits: number; microseconds?: number }[] } | null>(null);
  const [compileHash, setCompileHash] = useState("");
  const [commandsOpen, setCommandsOpen] = useState(false);
  const [quickOpen, setQuickOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [dockOpen, setDockOpen] = useState(false);
  const [protocolError, setProtocolError] = useState("");
  const [searchHits, setSearchHits] = useState<{ id: string; text: string }[]>([]);
  const graph = stack.present;
  const preview = mode === "preview";
  const graphRef = useRef(graph);
  const problemsRef = useRef(problems);
  const selectedRef = useRef(selected);
  const clipboardRef = useRef<string[]>([]);
  graphRef.current = graph;
  selectedRef.current = selected;
  problemsRef.current = problems;
  const enums = schemas.filter((schema) => schema.kind === "Enum");

  useEffect(() => {
    const items = [
      ...graphs.map((item) => ({ id: `graph:${item.id}`, text: `${item.name} graph ${item.tags ?? ""}` })),
      ...schemas.map((item) => ({ id: `schema:${item.id}`, text: `${item.name} schema ${item.kind ?? ""}` })),
      ...runtimeEntities.map((item) => ({ id: `entity:${item.entityId}`, text: `${item.entityId} ${item.schema} entity` })),
      ...catalog.map((item) => ({ id: `doc:${item.signature}`, text: `${item.signature} ${item.documentation}` }))
    ];
    setSearchHits(rankTexts(searchQuery, items));
    if (typeof Worker === "undefined") return;
    const worker = new Worker(new URL("../search/fuzzy.worker.ts", import.meta.url), { type: "module" });
    worker.onmessage = (event: MessageEvent<{ id: string; text: string }[]>) => setSearchHits(event.data);
    worker.postMessage({ query: searchQuery, items });
    return () => worker.terminate();
  }, [catalog, graphs, runtimeEntities, schemas, searchQuery]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const typing = target?.closest("input, textarea, select");
      if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === "p") {
        event.preventDefault();
        setCommandsOpen(true);
        return;
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "p") {
        event.preventDefault();
        setQuickOpen(true);
        return;
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setPaletteOpen(true);
      }
      if ((event.ctrlKey || event.metaKey) && event.key === " ") {
        event.preventDefault();
        setPaletteOpen(true);
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z" && !typing) {
        event.preventDefault();
        setStack((current) => event.shiftKey ? redo(current) : undo(current));
      }
      if (typing) return;
      if (event.altKey && event.key === "Enter") {
        const problem = problemsRef.current.find((item) => item.nodeId && item.suggestedFix === "remove-node");
        if (problem?.nodeId) {
          event.preventDefault();
          setStack((current) => edit(current, removeNodes(current.present, [problem.nodeId!])));
        }
      }
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
      if (event.key === "F12" && event.shiftKey) {
        event.preventDefault();
        const node = graphRef.current.nodes.find((item) => item.id === id);
        const bindingId = node?.properties.bindingId;
        const uses = graphRef.current.nodes.filter((item) => item.id !== id && (bindingId ? item.properties.bindingId === bindingId : item.nodeType === node?.nodeType));
        setProblems(uses.length > 0
          ? uses.map((item) => ({ severity: "info", code: "REF", message: `${item.name} uses ${bindingId || node?.nodeType}`, nodeId: item.id }))
          : [{ severity: "info", code: "REF", message: "No other usages" }]);
        setDockTab("problems");
        setDockOpen(true);
        return;
      }
      if (event.key === "F12") {
        const node = graphRef.current.nodes.find((item) => item.id === id);
        const bindingId = node?.properties.bindingId;
        if (bindingId) {
          event.preventDefault();
          setActivity("bindings");
          setBindingQuery(bindingId);
        }
      }
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
    let socket: WebSocket | null = null;
    void connect();
    return () => {
      disposed = true;
      socket?.close();
    };

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
      socket = new WebSocket(`${protocol}//${window.location.host}/ws?nonce=${encodeURIComponent(nonce)}`);
      socket.binaryType = "arraybuffer";
      socket.addEventListener("close", () => {
        if (disposed) return;
        setStatus("offline");
        setClient(null);
        setProblems([{ severity: "warning", code: "OFFLINE", message: "Authoring backend unavailable" }]);
      });
      const authoring = new AuthoringClient(socket);
      authoring.onUnsolicited = (message) => {
        if (message.kind === "profiler.snapshot.response" && message.body.snapshot) {
          setProfile(message.body.snapshot as typeof profile);
        }
        if (message.kind === "session.updated") {
          const nextBits = permissionBits(message.body.permissions);
          setBits(nextBits);
          if ((nextBits & 64) === 0) setDockTab((tab) => tab === "debug" || tab === "watch" || tab === "profiler" ? "problems" : tab);
        }
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
          setClientCount(Number(listed.body.clientCount ?? 0));
          setMigrationSummary(String(listed.body.migrationSummary ?? "No schema migration"));
          try {
            const uiCatalogResponse = await authoring.ui.catalog();
            if (!disposed && Array.isArray(uiCatalogResponse.body.controls)) setUiCatalog(uiCatalogResponse.body.controls as UiControlInfo[]);
            if (!disposed && Array.isArray(uiCatalogResponse.body.styles)) setUiStyles(uiCatalogResponse.body.styles as string[]);
          } catch {
            if (!disposed) setUiCatalog([]);
          }
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
          const requested = params.get("graph");
          const chosen = items.find((item) => item.id === requested) ?? items[0];
          if (chosen) {
            await openGraph(authoring, chosen);
            setOpenTabs((tabs) => tabs.some((tab) => tab.id === chosen.id) ? tabs : [...tabs, chosen]);
            const node = params.get("node");
            if (node) {
              setSelected(node);
              setFocusToken((token) => token + 1);
            }
            const entity = params.get("entity");
            if (entity) {
              setActivity("runtime");
              setSelectedEntity(Number(entity));
              const runtime = await authoring.runtime.status();
              setRuntimeGraphs(Array.isArray(runtime.body.graphs) ? runtime.body.graphs as { id: string; name: string; activeRevision: string }[] : []);
              setRuntimeEntities(Array.isArray(runtime.body.entities) ? runtime.body.entities as { entityId: number; schema: string; fields: { name: string; value: string }[] }[] : []);
            }
          }
        });
      });
    }

    async function openGraph(authoring: AuthoringClient, summary: GraphSummary) {
      const fetched = await authoring.graphs.fetch(summary.id);
      const revision = String(fetched.body.baseRevisionId || summary.activeRevision || emptyRevision);
      setBaseRevision(revision);
      setHeadRevision(String(fetched.body.activeRevisionId || revision));
      const source = String(fetched.body.draftJson ?? "");
      const next = source
        ? parseGraph(source)
        : { ...createGraph(summary.name), id: summary.id };
      const local = await recallDraft(next.id);
      if (source && chooseRecovery(source, local) === "local" && local) {
        setRecoveryJson(local);
        setProblems([{ severity: "warning", code: "RECOVERY", message: "Local recovery differs from the server draft" }]);
      } else {
        setRecoveryJson("");
      }
      setStack({ past: [], present: next, future: [] });
      setSelected("");
      const history = await authoring.history.list(summary.id);
      setRevisions(readRevisions(history.body));
    }
  }, [reconnectToken]);

  useEffect(() => {
    if (problems.length > 0 || revisions.length > 0) setDockOpen(true);
  }, [problems.length, revisions.length]);

  useEffect(() => {
    localStorage.setItem("astra-activity", activity);
    localStorage.setItem("astra-dock", dockTab);
  }, [activity, dockTab]);

  useEffect(() => {
    if (!graph.id) return;
    void rememberDraft(graph.id, serializeGraph(graph));
  }, [graph]);

  useEffect(() => {
    if (!client || preview || !canEdit(bits) || stack.past.length === 0) return;
    const timer = window.setTimeout(() => { void save(true); }, 1200);
    return () => window.clearTimeout(timer);
  }, [bits, client, graph, preview, stack.past.length]);

  useEffect(() => {
    if (!client) return;
    const timer = window.setInterval(() => {
      void client.graphs.list().then((listed) => {
        const items = (listed.body.graphs as GraphSummary[] | undefined) ?? [];
        const match = items.find((item) => item.id === graph.id);
        if (match?.activeRevision) setHeadRevision(match.activeRevision);
      });
    }, 4000);
    return () => window.clearInterval(timer);
  }, [client, graph.id]);

  useEffect(() => {
    if (!publishOpen || !client) return;
    void compareRevision(baseRevision);
  }, [publishOpen]);

  const publishAllowed = canPublish(bits, capabilities.publish) && !preview;
  const compileAllowed = canCompile(bits, capabilities.compile) && !preview;
  const rollbackAllowed = canRollback(bits) && !preview;
  const debugAllowed = canDebug(bits, capabilities.debug) && !preview;
  const json = useMemo(() => serializeGraph(graph), [graph]);

  async function save(quiet = false) {
    if (!client) return;
    const response = await client.drafts.save(graph.id, baseRevision, json);
    if (response.body.hasConflict) {
      setHeadRevision(String(response.body.errorMessage ?? headRevision));
      setProblems([{ severity: "error", code: "CONFLICT", message: "OUT OF DATE" }]);
      return;
    }
    if (!quiet) setProblems([{ severity: "info", code: "SAVE", message: "Draft saved" }]);
  }

  async function discardDraft() {
    if (!client) return;
    const response = await client.drafts.discard(graph.id);
    const source = String(response.body.draftJson ?? "");
    if (!source) return;
    const revision = String(response.body.baseRevisionId || emptyRevision);
    setBaseRevision(revision);
    setHeadRevision(String(response.body.activeRevisionId || revision));
    setRecoveryJson("");
    setStack({ past: [], present: parseGraph(source), future: [] });
  }

  function restoreRecovery() {
    if (!recoveryJson) return;
    setStack({ past: [], present: parseGraph(recoveryJson), future: [] });
    setRecoveryJson("");
  }

  async function compile() {
    if (!client) return;
    const started = performance.now();
    const response = await client.drafts.compile(graph.id, json);
    setCompileMs(Math.round(performance.now() - started));
    setProblems(readProblems(response.body.diagnostics, response.body.errorMessage));
    setCompileHash(String(response.body.bytecodeHash ?? ""));
    setCompileStage(String(response.body.stage ?? pipelineStage(readProblems(response.body.diagnostics, response.body.errorMessage))));
    const listed = await client.graphs.list();
    setGraphs((listed.body.graphs as GraphSummary[] | undefined) ?? []);
    setClientCount(Number(listed.body.clientCount ?? clientCount));
    setMigrationSummary(String(listed.body.migrationSummary ?? migrationSummary));
    setDockTab("output");
    setDockOpen(true);
  }

  async function runSandbox() {
    if (!client) return;
    const response = await client.sandbox.run(json);
    setCompileStage(String(response.body.stage ?? ""));
    setSandboxResult(String(response.body.result ?? response.body.errorMessage ?? ""));
    setDockTab("output");
    setDockOpen(true);
  }

  async function loadRuntime() {
    if (!client) return;
    const response = await client.runtime.status();
    setRuntimeGraphs(Array.isArray(response.body.graphs) ? response.body.graphs as { id: string; name: string; activeRevision: string }[] : []);
    setRuntimeEntities(Array.isArray(response.body.entities) ? response.body.entities as { entityId: number; schema: string; fields: { name: string; value: string }[] }[] : []);
  }

  async function publish() {
    if (!client) return;
    setPublishOpen(false);
    const response = await client.publish(graph.id, baseRevision, json, publishMessage);
    if (response.body.publishedRevision) setBaseRevision(String(response.body.publishedRevision));
    setActivationTick(Number(response.body.activationTick ?? 0));
    setClientCount(Number(response.body.clientCount ?? clientCount));
    setMigrationSummary(String(response.body.migrationSummary ?? migrationSummary));
    setProblems(readProblems(response.body.diagnostics, response.body.errorMessage));
    const listed = await client.graphs.list();
    setGraphs((listed.body.graphs as GraphSummary[] | undefined) ?? []);
    if (response.body.publishedRevision) {
      const history = await client.history.list(graph.id);
      setRevisions(readRevisions(history.body));
      setDockTab("history");
      setDockOpen(true);
    }
  }

  async function rebaseDraft() {
    if (!client) return;
    const response = await client.drafts.rebase(graph.id);
    if (response.body.conflicts) {
      setProblems([{ severity: "error", code: "MERGE", message: String(response.body.conflicts) }]);
      setDockTab("problems");
      setDockOpen(true);
      return;
    }
    if (response.body.draftJson) setStack({ past: [], present: parseGraph(String(response.body.draftJson)), future: [] });
    if (response.body.baseRevisionId) setBaseRevision(String(response.body.baseRevisionId));
  }

  async function mergeDraft() {
    if (!client || !otherDraft.trim()) return;
    const response = await client.drafts.merge(graph.id, otherDraft);
    if (response.body.conflicts) {
      setProblems([{ severity: "error", code: "MERGE", message: String(response.body.conflicts) }]);
      setDockTab("problems");
      setDockOpen(true);
      return;
    }
    if (response.body.draftJson) setStack({ past: [], present: parseGraph(String(response.body.draftJson)), future: [] });
  }

  async function runGraphTest() {
    if (!client) return;
    const response = await client.tests.run(serializeGraph(graph));
    const passed = response.body.passed === true;
    setProblems([{
      severity: passed ? "info" : "error",
      code: "TEST",
      message: passed ? "Expected result matched" : `Expected ${response.body.expected ?? ""}, got ${response.body.actual ?? response.body.errorMessage ?? ""}`
    }]);
    setDockTab("problems");
    setDockOpen(true);
  }

  function patchAttribute(key: keyof GraphDocument["attributes"], value: string) {
    setStack((current) => edit(current, {
      ...current.present,
      attributes: { ...emptyAttributes(), ...current.present.attributes, [key]: value }
    }));
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
    let response;
    try {
      response = await client.graphs.create(createName || "New graph", createKind, createSide);
    } catch (error) {
      setProblems([{ severity: "error", code: "CREATE", message: error instanceof Error ? error.message : "Graph was not created" }]);
      return;
    }
    if (Number(response.body.status) !== 0) {
      setProblems([{ severity: "error", code: "CREATE", message: String(response.body.errorMessage ?? "Graph was not created") }]);
      return;
    }
    const created = response.body.graph as GraphSummary | undefined;
    if (!created?.id) {
      setProblems([{ severity: "error", code: "CREATE", message: "Graph was not created" }]);
      return;
    }
    setGraphs((current) => [...current.filter((item) => item.id !== created.id), created]);
    setBaseRevision(String(created.activeRevision || emptyRevision));
    const source = String(response.body.sourceJson ?? "");
    setStack({ past: [], present: source ? parseGraph(source) : { ...createGraph(created.name || "New graph"), id: created.id }, future: [] });
    setSelected("");
  }

  async function disableGraph(id: string, disabled: boolean) {
    if (!client) return;
    const response = await client.graphs.disable(id, disabled);
    if (Number(response.body.status) !== 0) {
      setProblems([{ severity: "error", code: "GRAPH", message: String(response.body.errorMessage ?? "Disable failed") }]);
      return;
    }
    const listed = await client.graphs.list();
    setGraphs((listed.body.graphs as GraphSummary[] | undefined) ?? []);
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

  async function restoreLkg() {
    if (!client) return;
    const response = await client.history.lkg(graph.id);
    if (Number(response.body.status) !== 0) {
      setProblems([{ severity: "error", code: "LKG", message: String(response.body.errorMessage ?? "No last known good revision.") }]);
      return;
    }
    if (response.body.activeRevision) setBaseRevision(String(response.body.activeRevision));
    const fetched = await client.graphs.fetch(graph.id);
    const source = String(fetched.body.draftJson ?? "");
    if (source) setStack({ past: [], present: parseGraph(source), future: [] });
    const history = await client.history.list(graph.id);
    setRevisions(readRevisions(history.body));
  }

  async function diagnoseUi(document: UiDocumentModel) {
    if (!client) return;
    const response = await client.ui.compile(document);
    setProblems(readProblems(response.body.diagnostics, response.body.errorMessage));
    setUiLayout(`${response.body.elementCount ?? 0} controls · depth ${response.body.maxDepth ?? 0} · ${response.body.bindingCount ?? 0} bindings · ${response.body.mounted ?? ""}`);
    setDockTab("problems");
    setDockOpen(true);
  }

  async function openListed(item: GraphSummary) {
    if (!client) return;
    setOpenTabs((tabs) => tabs.some((tab) => tab.id === item.id) ? tabs : [...tabs, item]);
    const fetched = await client.graphs.fetch(item.id);
    const revision = String(fetched.body.baseRevisionId || item.activeRevision || emptyRevision);
    setBaseRevision(revision);
    setHeadRevision(String(fetched.body.activeRevisionId || revision));
    const source = String(fetched.body.draftJson ?? "");
    setStack({ past: [], present: source ? parseGraph(source) : { ...createGraph(item.name), id: item.id }, future: [] });
    setSelected("");
    const history = await client.history.list(item.id);
    setRevisions(readRevisions(history.body));
  }

  const selectedNode = graph.nodes.find((node) => node.id === selected);

  function placeNode(node: NodeDocument) {
    setStack((current) => edit(current, insertNode(current.present, node)));
    setSelected(node.id);
    setFocusToken((token) => token + 1);
    setPaletteOpen(false);
  }

  function nodeFromBinding(entry: CatalogEntry): NodeDocument {
    const pins: PinDocument[] = [];
    if (!entry.isPure) {
      pins.push({ id: crypto.randomUUID(), name: "In", direction: "input", kind: "execution" });
      pins.push({ id: crypto.randomUUID(), name: "Out", direction: "output", kind: "execution" });
    }
    for (const parameter of entry.parameters) {
      pins.push({
        id: crypto.randomUUID(),
        name: parameter.name || "value",
        direction: parameter.direction.toLowerCase() === "output" ? "output" : "input",
        kind: "data",
        dataType: parameter.typeName
      });
    }
    if (entry.returnType && entry.returnType !== "void" && entry.returnType !== "System.Void") {
      pins.push({ id: crypto.randomUUID(), name: "Result", direction: "output", kind: "data", dataType: entry.returnType });
    }
    return {
      id: crypto.randomUUID(),
      name: methodName(entry.signature),
      nodeType: entry.signature,
      properties: { bindingId: entry.bindingId, pure: entry.isPure ? "true" : "false", side: entry.side },
      pins
    };
  }

  function nodeFromSchema(schema: SchemaDocument): NodeDocument {
    const fields = schema.kind === "Enum"
      ? (schema.members ?? []).map((member) => ({ name: member, typeName: schema.name }))
      : schema.fields.map((field) => ({ name: field.name, typeName: field.typeName }));
    return {
      id: crypto.randomUUID(),
      name: schema.name,
      nodeType: `Schema.${schema.kind ?? "Component"}`,
      properties: { schemaId: schema.id, kind: schema.kind ?? "Component" },
      pins: [
        { id: crypto.randomUUID(), name: "In", direction: "input", kind: "execution" },
        { id: crypto.randomUUID(), name: "Out", direction: "output", kind: "execution" },
        ...fields.map((field) => ({ id: crypto.randomUUID(), name: field.name, direction: "output" as const, kind: "data" as const, dataType: field.typeName }))
      ]
    };
  }

  async function insertBinding(entry: CatalogEntry) {
    let node = nodeFromBinding(entry);
    if (client && entry.bindingId) {
      const response = await client.bindings.materialize(entry.bindingId);
      const shell = String(response.body.nodeJson ?? "");
      const materialized = shell ? parseGraph(shell).nodes[0] : undefined;
      if (Number(response.body.status) === 0 && materialized) node = materialized;
    }
    placeNode(node);
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
    else await client.debug.setBreakpoint(graph.id, selectedNode.id, breakpointCondition);
  }

  async function inspectDebug() {
    if (!client) return;
    const response = await client.debug.inspect(graph.id);
    setLocals(readValues(response.body.locals));
    setWatches(readValues(response.body.watches));
    setTrace(Array.isArray(response.body.trace) ? response.body.trace as { nodeId: string; instructionPointer: number }[] : []);
    setEventPayload(String(response.body.eventPayload ?? ""));
    const nodeId = String(response.body.suspendedNodeId ?? "");
    if (nodeId) {
      setSelected(nodeId);
      setFocusToken((token) => token + 1);
    }
    setDockTab("debug");
    setDockOpen(true);
  }

  async function addWatch() {
    if (!client || !watchName) return;
    await client.debug.watch(watchName, watchExpr);
    await inspectDebug();
  }

  async function loadProfile() {
    if (!client) return;
    await client.profiler.subscribe(graph.id);
    const response = await client.profiler.snapshot(graph.id);
    setProfile((response.body.snapshot ?? null) as typeof profile);
    setDockTab("profiler");
    setDockOpen(true);
  }

  async function loadAudit() {
    if (!client) return;
    const response = await client.audit.query();
    setAuditEntries(Array.isArray(response.body.entries) ? response.body.entries as { action: string; author: string; message: string }[] : []);
  }

  async function saveUi(document: UiDocumentModel) {
    if (!client) return;
    const response = await client.ui.save(document);
    if (Number(response.body.status) !== 0) {
      setProblems([{ severity: "error", code: "UI", message: String(response.body.errorMessage ?? "UI was not saved") }]);
      return;
    }
    const listed = await client.ui.list();
    setUiDocuments(Array.isArray(listed.body.documents) ? listed.body.documents as UiDocumentModel[] : []);
  }

  async function loadSchemas() {
    if (!client) return;
    const listed = await client.schemas.list();
    setSchemas(Array.isArray(listed.body.schemas) ? listed.body.schemas as SchemaDocument[] : []);
  }

  async function saveSchema(schema: SchemaDocument) {
    const stored = {
      id: schema.id,
      name: schema.name,
      kind: schema.kind ?? (schema.isComponent ? "Component" : "Struct"),
      isComponent: schema.kind ? schema.kind === "Component" : schema.isComponent,
      fields: schema.fields.map((field) => ({
        id: field.id,
        name: field.name,
        typeName: field.typeName,
        defaultValue: field.defaultValue ?? ""
      }))
    };
    setSchemas((current) => current.some((item) => item.id === schema.id)
      ? current.map((item) => item.id === schema.id ? schema : item)
      : [...current, schema]);
    setStack((current) => edit(current, upsertSchema(current.present, stored)));
    placeNode(nodeFromSchema(schema));
    if (!client) {
      setSchemaNote(`${schema.name} is in this graph. Connect a session to publish it.`);
      return;
    }
    try {
      const response = await client.schemas.save(schema);
      const changes = Array.isArray(response.body.changes) ? response.body.changes as { kind: string; detail: string }[] : [];
      setDiffLines(changes);
      if (Number(response.body.status) !== 0) {
        const message = String(response.body.errorMessage ?? "Schema was not saved");
        setSchemaNote(message);
        setProblems([{ severity: "error", code: "SCHEMA", message }]);
        setDockTab("problems");
        setDockOpen(true);
        return;
      }
      await loadSchemas();
      setSchemaNote(`${schema.name} is on the canvas.`);
    } catch (error) {
      const message = error instanceof Error ? error.message : "Schema was not saved";
      setSchemaNote(message);
      setProblems([{ severity: "error", code: "SCHEMA", message }]);
      setDockTab("problems");
      setDockOpen(true);
    }
  }

  const statusLabel = preview ? "Preview" : status === "bridge" ? "Connected" : "Offline";
  const errorCount = problems.filter((problem) => problem.severity === "error").length;
  const warningCount = problems.filter((problem) => problem.severity === "warning").length;

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
        <button disabled={!compileAllowed} onClick={() => void runSandbox()}>Sandbox</button>
        <button className="primary" disabled={!publishAllowed} onClick={() => setPublishOpen(true)}>Publish</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.pause(graph.id)}>Pause</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.resume(graph.id)}>Continue</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.stepOver(graph.id)}>Step Over</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.stepInto(graph.id)}>Step Into</button>
        <button disabled={!debugAllowed} onClick={() => void client?.debug.stepOut(graph.id)}>Step Out</button>
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
      <div className={activity === "design" ? "workspace ui-mode" : "workspace"}>
        <nav className="activity" aria-label="Activity">
          {activities.map((item) => (
            <button key={item} className={item === activity ? "active" : ""} onClick={() => { setActivity(item); if (item === "audit") void loadAudit(); if (item === "types") void loadSchemas(); }}>{item}</button>
          ))}
        </nav>
        <aside className="panel">
          {activity === "explorer" ? (
            <>
              <input value={graphQuery} placeholder="Filter graphs" onChange={(event) => setGraphQuery(event.target.value)} />
              <label className="field">Owner<input value={ownerFilter} onChange={(event) => setOwnerFilter(event.target.value)} /></label>
              <label className="field">Tag<input value={tagFilter} onChange={(event) => setTagFilter(event.target.value)} /></label>
              <label className="field">Revision<input value={revisionFilter} onChange={(event) => setRevisionFilter(event.target.value)} /></label>
              <label className="field">Side
                <select value={sideFilter} onChange={(event) => setSideFilter(event.target.value)}>
                  <option>All</option>
                  {graphSides.map((side) => <option key={side}>{side}</option>)}
                </select>
              </label>
              {graphs.some((item) => item.overrideOf) ? (
                <div>
                  <div className="section-label">Overrides</div>
                  <ul className="list">
                    {graphs.filter((item) => item.overrideOf && matchesGraph(item, graphQuery, sideFilter, ownerFilter, tagFilter, revisionFilter)).map((item) => (
                      <GraphRow key={item.id} item={item} currentId={graph.id} onOpen={() => void openListed(item)} onDisable={() => void disableGraph(item.id, true)} onDelete={() => void deleteGraph(item.id)} />
                    ))}
                  </ul>
                </div>
              ) : null}
              {(["Live", "Drafts", "Disabled", "Failed"] as const).map((bucket) => {
                const items = graphs.filter((item) => graphBucket(item) === bucket && matchesGraph(item, graphQuery, sideFilter, ownerFilter, tagFilter, revisionFilter));
                if (items.length === 0) return null;
                return (
                  <div key={bucket}>
                    <div className="section-label">{bucket}</div>
                    {bucket === "Live" ? graphKinds.map((kind) => {
                      const grouped = items.filter((item) => (item.kind ?? "System") === kind);
                      if (grouped.length === 0) return null;
                      return (
                        <div key={kind}>
                          <div className="muted">{kind}</div>
                          <ul className="list">{grouped.map((item) => <GraphRow key={item.id} item={item} currentId={graph.id} onOpen={() => void openListed(item)} onDisable={() => void disableGraph(item.id, true)} onDelete={() => void deleteGraph(item.id)} />)}</ul>
                        </div>
                      );
                    }) : (
                      <ul className="list">{items.map((item) => <GraphRow key={item.id} item={item} currentId={graph.id} onOpen={() => void openListed(item)} onDisable={() => void disableGraph(item.id, item.status !== "Disabled")} onDelete={() => void deleteGraph(item.id)} />)}</ul>
                    )}
                  </div>
                );
              })}
              {graphs.length === 0 ? <p className="muted">No graphs yet</p> : null}
              {!graphs.some((item) => item.id === graph.id) ? <p className="muted">Drafts · {graph.name}</p> : null}
              <button onClick={() => setCreateOpen(true)}>New graph</button>
            </>
          ) : null}
          {activity === "bindings" ? (
            <BindingBrowser
              entries={catalog}
              status={catalogStatus}
              error={catalogError}
              query={bindingQuery}
              onQuery={setBindingQuery}
              onInsert={(entry) => void insertBinding(entry)}
            />
          ) : null}
          {activity === "types" ? <SchemaEditor schemas={schemas} note={schemaNote} onSave={(schema) => void saveSchema(schema)} /> : null}
          {activity === "runtime" ? (
            <div>
              <button type="button" onClick={() => void loadRuntime()}>Refresh runtime</button>
              <div className="section-label">Graphs</div>
              <ul className="list">
                {runtimeGraphs.map((item) => <li key={item.id}>{item.name} <span className="muted">{item.activeRevision.slice(0, 8)}</span></li>)}
                {runtimeGraphs.length === 0 ? <li className="muted">No active graphs</li> : null}
              </ul>
              <div className="section-label">Entities</div>
              <ul className="list">
                {runtimeEntities.map((item) => (
                  <li key={`${item.entityId}-${item.schema}`}>
                    <button type="button" className={selectedEntity === item.entityId ? "active" : ""} onClick={() => setSelectedEntity(item.entityId)}>
                      {item.entityId} · {item.schema}
                    </button>
                    {selectedEntity === item.entityId ? item.fields.map((field) => <div key={field.name} className="muted">{field.name}: {field.value}</div>) : null}
                  </li>
                ))}
                {runtimeEntities.length === 0 ? <li className="muted">No entity components</li> : null}
              </ul>
            </div>
          ) : null}
          {activity === "access" ? (
            <ul className="list">
              {(["ViewGraphs", "EditDrafts", "Compile", "PublishServer", "PublishShared", "Rollback", "Debug"] as const).map((name, index) => (
                <li key={name} className={(bits & (1 << index)) !== 0 ? "" : "muted"}>{name}</li>
              ))}
            </ul>
          ) : null}
          {activity === "audit" ? (
            <ul className="list">
              {auditEntries.map((entry, index) => <li key={`${entry.action}-${index}`}>{entry.action} · {entry.author} · {entry.message}</li>)}
              {auditEntries.length === 0 ? <li className="muted">No audit entries</li> : null}
            </ul>
          ) : null}
        </aside>
        <main className="canvas">
          <div className="dock-bar" role="tablist" aria-label="Canvas">
            <button type="button" className={activity === "design" ? "active" : ""} onClick={() => setActivity("design")}>UI canvas</button>
            <button type="button" className={activity === "design" ? "" : "active"} onClick={() => setActivity("explorer")}>Graph canvas</button>
          </div>
          {activity === "design" ? <UiDesigner documents={uiDocuments} catalog={uiCatalog} styles={uiStyles} layout={uiLayout} onSave={(document) => void saveUi(document)} onDiagnose={(document) => void diagnoseUi(document)} onPreview={async (document) => {
            const response = await client?.ui.preview(document);
            const body = response?.body ?? {};
            return { mode: String(body.mode ?? "offline"), xaml: String(body.xaml ?? ""), framePng: String(body.framePng ?? "") };
          }} onPatch={(documentId, operations) => void client?.ui.patch(documentId, operations)} onOpenGraph={(next) => { setActivity("explorer"); setStack({ past: [], present: next, future: [] }); setSelected(""); }} /> : null}
          {activity !== "design" && openTabs.length > 0 ? (
            <div className="dock-bar" role="tablist" aria-label="Open graphs">
              {openTabs.map((tab) => (
                <button key={tab.id} type="button" role="tab" aria-selected={tab.id === graph.id} className={tab.id === graph.id ? "active" : ""} onClick={() => void openListed(tab)}>{tab.name}</button>
              ))}
            </div>
          ) : null}
          {activity !== "design" && preview ? <div className="banner"><strong>Preview Mode</strong><span>No authoring backend connected</span></div> : null}
          {activity !== "design" && headRevision !== baseRevision && headRevision !== emptyRevision && baseRevision !== emptyRevision ? (
            <div className="banner">
              <strong>OUT OF DATE</strong>
              <span>Head {headRevision.slice(0, 8)} · base {baseRevision.slice(0, 8)}</span>
              <button onClick={() => void discardDraft()}>Discard</button>
              {recoveryJson ? <button onClick={restoreRecovery}>Restore local</button> : null}
            </div>
          ) : null}
          {activity !== "design" && recoveryJson && headRevision === baseRevision ? <div className="banner"><strong>Local recovery</strong><button onClick={restoreRecovery}>Restore</button></div> : null}
          {activity !== "design" ? <GraphCanvas
            document={graph}
            selectedId={selected}
            focusToken={focusToken}
            alertPin={alertPin}
            heat={Object.fromEntries((profile?.hottest ?? []).map((item) => [item.nodeId, item.hits]))}
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
          /> : null}
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
              {selectedNode.nodeType === "Schema.GetField" ? (
                <SchemaFieldDefaultField
                  graph={graph}
                  schemaName={selectedNode.properties.Schema ?? ""}
                  fieldName={selectedNode.properties.Field ?? ""}
                  onChange={(value) => setStack((current) => edit(current, setSchemaFieldDefault(current.present, selectedNode.properties.Schema ?? "", selectedNode.properties.Field ?? "", value)))}
                />
              ) : null}
              <label className="field">Condition<input value={breakpointCondition} placeholder="r0==7" onChange={(event) => setBreakpointCondition(event.target.value)} /></label>
              <button disabled={!debugAllowed} onClick={() => void toggleBreakpoint()}>{selectedNode.properties.breakpoint === "true" ? "Clear breakpoint" : "Breakpoint"}</button>
            </div>
          ) : (
            <>
              <label className="field">Description
                <input value={graph.description ?? ""} onChange={(event) => setStack((current) => edit(current, { ...current.present, description: event.target.value }))} />
              </label>
              <label className="field">Tags
                <input value={graph.tags ?? ""} onChange={(event) => setStack((current) => edit(current, { ...current.present, tags: event.target.value }))} />
              </label>
              <label className="field">Version
                <input value={graph.version ?? "1.0.0"} onChange={(event) => setStack((current) => edit(current, { ...current.present, version: event.target.value }))} />
              </label>
              <label className="field">Owner<input value={graph.attributes?.author ?? ""} onChange={(event) => patchAttribute("author", event.target.value)} /></label>
              <label className="field">Schedule
                <select value={graph.attributes?.schedule || "Update"} onChange={(event) => patchAttribute("schedule", event.target.value)}>
                  <option>Update</option>
                  <option>Tick</option>
                  <option>Manual</option>
                </select>
              </label>
              <label className="field">Security profile
                <select value={graph.attributes?.securityProfile || "Default"} onChange={(event) => patchAttribute("securityProfile", event.target.value)}>
                  <option>Default</option>
                  <option>ServerOnly</option>
                  <option>Sandboxed</option>
                </select>
              </label>
              <label className="field">Hot reload
                <select value={graph.attributes?.hotReload || "Automatic"} onChange={(event) => patchAttribute("hotReload", event.target.value)}>
                  <option>Automatic</option>
                  <option>Manual</option>
                  <option>Disabled</option>
                </select>
              </label>
              <label className="field">Budget<input value={graph.attributes?.budget ?? "256"} onChange={(event) => patchAttribute("budget", event.target.value)} /></label>
              <label className="field">Override of<input value={graph.attributes?.overrideOf ?? ""} onChange={(event) => patchAttribute("overrideOf", event.target.value)} /></label>
              <label className="field">Entity<input value={graph.attributes?.entity ?? ""} placeholder="7" onChange={(event) => patchAttribute("entity", event.target.value)} /></label>
              <label className="field">Prototype<input value={graph.attributes?.prototype ?? ""} onChange={(event) => patchAttribute("prototype", event.target.value)} /></label>
              <label className="field">Enum
                <select value={graph.attributes?.enumType ?? ""} onChange={(event) => patchAttribute("enumType", event.target.value)}>
                  <option value="">None</option>
                  {enums.map((schema) => <option key={schema.id} value={schema.name}>{schema.name}</option>)}
                </select>
              </label>
              {graph.kind === "Function" ? <label className="field">Generic parameters<input value={graph.attributes?.generics ?? ""} placeholder="T, TState" onChange={(event) => patchAttribute("generics", event.target.value)} /></label> : null}
              <label className="field">Expected result<input value={graph.attributes?.expected ?? ""} onChange={(event) => patchAttribute("expected", event.target.value)} /></label>
              <button type="button" onClick={() => void runGraphTest()}>Run graph test</button>
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
          <div className="section-label">{graph.kind === "Function" ? "Parameters" : "Variables"}</div>
          <VariableEditor
            variables={graph.variables}
            onChange={(variables) => setStack((current) => edit(current, { ...current.present, variables }))}
            onFind={(variable) => {
              const uses = findVariableUses(graph, variable);
              setProblems(uses.length > 0 ? uses.map((use) => ({ severity: "info", code: "REF", message: use.message, nodeId: use.nodeId })) : [{ severity: "info", code: "REF", message: `${variable.name} has no references` }]);
              setDockTab("problems");
              setDockOpen(true);
            }}
          />
        </aside>
      </div>
      <footer className="dock">
        <div className="dock-bar">
          {dockTabs.map((tab) => (
            <button key={tab} className={tab === dockTab ? "active" : ""} onClick={() => { setDockTab(tab); setDockOpen(true); if (tab === "debug") void inspectDebug(); if (tab === "profiler") void loadProfile(); }}>{tab}</button>
          ))}
          <span className="grow" />
          <button onClick={() => setDockOpen((open) => !open)}>{dockOpen ? "Hide" : "Show"}</button>
        </div>
        {dockOpen ? (
          <div className="dock-body">
            {dockTab === "problems" ? <section>
              {(["Errors", "Warnings", "Security", "Prediction", "Performance", "Migration"] as const).map((group) => {
                const items = problems.filter((problem) => problemGroup(problem.code, problem.severity) === group);
                if (items.length === 0) return null;
                return (
                  <div key={group}>
                    <div className="section-label">{group}</div>
                    <ul className="list">
                      {items.map((problem, index) => (
                        <li key={`${problem.code}-${index}`}>
                          <button className={`problem ${problem.severity}`} onClick={() => {
                            if (!problem.nodeId) return;
                            setSelected(problem.nodeId);
                            setAlertPin(problem.pinId ?? "");
                            setFocusToken((token) => token + 1);
                          }}>{problem.code}: {problem.message}</button>
                          {problem.relatedNodeId ? <button type="button" onClick={() => { setSelected(problem.relatedNodeId!); setFocusToken((token) => token + 1); }}>Related</button> : null}
                          {problem.suggestedFix ? <button type="button" onClick={() => setStack((current) => edit(current, applyQuickFix(current.present, problem)))}>{problem.suggestedFix}</button> : null}
                        </li>
                      ))}
                    </ul>
                  </div>
                );
              })}
              {problems.length === 0 ? <p className="muted">No problems</p> : null}
            </section> : null}
            {dockTab === "history" ? <section>
              <ul className="list">
                {revisions.slice(0, historyLimit).map((revision) => (
                  <li key={revision.revisionId}>
                    <span className="mono">{revision.message || revision.revisionId}</span>
                    {revision.activationTick ? <span className="muted">tick {revision.activationTick}</span> : null}
                    <span className="muted">{revision.author}</span>
                    <button onClick={() => void compareRevision(revision.revisionId)}>Diff</button>
                    <button disabled={!rollbackAllowed} onClick={() => void rollback(revision.revisionId)}>Rollback</button>
                    <button disabled={!rollbackAllowed} onClick={() => void restoreLkg()}>Last known good</button>
                  </li>
                ))}
                {revisions.length === 0 ? <li className="muted">No revisions</li> : null}
                {revisions.length > historyLimit ? <li><button type="button" onClick={() => setHistoryLimit((value) => value + 40)}>Show more</button></li> : null}
                <li><button type="button" onClick={() => void rebaseDraft()}>Rebase onto head</button></li>
                <li>
                  <label className="field">Other author draft
                    <textarea value={otherDraft} onChange={(event) => setOtherDraft(event.target.value)} />
                  </label>
                  <button type="button" onClick={() => void mergeDraft()}>Semantic merge</button>
                </li>
                {diffLines.map((line, index) => <li key={`${line.kind}-${index}`} className="muted">{line.kind}: {line.detail}</li>)}
              </ul>
            </section> : null}
            {dockTab === "watch" || dockTab === "debug" ? (
              <section>
                <ul className="list">
                  {locals.map((item) => <li key={item.name}>{item.name}: {item.value}</li>)}
                  {watches.map((item) => <li key={item.name}>{item.name} {item.expression}: {item.value}</li>)}
                  <li className="muted">Call stack runs until Return. The virtual machine has no frames.</li>
                  {eventPayload ? <li>Event payload {eventPayload}</li> : null}
                  {trace.map((item, index) => <li key={`${item.nodeId}-${index}`} className="muted">{item.nodeId} @{item.instructionPointer}</li>)}
                  {locals.length === 0 && watches.length === 0 ? <li className="muted">No suspension</li> : null}
                </ul>
                <label className="field">Watch<input value={watchName} onChange={(event) => setWatchName(event.target.value)} /></label>
                <label className="field">Expression<input value={watchExpr} placeholder="r0 or entity:7.Door.State" onChange={(event) => setWatchExpr(event.target.value)} /></label>
                <button disabled={!debugAllowed} onClick={() => void addWatch()}>Add watch</button>
              </section>
            ) : null}
            {dockTab === "profiler" ? (
              <section>
                <p className="muted">invocations {profile?.invocations ?? 0} · avg {Math.round(profile?.averageMicroseconds ?? 0)} µs · p95 {Math.round(profile?.p95Microseconds ?? 0)} µs · queries {profile?.queryIterations ?? 0} · budget {profile?.budgetViolations ?? 0} · alloc {profile?.allocatedBytes ?? 0} B · net {profile?.networkBytes ?? 0} B · instructions {profile?.instructions ?? 0} · native {profile?.nativeCalls ?? 0} · yields {profile?.yields ?? 0}</p>
                <p className="muted">{(profile?.samples ?? []).slice(-16).map((sample) => Math.round(sample)).join(" ")}</p>
                <ul className="list">
                  {(profile?.hottest ?? []).map((item) => <li key={item.nodeId}>{item.hits} · {Math.round(item.microseconds ?? 0)} µs · {item.nodeId}</li>)}
                </ul>
              </section>
            ) : null}
            {dockTab === "output" ? <section><p className="muted">{compileHash ? `SemanticHash ${compileHash}` : "Compile to see the semantic hash"} · {compileStage}{compileMs > 0 ? ` · ${compileMs} ms` : ""}</p>{sandboxResult ? <p>{sandboxResult}</p> : null}</section> : null}
          </div>
        ) : null}
      </footer>
      <footer className="statusline" role="status">
        <span>{statusLabel}</span>
        <span>{graph.side}</span>
        <span>{graph.kind}</span>
        <span className="mono">{baseRevision.slice(0, 8)}</span>
        <span>{errorCount} errors</span>
        <span>{warningCount} warnings</span>
        <span className="grow" />
        <button type="button" onClick={() => setReconnectToken((token) => token + 1)}>Reconnect</button>
        <span>{target}</span>
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
            <p className="muted">{graph.side} · base {baseRevision.slice(0, 8)} · head {headRevision.slice(0, 8)} · {graph.nodes.length} nodes · {clientCount} clients</p>
            <p className="muted">{migrationSummary}</p>
            <p className="muted">{activationTick > 0 ? `Activation counter ${activationTick}. It is the host tick or the local authoring counter plus 4, not a game tick.` : "Shared publish stores an activation counter: host tick or local counter plus 4."}</p>
            <ul className="list">
              {diffLines.map((line, index) => <li key={`${line.kind}-${index}`} className="muted">{line.kind}: {line.detail}</li>)}
              {diffLines.length === 0 ? <li className="muted">No semantic changes</li> : null}
            </ul>
            <label className="field">Message<input value={publishMessage} onChange={(event) => setPublishMessage(event.target.value)} /></label>
            {graph.side === "Shared" || graph.side === "SharedPredicted" ? (
              <label className="field">Confirm shared publish
                <input type="checkbox" checked={publishConfirm} onChange={(event) => setPublishConfirm(event.target.checked)} />
              </label>
            ) : null}
            <div className="dialog-actions">
              <button type="button" onClick={() => setPublishOpen(false)}>Cancel</button>
              <button className="primary" type="submit" disabled={(graph.side === "Shared" || graph.side === "SharedPredicted") && !publishConfirm}>Publish</button>
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
      {quickOpen ? (
        <div className="palette" onClick={() => setQuickOpen(false)}>
          <form onClick={(event) => event.stopPropagation()}>
            <input autoFocus placeholder="Graphs, schemas, docs, entities" value={searchQuery} onChange={(event) => setSearchQuery(event.target.value)} onKeyDown={(event) => { if (event.key === "Escape") setQuickOpen(false); }} />
            <ul className="list">
              {searchHits.slice(0, 30).map((item) => <li key={item.id}><button type="button" onClick={() => {
                setQuickOpen(false);
                if (item.id.startsWith("graph:")) {
                  const found = graphs.find((graphItem) => graphItem.id === item.id.slice(6));
                  if (found) void openListed(found);
                }
                if (item.id.startsWith("schema:")) setActivity("types");
                if (item.id.startsWith("entity:")) setActivity("runtime");
                if (item.id.startsWith("doc:")) setActivity("bindings");
              }}>{item.text}</button></li>)}
            </ul>
          </form>
        </div>
      ) : null}
      {commandsOpen ? (
        <div className="palette" onClick={() => setCommandsOpen(false)}>
          <form onClick={(event) => event.stopPropagation()}>
            <div className="section-label">Commands</div>
            <ul className="list">
              <li><button type="button" onClick={() => { setCommandsOpen(false); void save(); }}>Save draft</button></li>
              <li><button type="button" onClick={() => { setCommandsOpen(false); void compile(); }}>Compile current</button></li>
              <li><button type="button" onClick={() => { setCommandsOpen(false); setPublishOpen(true); }}>Publish</button></li>
              <li><button type="button" onClick={() => { setCommandsOpen(false); void inspectDebug(); }}>Inspect debugger</button></li>
            </ul>
          </form>
        </div>
      ) : null}
      {paletteOpen ? (
        <div className="palette" onClick={() => setPaletteOpen(false)}>
          <form onClick={(event) => event.stopPropagation()}>
            <input autoFocus placeholder="Add node" onKeyDown={(event) => { if (event.key === "Escape") setPaletteOpen(false); }} />
            <ul className="list">
              {(graph.kind === "UI" ? [...palette, ["UI.OnEvent", "On UI event"], ["UI.GetProperty", "Get property"], ["UI.SetProperty", "Set property"], ["UI.Focus", "Focus"], ["UI.OnAction", "On BUI action"], ["UI.Notify", "Notify"]] as const : palette).map(([nodeType, name]) => (
                <li key={nodeType}><button type="button" onClick={() => insert(nodeType, name)}>{name} <span className="muted">{nodeType}</span></button></li>
              ))}
            </ul>
          </form>
        </div>
      ) : null}
    </div>
  );
}

function readStored<T extends string>(value: string | null, allowed: readonly T[], fallback: T): T {
  return allowed.includes(value as T) ? value as T : fallback;
}

function readValues(value: unknown): { name: string; expression: string; value: string }[] {
  if (!Array.isArray(value)) return [];
  return value.map((item) => {
    const row = item as { name?: unknown; expression?: unknown; value?: unknown };
    return { name: String(row.name ?? ""), expression: String(row.expression ?? ""), value: String(row.value ?? "") };
  });
}

function pipelineStage(problems: Problem[]): string {
  const error = problems.find((problem) => problem.severity === "error");
  if (!error) return "Parse → Type Check → Binding → Side → Verify";
  if (error.code.startsWith("DOC")) return "Stopped at Parse";
  if (error.code.startsWith("TYP")) return "Stopped at Type Check";
  if (error.code.startsWith("POL")) return "Stopped at Side";
  return "Stopped at Verify";
}

function graphBucket(item: GraphSummary): "Live" | "Drafts" | "Disabled" | "Failed" {
  if (item.status === "Disabled") return "Disabled";
  if (item.status === "Failed") return "Failed";
  if (item.hasDraft) return "Drafts";
  return "Live";
}

function matchesGraph(item: GraphSummary, query: string, side: string, owner = "", tag = "", revision = ""): boolean {
  const name = item.name.toLowerCase().includes(query.trim().toLowerCase());
  const sideMatches = side === "All" || (item.side ?? "Server") === side;
  const ownerMatches = !owner.trim() || (item.owner ?? "").toLowerCase().includes(owner.trim().toLowerCase());
  const tagMatches = !tag.trim() || (item.tags ?? "").toLowerCase().includes(tag.trim().toLowerCase());
  const revisionMatches = !revision.trim() || (item.activeRevision ?? "").toLowerCase().includes(revision.trim().toLowerCase());
  return name && sideMatches && ownerMatches && tagMatches && revisionMatches;
}

function SchemaFieldDefaultField(props: { graph: GraphDocument; schemaName: string; fieldName: string; onChange: (value: string) => void }) {
  const field = (props.graph.schemas ?? []).find((schema) => schema.name === props.schemaName)?.fields.find((item) => item.name === props.fieldName);
  if (!field) {
    return <p className="muted">Schema {props.schemaName || "?"} has no field {props.fieldName || "?"}.</p>;
  }

  const prototype = field.typeName.includes("EntProtoId");
  return (
    <label className="field">{prototype ? "Prototype" : "Default"}
      <input value={field.defaultValue ?? ""} onChange={(event) => props.onChange(event.target.value)} />
      <span className="muted">Applies when the entity prototype omits {field.name}. A value in YAML overrides it.</span>
    </label>
  );
}

function GraphRow(props: { item: GraphSummary; currentId: string; onOpen: () => void; onDisable: () => void; onDelete: () => void }) {
  return (
    <li className="graph-row">
      <button className={props.item.id === props.currentId ? "active" : ""} onClick={props.onOpen}>{props.item.name}</button>
      <button type="button" onClick={props.onDisable}>{props.item.status === "Disabled" ? "Enable" : "Disable"}</button>
      <button type="button" onClick={props.onDelete}>Delete</button>
    </li>
  );
}

function readProblems(value: unknown, error: unknown): Problem[] {
  const diagnostics = Array.isArray(value) ? value as { severity?: unknown; code?: unknown; message?: unknown; nodeId?: unknown; pinId?: unknown; relatedNodeId?: unknown; suggestedFix?: unknown }[] : [];
  if (diagnostics.length > 0) return diagnostics.map((item) => {
    const code = String(item.code ?? "DIAG");
    return {
      severity: String(item.severity ?? "error"),
      code,
      message: String(item.message ?? ""),
      nodeId: item.nodeId ? String(item.nodeId) : undefined,
      pinId: item.pinId ? String(item.pinId) : undefined,
      relatedNodeId: item.relatedNodeId ? String(item.relatedNodeId) : undefined,
      suggestedFix: suggestedFixFor(code, item.suggestedFix ? String(item.suggestedFix) : undefined)
    };
  });
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
