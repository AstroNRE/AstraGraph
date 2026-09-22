using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstraGraph.Core;
using AstraGraph.VM;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AstraGraph.JIT;

/// <summary>
/// Compiles generated C# code using Roslyn in memory into a collectible AssemblyLoadContext.
/// </summary>
public sealed class RoslynCompiler
{
    private static readonly List<MetadataReference> DefaultReferences = CreateMetadataReferences();

    private static List<MetadataReference> CreateMetadataReferences()
    {
        var refs = new HashSet<MetadataReference>();
        var assemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(AstraValue).Assembly,
            typeof(AstraVm).Assembly,
            Assembly.Load("System.Runtime"),
            Assembly.Load("System.Collections")
        };

        foreach (var asm in assemblies)
        {
            if (!string.IsNullOrEmpty(asm.Location))
            {
                refs.Add(MetadataReference.CreateFromFile(asm.Location));
            }
        }

        // Add additional trusted platform assemblies
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrEmpty(trustedAssemblies))
        {
            var paths = trustedAssemblies.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var fileName = Path.GetFileName(path);
                if (fileName is "System.Runtime.dll" or "System.Collections.dll" or "System.Private.CoreLib.dll" or "netstandard.dll")
                {
                    refs.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        return refs.ToList();
    }

    /// <summary>
    /// Compiles an IrProgram into an in-memory executable JitCompiledProgram.
    /// </summary>
    public static JitCompiledProgram Compile(IrProgram irProgram, RevisionId? revision = null)
    {
        ArgumentNullException.ThrowIfNull(irProgram);

        var rev = revision ?? RevisionId.New();
        var csharpSource = IrToCSharpCompiler.GenerateCSharp(irProgram, out var className);
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);

        var assemblyName = $"AstraGraph_Jit_{irProgram.Id.Value:N}_{rev.Value:N}";
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            DefaultReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        if (!emitResult.Success)
        {
            var failures = emitResult.Diagnostics
                .Where(d => d.IsWarningAsError || d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
                .ToList();

            throw new InvalidOperationException($"Roslyn JIT compilation failed:\n{string.Join("\n", failures)}\n\nSource:\n{csharpSource}");
        }

        peStream.Seek(0, SeekOrigin.Begin);
        var loadContext = new AstraLoadContext(assemblyName);
        var assembly = loadContext.LoadFromStream(peStream);

        var type = assembly.GetType($"AstraGraph.JIT.Generated.{className}")
            ?? throw new InvalidOperationException($"Generated class {className} was not found in compiled assembly.");

        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Failed to instantiate generated class {className}.");

        var program = new JitCompiledProgram(irProgram.Id, rev, className, loadContext, assembly);

        var allFunctions = new List<IrFunction>(irProgram.Functions);
        foreach (var ep in irProgram.EntryPoints)
        {
            if (!allFunctions.Contains(ep))
            {
                allFunctions.Add(ep);
            }
        }

        foreach (var func in allFunctions)
        {
            var methodName = $"Execute_{SanitizeIdentifier(func.Name)}";
            var methodInfo = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (methodInfo != null)
            {
                JitFunctionInvoker invoker = (regs, services, budget) =>
                {
                    try
                    {
                        return (AstraValue)methodInfo.Invoke(instance, [regs, services, budget])!;
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException != null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                        throw;
                    }
                };
                program.RegisterInvoker(func.Name, invoker);
            }
        }

        return program;
    }

    private static string SanitizeIdentifier(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }
        return sb.ToString();
    }
}
