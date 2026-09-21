using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.JIT;
using AstraGraph.Persistence;
using AstraGraph.Runtime.Security;
using AstraGraph.Tests.Fuzzing;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class ProductionHardeningAndFuzzingTests
{
    private string _tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AstraGraph_Fuzz_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void JsonParser_Fuzzing_SurvivesMutatedPayloadsWithoutCrash()
    {
        var node1Id = NodeId.New();
        var pin1Id = PinId.New();

        var validDoc = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "SampleValidDoc",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes =
            [
                new NodeDocument
                {
                    Id = node1Id,
                    Name = "Node1",
                    NodeType = "Core.Log",
                    Pins =
                    [
                        new PinDocument
                        {
                            Id = pin1Id,
                            Name = "In",
                            Direction = PinDirection.Input,
                            Kind = PinKind.Execution,
                            DataType = "Flow"
                        }
                    ]
                }
            ],
            Connections = []
        };

        var baseJson = GraphSerializer.Serialize(validDoc);
        var rng = new Random(12345);
        var iterations = 300;
        var rejectedCount = 0;
        var acceptedCount = 0;

        for (var i = 0; i < iterations; i++)
        {
            var mutated = GraphFuzzer.MutateJson(baseJson, rng);

            try
            {
                var doc = GraphSerializer.Deserialize(mutated);
                if (doc != null)
                {
                    acceptedCount++;
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException)
            {
                // Handled parse rejection — expected behavior for invalid inputs
                rejectedCount++;
            }
        }

        Assert.That(rejectedCount + acceptedCount, Is.EqualTo(iterations));
        Assert.That(rejectedCount, Is.GreaterThan(0));
    }

    [Test]
    public void SemanticAnalyzer_Fuzzing_HandlesRandomMalformedGraphsGracefully()
    {
        var rng = new Random(54321);
        var analyzer = new SemanticAnalyzer();
        var iterations = 100;
        var errorCount = 0;

        for (var i = 0; i < iterations; i++)
        {
            var malformedDoc = GraphFuzzer.GenerateMalformedDocument(rng);

            // Analysis must never crash or throw unhandled exceptions
            var result = analyzer.Analyze(malformedDoc);

            Assert.That(result, Is.Not.Null);
            if (!result.Success)
            {
                errorCount++;
                Assert.That(result.Diagnostics.Count, Is.GreaterThan(0));
            }
        }

        Assert.That(errorCount, Is.GreaterThan(0));
    }

    [Test]
    public void HotReload_100ConsecutiveSwaps_UnloadsCollectibleALCWithoutLeaks()
    {
        var weakRefs = new List<WeakReference>();

        for (var i = 0; i < 100; i++)
        {
            var weakRef = ExecuteAndUnloadCollectibleContext(i);
            weakRefs.Add(weakRef);
        }

        // Force GC to reclaim collectible load contexts
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        var deadCount = weakRefs.Count(r => !r.IsAlive);

        // At least 95% of contexts should be collected cleanly by GC
        Assert.That(deadCount, Is.GreaterThanOrEqualTo(95));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExecuteAndUnloadCollectibleContext(int iteration)
    {
        var alc = new AstraLoadContext($"HotReload_ALC_{iteration}");
        var program = new JitCompiledProgram(
            GraphId.New(),
            RevisionId.New(),
            $"Generated_Graph_{iteration}",
            alc,
            Assembly.GetExecutingAssembly());

        program.RegisterInvoker("Main", (regs, services, budget) => AstraValue.FromInt64(iteration * 10));

        var result = program.Execute("Main", null, null!, new ExecutionBudget());
        Assert.That(result.AsInt64(), Is.EqualTo(iteration * 10));

        var weakRef = new WeakReference(alc, trackResurrection: false);

        program.Dispose(); // Unloads the ALC
        return weakRef;
    }

    [Test]
    public void AtomicFileStore_SimulatedPowerLossDuringPublish_RecoversSafely()
    {
        var storage = new StorageLayout(_tempDirectory, Path.Combine(_tempDirectory, "data"));
        storage.EnsureDirectories();

        var filePath = storage.GetLivePath("gameplay_system.agraph");
        var initialContent = "{\"version\": 1, \"status\": \"valid_initial\"}";
        AtomicFileStore.WriteAllTextAtomic(filePath, initialContent, storage.BackupsDirectory);

        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(AtomicFileStore.ReadAllText(filePath), Is.EqualTo(initialContent));

        // Simulate crash during write: validator throws
        var corruptAttempt = "{\"version\": 2, \"status\": \"interrupted_during_write\"}";
        Assert.Throws<InvalidOperationException>(() =>
        {
            AtomicFileStore.WriteAllTextAtomic(
                filePath,
                corruptAttempt,
                storage.BackupsDirectory,
                validator: _ => throw new InvalidOperationException("Simulated power loss midway"));
        });

        // The target file must remain intact and completely uncorrupted!
        Assert.That(AtomicFileStore.ReadAllText(filePath), Is.EqualTo(initialContent));

        // Create an orphan temporary file to simulate unexpected process termination
        var orphanPath = filePath + ".simulated_crash.tmp";
        File.WriteAllText(orphanPath, "partial junk data");

        Assert.That(File.Exists(orphanPath), Is.True);

        // Run recovery cleanup
        var cleaned = storage.CleanOrphanTempFiles();
        Assert.That(cleaned, Is.GreaterThan(0));
        Assert.That(File.Exists(orphanPath), Is.False);
        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(AtomicFileStore.ReadAllText(filePath), Is.EqualTo(initialContent));
    }

    [Test]
    public void SecurityPolicy_BlocksForbiddenCallsAndInfiniteExecutionBudget()
    {
        // 1. RBAC Check: Gameplay user cannot publish Shared or Engine profile graphs
        var gameplayUser = new AstraUser("user1", "JuniorDesigner", AstraPermission.EditDrafts | AstraPermission.Compile | AstraPermission.PublishServer, SecurityProfile.Gameplay);
        var canPublishShared = AstraAuthorizationService.CanPublish(gameplayUser, GraphSide.Shared, SecurityProfile.Gameplay);
        Assert.That(canPublishShared, Is.False);

        var canPublishEngine = AstraAuthorizationService.CanPublish(gameplayUser, GraphSide.Server, SecurityProfile.Engine);
        Assert.That(canPublishEngine, Is.False);

        // Admin can publish both
        var adminUser = new AstraUser("admin1", "LeadDev", AstraPermission.Admin, SecurityProfile.Engine);
        Assert.That(AstraAuthorizationService.CanPublish(adminUser, GraphSide.Shared, SecurityProfile.Gameplay), Is.True);
        Assert.That(AstraAuthorizationService.CanPublish(adminUser, GraphSide.Server, SecurityProfile.Engine), Is.True);

        // 2. Access Policy: Gameplay profile cannot call Engine methods
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Engine, SecurityProfile.Gameplay), Is.False);
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Trusted, SecurityProfile.Gameplay), Is.False);
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Gameplay, SecurityProfile.Gameplay), Is.True);

        // 3. Execution Budget: Infinite loop terminates cleanly via VmExecutionStatus.ExceededBudget
        var pool = new ConstantPool();
        var fnName = pool.GetOrAddString("LoopFunction");
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.Nop, 0, 0, 0, 0),
            new((byte)IrOpCode.Jump, BytecodeInstruction.NoRegister, 0, 0, 0) // Jump back to IP 0
        };

        var entryPoint = new BytecodeFunction(fnName, 1, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var vm = new AstraVm();
        var strictBudget = new ExecutionBudget { MaxInstructions = 25 };

        var result = vm.Execute(program, entryPoint, budget: strictBudget);

        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.ExceededBudget));
        Assert.That(result.Exception, Is.InstanceOf<ExecutionBudgetExceededException>());
        Assert.That(result.InstructionsExecuted, Is.GreaterThanOrEqualTo(25));
    }
}
