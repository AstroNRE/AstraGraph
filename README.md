# AstraGraph

Независимый сабмодуль визуального gameplay-языка для RobustToolbox / Space Station 14. Исходник механики — файл `.agraph`. Исполнение идёт через Astra IR, портативную VM и серверный JIT. Состояние отделено от кода, поэтому публикация на границе тика не стирает мир.

Supported engine commit and profile are defined only in `Compatibility.json`.

## Сборка этого репозитория

```bash
dotnet test AstraGraph.slnx
```

Ядро собирается под `net9.0` и `net10.0`. Проекты `AstraGraph.Robust.*` подключают RobustToolbox, если checkout лежит рядом с репозиторием (`../RobustToolbox`) или задан `RobustToolboxRoot`. CI клонирует движок на `testedRobustCommit` из `Compatibility.json`.

## Подключение к форку SS14

1. Добавить этот репозиторий сабмодулем рядом с `RobustToolbox`.
2. В серверном и клиентском Content-проектах сослаться на `AstraGraph.Robust.Server` и `AstraGraph.Robust.Client`.
3. Собрать решение. Корень движка подхватится сам, либо укажите его явно:

```bash
dotnet build AstraGraph.slnx -p:RobustToolboxRoot=../RobustToolbox
```

4. До `ServerAstraGraphSystem.Initialize()` передать настройки хоста. Форк регистрирует `IAstraServerHostConfiguration` в IoC или вызывает `AstraServerHost.Configure` заранее:

```csharp
var options = new AstraServerHostOptions
{
    Storage = new StorageLayout(
        "<consumer>/Resources/AstraGraph",
        "<consumer>/data/AstraGraph"),

    CompatibilityManifestPath =
        "<consumer>/AstraGraph/Compatibility.json",

    RobustToolboxRoot =
        "<consumer>/RobustToolbox",

    AdminDirectory = consumerAdminDirectory
};
```

`IAstraAdminDirectory` или собственный `IAstraPermissionProvider` реализуются в форке. AstraGraph не ссылается на `IAdminManager`.
5. Положить графы проекта в `Resources/AstraGraph`, живые ревизии пишутся в `data/AstraGraph`. Если commit движка нельзя прочитать из `.git`, передайте `RobustCommit`. Иначе старт останавливается: engine identity не подменяется значением из манифеста.

Content вызывает `IAstraGraphManager`. Ядро не ссылается на RobustToolbox и не ссылается на `AdminFlags`. Право на вход в Studio даёт `IAstraPermissionProvider` в адаптере форка.

`AstraGraph.StudioWeb/wwwroot` отдаётся локальным мостом `AstraLocalBridge` через `DirectoryWebAssetProvider`.

## Astra Studio development

DevHost — отдельный инструмент разработки. Он не заменяет внутриигровой local bridge и не входит в поставку игрового сервера.

Интерфейс — один frontend в `AstraGraph.StudioWeb`. Исходники собираются в `dist/`, и именно этот каталог раздают Local Bridge и DevHost. Сборка игрового сервера npm не вызывает.

```bash
cd AstraGraph.StudioWeb
npm install
npm run build
npm test
```

### Preview

```bash
dotnet run --project AstraGraph.Studio.DevHost -- --mode preview
```

### Standalone

```bash
dotnet run --project AstraGraph.Studio.DevHost -- --mode standalone
```

### Codespaces

Внешний адрес требует токен. В лог попадает только `Token: ********`. Порт 5173 открывайте как Private.

```bash
dotnet run \
  --project AstraGraph.Studio.DevHost \
  -- \
  --mode standalone \
  --listen 0.0.0.0 \
  --port 5173 \
  --token <your-token>
```

После старта: Ports → 5173 → Private → Open in Browser. Public для этого порта не используйте.

Пока в Robust нет хуков подписки по `Type` и динамического порядка систем, хост использует `FixedPhaseScheduleHook`. Желаемые хуки перечислены в `Compatibility.json`.
