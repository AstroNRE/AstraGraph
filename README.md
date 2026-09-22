# AstraGraph

Независимый сабмодуль визуального gameplay-языка для RobustToolbox / Space Station 14. Исходник механики — файл `.agraph`. Исполнение идёт через Astra IR, портативную VM и серверный JIT. Состояние отделено от кода, поэтому публикация на границе тика не стирает мир.

Зафиксированная база записана в `Compatibility.json`:

- Space Station 14 `811531c3c533cb644351d237a467648cb9613813`
- RobustToolbox `fee0dd647ebe95ad62d8945440450fb6cd4749a7`

## Сборка этого репозитория

```bash
dotnet test AstraGraph.slnx
```

Ядро собирается под `net9.0` и `net10.0`. Проекты `AstraGraph.Robust.*` подключают RobustToolbox, если checkout лежит рядом с репозиторием (`../RobustToolbox`) или задан `RobustToolboxRoot`. CI клонирует движок на `81e82b01d6cc5957015a8c9f3baf5e573b0a5217`.

## Подключение к форку SS14

1. Добавить этот репозиторий сабмодулем рядом с `RobustToolbox`.
2. В серверном и клиентском Content-проектах сослаться на `AstraGraph.Robust.Server` и `AstraGraph.Robust.Client`.
3. Собрать решение. Корень движка подхватится сам, либо укажите его явно:

```bash
dotnet build AstraGraph.slnx -p:RobustToolboxRoot=../RobustToolbox
```

4. Зарегистрировать `SharedAstraGraphSystem`, `ServerAstraGraphSystem` и `ClientAstraGraphSystem` в IoC и передать им `AstraGraphFacade`.
5. Положить графы проекта в `Resources/AstraGraph`, живые ревизии пишутся в `data/AstraGraph`.

Content вызывает `IAstraGraphManager`. Ядро не ссылается на RobustToolbox и не ссылается на `AdminFlags`. Право на вход в Studio даёт `IAstraPermissionProvider` в адаптере форка.

`AstraGraph.StudioWeb/wwwroot` отдаётся локальным мостом `AstraLocalBridge` через `DirectoryWebAssetProvider`.

Пока в Robust нет хуков подписки по `Type` и динамического порядка систем, хост использует `FixedPhaseScheduleHook`. Желаемые хуки перечислены в `Compatibility.json`.
