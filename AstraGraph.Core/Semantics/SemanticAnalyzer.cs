using System.Globalization;

namespace AstraGraph.Core;

/// <summary>
/// Compiles a raw GraphDocument into a fully typed AstProgram with comprehensive semantic validation.
/// </summary>
public sealed class SemanticAnalyzer
{
    private readonly TypeRegistry _typeRegistry;

    public SemanticAnalyzer(TypeRegistry? typeRegistry = null)
    {
        _typeRegistry = typeRegistry ?? TypeRegistry.Default;
    }

    public SemanticResult Analyze(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new DiagnosticBag();
        var nodeMap = new Dictionary<NodeId, NodeDocument>();
        var pinMap = new Dictionary<PinId, (NodeDocument Node, PinDocument Pin)>();

        // 1. Index and validate nodes and pins
        foreach (var node in document.Nodes)
        {
            if (!nodeMap.TryAdd(node.Id, node))
            {
                diagnostics.ReportError(DiagnosticCodes.DuplicateNodeId, $"Duplicate NodeId '{node.Id}'.", node.Id);
                continue;
            }

            foreach (var pin in node.Pins)
            {
                if (!pinMap.TryAdd(pin.Id, (node, pin)))
                {
                    diagnostics.ReportError(DiagnosticCodes.DuplicatePinId, $"Duplicate PinId '{pin.Id}' on node '{node.Name}'.", node.Id, pin.Id);
                }
            }
        }

        // 2. Validate variables
        var variableDecls = new List<AstVariableDeclaration>();
        var variableMap = new Dictionary<string, AstVariableDeclaration>(StringComparer.Ordinal);

        foreach (var v in document.Variables)
        {
            if (!_typeRegistry.TryGetType(v.TypeName, out var varType) || varType is null)
            {
                diagnostics.ReportError(DiagnosticCodes.UnknownType, $"Variable '{v.Name}' has unknown type '{v.TypeName}'.", symbolId: v.Id);
                varType = PrimitiveType.Int32; // fallback to avoid null ref cascades
            }

            var decl = new AstVariableDeclaration(v.Id, v.Name, varType);
            variableDecls.Add(decl);
            variableMap[v.Name] = decl;
        }

        // 3. Validate connections
        var incomingConnections = new Dictionary<PinId, List<ConnectionDocument>>();
        var outgoingConnections = new Dictionary<PinId, List<ConnectionDocument>>();

        foreach (var conn in document.Connections)
        {
            if (!pinMap.TryGetValue(conn.FromPin, out var fromTuple))
            {
                diagnostics.ReportError(DiagnosticCodes.InvalidConnection, $"Connection source pin '{conn.FromPin}' does not exist.", conn.FromNode, conn.FromPin);
                continue;
            }

            if (!pinMap.TryGetValue(conn.ToPin, out var toTuple))
            {
                diagnostics.ReportError(DiagnosticCodes.InvalidConnection, $"Connection destination pin '{conn.ToPin}' does not exist.", conn.ToNode, conn.ToPin);
                continue;
            }

            var fromPin = fromTuple.Pin;
            var toPin = toTuple.Pin;

            if (fromPin.Direction != PinDirection.Output)
            {
                diagnostics.ReportError(DiagnosticCodes.InvalidConnection, $"Connection source pin '{fromPin.Name}' must be an Output pin.", conn.FromNode, conn.FromPin);
            }

            if (toPin.Direction != PinDirection.Input)
            {
                diagnostics.ReportError(DiagnosticCodes.InvalidConnection, $"Connection destination pin '{toPin.Name}' must be an Input pin.", conn.ToNode, conn.ToPin);
            }

            if (fromPin.Kind != toPin.Kind)
            {
                diagnostics.ReportError(DiagnosticCodes.InvalidConnection, $"Cannot connect {fromPin.Kind} pin to {toPin.Kind} pin.", conn.FromNode, conn.FromPin);
            }

            // Type compatibility for Data pins
            if (fromPin.Kind == PinKind.Data && toPin.Kind == PinKind.Data)
            {
                _typeRegistry.TryGetType(fromPin.DataType, out var fromType);
                _typeRegistry.TryGetType(toPin.DataType, out var toType);

                if (fromType != null && toType != null)
                {
                    if (!toType.IsAssignableFrom(fromType))
                    {
                        diagnostics.ReportError(
                            DiagnosticCodes.TypeMismatch,
                            $"Type mismatch: cannot assign '{fromType.TypeName}' to '{toType.TypeName}'.",
                            conn.ToNode,
                            conn.ToPin,
                            suggestedFix: $"Convert {fromType.TypeName} to {toType.TypeName}");
                    }
                }
            }

            if (!incomingConnections.TryGetValue(conn.ToPin, out var inList))
            {
                inList = [];
                incomingConnections[conn.ToPin] = inList;
            }
            inList.Add(conn);

            if (!outgoingConnections.TryGetValue(conn.FromPin, out var outList))
            {
                outList = [];
                outgoingConnections[conn.FromPin] = outList;
            }
            outList.Add(conn);
        }

        // Validate execution pin incoming constraints (single input connection)
        foreach (var (pinId, conns) in incomingConnections)
        {
            if (pinMap.TryGetValue(pinId, out var tuple) && tuple.Pin.Kind == PinKind.Execution && conns.Count > 1)
            {
                diagnostics.ReportError(
                    DiagnosticCodes.MultipleInputConnectionsToExecutionPin,
                    $"Execution input pin '{tuple.Pin.Name}' on node '{tuple.Node.Name}' has {conns.Count} incoming connections. Only 1 is allowed without a Merge node.",
                    tuple.Node.Id,
                    pinId);
            }
        }

        // Validate pure dataflow cycles across the graph
        ValidateDataFlowCycles(document.Nodes, incomingConnections, pinMap, diagnostics);

        // 4. Side & Prediction Policy checks
        foreach (var node in document.Nodes)
        {
            if (document.Side == GraphSide.Client && node.Properties.TryGetValue("Side", out var side) && side.Equals("Server", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.ReportError(DiagnosticCodes.ServerApiCalledOnClient, $"Node '{node.Name}' requires Server side, but graph is configured for Client.", node.Id);
            }

            if (document.Side == GraphSide.SharedPredicted && node.Properties.TryGetValue("IsDeterministic", out var det) && det.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.ReportError(DiagnosticCodes.NonDeterministicOperationInPrediction, $"Node '{node.Name}' is non-deterministic and cannot be executed in predicted context.", node.Id);
            }
        }

        if (diagnostics.HasErrors)
        {
            return new SemanticResult(null, diagnostics);
        }

        // 5. Build AST
        var entryPoints = new List<AstEntryPointStatement>();
        var expressionContext = new ExpressionLowerer(_typeRegistry, nodeMap, pinMap, incomingConnections, variableMap, diagnostics);

        // Find entry point nodes
        var entryNodes = document.Nodes.Where(n =>
            n.NodeType.StartsWith("Event.", StringComparison.OrdinalIgnoreCase) ||
            n.NodeType.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
            n.NodeType.Equals("EntryPoint", StringComparison.OrdinalIgnoreCase) ||
            (n.Pins.Any(p => p.Kind == PinKind.Execution && p.Direction == PinDirection.Output) &&
             !n.Pins.Any(p => p.Kind == PinKind.Execution && p.Direction == PinDirection.Input))).ToList();

        if (entryNodes.Count == 0 && document.Kind == GraphKind.System)
        {
            diagnostics.ReportWarning(DiagnosticCodes.MissingEntryPoint, "System graph has no recognized entry points (e.g. Event.* or System.Update).");
        }

        foreach (var entryNode in entryNodes)
        {
            var bodyStatements = LowerExecutionBlock(entryNode, expressionContext, outgoingConnections, pinMap);
            var entryName = entryNode.Properties.GetValueOrDefault("EventName", entryNode.Name);
            entryPoints.Add(new AstEntryPointStatement(entryName, [], new AstBlock(bodyStatements), entryNode.Id));
        }

        var program = new AstProgram(
            document.Id,
            document.Name,
            document.Kind,
            document.Side,
            variableDecls,
            entryPoints,
            []);

        return new SemanticResult(program, diagnostics);
    }

    private static List<AstStatement> LowerExecutionBlock(
        NodeDocument startNode,
        ExpressionLowerer exprLowerer,
        Dictionary<PinId, List<ConnectionDocument>> outgoing,
        Dictionary<PinId, (NodeDocument Node, PinDocument Pin)> pinMap)
    {
        var statements = new List<AstStatement>();
        var currentNode = startNode;
        var visited = new HashSet<NodeId>();

        while (currentNode != null && visited.Add(currentNode.Id))
        {
            // Lower current node statement
            var stmt = exprLowerer.LowerStatement(currentNode);
            if (stmt != null)
            {
                statements.Add(stmt);
            }

            // If it's a branch or terminal, handle branches explicitly
            if (currentNode.NodeType.Equals("Core.Branch", StringComparison.OrdinalIgnoreCase) ||
                currentNode.NodeType.Equals("Branch", StringComparison.OrdinalIgnoreCase))
            {
                var truePin = currentNode.FindPin("True", PinDirection.Output);
                var falsePin = currentNode.FindPin("False", PinDirection.Output);

                var trueStatements = LowerBranchPath(truePin, exprLowerer, outgoing, pinMap);
                var falseStatements = LowerBranchPath(falsePin, exprLowerer, outgoing, pinMap);

                var condPin = currentNode.FindPin("Condition", PinDirection.Input);
                var condExpr = condPin != null
                    ? exprLowerer.LowerPinExpression(condPin)
                    : new AstLiteralExpression(false, PrimitiveType.Bool, currentNode.Id);

                var branchStmt = new AstBranchStatement(
                    condExpr,
                    new AstBlock(trueStatements),
                    falseStatements.Count > 0 ? new AstBlock(falseStatements) : null,
                    currentNode.Id);

                statements.Add(branchStmt);
                break; // branch terminates linear flow of current block
            }

            if (currentNode.NodeType.Equals("Core.Return", StringComparison.OrdinalIgnoreCase) ||
                currentNode.NodeType.Equals("Return", StringComparison.OrdinalIgnoreCase))
            {
                var valPin = currentNode.FindPin("Value", PinDirection.Input);
                var returnExpr = valPin != null ? exprLowerer.LowerPinExpression(valPin) : null;
                statements.Add(new AstReturnStatement(returnExpr, currentNode.Id));
                break;
            }

            // Move to next execution node connected via default execution output pin
            var outExecPin = currentNode.Pins.FirstOrDefault(p => p.Kind == PinKind.Execution && p.Direction == PinDirection.Output);
            if (outExecPin != null && outgoing.TryGetValue(outExecPin.Id, out var conns) && conns.Count > 0)
            {
                var targetConn = conns[0];
                if (pinMap.TryGetValue(targetConn.ToPin, out var targetTuple))
                {
                    currentNode = targetTuple.Node;
                    continue;
                }
            }

            break;
        }

        return statements;
    }

    private static List<AstStatement> LowerBranchPath(
        PinDocument? branchPin,
        ExpressionLowerer exprLowerer,
        Dictionary<PinId, List<ConnectionDocument>> outgoing,
        Dictionary<PinId, (NodeDocument Node, PinDocument Pin)> pinMap)
    {
        if (branchPin != null && outgoing.TryGetValue(branchPin.Id, out var conns) && conns.Count > 0)
        {
            if (pinMap.TryGetValue(conns[0].ToPin, out var target))
            {
                return LowerExecutionBlock(target.Node, exprLowerer, outgoing, pinMap);
            }
        }
        return [];
    }

    private static void ValidateDataFlowCycles(
        List<NodeDocument> nodes,
        Dictionary<PinId, List<ConnectionDocument>> incoming,
        Dictionary<PinId, (NodeDocument Node, PinDocument Pin)> pinMap,
        DiagnosticBag diagnostics)
    {
        var visited = new HashSet<NodeId>();
        var stack = new HashSet<NodeId>();

        foreach (var node in nodes)
        {
            if (!visited.Contains(node.Id))
            {
                CheckNodeForCycles(node, visited, stack, incoming, pinMap, diagnostics);
            }
        }
    }

    private static void CheckNodeForCycles(
        NodeDocument current,
        HashSet<NodeId> visited,
        HashSet<NodeId> stack,
        Dictionary<PinId, List<ConnectionDocument>> incoming,
        Dictionary<PinId, (NodeDocument Node, PinDocument Pin)> pinMap,
        DiagnosticBag diagnostics)
    {
        visited.Add(current.Id);
        stack.Add(current.Id);

        // Traverse incoming data connections (dependencies)
        foreach (var inputPin in current.Pins.Where(p => p.Kind == PinKind.Data && p.Direction == PinDirection.Input))
        {
            if (incoming.TryGetValue(inputPin.Id, out var conns))
            {
                foreach (var conn in conns)
                {
                    if (pinMap.TryGetValue(conn.FromPin, out var sourceTuple))
                    {
                        var depNode = sourceTuple.Node;
                        if (stack.Contains(depNode.Id))
                        {
                            diagnostics.ReportError(
                                DiagnosticCodes.InfinitePureDataLoop,
                                $"Infinite data dependency loop detected involving nodes '{current.Name}' and '{depNode.Name}'.",
                                current.Id,
                                inputPin.Id);
                            return;
                        }

                        if (!visited.Contains(depNode.Id))
                        {
                            CheckNodeForCycles(depNode, visited, stack, incoming, pinMap, diagnostics);
                        }
                    }
                }
            }
        }

        stack.Remove(current.Id);
    }

    private sealed class ExpressionLowerer
    {
        private readonly TypeRegistry _typeRegistry;
        private readonly Dictionary<NodeId, NodeDocument> _nodeMap;
        private readonly Dictionary<PinId, (NodeDocument Node, PinDocument Pin)> _pinMap;
        private readonly Dictionary<PinId, List<ConnectionDocument>> _incoming;
        private readonly Dictionary<string, AstVariableDeclaration> _variables;
        private readonly DiagnosticBag _diagnostics;
        private readonly HashSet<NodeId> _dataEvaluationStack = [];

        public ExpressionLowerer(
            TypeRegistry typeRegistry,
            Dictionary<NodeId, NodeDocument> nodeMap,
            Dictionary<PinId, (NodeDocument Node, PinDocument Pin)> pinMap,
            Dictionary<PinId, List<ConnectionDocument>> incoming,
            Dictionary<string, AstVariableDeclaration> variables,
            DiagnosticBag diagnostics)
        {
            _typeRegistry = typeRegistry;
            _nodeMap = nodeMap;
            _pinMap = pinMap;
            _incoming = incoming;
            _variables = variables;
            _diagnostics = diagnostics;
        }

        public AstStatement? LowerStatement(NodeDocument node)
        {
            var nodeType = node.NodeType;

            if (nodeType.Equals("Core.VariableAssign", StringComparison.OrdinalIgnoreCase))
            {
                var varName = node.Properties.GetValueOrDefault("VariableName", string.Empty);
                var valPin = node.FindPin("Value", PinDirection.Input);
                var valExpr = valPin != null ? LowerPinExpression(valPin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);

                if (_variables.TryGetValue(varName, out var decl))
                {
                    return new AstVariableAssignStatement(decl.Id, decl.Name, valExpr, node.Id);
                }

                _diagnostics.ReportError(DiagnosticCodes.UnknownVariable, $"Assignment to unknown variable '{varName}'.", node.Id);
                return null;
            }

            if (nodeType.Equals("Flow.Delay", StringComparison.OrdinalIgnoreCase))
            {
                var durationPin = node.FindPin("Seconds", PinDirection.Input);
                var durationExpr = durationPin != null
                    ? LowerPinExpression(durationPin)
                    : new AstLiteralExpression(1.0f, PrimitiveType.Float32, node.Id);

                return new AstYieldContinuationStatement(ContinuationKind.Delay, [durationExpr], node.Id.Value, node.Id);
            }

            if (nodeType.Equals("Flow.DoAfter", StringComparison.OrdinalIgnoreCase))
            {
                var delayPin = node.FindPin("Delay", PinDirection.Input);
                var delayExpr = delayPin != null
                    ? LowerPinExpression(delayPin)
                    : new AstLiteralExpression(3.0f, PrimitiveType.Float32, node.Id);

                return new AstYieldContinuationStatement(ContinuationKind.DoAfter, [delayExpr], node.Id.Value, node.Id);
            }

            if (nodeType.Equals("Native.Call", StringComparison.OrdinalIgnoreCase))
            {
                var descriptor = node.Properties.GetValueOrDefault("Method", string.Empty);
                var args = new List<AstExpression>();

                foreach (var pin in node.Pins.Where(p => p.Kind == PinKind.Data && p.Direction == PinDirection.Input))
                {
                    args.Add(LowerPinExpression(pin));
                }

                return new AstExpressionStatement(new AstNativeCallExpression(descriptor, args, PrimitiveType.Void, node.Id), node.Id);
            }

            return null;
        }

        public AstExpression LowerPinExpression(PinDocument pin)
        {
            if (_incoming.TryGetValue(pin.Id, out var conns) && conns.Count > 0)
            {
                var sourceConn = conns[0];
                if (_pinMap.TryGetValue(sourceConn.FromPin, out var sourceTuple))
                {
                    return LowerNodeDataOutput(sourceTuple.Node, sourceTuple.Pin);
                }
            }

            // Fallback to literal default value if pin has default
            return ParseLiteralFromDefault(pin);
        }

        private AstExpression LowerNodeDataOutput(NodeDocument node, PinDocument pin)
        {
            if (!_dataEvaluationStack.Add(node.Id))
            {
                _diagnostics.ReportError(DiagnosticCodes.InfinitePureDataLoop, $"Infinite data dependency loop detected at node '{node.Name}'.", node.Id, pin.Id);
                return new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
            }

            try
            {
                var nodeType = node.NodeType;

                // Variable Read
                if (nodeType.Equals("Core.VariableRead", StringComparison.OrdinalIgnoreCase))
                {
                    var varName = node.Properties.GetValueOrDefault("VariableName", string.Empty);
                    if (_variables.TryGetValue(varName, out var decl))
                    {
                        return new AstVariableReadExpression(decl.Id, decl.Name, decl.Type, node.Id);
                    }

                    _diagnostics.ReportError(DiagnosticCodes.UnknownVariable, $"Read of unknown variable '{varName}'.", node.Id);
                    return new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                }

                // Binary math / logic operations
                if (TryGetBinaryOperator(nodeType, out var op))
                {
                    var inA = node.FindPin("A", PinDirection.Input) ?? node.Pins.FirstOrDefault(p => p.Direction == PinDirection.Input);
                    var inB = node.FindPin("B", PinDirection.Input) ?? node.Pins.Skip(1).FirstOrDefault(p => p.Direction == PinDirection.Input);

                    var left = inA != null ? LowerPinExpression(inA) : new AstLiteralExpression(0, PrimitiveType.Int32, node.Id);
                    var right = inB != null ? LowerPinExpression(inB) : new AstLiteralExpression(0, PrimitiveType.Int32, node.Id);

                    var resultType = IsComparisonOperator(op) ? PrimitiveType.Bool : left.Type;
                    return new AstBinaryExpression(op, left, right, resultType, node.Id);
                }

                // Entity GetComponent
                if (nodeType.Equals("Entity.GetComponent", StringComparison.OrdinalIgnoreCase))
                {
                    var entityPin = node.FindPin("Entity", PinDirection.Input);
                    var entityExpr = entityPin != null ? LowerPinExpression(entityPin) : new AstLiteralExpression(null, EntityType.EntityUid, node.Id);
                    var compTypeName = node.Properties.GetValueOrDefault("ComponentType", "Component");
                    _typeRegistry.TryGetType(compTypeName, out var compType);
                    return new AstGetComponentExpression(entityExpr, compType ?? PrimitiveType.Void, node.Id);
                }

                // Entity HasComponent
                if (nodeType.Equals("Entity.HasComponent", StringComparison.OrdinalIgnoreCase))
                {
                    var entityPin = node.FindPin("Entity", PinDirection.Input);
                    var entityExpr = entityPin != null ? LowerPinExpression(entityPin) : new AstLiteralExpression(null, EntityType.EntityUid, node.Id);
                    var compTypeName = node.Properties.GetValueOrDefault("ComponentType", "Component");
                    _typeRegistry.TryGetType(compTypeName, out var compType);
                    return new AstHasComponentExpression(entityExpr, compType ?? PrimitiveType.Void, node.Id);
                }

                // Literal node
                if (nodeType.Equals("Core.Literal", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseLiteralFromProperties(node);
                }

                // Fallback literal
                return ParseLiteralFromDefault(pin);
            }
            finally
            {
                _dataEvaluationStack.Remove(node.Id);
            }
        }

        private AstLiteralExpression ParseLiteralFromDefault(PinDocument pin)
        {
            _typeRegistry.TryGetType(pin.DataType, out var type);
            type ??= PrimitiveType.String;

            if (string.IsNullOrEmpty(pin.DefaultValue))
            {
                return new AstLiteralExpression(null, type, null);
            }

            return ParseLiteralValue(pin.DefaultValue, type);
        }

        private AstLiteralExpression ParseLiteralFromProperties(NodeDocument node)
        {
            var valStr = node.Properties.GetValueOrDefault("Value", string.Empty);
            var typeStr = node.Properties.GetValueOrDefault("Type", "string");
            _typeRegistry.TryGetType(typeStr, out var type);
            type ??= PrimitiveType.String;

            return ParseLiteralValue(valStr, type);
        }

        private static AstLiteralExpression ParseLiteralValue(string text, AstraType type)
        {
            if (type is PrimitiveType prim)
            {
                switch (prim.Kind)
                {
                    case PrimitiveKind.Bool:
                        if (bool.TryParse(text, out var b)) return new AstLiteralExpression(b, prim);
                        break;
                    case PrimitiveKind.Int32:
                        if (int.TryParse(text, CultureInfo.InvariantCulture, out var i32)) return new AstLiteralExpression(i32, prim);
                        break;
                    case PrimitiveKind.Int64:
                        if (long.TryParse(text, CultureInfo.InvariantCulture, out var i64)) return new AstLiteralExpression(i64, prim);
                        break;
                    case PrimitiveKind.Float32:
                        if (float.TryParse(text, CultureInfo.InvariantCulture, out var f32)) return new AstLiteralExpression(f32, prim);
                        break;
                    case PrimitiveKind.Float64:
                        if (double.TryParse(text, CultureInfo.InvariantCulture, out var f64)) return new AstLiteralExpression(f64, prim);
                        break;
                    case PrimitiveKind.String:
                        return new AstLiteralExpression(text, prim);
                }
            }

            return new AstLiteralExpression(text, type);
        }

        private static bool TryGetBinaryOperator(string nodeType, out AstBinaryOperator op)
        {
            switch (nodeType.ToLowerInvariant())
            {
                case "math.add":
                case "add":
                    op = AstBinaryOperator.Add;
                    return true;
                case "math.subtract":
                case "subtract":
                    op = AstBinaryOperator.Subtract;
                    return true;
                case "math.multiply":
                case "multiply":
                    op = AstBinaryOperator.Multiply;
                    return true;
                case "math.divide":
                case "divide":
                    op = AstBinaryOperator.Divide;
                    return true;
                case "cmp.equal":
                case "equal":
                    op = AstBinaryOperator.Equal;
                    return true;
                case "cmp.notequal":
                case "notequal":
                    op = AstBinaryOperator.NotEqual;
                    return true;
                case "cmp.greaterthan":
                case "greaterthan":
                    op = AstBinaryOperator.GreaterThan;
                    return true;
                case "cmp.lessthan":
                case "lessthan":
                    op = AstBinaryOperator.LessThan;
                    return true;
                case "cmp.greaterthanorequal":
                case "greaterthanorequal":
                    op = AstBinaryOperator.GreaterThanOrEqual;
                    return true;
                case "cmp.lessthanorequal":
                case "lessthanorequal":
                    op = AstBinaryOperator.LessThanOrEqual;
                    return true;
                case "logic.and":
                case "and":
                    op = AstBinaryOperator.And;
                    return true;
                case "logic.or":
                case "or":
                    op = AstBinaryOperator.Or;
                    return true;
                default:
                    op = default;
                    return false;
            }
        }

        private static bool IsComparisonOperator(AstBinaryOperator op) =>
            op is AstBinaryOperator.Equal or AstBinaryOperator.NotEqual or
                  AstBinaryOperator.GreaterThan or AstBinaryOperator.GreaterThanOrEqual or
                  AstBinaryOperator.LessThan or AstBinaryOperator.LessThanOrEqual;
    }
}
