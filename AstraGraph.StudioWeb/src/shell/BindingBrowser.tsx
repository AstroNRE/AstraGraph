import { useMemo, useState } from "react";
import { filterBindings, groupBindings, methodName, type CatalogEntry } from "../bindings/catalog";

export function BindingBrowser(props: {
  entries: CatalogEntry[];
  status: "idle" | "loading" | "ready" | "error";
  error: string;
  onInsert: (entry: CatalogEntry) => void;
}) {
  const [query, setQuery] = useState("");
  const groups = useMemo(() => groupBindings(filterBindings(props.entries, query)), [props.entries, query]);

  return (
    <div className="browser">
      <input value={query} placeholder="Search bindings" onChange={(event) => setQuery(event.target.value)} />
      {props.status === "idle" ? <p className="muted">Bindings load after DevHost accepts the session.</p> : null}
      {props.status === "loading" ? <p className="muted">Loading bindings…</p> : null}
      {props.status === "error" ? <p className="problem error">{props.error}</p> : null}
      {props.status === "ready" && groups.length === 0 ? <p className="muted">No bindings match.</p> : null}
      {groups.map((group) => (
        <details key={group.category} open={query.trim().length > 0}>
          <summary>{group.category}<span className="muted"> {group.items.length}</span></summary>
          <ul className="list">
            {group.items.map((entry) => (
              <li key={entry.signature}>
                <button type="button" title={entry.documentation || entry.signature} onClick={() => props.onInsert(entry)}>
                  <span>{methodName(entry.signature)}</span>
                  <span className="muted">{entry.side}{entry.isPure ? " · pure" : ""}{entry.isPredictionSafe ? " · predicted" : ""}</span>
                </button>
              </li>
            ))}
          </ul>
        </details>
      ))}
    </div>
  );
}
