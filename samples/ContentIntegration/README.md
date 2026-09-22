# Content integration

Этот каталог — эталонный diff форка. Классы хоста уже лежат в `AstraGraph.Robust.Shared`.

В Content остаётся:

1. Ссылки проектов: сервер на `AstraGraph.Robust.Server`, клиент на `AstraGraph.Robust.Client`, shared на `AstraGraph.Robust.Shared`.
2. Регистрация трёх систем в IoC и создание `AstraGraphFacade` на старте раунда или в `Initialize`.
3. Флаг `AdminFlags.AstraGraph` в сборке Content. Реализация `IAstraPermissionProvider` читает `IAdminManager` и не попадает в `AstraGraph.Core`.
4. Команда или пункт меню, который поднимает `AstraLocalBridge` с `DirectoryWebAssetProvider` на `AstraGraph.StudioWeb/wwwroot` и открывает браузер.
5. Каталоги `Resources/AstraGraph` и `data/AstraGraph`.

Сборка внутри форка:

```bash
dotnet build -p:RobustToolboxRoot=../RobustToolbox
```

## Приёмка Playable

- [ ] Сервер стартует с сабмодулем без отдельного `dotnet build` при изменении `.agraph`.
- [ ] `DollToMothroach.agraph` публикуется на границе тика.
- [ ] Правка графа применяется без перезапуска.
- [ ] Rollback возвращает предыдущую revision.
- [ ] Повторный запуск того же бинарника поднимает последнюю опубликованную revision из `data/AstraGraph/Live`.
