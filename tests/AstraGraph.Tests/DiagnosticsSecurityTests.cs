using System;
using System.Linq;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Runtime.Debugging;
using AstraGraph.Runtime.Profiling;
using AstraGraph.Runtime.Security;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class DiagnosticsSecurityTests
{
    [Test]
    public void AstraAuthorizationService_EnforcesFineGrainedRbac()
    {
        var gameplayPlayer = new AstraUser(
            "User1",
            "Player",
            AstraPermission.ViewGraphs | AstraPermission.EditDrafts,
            SecurityProfile.Gameplay);

        var developer = new AstraUser(
            "User2",
            "ContentDev",
            AstraPermission.ViewGraphs | AstraPermission.EditDrafts | AstraPermission.Compile | AstraPermission.PublishServer,
            SecurityProfile.Trusted);

        var admin = new AstraUser(
            "User3",
            "LeadAdmin",
            AstraPermission.Admin,
            SecurityProfile.Engine);

        // Check gameplay user permissions
        Assert.That(AstraAuthorizationService.CanEditDraft(gameplayPlayer), Is.True);
        Assert.That(AstraAuthorizationService.CanCompile(gameplayPlayer), Is.False);
        Assert.That(AstraAuthorizationService.CanPublish(gameplayPlayer, GraphSide.Server, SecurityProfile.Gameplay), Is.False);

        // Check developer permissions
        Assert.That(AstraAuthorizationService.CanCompile(developer), Is.True);
        Assert.That(AstraAuthorizationService.CanPublish(developer, GraphSide.Server, SecurityProfile.Gameplay), Is.True);
        Assert.That(AstraAuthorizationService.CanPublish(developer, GraphSide.Server, SecurityProfile.Trusted), Is.True);
        Assert.That(AstraAuthorizationService.CanPublish(developer, GraphSide.Shared, SecurityProfile.Gameplay), Is.False); // Missing PublishShared!
        Assert.That(AstraAuthorizationService.CanPublish(developer, GraphSide.Server, SecurityProfile.Engine), Is.False);  // Missing Engine profile!

        // Check admin permissions
        Assert.That(AstraAuthorizationService.CanPublish(admin, GraphSide.Shared, SecurityProfile.Engine), Is.True);
        Assert.That(AstraAuthorizationService.CanDebug(admin), Is.True);
        Assert.That(AstraAuthorizationService.CanRollback(admin), Is.True);
        Assert.That(AstraAuthorizationService.CanModifyPersistentState(admin), Is.True);
    }

    [Test]
    public void GraphDebugger_HandlesConditionalBreakpoints_AndTracepoints()
    {
        var debugger = new GraphDebugger();
        var node1 = NodeId.New();
        var node2 = NodeId.New();

        // Conditional breakpoint on node1: only break when r0 > 100
        debugger.SetBreakpoint(new Breakpoint(
            node1,
            BreakpointMode.GraphPause,
            Condition: regs => regs.Count > 0 && regs[0].AsInt64() > 100));

        // Tracepoint on node2: logs without halting
        debugger.SetBreakpoint(new Breakpoint(node2, BreakpointMode.Tracepoint));

        // 1. Node 1 with r0 = 50 -> Does NOT suspend
        var regsSmall = new[] { AstraValue.FromInt64(50) };
        var hit1 = debugger.CheckBreakpoint(node1, instructionPointer: 10, regsSmall, out var suspension1);
        Assert.That(hit1, Is.False);
        Assert.That(suspension1, Is.Null);

        // 2. Node 1 with r0 = 150 -> Suspends!
        var regsLarge = new[] { AstraValue.FromInt64(150) };
        var hit2 = debugger.CheckBreakpoint(node1, instructionPointer: 10, regsLarge, out var suspension2);
        Assert.That(hit2, Is.True);
        Assert.That(suspension2, Is.Not.Null);
        Assert.That(suspension2.NodeId, Is.EqualTo(node1));
        Assert.That(suspension2.Registers[0].AsInt64(), Is.EqualTo(150L));

        // 3. Node 2 -> Tracepoint logs to ring buffer, does not suspend
        var regsTrace = new[] { AstraValue.FromInt64(999) };
        var hit3 = debugger.CheckBreakpoint(node2, instructionPointer: 25, regsTrace, out var suspension3);
        Assert.That(hit3, Is.False);
        Assert.That(suspension3, Is.Null);

        // Trace ring buffer should contain all 3 visited steps
        var recentTrace = debugger.GetRecentTrace(count: 10);
        Assert.That(recentTrace.Count, Is.EqualTo(3));
        Assert.That(recentTrace[2].NodeId, Is.EqualTo(node2));
    }

    [Test]
    public void GraphProfiler_MeasuresScopeTiming_AndTracksHottestNodes()
    {
        var profiler = new GraphProfiler();
        var graphId = GraphId.New();
        var nodeA = NodeId.New();
        var nodeB = NodeId.New();

        // Run profile scopes
        using (profiler.BeginScope(graphId))
        {
            profiler.RecordInstruction(graphId, nodeA);
            profiler.RecordInstruction(graphId, nodeA);
            profiler.RecordInstruction(graphId, nodeB);
            profiler.RecordNativeCall(graphId);
        }

        using (profiler.BeginScope(graphId))
        {
            profiler.RecordInstruction(graphId, nodeA);
            profiler.RecordYield(graphId);
        }

        var metrics = profiler.GetMetrics(graphId);
        Assert.That(metrics.Invocations, Is.EqualTo(2));
        Assert.That(metrics.InstructionsExecuted, Is.EqualTo(4));
        Assert.That(metrics.NativeCallsExecuted, Is.EqualTo(1));
        Assert.That(metrics.Yields, Is.EqualTo(1));
        Assert.That(metrics.TotalMicroseconds, Is.GreaterThanOrEqualTo(0.0));

        // Heat map
        var hottest = profiler.GetHottestNodes(top: 5);
        Assert.That(hottest.Count, Is.EqualTo(2));
        Assert.That(hottest[0].Key, Is.EqualTo(nodeA));
        Assert.That(hottest[0].Value, Is.EqualTo(3)); // 3 hits
        Assert.That(hottest[1].Key, Is.EqualTo(nodeB));
        Assert.That(hottest[1].Value, Is.EqualTo(1)); // 1 hit

        // Reset
        profiler.Reset();
        Assert.That(profiler.GetMetrics(graphId).Invocations, Is.EqualTo(0));
        Assert.That(profiler.GetHottestNodes(), Is.Empty);
    }
}
