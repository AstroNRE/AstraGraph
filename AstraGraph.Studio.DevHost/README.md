# Astra Studio DevHost

Development host for `AstraGraph.StudioWeb`. It serves the canonical Studio assets and, in standalone mode, the existing authoring WebSocket protocol. It does not replace the in-game local bridge.

## Preview

```bash
dotnet run --project AstraGraph.Studio.DevHost -- --mode preview
```

Studio opens with compile, publish, rollback, and debug disabled.

## Standalone

```bash
dotnet run --project AstraGraph.Studio.DevHost -- --mode standalone
```

This process owns one `AstraGraphFacade`. Graphs are stored under `./AstraGraphDev/Resources/AstraGraph` and `./AstraGraphDev/data/AstraGraph`.

## External bind

`0.0.0.0` starts only when `--token` or `ASTRA_STUDIO_TOKEN` is set. The token is never written to the log. In GitHub Codespaces, forward port 5173 as Private.

DevHost is a development tool. Do not deploy it as part of the game server.
