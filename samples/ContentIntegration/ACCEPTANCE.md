# AstraGraph 1.0 acceptance

This file maps Implementation Roadmap §25 and Release Plan phases 0–12 to checks that run in this repository. A live SS14 Content server is the consumer of the submodule. It is not built here.

## What CI proves

`dotnet test AstraGraph.slnx --configuration Release -p:RobustToolboxRoot=../RobustToolbox -p:RobustToolsBuild=true`

| Step | Check |
| --- | --- |
| Schema, publish, migrate, rollback, restart | `ProductionIntegrationTests.PlayableLifecycle_PublishEditRollbackAndRestart` |
| Rollback reads the previous graph from disk | `ReleasePhaseTests.Rollback_RestoresPreviousDocumentFromDiskWhenMemoryIsEmpty` |
| Native event on the Robust event bus | `ProductionIntegrationTests.PublishedGraph_RunsEntryPointThroughRobustEventBus` |
| By-ref event mutation | `RefEventIntegrationTests` |
| Mixed query | `ProductionIntegrationTests` mixed MetaData query |
| Spawn and delete | `RobustGameplayTests.SpawnAndDelete_UsesEntityManager` |
| Predicted publish rejects an unlisted native call | `ReleasePhaseTests.SharedPredicted_PublishRejectsUnlistedNativeCall` |
| Network frame on `INetManager` | `ProductionIntegrationTests.RobustNetManager_CarriesAstraSyncFrames` |
| Prediction tick, rollback, replay | `ProductionIntegrationTests.RobustPrediction_UsesEngineTick_ThenRollsBackAndReplays` |
| BUI revision | `ProductionIntegrationTests.RobustBui_AppliesAuthoritativeStateAndClientMessage` |
| Shared activation tick | `SharedAstraGraphSystem.Update` calls `SharedActivationCoordinator.Update` |
| Studio protocol | `ReleaseReadinessTests` checks `draft.publish.request` in `AstraGraph.StudioWeb/wwwroot/studio.js` |
| Permissions | Content Developer cannot publish; a Publisher rank can; deny wins |
| Sample graph | `samples/ContentIntegration/Resources/AstraGraph/DollToMothroach.agraph` |

## Still outside this repository

- Phase 6. `DynamicNativeSystemOrdering` stays false. Graphs do not insert themselves between native EntitySystems. `FixedPhaseScheduleHook` records Before/After as an approximation and the publish diagnostic says so.
- DoAfter as a Content system. Graphs use the `Flow.DoAfter` latent node. This Robust commit has no DoAfter type to bind.
- A second OS process of a Content server, a connected player, and the in-game Open Studio menu. The sample registration in this folder is the seam those steps call.
- Engine, process, and unsafe capabilities stay outside the Gameplay profile.
