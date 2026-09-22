import { useEffect, useState } from "react";

export interface SchemaField {
  id: string;
  name: string;
  typeName: string;
  defaultValue?: string;
  persistent: boolean;
  replicated: boolean;
}

export interface SchemaDocument {
  id: string;
  name: string;
  isComponent: boolean;
  fields: SchemaField[];
}

const fieldTypes = ["int32", "int64", "float32", "float64", "bool", "string"];

export function SchemaEditor(props: {
  schemas: SchemaDocument[];
  onSave: (schema: SchemaDocument) => void;
}) {
  const [draft, setDraft] = useState<SchemaDocument | null>(props.schemas[0] ?? null);

  useEffect(() => {
    setDraft(props.schemas[0] ?? null);
  }, [props.schemas]);

  if (!draft) {
    return <button type="button" onClick={() => setDraft({ id: crypto.randomUUID(), name: "Component", isComponent: true, fields: [] })}>New component schema</button>;
  }

  return (
    <div>
      <label className="field">Schema
        <input value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
      </label>
      <label className="field">Component
        <input type="checkbox" checked={draft.isComponent} onChange={(event) => setDraft({ ...draft, isComponent: event.target.checked })} />
      </label>
      <ul className="list">
        {draft.fields.map((field) => (
          <li key={field.id}>
            <input aria-label="Field name" value={field.name} onChange={(event) => setDraft({
              ...draft,
              fields: draft.fields.map((item) => item.id === field.id ? { ...item, name: event.target.value } : item)
            })} />
            <select aria-label="Field type" value={field.typeName} onChange={(event) => setDraft({
              ...draft,
              fields: draft.fields.map((item) => item.id === field.id ? { ...item, typeName: event.target.value } : item)
            })}>
              {fieldTypes.map((typeName) => <option key={typeName} value={typeName}>{typeName}</option>)}
            </select>
            <span className="muted">{field.id.slice(0, 8)}</span>
          </li>
        ))}
      </ul>
      <button type="button" onClick={() => setDraft({
        ...draft,
        fields: [...draft.fields, { id: crypto.randomUUID(), name: "Field", typeName: "int32", persistent: true, replicated: false }]
      })}>Add field</button>
      <button type="button" onClick={() => props.onSave(draft)}>Save schema</button>
    </div>
  );
}
