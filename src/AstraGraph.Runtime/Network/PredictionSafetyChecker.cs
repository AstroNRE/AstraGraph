using System;
using System.Collections.Generic;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Network;

/// <summary>
/// Enforces client prediction rules ensuring that predicted graphs only execute
/// deterministic, prediction-safe operations.
/// </summary>
public sealed class PredictionSafetyChecker
{
    private readonly HashSet<string> _predictionSafeMethods = new(StringComparer.Ordinal);

    public PredictionSafetyChecker(IEnumerable<string>? predictionSafeMethods = null)
    {
        if (predictionSafeMethods != null)
        {
            foreach (var m in predictionSafeMethods)
            {
                _predictionSafeMethods.Add(m);
            }
        }
    }

    public void RegisterSafeMethod(string methodDescriptor)
    {
        ArgumentNullException.ThrowIfNull(methodDescriptor);
        _predictionSafeMethods.Add(methodDescriptor);
    }

    /// <summary>
    /// Validates an IrProgram against prediction safety constraints.
    /// Returns true if valid, false if non-deterministic or server-only calls were detected.
    /// </summary>
    public bool Validate(IrProgram program, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (program.Side != GraphSide.SharedPredicted)
        {
            // Server-only, Client-only, and Shared non-predicted graphs are not constrained by prediction rules
            return true;
        }

        var isValid = true;
        var allFunctions = new List<IrFunction>(program.Functions);
        foreach (var ep in program.EntryPoints)
        {
            if (!allFunctions.Contains(ep))
            {
                allFunctions.Add(ep);
            }
        }

        foreach (var func in allFunctions)
        {
            foreach (var block in func.Blocks)
            {
                foreach (var instr in block.Instructions)
                {
                    if (instr.OpCode == IrOpCode.CallNative)
                    {
                        var method = instr.StringPayload ?? string.Empty;
                        if (!IsSafeMethod(method))
                        {
                            diagnostics.ReportError(
                                "PRED001",
                                $"Method '{method}' in function '{func.Name}' is not marked prediction-safe and cannot be called in a SharedPredicted graph.");
                            isValid = false;
                        }
                    }
                }
            }
        }

        return isValid;
    }

    public bool IsSafeMethod(string methodDescriptor)
    {
        if (string.IsNullOrWhiteSpace(methodDescriptor)) return false;

        // Automatically reject known non-deterministic or server-only keywords
        if (methodDescriptor.Contains("WallClock", StringComparison.OrdinalIgnoreCase) ||
            methodDescriptor.Contains("DateTime", StringComparison.OrdinalIgnoreCase) ||
            methodDescriptor.Contains("UnseededRandom", StringComparison.OrdinalIgnoreCase) ||
            methodDescriptor.Contains("Http", StringComparison.OrdinalIgnoreCase) ||
            methodDescriptor.Contains("FileSystem", StringComparison.OrdinalIgnoreCase) ||
            methodDescriptor.Contains("ServerOnly", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _predictionSafeMethods.Contains(methodDescriptor);
    }
}
