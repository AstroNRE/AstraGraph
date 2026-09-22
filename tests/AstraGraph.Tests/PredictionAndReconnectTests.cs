using System;
using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Network;
using AstraGraph.State;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class PredictionAndReconnectTests
{
    [Test]
    public void PredictionReconciler_CorrectPrediction_ProducesNoRollback()
    {
        var store = new DynamicComponentStore();
        var reconciler = new PredictionReconciler(store);

        var schemaId = SchemaId.New();
        var fHealth = FieldId.New();
        var schema = new SchemaType(schemaId, "HealthComp", true, [new SchemaField(fHealth, "Health", PrimitiveType.Float64)]);

        const int entityId = 100;
        store.AddComponent(entityId, schema, [AstraValue.FromDouble(100.0)]);

        // 1. Client predicts Health = 90.0 at tick 42
        var predictedValues = new[] { AstraValue.FromDouble(90.0) };
        reconciler.RecordPredictedState(42, entityId, schemaId, predictedValues);

        var mispredictedFired = false;
        reconciler.OnMispredictionDetected += _ => mispredictedFired = true;

        // 2. Authoritative server state arrives confirming Health = 90.0 at tick 42
        var serverValues = new[] { AstraValue.FromDouble(90.0) };
        var result = reconciler.ReconcileServerState(42, entityId, schemaId, serverValues);

        Assert.That(result.Mispredicted, Is.False);
        Assert.That(mispredictedFired, Is.False);
    }

    [Test]
    public void PredictionReconciler_Misprediction_RollsBackComponentAndFiresCallback()
    {
        var store = new DynamicComponentStore();
        var reconciler = new PredictionReconciler(store);

        var schemaId = SchemaId.New();
        var fHealth = FieldId.New();
        var schema = new SchemaType(schemaId, "HealthComp", true, [new SchemaField(fHealth, "Health", PrimitiveType.Float64)]);

        const int entityId = 100;
        var comp = store.AddComponent(entityId, schema, [AstraValue.FromDouble(100.0)]);

        // 1. Client predicts Health = 95.0 at tick 50
        comp.SetField(0, AstraValue.FromDouble(95.0));
        reconciler.RecordPredictedState(50, entityId, schemaId, [AstraValue.FromDouble(95.0)]);

        ReconciliationResult? capturedResult = null;
        reconciler.OnMispredictionDetected += res => capturedResult = res;

        // 2. Authoritative server state arrives for tick 50 with Health = 70.0 (enemy damage occurred on server)
        var serverAuthoritative = new[] { AstraValue.FromDouble(70.0) };
        var result = reconciler.ReconcileServerState(50, entityId, schemaId, serverAuthoritative);

        // Verify misprediction detected
        Assert.That(result.Mispredicted, Is.True);
        Assert.That(capturedResult, Is.Not.Null);
        Assert.That(capturedResult!.ServerValues[0].AsDouble(), Is.EqualTo(70.0));
        Assert.That(capturedResult.PredictedValues![0].AsDouble(), Is.EqualTo(95.0));

        // Verify local store was rolled back to server authoritative value
        var currentComp = store.GetComponent(entityId, schemaId);
        Assert.That(currentComp.GetField(0).AsDouble(), Is.EqualTo(70.0), "Dynamic component field must be rolled back to authoritative server value!");
    }

    [Test]
    public void NetworkSync_JoinInProgress_ExchangesHandshakeAndInstallsMissingPackages()
    {
        var serverHost = new AstraGraphHost();
        var clientHost = new AstraGraphHost();

        var serverSync = new AstraNetworkSyncService(serverHost);
        var clientSync = new AstraNetworkSyncService(clientHost);

        // 1. Server has 2 active programs registered in scheduler
        var graphA = GraphId.New();
        var revA = RevisionId.New();
        var progA = new BytecodeProgram(graphA, revA, "hashA", new ConstantPool());
        serverHost.RegisterProgram(progA);
        serverHost.Scheduler.RegisterSystem(new SystemRegistration(graphA, "SystemA", [], [], 0, (_, _) => { }));

        var graphB = GraphId.New();
        var revB = RevisionId.New();
        var progB = new BytecodeProgram(graphB, revB, "hashB", new ConstantPool());
        serverHost.RegisterProgram(progB);
        serverHost.Scheduler.RegisterSystem(new SystemRegistration(graphB, "SystemB", [], [], 0, (_, _) => { }));

        // 2. Client connects: Server generates handshake at tick 100
        var handshake = serverSync.BuildHandshakeMessage(currentServerTick: 100);
        Assert.That(handshake.Manifest.Entries.Count, Is.EqualTo(2));

        // 3. Client processes handshake: detects both graphs missing
        var missingReq = clientSync.ProcessHandshake(handshake);
        Assert.That(missingReq, Is.Not.Null);
        Assert.That(missingReq!.MissingGraphIds.Count, Is.EqualTo(2));

        // 4. Server exports packages for missing graphs
        foreach (var missingId in missingReq.MissingGraphIds)
        {
            var pkg = serverSync.ExportGraphPackage(missingId);
            Assert.That(pkg, Is.Not.Null);

            // 5. Client installs incoming package
            var installed = clientSync.ApplyGraphPackage(pkg!);
            Assert.That(installed.Id, Is.EqualTo(missingId));
        }

        // 6. Verify client host now has both programs installed
        Assert.That(clientHost.GetProgram(graphA), Is.Not.Null);
        Assert.That(clientHost.GetProgram(graphA)!.Revision, Is.EqualTo(revA));
        Assert.That(clientHost.GetProgram(graphB), Is.Not.Null);
        Assert.That(clientHost.GetProgram(graphB)!.Revision, Is.EqualTo(revB));

        // 7. Client is ready
        var readyMsg = AstraNetworkSyncService.CreateReadyMessage(clientId: 1, currentClientTick: 100);
        Assert.That(readyMsg.ClientId, Is.EqualTo(1));
    }

    [Test]
    public void NetworkSync_Reconnect_DetectsOutdatedRevisionAfterServerHotReload()
    {
        var serverHost = new AstraGraphHost();
        var clientHost = new AstraGraphHost();

        var serverSync = new AstraNetworkSyncService(serverHost);
        var clientSync = new AstraNetworkSyncService(clientHost);

        var graphId = GraphId.New();

        // 1. Initial shared state: Revision 1
        var rev1 = RevisionId.New();
        var progV1 = new BytecodeProgram(graphId, rev1, "hash_v1", new ConstantPool());
        serverHost.RegisterProgram(progV1);
        serverHost.Scheduler.RegisterSystem(new SystemRegistration(graphId, "LiveSystem", [], [], 0, (_, _) => { }));
        clientHost.RegisterProgram(progV1);

        // 2. Client disconnects.
        // Server hot-reloads to Revision 2!
        var rev2 = RevisionId.New();
        var progV2 = new BytecodeProgram(graphId, rev2, "hash_v2", new ConstantPool());
        serverHost.RegisterProgram(progV2);

        // 3. Client reconnects: receives new server handshake with Revision 2
        var reconnectHandshake = serverSync.BuildHandshakeMessage(currentServerTick: 500);
        Assert.That(reconnectHandshake.Manifest.Entries[0].Revision, Is.EqualTo(rev2));

        // 4. Client detects revision mismatch (currently has Rev 1, server wants Rev 2)
        var missingReq = clientSync.ProcessHandshake(reconnectHandshake);
        Assert.That(missingReq, Is.Not.Null);
        Assert.That(missingReq!.MissingGraphIds, Does.Contain(graphId));

        // 5. Server sends package for Revision 2
        var pkgV2 = serverSync.ExportGraphPackage(graphId);
        Assert.That(pkgV2, Is.Not.Null);

        // 6. Client installs Revision 2
        clientSync.ApplyGraphPackage(pkgV2!);

        // 7. Verify client host now runs Revision 2 seamlessly
        var clientProg = clientHost.GetProgram(graphId);
        Assert.That(clientProg, Is.Not.Null);
        Assert.That(clientProg!.Revision, Is.EqualTo(rev2), "Client host must be updated to new revision upon reconnect without full restart!");
    }
}
