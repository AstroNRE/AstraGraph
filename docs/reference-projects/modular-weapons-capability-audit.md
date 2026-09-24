# Mechanic 24 — capability audit

Audit of AstraGraph `main` at `9699aeac`, plus the uncommitted library work after that commit. Struct values, list mutation, persistence, containers, BUI rows, and the first weapon graphs are in the library checkout. The Night City pin still has to move before a build sees them.

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
| inventory / containers | Partial | Named Robust containers: has, insert, remove, contents, find by prototype, contains, held item. No weapon slot. Nested inventory and hand-specific active-item rules are still the engine's. |
| DoAfter / Delay | Partial | Nodes and `YieldContinuation` exist. A yielded call stack is not a save. |
| scheduled work | Not implemented | No `ScheduleAt` or `RepeatEvery`. |
| game time | Partial | `TimeSpan` is a primitive name. No `Now` / `Since`. |
| RNG | Not implemented | |
| persistent primitives | Working end-to-end | Bool, int, double, entity id, vector, and a single string. |
| persistent structs / collections / components | Partial | State format 2 stores a struct by schema id and field id, and a list, including nested values. Format 1 still loads. One bad object becomes null and the rest of the file stays. Component instances are still not a snapshot. |
| network replication | Partial | Field flags `Replicated` and `Predicted` exist. Not proven for structs. |
| BUI state / actions | Partial | An item list renders server rows (`id`, `text`, `disabled`). A button can send the selected id. `Ui.Rows` formats a list of structs, `Bui.Field` reads the id, `Bui.Set` writes one string into the entity's interface state. The client still only sends intent. |
| hot reload | Partial | Same event and component can publish. A new pair after the bus lock does not subscribe. Schema changes do not migrate stored values. |
| debugging / profiling | Partial | Studio trace and profile exist. No struct or collection inspector. |

## What blocks vertical slice 1

The slice is: place a gun on a bench, open the Astra UI, see parts, swap a barrel, shoot with the new profile, restart, and see the same assembly.

The bench graph lists parts and installs a barrel. The profile graph writes the assembly's fire rate and projectile speed when the gun refreshes. The running game still uses the pinned AstraGraph until that pin moves, so these graphs do not execute in a build that predates the pin.

Heat, fouling, manufacturing lots, RNG, curves, and localization are later slices. They are absent and should stay absent until the slice above exists.

## Order

Do not add a second type system. Extend `SchemaType`, `AstraList`, and `PersistentStateStore`.

1. Struct value: get field, set field, copy. Nested struct is the same walk. Done in the VM and in Studio nodes.
2. `List<T>` mutate: add, remove, get, set, count, foreach. `T` may be a struct. Add, remove, get, set, and count are done. Foreach already walked a list.
3. Persist those values in the existing snapshot, versioned, by schema id and field id. One bad object must not wipe the file. Done as format 2. Format 1 still loads.
4. Persistent object id that is not `EntityUid`, usable by a gun, a car, or a tool. Done. `PersistentObjectId` is a value, stored in a struct field, and it round-trips in the state file. `PersistentId.New` is rejected on a predicted graph.
5. Generic container and inventory reads and inserts. No weapon slot node. Done. The slot name is a string. `Container.Has`, `Insert`, `Remove`, `Contents`, `Inventory.Find`, `Contains`, `TryInsert`, `TryRemove`, and `Entity.GetHeldItem`. A missing container is a failed insert, not a new slot.
6. BUI list, selection, and an action that carries an id. The server graph decides the result. Done. `Ui.Rows`, `Bui.Field`, and `Bui.Set`. A missing interface is a failed set, not a new window. `Bui.Set` is rejected on a predicted graph.
7. First weapon graphs, laid out left to right. `WeaponProfile` copies `WeaponAssembly` fire rate and projectile speed onto `GunRefreshModifiersEvent`. `WeaponBench` lists parts from the bench container and installs a barrel only when the server graph accepts the id. A directed value-type event keeps writes made through a boxed handler. Primitive schema writes are copied onto the component shell the map saver reads.

Graphs for the mechanic go in Night City content. The capabilities above go in AstraGraph.
