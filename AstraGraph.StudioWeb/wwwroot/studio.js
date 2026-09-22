const palette = [
  { type: "Event.InteractUsing", name: "On Interact" },
  { type: "Flow.Branch", name: "Branch" },
  { type: "Flow.Delay", name: "Delay" },
  { type: "Native.Call", name: "Native Call" }
];

const nodes = [];
const connections = [];
let selected = null;
let socket = null;
let sessionId = "";
let graphId = "";
let baseRevisionId = "00000000-0000-0000-0000-000000000000";
let draftRevisionId = "";
const canvas = document.getElementById("canvas");
const ctx = canvas.getContext("2d");
const problems = document.getElementById("problems");
const status = document.getElementById("status");

document.getElementById("palette").innerHTML = palette.map((item, index) =>
  `<div class="palette-item" data-index="${index}">${item.name}</div>`).join("");

document.getElementById("palette").addEventListener("click", (event) => {
  const row = event.target.closest(".palette-item");
  if (!row) return;
  const item = palette[Number(row.dataset.index)];
  nodes.push(makeNode(item));
  draw();
});

canvas.addEventListener("mousedown", (event) => {
  const point = pointFromEvent(event);
  const hit = nodes.findLast(node => point.x >= node.x && point.x <= node.x + 140 && point.y >= node.y && point.y <= node.y + 48) ?? null;
  if (selected && hit && hit !== selected) {
    const from = selected.pins.find(pin => pin.direction === "Output");
    const to = hit.pins.find(pin => pin.direction === "Input");
    if (from && to) connections.push({ fromNode: selected.id, fromPin: from.id, toNode: hit.id, toPin: to.id });
  }
  selected = hit;
  draw();
});

canvas.addEventListener("mousemove", (event) => {
  if (!selected || (event.buttons & 1) === 0) return;
  const point = pointFromEvent(event);
  selected.x = point.x - 70;
  selected.y = point.y - 24;
  draw();
});

document.getElementById("compile").addEventListener("click", () => {
  if (!sessionId) {
    renderProblems(["Authoring backend unavailable"]);
    return;
  }
  const draftJson = JSON.stringify(graphDocument());
  send("draft.save.request", {
    sessionId,
    graphId,
    baseRevisionId,
    draftJson,
    authorMessage: "Studio save"
  });
  send("draft.compile.request", { sessionId, graphId, draftJson });
});

document.getElementById("publish").addEventListener("click", () => {
  if (!sessionId) {
    renderProblems(["Authoring backend unavailable"]);
    return;
  }
  send("draft.publish.request", {
    sessionId,
    graphId,
    baseRevisionId,
    draftJson: JSON.stringify(graphDocument()),
    publishMessage: "Studio publish"
  });
});

document.getElementById("rollback").addEventListener("click", () => {
  if (!sessionId) {
    renderProblems(["Authoring backend unavailable"]);
    return;
  }
  const target = document.getElementById("history").dataset.revision || "";
  send("history.rollback.request", { sessionId, graphId, targetRevisionId: target || baseRevisionId });
});

document.getElementById("debug").addEventListener("click", () => {
  if (!sessionId) {
    renderProblems(["Authoring backend unavailable"]);
    return;
  }
  send("debugger.command.request", { sessionId, graphId, action: 3 });
});

connect();

function connect() {
  const params = new URLSearchParams(window.location.search);
  const nonce = params.get("nonce");
  const token = params.get("token") ?? "";
  if (!nonce) {
    status.textContent = "offline";
    renderProblems(["Authoring backend unavailable"]);
    return;
  }

  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  socket = new WebSocket(`${protocol}//${window.location.host}/ws?nonce=${encodeURIComponent(nonce)}`);
  socket.binaryType = "arraybuffer";
  socket.addEventListener("open", () => {
    status.textContent = "bridge";
    send("auth.handshake.request", {
      clientVersion: "1.0.0",
      authorToken: token,
      authorName: params.get("author") ?? "Studio"
    });
  });
  socket.addEventListener("message", (event) => {
    const message = unframe(new Uint8Array(event.data));
    if (!message) return;
    if (message.kind === "auth.handshake.response" && message.body.status === 0) {
      sessionId = message.body.sessionId;
      applyPermissions(message.body.permissions);
      send("graph.list.request", { sessionId });
      send("catalog.query.request", { sessionId });
      return;
    }
    if (message.kind === "session.updated") {
      applyPermissions(message.body.permissions);
      if (!permissionBits(message.body.permissions)) {
        sessionId = "";
        renderProblems(["Authoring backend unavailable"]);
      }
      return;
    }
    if (message.kind === "graph.list.response" && message.body.graphs?.length) {
      graphId = message.body.graphs[0].id;
      baseRevisionId = message.body.graphs[0].activeRevision || baseRevisionId;
      send("graph.fetch.request", { sessionId, graphId });
      send("history.list.request", { sessionId, graphId });
    }
    if (message.kind === "catalog.query.response") {
      for (const entry of message.body.entries ?? []) {
        palette.push({ type: "Native.Call", name: entry.signature, method: entry.signature });
      }
      renderPalette();
    }
    if (message.kind === "history.list.response") {
      renderHistory(message.body.revisions ?? []);
    }
    if (message.kind === "draft.save.response" && message.body.draftRevisionId) {
      draftRevisionId = message.body.draftRevisionId;
    }
    if (message.kind === "graph.fetch.response" && message.body.draftJson) {
      loadDraft(message.body.draftJson);
    }
    if (message.kind === "draft.save.response" && message.body.errorMessage) {
      renderProblems([message.body.errorMessage]);
    }
    if (message.kind === "draft.compile.response" || message.kind === "draft.publish.response") {
      const lines = (message.body.diagnostics ?? []).map(item => item.message ?? item.Message ?? "diagnostic");
      if (message.body.errorMessage) lines.push(message.body.errorMessage);
      if (message.body.publishedRevision) baseRevisionId = message.body.publishedRevision;
      if (message.body.status === 2) lines.push("Stale draft conflict.");
      if (!lines.length) lines.push(message.kind === "draft.publish.response" ? `Published ${message.body.publishedRevision ?? ""}` : "Compile finished.");
      renderProblems(lines);
    }
  });
  socket.addEventListener("close", () => {
    status.textContent = "offline";
    sessionId = "";
  });
  socket.addEventListener("error", () => {
    status.textContent = "offline";
    renderProblems(["Authoring backend unavailable"]);
  });
}

function permissionBits(value) {
  const bits = typeof value === "number" ? value : Number(value);
  return Number.isFinite(bits) ? bits : 0;
}

function applyPermissions(value) {
  const bits = permissionBits(value);
  document.getElementById("compile").disabled = (bits & 4) === 0;
  document.getElementById("publish").disabled = (bits & 8) === 0 && (bits & 16) === 0;
  document.getElementById("rollback").disabled = (bits & 32) === 0;
}

function loadDraft(json) {
  try {
    const document = JSON.parse(json);
    nodes.splice(0, nodes.length, ...(document.nodes ?? []).map(node => ({
      id: node.id,
      nodeType: node.nodeType,
      name: node.name,
      x: node.x ?? 80,
      y: node.y ?? 80,
      properties: node.properties ?? {},
      pins: node.pins ?? []
    })));
    connections.splice(0, connections.length, ...(document.connections ?? []));
    draw();
  } catch {
    renderProblems(["Saved draft could not be read."]);
  }
}

function send(kind, body) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    renderProblems(["Authoring backend unavailable"]);
    return;
  }
  body.kind = kind;
  body.messageId = crypto.randomUUID().replaceAll("-", "");
  body.timestamp = new Date().toISOString();
  socket.send(frame(kind, JSON.stringify(body)));
}

function frame(kind, payload) {
  const kindBytes = new TextEncoder().encode(kind);
  const payloadBytes = new TextEncoder().encode(payload);
  const buffer = new Uint8Array(4 + kindBytes.length + payloadBytes.length);
  new DataView(buffer.buffer).setInt32(0, kindBytes.length, false);
  buffer.set(kindBytes, 4);
  buffer.set(payloadBytes, 4 + kindBytes.length);
  return buffer;
}

function unframe(buffer) {
  if (buffer.length < 4) return null;
  const kindLength = new DataView(buffer.buffer, buffer.byteOffset, buffer.byteLength).getInt32(0, false);
  const kind = new TextDecoder().decode(buffer.slice(4, 4 + kindLength));
  const payload = new TextDecoder().decode(buffer.slice(4 + kindLength));
  return { kind, body: JSON.parse(payload) };
}

function makeNode(item) {
  const id = crypto.randomUUID();
  return {
    id,
    name: item.name,
    nodeType: item.type,
    x: 80 + nodes.length * 24,
    y: 80 + nodes.length * 16,
    properties: item.method ? { Method: item.method } : {},
    pins: [
      { id: crypto.randomUUID(), name: "In", direction: "Input", kind: "Execution" },
      { id: crypto.randomUUID(), name: "Out", direction: "Output", kind: "Execution" }
    ]
  };
}

function renderPalette() {
  document.getElementById("palette").innerHTML = palette.map((item, index) =>
    `<div class="palette-item" data-index="${index}">${item.name}</div>`).join("");
}

function renderHistory(revisions) {
  const history = document.getElementById("history");
  history.innerHTML = revisions.map(line => {
    const id = String(line).split("|")[0];
    return `<div class="problem" data-revision="${id}">${line}</div>`;
  }).join("");
  history.onclick = (event) => {
    const row = event.target.closest("[data-revision]");
    if (row) history.dataset.revision = row.dataset.revision;
  };
}

function graphDocument() {
  return {
    id: graphId || undefined,
    name: "StudioDraft",
    kind: "System",
    baseRevisionId,
    draftRevisionId,
    nodes: nodes.map(node => ({
      id: node.id,
      name: node.name,
      nodeType: node.nodeType,
      x: node.x,
      y: node.y,
      properties: node.properties,
      pins: node.pins
    })),
    connections
  };
}

function renderProblems(lines) {
  problems.innerHTML = lines.map(line => `<div class="problem">${line}</div>`).join("");
}

function pointFromEvent(event) {
  const rect = canvas.getBoundingClientRect();
  return {
    x: (event.clientX - rect.left) * (canvas.width / rect.width),
    y: (event.clientY - rect.top) * (canvas.height / rect.height)
  };
}

function draw() {
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.strokeStyle = "#8ab4f8";
  for (const connection of connections) {
    const from = nodes.find(node => node.id === connection.fromNode);
    const to = nodes.find(node => node.id === connection.toNode);
    if (!from || !to) continue;
    ctx.beginPath();
    ctx.moveTo(from.x + 140, from.y + 24);
    ctx.lineTo(to.x, to.y + 24);
    ctx.stroke();
  }
  for (const node of nodes) {
    ctx.fillStyle = node === selected ? "#3a6ea5" : "#262b3a";
    ctx.fillRect(node.x, node.y, 140, 48);
    ctx.fillStyle = "#e8e6e3";
    ctx.fillText(node.name, node.x + 10, node.y + 28);
  }
}

draw();
