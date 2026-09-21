using System;
using System.Collections.Generic;
using System.Text;
using AstraGraph.Core;

namespace AstraGraph.Tests.Fuzzing;

/// <summary>
/// Mutation-based and generative fuzzer for stress-testing AstraGraph JSON deserializers,
/// AST semantic analyzers, IR compilers, and VM bytecode verifiers.
/// </summary>
public static class GraphFuzzer
{
    private static readonly string[] MaliciousStrings =
    [
        "",
        "\"\"\"",
        "{ \"unclosed\": ",
        "[ 1, 2, 3, ",
        "null",
        "undefined",
        "NaN",
        "Infinity",
        "-Infinity",
        "1e308",
        "-1e308",
        "\"\\uD800\"", // lone high surrogate
        "\"\\uDFFF\"", // lone low surrogate
        new string('A', 50_000), // large string
        new string('{', 500), // deep nesting
        "<!--#exec cmd=\"ls\"-->",
        "System.IO.File.Delete(\"*\");",
        "{\"kind\": \"system\", \"nodes\": [null, {}]}"
    ];

    public static string MutateJson(string baseJson, Random rng)
    {
        ArgumentNullException.ThrowIfNull(baseJson);
        ArgumentNullException.ThrowIfNull(rng);

        var strategy = rng.Next(0, 6);
        switch (strategy)
        {
            case 0:
                // Truncate at random point
                var cutIndex = rng.Next(0, baseJson.Length);
                return baseJson[..cutIndex];

            case 1:
                // Bit flip / character replacement
                var chars = baseJson.ToCharArray();
                var flipCount = rng.Next(1, Math.Min(10, chars.Length));
                for (var i = 0; i < flipCount; i++)
                {
                    var idx = rng.Next(0, chars.Length);
                    chars[idx] = (char)rng.Next(0, 255);
                }
                return new string(chars);

            case 2:
                // Inject malicious token at random position
                var token = MaliciousStrings[rng.Next(0, MaliciousStrings.Length)];
                var insertPos = rng.Next(0, baseJson.Length);
                return baseJson.Insert(insertPos, token);

            case 3:
                // Nullify or corrupt random property
                var commonTokens = new[] { "\"id\":", "\"nodes\":", "\"connections\":", "\"pins\":", "\"kind\":" };
                var targetToken = commonTokens[rng.Next(0, commonTokens.Length)];
                var foundIdx = baseJson.IndexOf(targetToken, StringComparison.Ordinal);
                if (foundIdx >= 0)
                {
                    return baseJson.Insert(foundIdx + targetToken.Length, " null, \"corrupt\": ");
                }
                return baseJson + " {\"corrupt\": null}";

            case 4:
                // Deep bracket nesting
                var sb = new StringBuilder();
                var depth = rng.Next(10, 100);
                for (var i = 0; i < depth; i++) sb.Append('[');
                sb.Append(baseJson);
                for (var i = 0; i < depth; i++) sb.Append(']');
                return sb.ToString();

            default:
                // Random junk bytes
                var junkBytes = new byte[rng.Next(1, 512)];
                rng.NextBytes(junkBytes);
                return Encoding.UTF8.GetString(junkBytes);
        }
    }

    public static GraphDocument GenerateMalformedDocument(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var graphId = GraphId.New();
        var nodeCount = rng.Next(1, 10);
        var nodes = new List<NodeDocument>();
        var allPins = new List<(NodeId NodeId, PinId PinId)>();

        for (var i = 0; i < nodeCount; i++)
        {
            var nodeId = NodeId.New();
            var nodeName = rng.Next(0, 3) == 0 ? "" : $"FuzzNode_{i}_{rng.Next(1000)}";
            var nodeType = rng.Next(0, 4) switch
            {
                0 => "Core.Log",
                1 => "Math.Add",
                2 => "Unknown.Random_Type_" + rng.Next(1000),
                _ => ""
            };

            var nodePins = new List<PinDocument>();
            var pinCount = rng.Next(0, 4);
            for (var p = 0; p < pinCount; p++)
            {
                var pinId = PinId.New();
                var dir = rng.Next(0, 2) == 0 ? PinDirection.Input : PinDirection.Output;
                var kind = rng.Next(0, 2) == 0 ? PinKind.Execution : PinKind.Data;
                var dataType = rng.Next(0, 3) switch
                {
                    0 => "Flow",
                    1 => "System.Int64",
                    _ => "Invalid.Type." + rng.Next(100)
                };

                nodePins.Add(new PinDocument
                {
                    Id = pinId,
                    Name = $"Pin_{p}",
                    Direction = dir,
                    Kind = kind,
                    DataType = dataType
                });
                allPins.Add((nodeId, pinId));
            }

            nodes.Add(new NodeDocument
            {
                Id = nodeId,
                Name = nodeName,
                NodeType = nodeType,
                Pins = nodePins
            });
        }

        // Malformed connections (dangling pins, cross-kind, self-referential)
        var connections = new List<ConnectionDocument>();
        var connCount = rng.Next(0, 10);
        for (var c = 0; c < connCount; c++)
        {
            NodeId fromNode;
            PinId fromPin;
            NodeId toNode;
            PinId toPin;

            if (rng.Next(0, 3) == 0 || allPins.Count == 0)
            {
                fromNode = NodeId.New();
                fromPin = PinId.New();
                toNode = NodeId.New();
                toPin = PinId.New();
            }
            else
            {
                var p1 = allPins[rng.Next(0, allPins.Count)];
                var p2 = allPins[rng.Next(0, allPins.Count)];
                fromNode = p1.NodeId;
                fromPin = p1.PinId;
                toNode = p2.NodeId;
                toPin = p2.PinId;
            }

            connections.Add(new ConnectionDocument
            {
                FromNode = fromNode,
                FromPin = fromPin,
                ToNode = toNode,
                ToPin = toPin
            });
        }

        return new GraphDocument
        {
            Id = graphId,
            Name = "FuzzTestGraph",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = nodes,
            Connections = connections
        };
    }
}
