# Content integration

Этот каталог — эталонный diff форка. Классы хоста уже лежат в `AstraGraph.Robust.Shared`.

В Content остаётся:

1. Ссылки проектов: сервер на `AstraGraph.Robust.Server`, клиент на `AstraGraph.Robust.Client`, shared на `AstraGraph.Robust.Shared`.
2. Регистрация трёх систем в IoC. Сервер собирает runtime одним вызовом `AstraGraphFacade.ForServer(host, layout, permissions)`.
3. Флаг `AdminFlags.AstraGraph` в сборке Content. Production-права читает `RobustAdminPermissionProvider` через `IAstraAdminDirectory` (обёртка над `IAdminManager`). Отражения по сессии нет. `PolicyPermissionProvider` остаётся для тестов и standalone. Content Developer не получает `PublishServer`; его выдаёт ранг Publisher или явный grant. Deny перекрывает grant.
4. Команда или пункт меню, который поднимает `AstraLocalBridge` с `DirectoryWebAssetProvider` на `AstraGraph.StudioWeb/wwwroot` и открывает браузер.
5. Каталоги `Resources/AstraGraph` и `data/AstraGraph`.

Сборка внутри форка:

```bash
dotnet build -p:RobustToolboxRoot=../RobustToolbox
```

Код регистрации: `Shared/AstraContentRegistration.cs`, `Server/AstraServerRegistration.cs`, `Client/AstraClientRegistration.cs`.

Пример графа: `Resources/AstraGraph/DollToMothroach.agraph`. Карта приёмки §25 и фаз 0–12: `ACCEPTANCE.md`.

Приёмка publish, hot edit, rollback и restart покрыта `ProductionIntegrationTests.PlayableLifecycle_PublishEditRollbackAndRestart`.
