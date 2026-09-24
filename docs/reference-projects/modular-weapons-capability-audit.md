# Mechanic 24 — capability audit

Audit of AstraGraph `main` at `9699aeac`. This pass did not add weapon code. Struct values, list mutation, and their persistence are now in the library; the Night City pin still has to move before a build sees them.

Status words: Not implemented, Partial, Runtime only, Compiler only, Studio only, Working end-to-end.

## What already works end-to-end

A graph can subscribe to a Robust event, read a schema component, branch, loop, and spawn an entity. That path was used by the mothroach bat: `Event` → `Flow.ForEach` → `Entity.TryGetComponent` → `Schema.GetField` → `Entity.SpawnAt`. Schema fields of primitive types survive YAML and the VM when the schema source is wired. Studio can edit those defaults on the node. Publish updates the router for an event that was subscribed before the bus lock.

`SchemaType` is the struct and the component. `IsComponentSchema == false` means a value. Studio can create Component, Struct, and Enum schemas. `CollectionType` names `List<T>`, `Set<T>`, and `Dictionary<K,V>`. `AstraList` can be read (`Count`, `Get`). Bytecode has `CollectionLength` and `CollectionGet`, and `GetField` / `SetComponentField`. `Flow.For`, `Flow.ForEach`, `Flow.Delay`, and `Flow.DoAfter` exist. `CallLocal` exists for functions. Entity spawn, spawn-at, exists, delete, and queue-delete are generic gameplay calls. `MixedQueryEngine` refuses a query with no component filter. `PersistentStateStore` writes an atomic, checksummed, versioned file. HTML UI documents render to one page, and a bound interface can open that page.

## Matrix

| Capability | Status | Notes |
| --- | --- | --- |
| User-defined struct | Partial | `Schema.Make`, `SetField`, `GetField`, and `Copy` run on `SchemaType`. Compare is still absent. |
| User-defined component | Working end-to-end | Primitive fields. Mothroach bat. |
| Nested struct | Partial | A field may name another schema in the same graph. The VM and the state file walk nested structs. |
| List, Set, Dictionary | Partial | `AstraList` can add, set, remove, get, and count, including a list of structs. Set and dictionary still have no VM value. |
| nullable | Partial | `NullableType` and `HasValue`. Not a studio field editor. |
| enum | Studio only | Studio kind exists. Not a VM value distinct from an int. |
| foreach | Working end-to-end | `Flow.ForEach` over a list such as hit entities. |
| collection mutation | Partial | `List.Add`, `List.Set`, `List.Remove`, `List.Get`, `List.Count`. No clear or contains. |
| functions | Partial | `CallLocal` and `FunctionType`. Not proven as a readable library of pure functions in Studio. |
| subgraphs | Not implemented | No collapsed graph document. |
| events | Working end-to-end | Robust events by name, directed by component. |
| custom event payload | Not implemented | No graph-defined event. |
| entity query | Runtime only | `MixedQueryEngine` has no studio node. |
| native component read | Partial | `GetMember` / `TryGetComponent` for indexed members. |
| native component write | Partial | `SetComponentField` exists. No write-permission catalog in Studio. |
| dynamic component read / write | Working end-to-end | `Schema.GetField` and schema defaults. Write of nested data is not there. |
| component add / remove | Runtime only | Query model can add and remove. No graph node. |
| prototype access | Partial | A prototype id string can be passed to `Entity.SpawnAt`. No field read from a prototype. |
| entity spawn / delete | Working end-to-end | `Entity.Spawn`, `SpawnAt`, `Delete`, `QueueDelete`, `Exists`. |
| inventory / containers | Not implemented | Not in `GameplayBindings`. |
| DoAfter / Delay | Partial | Nodes and `YieldContinuation` exist. A yielded call stack is not a save. |
| scheduled work | Not implemented | No `ScheduleAt` or `RepeatEvery`. |
| game time | Partial | `TimeSpan` is a primitive name. No `Now` / `Since`. |
| RNG | Not implemented | |
| persistent primitives | Working end-to-end | Bool, int, double, entity id, vector, and a single string. |
| persistent structs / collections / components | Partial | State format 2 stores a struct by schema id and field id, and a list, including nested values. Format 1 still loads. One bad object becomes null and the rest of the file stays. Component instances are still not a snapshot. |
| network replication | Partial | Field flags `Replicated` and `Predicted` exist. Not proven for structs. |
| BUI state / actions | Partial | State is a string map. Actions are a name plus a payload. No list, selection, or disabled row. |
| hot reload | Partial | Same event and component can publish. A new pair after the bus lock does not subscribe. Schema changes do not migrate stored values. |
| debugging / profiling | Partial | Studio trace and profile exist. No struct or collection inspector. |

## What blocks vertical slice 1

The slice is: place a gun on a bench, open the Astra UI, see parts, swap a barrel, shoot with the new profile, restart, and see the same assembly.

That fails today for four generic reasons:

1. A part can be a struct in a list, and that list can be saved. A persistent id for the gun is still missing, so `EntityUid` after a restart is a different object.
2. The bench cannot ask a container or inventory for the gun and the loose parts.
3. The UI cannot show a server-owned list of parts and send `InstallPart` as an intent. It can show a static page and a button name.

Heat, fouling, manufacturing lots, RNG, curves, and localization are later slices. They are absent and should stay absent until the slice above exists.

## Order

Do not add a second type system. Extend `SchemaType`, `AstraList`, and `PersistentStateStore`.

1. Struct value: get field, set field, copy. Nested struct is the same walk. Done in the VM and in Studio nodes.
2. `List<T>` mutate: add, remove, get, set, count, foreach. `T` may be a struct. Add, remove, get, set, and count are done. Foreach already walked a list.
3. Persist those values in the existing snapshot, versioned, by schema id and field id. One bad object must not wipe the file. Done as format 2. Format 1 still loads.
4. Persistent object id that is not `EntityUid`, usable by a gun, a car, or a tool. Next.
5. Generic container and inventory reads and inserts. No weapon slot node.
6. BUI list, selection, and an action that carries an id. The server graph decides the result.
7. Only then the first weapon graphs, laid out left to right as a chain.

Graphs for the mechanic go in Night City content. The capabilities above go in AstraGraph.
