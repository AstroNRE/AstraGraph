namespace AstraGraph.Editor.Bridge;

/// <summary>
/// Embedded source definitions for the Astra Studio Web IDE frontend.
/// </summary>
public static class StudioAssets
{
    public const string IndexHtml = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Astra Studio Web</title>
    <link rel="stylesheet" href="/css/studio.css">
</head>
<body class="theme-dark">
    <div id="studio-app">
        <!-- Top Toolbar -->
        <header id="toolbar" class="panel">
            <div class="toolbar-brand">
                <span class="brand-icon">🌌</span>
                <span class="brand-name">Astra Studio</span>
                <span id="connection-status" class="badge status-disconnected">Offline</span>
            </div>
            <div class="toolbar-divider"></div>
            <div class="toolbar-graph-info">
                <span id="current-graph-name" class="graph-title">No Graph Loaded</span>
                <span id="current-graph-side" class="badge badge-side">Server</span>
                <span id="current-revision" class="badge badge-rev">Rev: draft</span>
            </div>
            <div class="toolbar-actions">
                <button id="btn-save-draft" class="btn btn-secondary" title="Save Draft (Ctrl+S)">💾 Save Draft</button>
                <button id="btn-compile" class="btn btn-secondary" title="Compile Graph (Ctrl+B)">⚡ Compile</button>
                <button id="btn-publish" class="btn btn-primary" title="Publish Revision (Ctrl+Shift+P)">🚀 Publish</button>
                <button id="btn-rollback" class="btn btn-secondary" title="Rollback to Previous Revision">⏪ Rollback</button>
                <div class="toolbar-divider"></div>
                <button id="btn-debug-step" class="btn btn-icon" title="Step Over (F10)" disabled>⏭️</button>
                <button id="btn-debug-continue" class="btn btn-icon" title="Continue (F5)" disabled>▶️</button>
                <button id="btn-profiler-toggle" class="btn btn-icon" title="Toggle Profiler Heatmap">🔥</button>
            </div>
        </header>

        <!-- Main Workspace -->
        <div id="workspace">
            <!-- Left Sidebar -->
            <aside id="left-sidebar" class="sidebar panel">
                <div class="tabs-header">
                    <button class="tab-btn active" data-tab="tab-palette">Nodes</button>
                    <button class="tab-btn" data-tab="tab-symbols">Variables</button>
                </div>
                <div id="tab-palette" class="tab-content active">
                    <div class="search-box">
                        <input type="text" id="palette-search-input" placeholder="Search nodes (Space / Drag)...">
                    </div>
                    <div id="palette-categories" class="tree-view"></div>
                </div>
                <div id="tab-symbols" class="tab-content">
                    <div class="variables-header">
                        <span>Graph Variables</span>
                        <button id="btn-add-variable" class="btn btn-xs">+ Add</button>
                    </div>
                    <div id="variables-list" class="tree-view"></div>
                </div>
            </aside>

            <!-- Center Canvas -->
            <main id="canvas-container">
                <svg id="canvas-wires" class="wires-layer"></svg>
                <div id="canvas-nodes" class="nodes-layer"></div>
                <div id="canvas-overlay" class="overlay-layer"></div>
                <!-- Mini map -->
                <div id="minimap" class="minimap"></div>
            </main>

            <!-- Right Sidebar -->
            <aside id="right-sidebar" class="sidebar panel">
                <div class="tabs-header">
                    <button class="tab-btn active" data-tab="tab-inspector">Inspector</button>
                    <button class="tab-btn" data-tab="tab-history">History</button>
                </div>
                <div id="tab-inspector" class="tab-content active">
                    <div id="inspector-content">
                        <div class="placeholder-text">Select a node or pin to view properties</div>
                    </div>
                </div>
                <div id="tab-history" class="tab-content">
                    <div id="history-revisions-list" class="revisions-list"></div>
                </div>
            </aside>
        </div>

        <!-- Bottom Panel -->
        <footer id="bottom-panel" class="panel collapsed">
            <div class="bottom-panel-header">
                <div class="tabs-header">
                    <button class="tab-btn active" data-tab="tab-problems">Problems <span id="problems-count" class="badge">0</span></button>
                    <button class="tab-btn" data-tab="tab-debugger">Debugger</button>
                    <button class="tab-btn" data-tab="tab-profiler">Profiler</button>
                </div>
                <button id="btn-toggle-bottom-panel" class="btn btn-icon" title="Toggle Bottom Panel">▲</button>
            </div>
            <div class="bottom-panel-content">
                <div id="tab-problems" class="tab-content active">
                    <div id="problems-list" class="problems-table"></div>
                </div>
                <div id="tab-debugger" class="tab-content">
                    <div class="debugger-grid">
                        <div class="debug-pane">
                            <h4>Call Stack</h4>
                            <div id="debug-callstack"></div>
                        </div>
                        <div class="debug-pane">
                            <h4>Variables / Watch</h4>
                            <div id="debug-watch"></div>
                        </div>
                    </div>
                </div>
                <div id="tab-profiler" class="tab-content">
                    <div class="profiler-stats-header">
                        <span id="profiler-summary">Invocations: 0 | P95: 0.00ms | Alloc: 0 KB</span>
                    </div>
                    <div id="profiler-nodes-list" class="profiler-table"></div>
                </div>
            </div>
        </footer>
    </div>

    <!-- Scripts -->
    <script src="/js/transport.js"></script>
    <script src="/js/wire-renderer.js"></script>
    <script src="/js/canvas.js"></script>
    <script src="/js/palette.js"></script>
    <script src="/js/inspector.js"></script>
    <script src="/js/problems.js"></script>
    <script src="/js/debugger.js"></script>
    <script src="/js/profiler.js"></script>
    <script src="/js/toolbar.js"></script>
    <script src="/js/studio.js"></script>
</body>
</html>
""";

    public const string StudioCss = """
/* Astra Studio Dark Theme */
:root {
    --bg-primary: #181820;
    --bg-secondary: #22222c;
    --bg-tertiary: #2a2a38;
    --bg-hover: #343446;
    --border-color: #3e3e52;
    --text-primary: #f0f0f5;
    --text-muted: #9e9eb4;
    --accent: #5e6ad2;
    --accent-hover: #727ee0;
    --color-exec: #e0e0e0;
    --color-data: #4caf50;
    --color-entity: #ff9800;
    --color-error: #f44336;
    --color-warning: #ffb74d;
    --color-info: #29b6f6;
    --color-success: #66bb6a;
}

* { box-sizing: border-box; margin: 0; padding: 0; }

body {
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    background-color: var(--bg-primary);
    color: var(--text-primary);
    overflow: hidden;
    height: 100vh;
    user-select: none;
}

#studio-app {
    display: flex;
    flex-direction: column;
    height: 100vh;
    width: 100vw;
}

/* Toolbar */
#toolbar {
    display: flex;
    align-items: center;
    height: 48px;
    background-color: var(--bg-secondary);
    border-bottom: 1px solid var(--border-color);
    padding: 0 16px;
    gap: 12px;
}

.toolbar-brand { display: flex; align-items: center; gap: 8px; font-weight: 600; }
.toolbar-divider { width: 1px; height: 24px; background: var(--border-color); }
.toolbar-graph-info { display: flex; align-items: center; gap: 8px; font-size: 13px; }
.graph-title { font-weight: 600; color: var(--text-primary); }
.toolbar-actions { margin-left: auto; display: flex; align-items: center; gap: 8px; }

/* Buttons & Badges */
.btn {
    background: var(--bg-tertiary);
    border: 1px solid var(--border-color);
    color: var(--text-primary);
    padding: 6px 12px;
    border-radius: 4px;
    font-size: 12px;
    cursor: pointer;
    transition: all 0.15s ease;
}
.btn:hover { background: var(--bg-hover); border-color: var(--accent); }
.btn-primary { background: var(--accent); border-color: var(--accent); }
.btn-primary:hover { background: var(--accent-hover); }
.btn-icon { padding: 6px 8px; }
.btn-xs { padding: 2px 6px; font-size: 10px; }
.badge {
    font-size: 10px;
    padding: 2px 6px;
    border-radius: 10px;
    font-weight: 600;
}
.status-connected { background: rgba(76, 175, 80, 0.2); color: var(--color-success); border: 1px solid var(--color-success); }
.status-disconnected { background: rgba(244, 67, 54, 0.2); color: var(--color-error); border: 1px solid var(--color-error); }
.badge-side { background: rgba(94, 106, 210, 0.2); color: var(--accent); border: 1px solid var(--accent); }
.badge-rev { background: var(--bg-tertiary); color: var(--text-muted); }

/* Workspace */
#workspace {
    flex: 1;
    display: flex;
    overflow: hidden;
    position: relative;
}

.sidebar {
    width: 280px;
    background-color: var(--bg-secondary);
    display: flex;
    flex-direction: column;
    z-index: 10;
}
#left-sidebar { border-right: 1px solid var(--border-color); }
#right-sidebar { border-left: 1px solid var(--border-color); }

.tabs-header {
    display: flex;
    border-bottom: 1px solid var(--border-color);
    background-color: var(--bg-primary);
}
.tab-btn {
    flex: 1;
    background: none;
    border: none;
    border-bottom: 2px solid transparent;
    color: var(--text-muted);
    padding: 8px 12px;
    font-size: 12px;
    cursor: pointer;
}
.tab-btn.active { color: var(--text-primary); border-bottom-color: var(--accent); background: var(--bg-secondary); }
.tab-content { display: none; flex: 1; overflow-y: auto; padding: 8px; }
.tab-content.active { display: flex; flex-direction: column; gap: 8px; }

/* Canvas */
#canvas-container {
    flex: 1;
    position: relative;
    overflow: hidden;
    background-color: var(--bg-primary);
    background-image: radial-gradient(var(--border-color) 1px, transparent 1px);
    background-size: 24px 24px;
    cursor: grab;
}
#canvas-container.panning { cursor: grabbing; }

.wires-layer { position: absolute; top: 0; left: 0; width: 100%; height: 100%; pointer-events: none; }
.nodes-layer { position: absolute; top: 0; left: 0; width: 100%; height: 100%; transform-origin: 0 0; }
.wire { stroke: var(--color-exec); stroke-width: 2.5; fill: none; pointer-events: stroke; cursor: pointer; transition: stroke-width 0.1s; }
.wire:hover { stroke-width: 4; stroke: var(--accent-hover); }
.wire.data-wire { stroke: var(--color-data); }
.wire.entity-wire { stroke: var(--color-entity); }

/* Node UI */
.graph-node {
    position: absolute;
    min-width: 180px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 6px;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
    cursor: move;
    font-size: 12px;
}
.graph-node.selected { border-color: var(--accent); box-shadow: 0 0 0 2px var(--accent); }
.graph-node.executing { border-color: var(--color-success); box-shadow: 0 0 12px var(--color-success); }
.graph-node.has-error { border-color: var(--color-error); }

.node-header {
    background: var(--bg-tertiary);
    padding: 6px 10px;
    border-top-left-radius: 5px;
    border-top-right-radius: 5px;
    font-weight: 600;
    display: flex;
    justify-content: space-between;
    align-items: center;
    border-bottom: 1px solid var(--border-color);
}
.node-body { display: flex; justify-content: space-between; padding: 8px 10px; gap: 16px; }
.node-pins-in, .node-pins-out { display: flex; flex-direction: column; gap: 6px; }
.node-pin { display: flex; align-items: center; gap: 6px; cursor: pointer; }
.node-pin:hover .pin-dot { transform: scale(1.3); }
.pin-dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    background: var(--color-data);
    border: 1px solid #fff;
    transition: transform 0.15s;
}
.pin-dot.exec { border-radius: 2px; background: var(--color-exec); }
.pin-dot.entity { background: var(--color-entity); }

/* Bottom Panel */
#bottom-panel {
    background: var(--bg-secondary);
    border-top: 1px solid var(--border-color);
    height: 180px;
    display: flex;
    flex-direction: column;
    transition: height 0.2s;
}
#bottom-panel.collapsed { height: 32px; }
.bottom-panel-header {
    height: 32px;
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0 8px;
    border-bottom: 1px solid var(--border-color);
}
.bottom-panel-content { flex: 1; overflow-y: auto; padding: 8px; }

/* Problems list */
.problems-table { display: flex; flex-direction: column; gap: 4px; font-size: 12px; }
.problem-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 8px;
    border-radius: 4px;
    background: var(--bg-primary);
    cursor: pointer;
}
.problem-item:hover { background: var(--bg-hover); }
.problem-item.error { border-left: 3px solid var(--color-error); }
.problem-item.warning { border-left: 3px solid var(--color-warning); }
""";

    public const string TransportJs = """
// WebSocket transport and session management for Astra Studio
class AstraStudioTransport {
    constructor() {
        this.ws = null;
        this.nonce = null;
        this.session = null;
        this.port = window.location.port || (window.location.protocol === 'https:' ? 443 : 80);
        this.listeners = new Map();
        this.isConnected = false;
        this.pendingRequests = new Map();
        this.requestIdCounter = 1;
    }

    initFromUrl() {
        const params = new URLSearchParams(window.location.search);
        this.nonce = params.get('nonce');
        this.session = params.get('session');
    }

    connect() {
        this.initFromUrl();
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = `${protocol}//${window.location.hostname}:${this.port}/ws?nonce=${encodeURIComponent(this.nonce || '')}&session=${encodeURIComponent(this.session || '')}`;
        
        console.log(`Connecting to Astra Local Bridge at ${wsUrl}`);
        this.ws = new WebSocket(wsUrl);

        this.ws.onopen = () => {
            console.log("WebSocket connected to Astra Local Bridge");
            this.isConnected = true;
            this.emit('connectionChanged', true);
            this.sendHello();
        };

        this.ws.onclose = () => {
            console.log("WebSocket disconnected");
            this.isConnected = false;
            this.emit('connectionChanged', false);
            // Reconnect after delay
            setTimeout(() => this.connect(), 2500);
        };

        this.ws.onerror = (err) => {
            console.error("WebSocket error:", err);
            this.emit('error', err);
        };

        this.ws.onmessage = (event) => {
            try {
                const message = JSON.parse(event.data);
                this.handleIncomingMessage(message);
            } catch (e) {
                console.error("Failed to parse incoming message:", event.data, e);
            }
        };
    }

    sendHello() {
        this.send({
            type: "Hello",
            clientVersion: "1.0.0",
            nonce: this.nonce,
            session: this.session
        });
    }

    send(message) {
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.ws.send(JSON.stringify(message));
        } else {
            console.warn("WebSocket not open, buffering not supported for this call:", message);
        }
    }

    request(type, payload = {}) {
        return new Promise((resolve, reject) => {
            const requestId = (this.requestIdCounter++).toString();
            const message = { type, requestId, ...payload };
            
            const timeout = setTimeout(() => {
                this.pendingRequests.delete(requestId);
                reject(new Error(`Request timed out: ${type}`));
            }, 10000);

            this.pendingRequests.set(requestId, { resolve, reject, timeout });
            this.send(message);
        });
    }

    handleIncomingMessage(message) {
        // Handle request-response pattern
        if (message.requestId && this.pendingRequests.has(message.requestId)) {
            const pending = this.pendingRequests.get(message.requestId);
            clearTimeout(pending.timeout);
            this.pendingRequests.delete(message.requestId);
            pending.resolve(message);
            return;
        }

        // Emit general message
        this.emit(message.type || 'message', message);
    }

    on(type, callback) {
        if (!this.listeners.has(type)) {
            this.listeners.set(type, []);
        }
        this.listeners.get(type).push(callback);
    }

    emit(type, data) {
        const cbs = this.listeners.get(type);
        if (cbs) {
            cbs.forEach(cb => cb(data));
        }
    }
}

window.astraTransport = new AstraStudioTransport();
""";

    public const string WireRendererJs = """
// Spline and Bezier wire renderer for Astra Graph Canvas
class AstraWireRenderer {
    static createCubicBezierPath(x1, y1, x2, y2) {
        const dx = Math.abs(x2 - x1);
        const curvature = Math.max(dx * 0.5, 40);
        const cx1 = x1 + curvature;
        const cy1 = y1;
        const cx2 = x2 - curvature;
        const cy2 = y2;
        return `M ${x1} ${y1} C ${cx1} ${cy1}, ${cx2} ${cy2}, ${x2} ${y2}`;
    }

    static renderConnection(svg, connection, pinPositions) {
        const fromPos = pinPositions.get(connection.fromPinId);
        const toPos = pinPositions.get(connection.toPinId);
        if (!fromPos || !toPos) return null;

        const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
        const d = this.createCubicBezierPath(fromPos.x, fromPos.y, toPos.x, toPos.y);
        path.setAttribute("d", d);
        path.setAttribute("class", `wire ${connection.isData ? 'data-wire' : ''}`);
        path.dataset.connectionId = connection.id;
        svg.appendChild(path);
        return path;
    }
}

window.astraWireRenderer = AstraWireRenderer;
""";

    public const string CanvasJs = """
// Interactive graph canvas: pan, zoom, node drag & pin connections
class AstraCanvasController {
    constructor(containerId, wiresLayerId, nodesLayerId) {
        this.container = document.getElementById(containerId);
        this.wiresLayer = document.getElementById(wiresLayerId);
        this.nodesLayer = document.getElementById(nodesLayerId);

        this.zoom = 1.0;
        this.panX = 0;
        this.panY = 0;
        this.isPanning = false;
        this.panStart = { x: 0, y: 0 };

        this.nodes = new Map();
        this.connections = [];
        this.selectedNodeId = null;
        this.pinPositions = new Map();

        this.initEvents();
    }

    initEvents() {
        this.container.addEventListener('mousedown', (e) => {
            if (e.target === this.container || e.target === this.wiresLayer) {
                this.isPanning = true;
                this.panStart = { x: e.clientX - this.panX, y: e.clientY - this.panY };
                this.container.classList.add('panning');
            }
        });

        window.addEventListener('mousemove', (e) => {
            if (this.isPanning) {
                this.panX = e.clientX - this.panStart.x;
                this.panY = e.clientY - this.panStart.y;
                this.updateTransform();
            }
        });

        window.addEventListener('mouseup', () => {
            if (this.isPanning) {
                this.isPanning = false;
                this.container.classList.remove('panning');
            }
        });

        this.container.addEventListener('wheel', (e) => {
            e.preventDefault();
            const delta = e.deltaY > 0 ? 0.9 : 1.1;
            const newZoom = Math.min(Math.max(this.zoom * delta, 0.2), 3.0);
            
            // Zoom towards mouse
            const rect = this.container.getBoundingClientRect();
            const mouseX = e.clientX - rect.left;
            const mouseY = e.clientY - rect.top;

            this.panX = mouseX - (mouseX - this.panX) * (newZoom / this.zoom);
            this.panY = mouseY - (mouseY - this.panY) * (newZoom / this.zoom);
            this.zoom = newZoom;
            this.updateTransform();
        }, { passive: false });
    }

    updateTransform() {
        this.nodesLayer.style.transform = `translate(${this.panX}px, ${this.panY}px) scale(${this.zoom})`;
        this.refreshWires();
    }

    loadGraph(graphDoc) {
        this.nodesLayer.innerHTML = '';
        this.wiresLayer.innerHTML = '';
        this.nodes.clear();
        this.connections = graphDoc.connections || [];

        if (graphDoc.nodes) {
            graphDoc.nodes.forEach(n => this.renderNode(n));
        }
        this.updatePinPositions();
        this.refreshWires();
    }

    renderNode(nodeData) {
        const nodeEl = document.createElement('div');
        nodeEl.className = 'graph-node';
        nodeEl.id = `node-${nodeData.id}`;
        nodeEl.style.left = `${nodeData.x || 100}px`;
        nodeEl.style.top = `${nodeData.y || 100}px`;

        const header = document.createElement('div');
        header.className = 'node-header';
        header.innerHTML = `<span>${nodeData.name || nodeData.nodeType}</span><small>${nodeData.nodeType}</small>`;
        nodeEl.appendChild(header);

        const body = document.createElement('div');
        body.className = 'node-body';

        const inPins = document.createElement('div');
        inPins.className = 'node-pins-in';
        (nodeData.pins || []).filter(p => p.direction === 'Input').forEach(p => {
            inPins.appendChild(this.createPinEl(p));
        });

        const outPins = document.createElement('div');
        outPins.className = 'node-pins-out';
        (nodeData.pins || []).filter(p => p.direction === 'Output').forEach(p => {
            outPins.appendChild(this.createPinEl(p));
        });

        body.appendChild(inPins);
        body.appendChild(outPins);
        nodeEl.appendChild(body);

        this.makeDraggable(nodeEl, nodeData);
        nodeEl.addEventListener('click', (e) => {
            e.stopPropagation();
            this.selectNode(nodeData.id);
        });

        this.nodesLayer.appendChild(nodeEl);
        this.nodes.set(nodeData.id, { data: nodeData, el: nodeEl });
    }

    createPinEl(pin) {
        const pinEl = document.createElement('div');
        pinEl.className = 'node-pin';
        pinEl.dataset.pinId = pin.id;
        const isExec = pin.kind === 'Execution';
        pinEl.innerHTML = `
            <div class="pin-dot ${isExec ? 'exec' : ''}"></div>
            <span class="pin-label">${pin.name}</span>
        `;
        return pinEl;
    }

    makeDraggable(el, nodeData) {
        let isDragging = false;
        let startX, startY;

        el.addEventListener('mousedown', (e) => {
            if (e.target.closest('.pin-dot')) return; // Don't drag node if pulling wire
            isDragging = true;
            startX = (e.clientX / this.zoom) - (nodeData.x || 100);
            startY = (e.clientY / this.zoom) - (nodeData.y || 100);
            e.stopPropagation();
        });

        window.addEventListener('mousemove', (e) => {
            if (!isDragging) return;
            nodeData.x = (e.clientX / this.zoom) - startX;
            nodeData.y = (e.clientY / this.zoom) - startY;
            el.style.left = `${nodeData.x}px`;
            el.style.top = `${nodeData.y}px`;
            this.updatePinPositions();
            this.refreshWires();
        });

        window.addEventListener('mouseup', () => { isDragging = false; });
    }

    selectNode(nodeId) {
        this.selectedNodeId = nodeId;
        document.querySelectorAll('.graph-node').forEach(n => n.classList.remove('selected'));
        const target = document.getElementById(`node-${nodeId}`);
        if (target) target.classList.add('selected');
        window.dispatchEvent(new CustomEvent('nodeSelected', { detail: this.nodes.get(nodeId) }));
    }

    updatePinPositions() {
        this.pinPositions.clear();
        const containerRect = this.container.getBoundingClientRect();

        document.querySelectorAll('.pin-dot').forEach(dot => {
            const pinEl = dot.closest('.node-pin');
            if (!pinEl) return;
            const pinId = pinEl.dataset.pinId;
            const rect = dot.getBoundingClientRect();
            
            // Compute coordinate inside svg plane
            const x = (rect.left + rect.width / 2 - containerRect.left);
            const y = (rect.top + rect.height / 2 - containerRect.top);
            this.pinPositions.set(pinId, { x, y });
        });
    }

    refreshWires() {
        this.wiresLayer.innerHTML = '';
        this.updatePinPositions();
        this.connections.forEach(c => {
            AstraWireRenderer.renderConnection(this.wiresLayer, c, this.pinPositions);
        });
    }
}
""";

    public const string PaletteJs = """
// Node palette, autocomplete and search
class AstraPalette {
    constructor(searchInputId, categoriesContainerId) {
        this.searchInput = document.getElementById(searchInputId);
        this.container = document.getElementById(categoriesContainerId);
        this.catalog = [];

        this.initEvents();
    }

    initEvents() {
        this.searchInput.addEventListener('input', () => {
            this.filter(this.searchInput.value);
        });
    }

    loadCatalog(catalogItems) {
        this.catalog = catalogItems || [];
        this.renderCatalog(this.catalog);
    }

    renderCatalog(items) {
        this.container.innerHTML = '';
        const groups = new Map();

        items.forEach(item => {
            const cat = item.category || 'General';
            if (!groups.has(cat)) groups.set(cat, []);
            groups.get(cat).push(item);
        });

        groups.forEach((itemsInCat, category) => {
            const catHeader = document.createElement('div');
            catHeader.className = 'palette-category-header';
            catHeader.textContent = category;
            this.container.appendChild(catHeader);

            itemsInCat.forEach(item => {
                const itemEl = document.createElement('div');
                itemEl.className = 'palette-item';
                itemEl.innerHTML = `<span class="item-name">${item.displayName || item.name}</span><small class="item-type">${item.returnType || ''}</small>`;
                itemEl.addEventListener('click', () => {
                    window.dispatchEvent(new CustomEvent('createNodeRequested', { detail: item }));
                });
                this.container.appendChild(itemEl);
            });
        });
    }

    filter(query) {
        if (!query.trim()) {
            this.renderCatalog(this.catalog);
            return;
        }
        const lower = query.toLowerCase();
        const filtered = this.catalog.filter(i => 
            (i.name && i.name.toLowerCase().includes(lower)) ||
            (i.displayName && i.displayName.toLowerCase().includes(lower)) ||
            (i.category && i.category.toLowerCase().includes(lower))
        );
        this.renderCatalog(filtered);
    }
}
""";

    public const string InspectorJs = """
// Inspector for node properties and pins
class AstraInspector {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        window.addEventListener('nodeSelected', (e) => this.showNode(e.detail));
    }

    showNode(node) {
        if (!node || !node.data) {
            this.container.innerHTML = '<div class="placeholder-text">Select a node or pin</div>';
            return;
        }
        const d = node.data;
        this.container.innerHTML = `
            <div class="inspector-section">
                <h3>Node Properties</h3>
                <div class="form-row">
                    <label>Name</label>
                    <input type="text" value="${d.name || ''}" class="form-input" id="prop-node-name">
                </div>
                <div class="form-row">
                    <label>Type</label>
                    <span class="badge">${d.nodeType}</span>
                </div>
                <div class="form-row">
                    <label>ID</label>
                    <code>${d.id}</code>
                </div>
            </div>
        `;
    }
}
""";

    public const string ProblemsJs = """
// Problems and diagnostics panel
class AstraProblemsPanel {
    constructor(containerId, countBadgeId) {
        this.container = document.getElementById(containerId);
        this.countBadge = document.getElementById(countBadgeId);
    }

    updateProblems(diagnostics) {
        const diags = diagnostics || [];
        this.countBadge.textContent = diags.length;
        this.countBadge.className = `badge ${diags.length > 0 ? 'status-disconnected' : 'status-connected'}`;
        this.container.innerHTML = '';

        if (diags.length === 0) {
            this.container.innerHTML = '<div class="placeholder-text">No problems detected. Graph is clean.</div>';
            return;
        }

        diags.forEach(d => {
            const el = document.createElement('div');
            el.className = `problem-item ${d.severity === 'Error' ? 'error' : 'warning'}`;
            el.innerHTML = `
                <span class="problem-icon">${d.severity === 'Error' ? '❌' : '⚠️'}</span>
                <span class="problem-code"><strong>${d.code || 'ERR'}</strong></span>
                <span class="problem-message">${d.message}</span>
            `;
            el.addEventListener('click', () => {
                if (d.nodeId) {
                    window.dispatchEvent(new CustomEvent('focusNodeRequested', { detail: d.nodeId }));
                }
            });
            this.container.appendChild(el);
        });
    }
}
""";

    public const string DebuggerJs = """
// Visual Debugger view
class AstraDebugger {
    constructor() {
        this.activeBreakpointNodes = new Set();
    }

    highlightExecutionPoint(nodeId) {
        document.querySelectorAll('.graph-node').forEach(n => n.classList.remove('executing'));
        const target = document.getElementById(`node-${nodeId}`);
        if (target) {
            target.classList.add('executing');
            target.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }
}
""";

    public const string ProfilerJs = """
// Visual Profiler heatmap overlay
class AstraProfiler {
    constructor(summaryId, listId) {
        this.summary = document.getElementById(summaryId);
        this.list = document.getElementById(listId);
        this.enabled = false;
    }

    toggle() {
        this.enabled = !this.enabled;
        console.log("Profiler overlay:", this.enabled);
    }

    updateStats(stats) {
        if (!stats) return;
        this.summary.textContent = `Invocations: ${stats.invocations || 0} | P95: ${(stats.p95 || 0).toFixed(2)}ms | Alloc: ${stats.allocKb || 0} KB`;
    }
}
""";

    public const string ToolbarJs = """
// Toolbar action handlers
class AstraToolbar {
    constructor() {
        this.initEvents();
    }

    initEvents() {
        document.getElementById('btn-compile')?.addEventListener('click', () => {
            window.dispatchEvent(new CustomEvent('actionCompile'));
        });
        document.getElementById('btn-publish')?.addEventListener('click', () => {
            window.dispatchEvent(new CustomEvent('actionPublish'));
        });
        document.getElementById('btn-rollback')?.addEventListener('click', () => {
            window.dispatchEvent(new CustomEvent('actionRollback'));
        });
        document.getElementById('btn-toggle-bottom-panel')?.addEventListener('click', () => {
            document.getElementById('bottom-panel')?.classList.toggle('collapsed');
        });
    }
}
""";

    public const string StudioJs = """
// Main entry point for Astra Studio Web IDE
document.addEventListener('DOMContentLoaded', () => {
    console.log("Initializing Astra Studio Web IDE...");

    // Initialize sub-controllers
    const canvas = new AstraCanvasController('canvas-container', 'canvas-wires', 'canvas-nodes');
    const palette = new AstraPalette('palette-search-input', 'palette-categories');
    const inspector = new AstraInspector('inspector-content');
    const problems = new AstraProblemsPanel('problems-list', 'problems-count');
    const debuggerSession = new AstraDebugger();
    const profiler = new AstraProfiler('profiler-summary', 'profiler-nodes-list');
    const toolbar = new AstraToolbar();

    // Tab switching
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const parent = btn.closest('.panel, .sidebar');
            const targetId = btn.dataset.tab;
            if (!parent || !targetId) return;

            parent.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
            parent.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));

            btn.classList.add('active');
            const target = document.getElementById(targetId);
            if (target) target.classList.add('active');
        });
    });

    // Transport events
    window.astraTransport.on('connectionChanged', (connected) => {
        const badge = document.getElementById('connection-status');
        if (badge) {
            badge.textContent = connected ? 'Online' : 'Offline';
            badge.className = `badge ${connected ? 'status-connected' : 'status-disconnected'}`;
        }
    });

    window.astraTransport.on('GraphLoaded', (msg) => {
        if (msg.graph) {
            document.getElementById('current-graph-name').textContent = msg.graph.name || 'Untitled';
            canvas.loadGraph(msg.graph);
        }
    });

    window.astraTransport.on('DiagnosticsUpdated', (msg) => {
        problems.updateProblems(msg.diagnostics);
    });

    window.astraTransport.on('ExecutionPaused', (msg) => {
        if (msg.nodeId) debuggerSession.highlightExecutionPoint(msg.nodeId);
    });

    // Action dispatches
    window.addEventListener('actionCompile', () => {
        console.log("Triggering Compile...");
        window.astraTransport.send({ type: "CompileDraft" });
    });

    window.addEventListener('actionPublish', () => {
        console.log("Triggering Publish...");
        window.astraTransport.send({ type: "PublishDraft", message: "Live publish from Astra Studio Web" });
    });

    window.addEventListener('focusNodeRequested', (e) => {
        canvas.selectNode(e.detail);
    });

    // Connect to local bridge
    window.astraTransport.connect();
});
""";
}
