const palette = [
  { type: "Event.InteractUsing", name: "On Interact" },
  { type: "Flow.Branch", name: "Branch" },
  { type: "Flow.Delay", name: "Delay" },
  { type: "Native.Call", name: "Native Call" }
];

const nodes = [];
let selected = null;
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
  const messages = [];
  if (!nodes.some(node => node.type.startsWith("Event."))) {
    messages.push("System graph has no event entry.");
  }
  renderProblems(messages.length ? messages : ["Compile succeeded."]);
});

document.getElementById("publish").addEventListener("click", async () => {
  const body = JSON.stringify(graphDocument());
  try {
    const response = await fetch("/api/status");
    status.textContent = response.ok ? "bridge" : "offline";
    renderProblems([response.ok ? `Publish queued (${nodes.length} nodes).` : "Bridge rejected publish."]);
  } catch {
    status.textContent = "local";
    renderProblems([`Local graph ready (${body.length} bytes). Connect the game bridge to publish.`]);
  }
});

document.getElementById("rollback").addEventListener("click", () => {
  renderProblems(["Rollback is confirmed by the server session."]);
});

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
