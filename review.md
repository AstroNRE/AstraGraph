Да. Ниже — уже **не общий концепт**, а последовательный план доведения текущего `AstraGraph.StudioWeb` от состояния HEAD `681fd931` до **Astra Studio 1.0**, соответствующей Notion.

Этот план относится прежде всего к **репозиторию AstraGraph / сабмодулю**. Night City-specific launcher, `AdminFlags.AstraGraph` и подключение к реальному Content — отдельный consumer integration этап.

# Astra Studio 1.0 — план доведения до полной готовности

Сейчас база уже есть:

```text
React + TypeScript + Vite
React Flow
DevHost
LocalBridge
Authoring Protocol
Binding Catalog
Save / Compile / Publish
basic History
basic Debugger backend
basic Profiler backend
permissions handshake
production dist
```

Поэтому **переписывать Studio ещё раз не надо**. Надо последовательно достроить недостающие слои.

1. **Phase 0 — исправить фундаментальные ошибки текущего authoring flow.** Сначала починить существующий graph lifecycle. `graph.fetch` должен уметь получать source активной revision, а не только `_activeDrafts`. При открытии LIVE graph Studio должна делать `Active Revision → canonical source → Create/Open Draft(BaseRevision=X)`. Добавить полноценные `graph.create`, `graph.rename`, `graph.delete/disable`, а создание `Untitled` только локально убрать как production workflow. Rollback должен принимать и реально активировать конкретный `TargetRevisionId`; сейчас выбранная пользователем revision и фактический runtime rollback могут расходиться. `HistoryRollbackResponse` сделать отдельным typed DTO, а не возвращать `HistoryListResponse`. На этом же этапе добавить protocol version в handshake и понятный incompatible-version screen. **Gate:** existing LIVE graph можно открыть, отредактировать, сохранить Draft, Compile, Publish v2 и откатить именно к выбранной v1.

2. **Phase 1 — сделать Binding Catalog настоящим языком Studio.** Сейчас Studio видит binding, но создаёт ему только `In/Out`. Нужно расширить `CatalogEntryDto`: declaring type, method/function ID, parameters, return type, generic parameters/constraints, side, purity, deterministic/prediction-safe, security profile, capabilities, estimated cost, obsolete state, documentation. Создать `BindingNodeFactory`: descriptor `SurgerySystem.MakeIncision(EntityUid surgeon, EntityUid patient, BodyPartType bodyPart)` превращается в typed visual node с execution pins и настоящими data pins. Pure method не получает execution pins. Events превращаются в typed entry nodes. Properties/operators/constructors/generic calls тоже должны иметь собственные descriptors. Binding identity хранить через stable ID, а не только отображаемую signature. **Gate:** Studio может взять реальный C# method из Catalog и автоматически создать корректную typed node без ручного JSON.

3. **Phase 2 — полноценная типизированная работа с проводами.** Подключить реальный type system к canvas. `EntityUid → EntityUid` разрешается; `string → EntityUid` блокируется. Проверять data type, execution/data kind, side restrictions, nullability, generic specialization и schema compatibility. При drag провода совместимые pins подсвечиваются, несовместимые становятся недоступны и объясняют причину. Drop провода на пустой canvas открывает context-aware node search уже от требуемого типа. Использовать/унифицировать существующие `PinConnectionValidator` и `ContextCompletionEngine`, а не создавать параллельную JS-систему правил. Server compiler остаётся ultimate authority. **Gate:** пользователь физически не может собрать заведомо неправильную typed connection через нормальный UI.

4. **Phase 3 — настоящий Graph Canvas.** Довести React Flow editor: box/multi selection, copy/paste, duplicate, delete, keyboard movement, align/distribute, snapping, reroute nodes, comments/regions, minimap toggle, fit selection, bookmarks, semantic zoom, context menu. Execution/data/event pins должны отличаться формой, а не только цветом. Nodes показывают side, diagnostic, breakpoint, pure/impure, profiler badge. Event nodes визуально отдельны. Добавить command-based Undo/Redo для всех semantic и layout операций. Editor layout не должен менять semantic hash. **Gate:** большой graph можно редактировать без ручной правки `.agraph`.

5. **Phase 4 — нормальный IDE Shell.** Вместо текущего `Graphs | Canvas | Inspector` сделать Activity Bar, Explorer, Search, Bindings, Types, Runtime, Debug, Profiler, History, Audit, Permissions и UI Designer. Добавить document tabs и возможность одновременно открыть несколько graphs/schema/diff/runtime inspectors. Добавить bottom panel с `Problems | Watch | Debug | Profiler | History | Output`. Добавить status bar: target, transport, side, security profile, revision, draft status, errors/warnings. Сделать resizeable/collapsible panels и persistence workspace layout локально. **Gate:** Studio ощущается как IDE, а не одна web-страница редактора.

6. **Phase 5 — Explorer и управление проектом.** Explorer должен разделять `PROJECT`, `LIVE`, `OVERRIDES`, `DRAFTS`, `DISABLED`, `FAILED`. Группировка по System, Function, Behavior, Library, Schema, Migration, UI, Sandbox. Добавить filters по name/kind/side/status/owner/tags/revision. Graph summary backend расширить origin/status/draft/fault/modified/security profile. Добавить `New...` wizard: System Graph, Function, Behavior, Library, Component Schema, Struct, Enum, Migration, UI. **Gate:** разработчик создаёт весь типичный Astra content только через Studio.

7. **Phase 6 — Variables, Types, Schemas и Functions.** Нынешний Variables editor расширить scope/persistence/replication/default/GUID/type/reference count. Добавить полноценный Types workspace: Astra Component Schema, Struct, Enum, Interface/Contract, collection/nullable types. Functions workspace: parameters, outputs, generic parameters, pure/impure, side, export/native API option. Добавить `Find References`, rename со stable GUID и safe schema evolution. Для schema fields identity основана на GUID, а display rename не считается удалением+созданием. **Gate:** новый Astra component и function создаются без C#.

8. **Phase 7 — настоящий Inspector.** Inspector выбранной node должен показывать typed parameters, defaults, overload/generic selection, binding source, side, prediction status, capabilities, purity, cost, docs. Property editor выбирается по типу: checkbox, enum dropdown, numeric, Entity/Proto picker, type picker, collection editor и т.д. Без выбранной node Inspector переключается на Graph metadata: kind, side, scheduling, security profile, persistence, hot reload policy, dependencies, budgets. **Gate:** никакой типичной настройки node не требует редактировать JSON.

9. **Phase 8 — Problems и diagnostics navigation.** Расширить diagnostics до `GraphGuid + NodeGuid + PinGuid + code + severity + source + related locations + optional quick fix`. Problems panel группирует Errors, Warnings, Security, Prediction, Performance, Migration. Double click открывает нужный document, центрирует node и подсвечивает pin. Inline badges появляются на node/pin/edge. Добавить `Alt+Enter` для machine-readable quick fixes. **Gate:** compiler error в большом graph находится одним кликом.

10. **Phase 9 — Draft lifecycle и multi-user safety.** Autosave Draft отдельно от Publish. UI всегда показывает BaseRevision, Current Server Head и Draft revision. При stale base — `OUT OF DATE`, никаких silent overwrite. Добавить Compare, Rebase, Discard, позже Semantic Merge. Local emergency recovery хранить в IndexedDB, но не считать authority. После reconnect сравнивать server draft и local recovery. Drafts разных пользователей независимы. **Gate:** два developer могут работать от одной revision, и один не уничтожит работу другого.

11. **Phase 10 — Compile UX.** Отображать pipeline `Parse → Type Check → Generic Specialization → Binding Resolve → Side → Prediction → Capability → IR → Verify`. Показывать compilation duration, SemanticHash и diagnostics. Compile никогда не активирует graph. Добавить Compile Current / Compile Workspace и optional test compile. **Gate:** developer ясно понимает, на каком этапе и почему compilation не прошла.

12. **Phase 11 — полноценный Publish flow.** Publish dialog должен показывать target side, base/head revision, semantic diff summary, schema diff, migration plan, capabilities, warnings, publish message. Shared/Predicted publish показывает readiness clients и ActivationTick. High-risk graph может требовать confirmation/reviewer policy. Ошибка Publish оставляет текущую LIVE revision нетронутой и Draft сохранённым. **Gate:** Publish является понятной transactional operation, а не просто кнопкой.

13. **Phase 12 — History и Semantic Diff.** Заменить `IReadOnlyList<string>` на typed `RevisionSummaryDto`. History показывает revision ID, author, timestamp, message, source origin, ActivationTick, compilation/migration result, runtime faults. Создать semantic diff: added/removed node, connection, property change, function signature, schema field, event subscription, side/security changes. Layout diff хранить отдельно. Schema diff должен показывать compatibility/migration impact. **Gate:** различие v12 ↔ v13 понятно без просмотра JSON.

14. **Phase 13 — Rollback/LKG.** Rollback проходит тот же transactional path, что Publish. UI показывает target diff и migration impact. Поддержать rollback к конкретной revision и Last Known Good. При ошибке rollback live graph остаётся прежним. History/audit фиксируют операцию. **Gate:** выбрал revision 7 → runtime действительно работает на revision 7 после safe activation.

15. **Phase 14 — полноценный Debugger UI.** Использовать уже существующий backend и расширить protocol. Canvas breakpoint marker, conditional breakpoint, Graph Pause, Tracepoint, Dev World Pause. Toolbar `Pause / Continue / Step Into / Step Over / Step Out`. При suspension подсвечивать текущую node и execution path. Bottom views: Locals, Parameters, Event Payload, Graph State, Watch, Call Stack, Continuations, Trace. Watches должны понимать typed graph expressions, entity/component fields, а не только VM registers `r0`. `StepOut` реализовать реально, а не alias Resume. **Gate:** breakpoint → событие → pause → inspect locals → Step Over → Continue полностью работает из браузера.

16. **Phase 15 — Live Watch и Runtime Inspector.** Добавить selective/throttled runtime subscriptions. Watch targets: Entity, Graph System, Graph Instance, Component Schema, Player, Event invocation. Entity Inspector показывает UID, prototype, native components, Astra components, attached graphs, continuations, relevant state, revision. Поддержать deep links `entity`, `graph`, `revision`, `node`, `diagnostic`, `debug session`. **Gate:** из игры можно открыть конкретную entity в Studio и увидеть её состояние без full-world stream.

17. **Phase 16 — Profiler до требований Notion.** Backend расширить с текущих invocations/average/instructions/native calls/yields до P95, query iterations, allocations, network bytes, continuation count, budget violations, per-node time. Добавить `profiler.subscribe/unsubscribe/snapshot/reset/config`. Studio показывает summary, time series, hottest graphs/nodes и heat overlay на canvas. Heat не обозначать только цветом: показывать numbers/badges. **Gate:** можно найти graph/node, который съедает budget.

18. **Phase 17 — Audit и Security UI.** Добавить `audit.query` и отдельный Audit workspace. Показывать Publish, Rollback, Runtime State Edit, Permission Change, Engine Profile operations. Permission Inspector показывает effective profile и capabilities. `session.updated` в реальном времени отключает commands, закрывает forbidden panels и останавливает privileged debug streams. Manage Permissions UI только при соответствующем capability. **Gate:** deadmin/permission revoke отражается в Studio без reload/reconnect.

19. **Phase 18 — Remote/local transport parity.** Один `AuthoringClient`, transport-neutral frontend. Local Game Bridge, DevHost и RemoteServerTransport дают одинаковые возможности. Connection loss переводит IDE в offline editing mode, запрещает server operations, сохраняет recovery. Reconnect делает re-auth, refresh permissions/head revision, восстанавливает subscriptions и проверяет conflicts. **Gate:** один frontend проходит одинаковый workflow через DevHost и Local/Remote transport.

20. **Phase 19 — UI Designer.** Это отдельная workspace для `GraphKind.UI`, а не новый frontend. Tabs: `Design | Logic | State | Contract | Styles | Preview | Diagnostics`. Design: Control Palette из реального UI Control Catalog, hierarchy tree, canvas, selection, resize, alignment, guides, snapping, grouping, copy/paste, responsive sizes. Inspector properties имеют `Constant / Binding / Expression`. Logic использует обычный Astra Graph canvas. State: Local/Replicated/Derived. Contract: state/actions/notifications/validation/rate limits. Styles: StyleClass/theme tokens/resources/localization. **Gate:** UI Graph создаётся без отдельного C# window.

21. **Phase 20 — UI Preview/Debugger/Profiler.** Browser mock preview поддерживает screen size, scale, locale, theme, mock state. Connected-client preview отображает настоящие Robust Controls. UI Inspector: ElementGuid, native control, properties, active bindings, state dependencies, events, focus, layout metrics. UI profiler: layout passes, binding evaluations, controls churn, collection updates, event rates, network bytes, allocations. **Gate:** UI можно создать, hot reload, inspect и profile из той же Studio.

22. **Phase 21 — Search и navigation.** `Ctrl+P` Quick Open; `Ctrl+Shift+P` Command Palette; `Ctrl+Space` contextual node search; F12 Go to Definition; Shift+F12 Find Usages. Search по graphs, bindings, schemas, functions, nodes, docs и optionally runtime entities. Node insertion search должен использовать fuzzy ranking и Catalog metadata. **Gate:** к любой части проекта можно перейти без ручного Explorer hunting.

23. **Phase 22 — Test Sandbox.** Для Functions/Behaviors/System fragments сделать Test workspace: typed inputs, mock event/entity/components, expected outputs/state. `Run / Debug / Profile`. Тесты можно хранить рядом с `.agraph`. Это не заменяет real Robust integration tests, но сильно ускоряет authoring. **Gate:** простая новая логика проверяется до Publish.

24. **Phase 23 — Accessibility и ergonomics.** Keyboard navigation, visible focus, ARIA, non-color-only states, high contrast, reduced motion, scalable UI. Desktop-first, рекомендованный минимум около 1280 px; на меньших экранах panels collapsible. Настраиваемые shortcuts. Убрать опасный accidental publish: destructive/high-risk actions требуют confirmation. **Gate:** IDE полностью usable keyboard-first.

25. **Phase 24 — performance.** Большие Catalog lists и History виртуализировать. Canvas viewport culling/memoization. Search/diff/auto-layout можно вынести в Web Worker. Цель: обычное редактирование ~60 FPS, Catalog 10k+ entries без фризов, graph 500–1000 logical nodes остаётся usable. Compiler всё ещё server-side. **Gate:** stress tests не деградируют IDE до непригодного состояния.

26. **Phase 25 — frontend CI.** Текущий CI недостаточен: он проверяет только `.NET`. Добавить Node setup → `npm ci` → `npm test` → `npm run build` → проверка, что `dist` синхронизирован с source. Затем `.NET restore/build/test`. Добавить Vitest/React Testing Library и Playwright E2E через DevHost. Отдельные E2E: create/compile/publish/rollback, permissions, debugger, reconnect, UI Designer smoke. **Gate:** green CI доказывает не только runtime, но и работоспособность реальной Studio.

27. **Phase 26 — protocol contract tests.** C# protocol и TypeScript client должны иметь общие fixtures. Проверять serialize C# → parse TS и наоборот. Все protocol messages versioned. Удалить stringly typed history и прочие временные ответы. Unknown capability должен gracefully degrade frontend, а не ломать его. **Gate:** backend/frontend нельзя случайно рассинхронизировать коммитом.

28. **Phase 27 — packaging.** Единственный canonical frontend — `AstraGraph.StudioWeb`. `dist` включается в Bridge/DevHost package. `node_modules` никогда не нужны consumer build или production server. Consumer делает только `dotnet build`. В CI AstraGraph rebuild'ит frontend. Удалить/мигрировать legacy duplicated `Editor.Bridge/Assets`, чтобы не было двух источников Studio. **Gate:** clean server/client deployment открывает IDE без npm/Node.

29. **Phase 28 — acceptance against Notion.** Пройти полный сценарий: running SS14 → Open Astra Studio → auto-auth → создать System Graph + component schema → найти native API → native event → Compile diagnostic → исправить → Publish без restart → проверить в игре → Inspect Entity from game → Watch → breakpoint + Step Over → Profiler → Revision 2 → semantic diff → rollback Revision 1 → restart server → mechanic восстановилась → подключиться RemoteServerTransport → увидеть тот же graph/history. Отдельно пройти UI acceptance: UI Graph → native Robust controls → local state → reactive binding → event → server contract/action → virtualization → style/localization → Publish → live hot reload с сохранением focus/state → debugger/profiler → restart restoration.

30. **Phase 29 — только после этого объявлять Astra Studio 1.0.** Не считать готовностью наличие React, React Flow и Compile/Publish buttons. Финальная готовность означает, что обычный Content-level developer может работать без JSON и без Rider во всём диапазоне, который определён AstraGraph: gameplay systems, schemas/components, functions, events, queries, persistence, networking logic, shared/predicted mechanics и Astra UI. C# остаётся только для новых engine/native primitives.

### Порядок, который я бы дал Grok прямо сейчас

Самая важная последовательность:

```text
LIVE source / Draft lifecycle
        ↓
Typed Binding Nodes
        ↓
Typed Connections
        ↓
Inspector
        ↓
Problems navigation
        ↓
Explorer / New Graph / Types / Functions
        ↓
Publish + History + Semantic Diff
        ↓
Debugger
        ↓
Runtime Inspector / Watch
        ↓
Profiler
        ↓
Audit / Permissions
        ↓
UI Designer
        ↓
Hardening / E2E / Performance
```

Не начинать сейчас с UI Designer и не тратить ещё время на косметику. **Первые пять блоков превращают Studio из красивого graph viewer в реальный инструмент программирования.**

По достижении Phase 9–12 я бы уже считал Studio пригодной для активного создания gameplay mechanics. Phase 13–18 превращают её в полноценную live-development IDE. Phase 19–30 доводят именно до того **100% состояния, которое описано в Notion**.
