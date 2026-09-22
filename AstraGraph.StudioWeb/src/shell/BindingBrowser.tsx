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
  const [limit, setLimit] = useState(80);
  const query = props.query ?? localQuery;
  const groups = useMemo(() => groupBindings(filterBindings(props.entries, query)), [props.entries, query]);

  return (
    <div className="browser">
      <input value={query} placeholder="Search bindings" onChange={(event) => { setLocalQuery(event.target.value); props.onQuery?.(event.target.value); }} />
      {props.status === "idle" ? <p className="muted">Bindings load after DevHost accepts the session.</p> : null}
      {props.status === "loading" ? <p className="muted">Loading bindings…</p> : null}
      {props.status === "error" ? <p className="problem error">{props.error}</p> : null}
      {props.status === "ready" && groups.length === 0 ? <p className="muted">No bindings match.</p> : null}
      {groups.map((group) => (
        <details key={group.category} open={query.trim().length > 0}>
          <summary>{group.category}<span className="muted"> {group.items.length}</span></summary>
          <ul className="list">
            {group.items.slice(0, limit).map((entry) => (
              <li key={entry.signature}>
                <button type="button" title={entry.documentation || entry.signature} onClick={() => props.onInsert(entry)}>
                  <span>{methodName(entry.signature)}</span>
                  <span className="muted">{entry.side}{entry.isPure ? " · pure" : ""}{entry.isPredictionSafe ? " · predicted" : ""}</span>
                </button>
              </li>
            ))}
          </ul>
          {group.items.length > limit ? <button type="button" onClick={() => setLimit((value) => value + 80)}>Show more</button> : null}
        </details>
      ))}
    </div>
  );
}
