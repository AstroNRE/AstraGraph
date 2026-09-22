const palette = [
  { type: "Event.InteractUsing", name: "On Interact" },
  { type: "Flow.Branch", name: "Branch" },
  { type: "Flow.Delay", name: "Delay" },
  { type: "Native.Call", name: "Native Call" }
];

const nodes = [];
let selected = null;
let socket = null;
let sessionId = "";
let graphId = "";
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
  nodes.push({ id: crypto.randomUUID(), type: item.type, name: item.name, x: 80 + nodes.length * 24, y: 80 + nodes.length * 16 });
  draw();
});

canvas.addEventListener("mousedown", (event) => {
  const point = pointFromEvent(event);
  selected = nodes.findLast(node => point.x >= node.x && point.x <= node.x + 140 && point.y >= node.y && point.y <= node.y + 48) ?? null;
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
  send("draft.compile.request", {
    sessionId,
    graphId,
    draftJson: JSON.stringify(graphDocument())
  });
});

document.getElementById("publish").addEventListener("click", () => {
  if (!sessionId) {
    renderProblems(["Authoring backend unavailable"]);
    return;
  }
  send("draft.publish.request", {
    sessionId,
    graphId,
    baseRevisionId: "00000000-0000-0000-0000-000000000000",
    draftJson: JSON.stringify(graphDocument()),
    publishMessage: "Studio publish"
  });
});

document.getElementById("rollback").addEventListener("click", () => {
  if (!sessionId) {
    renderProblems(["Authoring backend unavailable"]);
    return;
  }
  send("debugger.command.request", { sessionId, graphId, action: "Rollback" });
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
      send("graph.list.request", { sessionId });
      return;
    }
    if (message.kind === "graph.list.response" && message.body.graphs?.length) {
      graphId = message.body.graphs[0].id;
    }
    if (message.kind === "draft.compile.response" || message.kind === "draft.publish.response") {
      const lines = (message.body.diagnostics ?? []).map(item => item.message ?? item.Message ?? "diagnostic");
      if (message.body.errorMessage) lines.push(message.body.errorMessage);
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

function graphDocument() {
  return {
    name: "StudioDraft",
    kind: "system",
    nodes: nodes.map(node => ({ id: node.id, name: node.name, nodeType: node.type, x: node.x, y: node.y }))
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
  for (const node of nodes) {
    ctx.fillStyle = node === selected ? "#3a6ea5" : "#262b3a";
    ctx.fillRect(node.x, node.y, 140, 48);
    ctx.fillStyle = "#e8e6e3";
    ctx.fillText(node.name, node.x + 10, node.y + 28);
  }
}

draw();
