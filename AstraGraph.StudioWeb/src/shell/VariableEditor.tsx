import { nextVariableName, type GraphDocument } from "../documents/graph";

const types = ["System.Int32", "System.Single", "System.Boolean", "System.String", "AstraGraph.Core.AstraEntityId"];

type Variable = GraphDocument["variables"][number];

export function VariableEditor(props: { variables: Variable[]; onChange: (variables: Variable[]) => void }) {
  function update(id: string, patch: Partial<Variable>) {
    props.onChange(props.variables.map((variable) => variable.id === id ? { ...variable, ...patch } : variable));
  }

  return (
    <div className="variables">
      {props.variables.map((variable) => (
        <div className="variable-row" key={variable.id}>
          <input aria-label="Variable name" value={variable.name} onChange={(event) => update(variable.id, { name: event.target.value })} />
          <select aria-label="Variable type" value={variable.typeName} onChange={(event) => update(variable.id, { typeName: event.target.value })}>
            {types.includes(variable.typeName) ? null : <option value={variable.typeName}>{variable.typeName}</option>}
            {types.map((typeName) => <option key={typeName} value={typeName}>{typeName}</option>)}
          </select>
          <button type="button" onClick={() => props.onChange(props.variables.filter((item) => item.id !== variable.id))}>Remove</button>
        </div>
      ))}
      <button type="button" onClick={() => props.onChange([...props.variables, {
        id: crypto.randomUUID(),
        name: nextVariableName(props.variables),
        typeName: "System.Int32"
      }])}>Add variable</button>
    </div>
  );
}
