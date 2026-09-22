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
  kind?: "Component" | "Struct" | "Enum" | "Interface" | "Contract";
  members?: string[];
}

const fieldTypes = ["int32", "int64", "float32", "float64", "bool", "string", "EntProtoId", "EntityUid", "int32?", "string?", "List<int32>", "Set<string>", "Dictionary<string, int32>"];
const schemaKinds = ["Component", "Struct", "Enum", "Interface", "Contract"] as const;

export function SchemaEditor(props: {
  schemas: SchemaDocument[];
  note?: string;
  onSave: (schema: SchemaDocument) => void;
}) {
  const [draft, setDraft] = useState<SchemaDocument | null>(props.schemas[0] ?? null);

  useEffect(() => {
    setDraft((current) => {
      if (current) return props.schemas.find((schema) => schema.id === current.id) ?? current;
      return props.schemas[0] ?? null;
    });
  }, [props.schemas]);

  if (!draft) {
    return (
      <div>
        {schemaKinds.map((kind) => (
          <button key={kind} type="button" onClick={() => setDraft({
            id: crypto.randomUUID(),
            name: kind,
            isComponent: kind === "Component",
            kind,
            fields: [],
            members: kind === "Enum" ? ["None"] : []
          })}>New {kind}</button>
        ))}
      </div>
    );
  }

  const kind = draft.kind ?? (draft.isComponent ? "Component" : "Struct");

  return (
    <div>
      <label className="field">Schema
        <input value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
      </label>
      <label className="field">Kind
        <select value={kind} onChange={(event) => {
          const next = event.target.value as (typeof schemaKinds)[number];
          setDraft({ ...draft, kind: next, isComponent: next === "Component", members: next === "Enum" ? (draft.members?.length ? draft.members : ["None"]) : draft.members });
        }}>
          {schemaKinds.map((item) => <option key={item}>{item}</option>)}
        </select>
      </label>
      {kind === "Enum" ? (
        <ul className="list">
          {(draft.members ?? []).map((member, index) => (
            <li key={`${member}-${index}`}>
              <input aria-label="Enum member" value={member} onChange={(event) => setDraft({
                ...draft,
                members: (draft.members ?? []).map((item, itemIndex) => itemIndex === index ? event.target.value : item)
              })} />
            </li>
          ))}
        </ul>
      ) : (
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
            <input aria-label="Default value" placeholder="Default" value={field.defaultValue ?? ""} onChange={(event) => setDraft({
              ...draft,
              fields: draft.fields.map((item) => item.id === field.id ? { ...item, defaultValue: event.target.value } : item)
            })} />
            <span className="muted">{field.id.slice(0, 8)}</span>
          </li>
        ))}
      </ul>
      )}
      {kind === "Enum" ? (
        <button type="button" onClick={() => setDraft({ ...draft, members: [...(draft.members ?? []), "Member"] })}>Add member</button>
      ) : (
        <button type="button" onClick={() => setDraft({
          ...draft,
          fields: [...draft.fields, { id: crypto.randomUUID(), name: "Field", typeName: "int32", persistent: true, replicated: false }]
        })}>Add field</button>
      )}
      <button type="button" onClick={() => props.onSave({ ...draft, kind, isComponent: kind === "Component" })}>Save and place on canvas</button>
      {props.note ? <p className="muted">{props.note}</p> : null}
    </div>
  );
}
