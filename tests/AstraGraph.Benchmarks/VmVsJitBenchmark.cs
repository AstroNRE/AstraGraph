using AstraGraph.Core;
using AstraGraph.JIT;
using AstraGraph.VM;
using BenchmarkDotNet.Attributes;

namespace AstraGraph.Benchmarks;

[MemoryDiagnoser]
public class VmVsJitBenchmark
{
    private BytecodeProgram _bytecodeProgram = null!;
    private BytecodeFunction _bytecodeFunc = null!;
    private JitCompiledProgram _jitProgram = null!;
    private AstraVm _vm = null!;
    private DefaultVmHostServices _services = null!;
    private long _paramA = 20L;
    private long _paramB = 30L;

    [GlobalSetup]
    public void Setup()
    {
        var graphId = GraphId.New();
        var irProgram = new IrProgram(graphId, "BenchmarkGraph", GraphKind.Function, GraphSide.Server);

        var entry = new IrBasicBlock(0, "entry");
        var func = new IrFunction("Calculate", [], PrimitiveType.Int64, entry);
        irProgram.EntryPoints.Add(func);

        var r0 = new IrRegister(0, PrimitiveType.Int64);
        var r1 = new IrRegister(1, PrimitiveType.Int64);
        var r2 = new IrRegister(2, PrimitiveType.Int64);
        var r3 = new IrRegister(3, PrimitiveType.Int64);
        var r4 = new IrRegister(4, PrimitiveType.Int64);

        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r0, [new IrConstant(20L, PrimitiveType.Int64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r1, [new IrConstant(30L, PrimitiveType.Int64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.Mul, r2, [r0, r0]));
        entry.AddInstruction(new IrInstruction(IrOpCode.Mul, r3, [r1, r1]));
        entry.AddInstruction(new IrInstruction(IrOpCode.Add, r4, [r2, r3]));
        entry.SetTerminator(new IrInstruction(IrOpCode.Return, null, [r4]));
        func.RegisterCount = 5;

        _bytecodeProgram = IrToBytecodeCompiler.Compile(irProgram);
        _bytecodeFunc = _bytecodeProgram.EntryPoints[0];

        _vm = new AstraVm();
        _services = new DefaultVmHostServices();
        _jitProgram = RoslynCompiler.Compile(irProgram);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _jitProgram.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long DirectCSharp()
    {
        return _paramA * _paramA + _paramB * _paramB;
    }

    [Benchmark]
    public AstraValue VmExecution()
    {
        var result = _vm.Execute(_bytecodeProgram, _bytecodeFunc, [], 0, _services);
        return result.ReturnValue;
    }

    [Benchmark]
    public AstraValue JitExecution()
    {
        return _jitProgram.Execute("Calculate", null, _services);
    }
}
