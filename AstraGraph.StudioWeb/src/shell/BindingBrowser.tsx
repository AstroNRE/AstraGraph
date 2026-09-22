import { useMemo, useState } from "react";
import { filterBindings, groupBindings, methodName, type CatalogEntry } from "../bindings/catalog";

export function BindingBrowser(props: {
  entries: CatalogEntry[];
  status: "idle" | "loading" | "ready" | "error";
  error: string;
  query?: string;
  onQuery?: (query: string) => void;
  onInsert: (entry: CatalogEntry) => void;
}) {
  const [localQuery, setLocalQuery] = useState("");
  const [scrollTop, setScrollTop] = useState(0);
  const query = props.query ?? localQuery;
  const groups = useMemo(() => groupBindings(filterBindings(props.entries, query)), [props.entries, query]);
  const flat = useMemo(() => groups.flatMap((group) => group.items.map((entry) => ({ group: group.category, entry }))), [groups]);
  const rowHeight = 36;
  const windowSize = 40;
  const start = Math.max(0, Math.floor(scrollTop / rowHeight) - 4);
  const visible = flat.slice(start, start + windowSize);

  return (
    <div className="browser">
      <input value={query} placeholder="Search bindings" onChange={(event) => { setLocalQuery(event.target.value); props.onQuery?.(event.target.value); }} />
      <p className="muted">Click a binding to put it on the canvas.</p>
      {props.status === "idle" ? <p className="muted">Bindings load after DevHost accepts the session.</p> : null}
      {props.status === "loading" ? <p className="muted">Loading bindings…</p> : null}
      {props.status === "error" ? <p className="problem error">{props.error}</p> : null}
      {props.status === "ready" && groups.length === 0 ? <p className="muted">No bindings match.</p> : null}
      <div className="virtual-list" onScroll={(event) => setScrollTop(event.currentTarget.scrollTop)}>
        <div style={{ height: flat.length * rowHeight, position: "relative" }}>
          {visible.map((row, index) => (
            <div key={row.entry.signature} style={{ position: "absolute", top: (start + index) * rowHeight, height: rowHeight, left: 0, right: 0 }}>
              <button type="button" title={row.entry.documentation || row.entry.signature} onClick={() => props.onInsert(row.entry)}>
                <span>{methodName(row.entry.signature)}</span>
                <span className="muted">{row.group} · {row.entry.side}{row.entry.isPure ? " · pure" : ""}</span>
              </button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
