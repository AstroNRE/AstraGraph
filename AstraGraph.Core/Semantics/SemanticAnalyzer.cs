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

        // 2. Schemas first, so a variable can be a struct or a list of structs from this graph.
        foreach (var schemaDocument in document.Schemas)
        {
            if (string.IsNullOrWhiteSpace(schemaDocument.Name))
            {
                diagnostics.ReportError(DiagnosticCodes.UnknownSchema, "Component schema is missing a name.");
            }
        }

        var pendingSchemas = document.Schemas.Where(schema => !string.IsNullOrWhiteSpace(schema.Name)).ToList();
        var registrationGuard = pendingSchemas.Count;
        while (pendingSchemas.Count > 0 && registrationGuard-- >= 0)
        {
            var readyIndex = pendingSchemas.FindIndex(schema => CanRegisterSchema(schema, pendingSchemas));
            if (readyIndex < 0)
            {
                break;
            }

            var ready = pendingSchemas[readyIndex];
            pendingSchemas.RemoveAt(readyIndex);
            _typeRegistry.RegisterSchema(SchemaDocuments.ToSchema(ready, _typeRegistry));
        }

        foreach (var schemaDocument in pendingSchemas)
        {
            _typeRegistry.RegisterSchema(SchemaDocuments.ToSchema(schemaDocument, _typeRegistry));
        }

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

        foreach (var schemaDocument in document.Schemas)
        {
            if (string.IsNullOrWhiteSpace(schemaDocument.Name))
            {
                continue;
            }

            foreach (var field in schemaDocument.Fields)
            {
                if (!_typeRegistry.TryGetType(field.TypeName, out _))
                {
                    diagnostics.ReportError(DiagnosticCodes.UnknownType, $"Schema '{schemaDocument.Name}' field '{field.Name}' has unknown type '{field.TypeName}'.");
                }
            }
        }

        // 3. Validate connections
        var incomingConnections = new Dictionary<PinId, List<ConnectionDocument>>();
        var outgoingConnections = new Dictionary<PinId, List<ConnectionDocument>>();

        foreach (var conn in document.Connections)
        {
            if (!pinMap.TryGetValue(conn.FromPin, out var fromTuple))
            {
                diagnostics.ReportError(DiagnosticCodes.InvalidConnection, $"Connection source pin '{conn.FromPin}' does not exist.", conn.FromNode, conn.FromPin, suggestedFix: "remove-connection", relatedNodeId: conn.ToNode);
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
                diagnostics.ReportError(DiagnosticCodes.InvalidConnection, $"Cannot connect {fromPin.Kind} pin to {toPin.Kind} pin.", conn.FromNode, conn.FromPin, suggestedFix: "remove-connection", relatedNodeId: conn.ToNode);
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
                    pinId,
                    suggestedFix: "remove-connection",
                    relatedNodeId: conns[0].FromNode);
            }
        }

        // Validate pure dataflow cycles across the graph
        ValidateDataFlowCycles(document.Nodes, incomingConnections, pinMap, diagnostics);

        // 4. Side & Prediction Policy checks
        foreach (var node in document.Nodes)
        {
            if (document.Side == GraphSide.Client && node.Properties.TryGetValue("Side", out var side) && side.Equals("Server", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.ReportError(DiagnosticCodes.ServerApiCalledOnClient, $"Node '{node.Name}' requires Server side, but graph is configured for Client.", node.Id, suggestedFix: "set-side:Server");
            }

            if (document.Side == GraphSide.SharedPredicted &&
                (node.NodeType.Equals("PersistentId.New", StringComparison.OrdinalIgnoreCase) ||
                 node.NodeType.Equals("Bui.Set", StringComparison.OrdinalIgnoreCase) ||
                 (node.Properties.TryGetValue("IsDeterministic", out var det) && det.Equals("false", StringComparison.OrdinalIgnoreCase))))
            {
                diagnostics.ReportError(DiagnosticCodes.NonDeterministicOperationInPrediction, $"Node '{node.Name}' is non-deterministic and cannot be executed in predicted context.", node.Id, suggestedFix: "set-deterministic");
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
            diagnostics.ReportWarning(DiagnosticCodes.MissingEntryPoint, "System graph has no recognized entry points (e.g. Event.* or System.Update).", suggestedFix: "add-entry");
        }

        foreach (var entryNode in entryNodes)
        {
            var bodyStatements = LowerExecutionBlock(entryNode, expressionContext, outgoingConnections, pinMap);
            var entryName = entryNode.Properties.GetValueOrDefault("EventName", entryNode.Name);
            entryPoints.Add(new AstEntryPointStatement(entryName, [], new AstBlock(bodyStatements), entryNode.Id, TriggerFor(entryNode)));
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
        NodeDocument? currentNode = startNode;
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

            if (IsFor(currentNode.NodeType))
            {
                var startPin = currentNode.FindPin("Start", PinDirection.Input);
                var endPin = currentNode.FindPin("Count", PinDirection.Input) ?? currentNode.FindPin("End", PinDirection.Input);
                var startExpr = startPin != null
                    ? exprLowerer.LowerPinExpression(startPin)
                    : new AstLiteralExpression(0, PrimitiveType.Int32, currentNode.Id);
                var endExpr = endPin != null
                    ? exprLowerer.LowerPinExpression(endPin)
                    : new AstLiteralExpression(0, PrimitiveType.Int32, currentNode.Id);
                var bodyPin = currentNode.FindPin("Body", PinDirection.Output);
                var body = new AstBlock(LowerBranchPath(bodyPin, exprLowerer, outgoing, pinMap));
                statements.Add(new AstForStatement(LoopName(currentNode, "Index"), startExpr, endExpr, body, currentNode.Id));
                currentNode = NextNode(currentNode, "Out", outgoing, pinMap);
                continue;
            }

            if (IsForEach(currentNode.NodeType))
            {
                var collectionPin = currentNode.FindPin("Collection", PinDirection.Input);
                var collectionExpr = collectionPin != null
                    ? exprLowerer.LowerPinExpression(collectionPin)
                    : new AstLiteralExpression(null, PrimitiveType.Void, currentNode.Id);
                var collectionType = collectionPin?.DataType ?? string.Empty;
                if (!string.IsNullOrEmpty(collectionType) &&
                    collectionType.IndexOf("List", StringComparison.OrdinalIgnoreCase) < 0 &&
                    collectionType.IndexOf("IEnumerable", StringComparison.OrdinalIgnoreCase) < 0 &&
                    collectionType.IndexOf("IReadOnlyList", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    exprLowerer.Report(DiagnosticCodes.InvalidCollection, $"Cannot iterate '{collectionType}'.", currentNode.Id, collectionPin?.Id);
                }

                var bodyPin = currentNode.FindPin("Body", PinDirection.Output);
                var body = new AstBlock(LowerBranchPath(bodyPin, exprLowerer, outgoing, pinMap));
                statements.Add(new AstForEachStatement(LoopName(currentNode, "Item"), collectionExpr, body, currentNode.Id));
                currentNode = NextNode(currentNode, "Out", outgoing, pinMap);
                continue;
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

    private static EntryPointTrigger TriggerFor(NodeDocument node)
    {
        var type = node.NodeType;
        if (type.Equals("Event.Start", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("System.Initialize", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Startup", StringComparison.OrdinalIgnoreCase))
        {
            return EntryPointTrigger.Startup;
        }

        if (type.StartsWith("Event.", StringComparison.OrdinalIgnoreCase))
        {
            return EntryPointTrigger.NativeEvent;
        }

        if (type.StartsWith("AstraEvent.", StringComparison.OrdinalIgnoreCase))
        {
            return EntryPointTrigger.AstraEvent;
        }

        if (type.StartsWith("Function.", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Function", StringComparison.OrdinalIgnoreCase))
        {
            return EntryPointTrigger.Function;
        }

        if (type.StartsWith("UI.", StringComparison.OrdinalIgnoreCase))
        {
            return EntryPointTrigger.UIAction;
        }

        return EntryPointTrigger.Update;
    }

    private static bool IsFor(string nodeType) =>
        nodeType.Equals("Flow.For", StringComparison.OrdinalIgnoreCase) ||
        nodeType.Equals("For", StringComparison.OrdinalIgnoreCase);

    private static bool IsForEach(string nodeType) =>
        nodeType.Equals("Flow.ForEach", StringComparison.OrdinalIgnoreCase) ||
        nodeType.Equals("ForEach", StringComparison.OrdinalIgnoreCase);

    private static string LoopName(NodeDocument node, string pinName) =>
        node.Properties.GetValueOrDefault("IndexName", "$" + pinName + ":" + node.Id.Value.ToString("N"));

    private static NodeDocument? NextNode(
        NodeDocument node,
        string pinName,
        Dictionary<PinId, List<ConnectionDocument>> outgoing,
        Dictionary<PinId, (NodeDocument Node, PinDocument Pin)> pinMap)
    {
        var pin = node.FindPin(pinName, PinDirection.Output);
        if (pin != null && outgoing.TryGetValue(pin.Id, out var connections) && connections.Count > 0 &&
            pinMap.TryGetValue(connections[0].ToPin, out var target))
        {
            return target.Node;
        }

        return null;
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

    private bool CanRegisterSchema(ComponentSchemaDocument schema, List<ComponentSchemaDocument> pending)
    {
        foreach (var field in schema.Fields)
        {
            if (_typeRegistry.TryGetType(field.TypeName, out _))
            {
                continue;
            }

            var waiting = pending.Exists(other =>
                !ReferenceEquals(other, schema) &&
                string.Equals(other.Name, field.TypeName, StringComparison.Ordinal));
            if (waiting)
            {
                return false;
            }
        }

        return true;
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

        public void Report(string code, string message, NodeId nodeId, PinId? pinId = null) =>
            _diagnostics.ReportError(code, message, nodeId, pinId);

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

            if (IsMutatingContainer(nodeType) || IsBuiMutation(nodeType))
            {
                return new AstVariableAssignStatement(SymbolId.Empty, ResultName(node), LowerCall(node), node.Id);
            }

            if (nodeType.Equals("Native.Call", StringComparison.OrdinalIgnoreCase) ||
                nodeType.Equals("Graph.Call", StringComparison.OrdinalIgnoreCase))
            {
                return new AstExpressionStatement(LowerCall(node), node.Id);
            }

            if (nodeType.Equals("Schema.SetField", StringComparison.OrdinalIgnoreCase))
            {
                var targetPin = node.FindPin("Target", PinDirection.Input);
                var valuePin = node.FindPin("Value", PinDirection.Input);
                var target = targetPin != null ? LowerPinExpression(targetPin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                var value = valuePin != null ? LowerPinExpression(valuePin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                var fieldName = node.Properties.GetValueOrDefault("Field", string.Empty);
                var schemaName = node.Properties.GetValueOrDefault("Schema", string.Empty);
                ReportMissingField(node, schemaName, fieldName);
                return new AstVariableAssignStatement(
                    SymbolId.Empty,
                    ResultName(node),
                    new AstSetFieldExpression(target, FieldKey(schemaName, fieldName), value, target.Type, node.Id),
                    node.Id);
            }

            if (nodeType.Equals("List.Add", StringComparison.OrdinalIgnoreCase))
            {
                return CollectionMutation(node, static (collection, index, item, type, id) => new AstCollectionAddExpression(collection, item, type, id));
            }

            if (nodeType.Equals("List.Set", StringComparison.OrdinalIgnoreCase))
            {
                return CollectionMutation(node, static (collection, index, item, type, id) => new AstCollectionSetExpression(collection, index, item, type, id));
            }

            if (nodeType.Equals("List.Remove", StringComparison.OrdinalIgnoreCase))
            {
                return CollectionMutation(node, static (collection, index, item, type, id) => new AstCollectionRemoveExpression(collection, index, type, id));
            }

            if (nodeType.Equals("PersistentId.New", StringComparison.OrdinalIgnoreCase))
            {
                _typeRegistry.TryGetType(PersistentIdType.Instance.TypeName, out var idType);
                return new AstVariableAssignStatement(
                    SymbolId.Empty,
                    ResultName(node),
                    new AstPersistentIdExpression(idType ?? PersistentIdType.Instance, node.Id),
                    node.Id);
            }

            return null;
        }

        private AstVariableAssignStatement CollectionMutation(
            NodeDocument node,
            Func<AstExpression, AstExpression, AstExpression, AstraType, NodeId, AstExpression> create)
        {
            var collectionPin = node.FindPin("List", PinDirection.Input);
            var indexPin = node.FindPin("Index", PinDirection.Input);
            var itemPin = node.FindPin("Item", PinDirection.Input);
            var collection = collectionPin != null ? LowerPinExpression(collectionPin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
            var index = indexPin != null ? LowerPinExpression(indexPin) : new AstLiteralExpression(0, PrimitiveType.Int32, node.Id);
            var item = itemPin != null ? LowerPinExpression(itemPin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
            _typeRegistry.TryGetType(collectionPin?.DataType ?? string.Empty, out var collectionType);
            return new AstVariableAssignStatement(
                SymbolId.Empty,
                ResultName(node),
                create(collection, index, item, collectionType ?? collection.Type, node.Id),
                node.Id);
        }

        private void ReportMissingField(NodeDocument node, string schemaName, string fieldName, PinId? pinId = null)
        {
            if (string.IsNullOrEmpty(schemaName) || string.IsNullOrEmpty(fieldName))
            {
                return;
            }

            if (_typeRegistry.TryGetType(schemaName, out var schemaType) && schemaType is SchemaType schema && schema.FindField(fieldName) == null)
            {
                _diagnostics.ReportError(DiagnosticCodes.UnknownField, $"Schema '{schemaName}' has no field '{fieldName}'.", node.Id, pinId);
            }
        }

        private string FieldKey(string schemaName, string fieldName)
        {
            if (_typeRegistry.TryGetType(schemaName, out var schemaType) && schemaType is SchemaType schema)
            {
                var field = schema.FindField(fieldName);
                if (field != null && field.Id.Value != Guid.Empty)
                {
                    return $"{field.Id.Value:D}|{fieldName}";
                }
            }

            return fieldName;
        }

        private string SchemaKey(string schemaName)
        {
            if (_typeRegistry.TryGetType(schemaName, out var schemaType) && schemaType is SchemaType schema && schema.Id.Value != Guid.Empty)
            {
                return $"{schema.Id.Value:D}|{schemaName}";
            }

            return schemaName;
        }

        private static string ResultName(NodeDocument node) => "$result:" + node.Id.Value.ToString("N");

        private static bool IsStoredMutation(string nodeType) =>
            nodeType.Equals("Schema.SetField", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("List.Add", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("List.Set", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("List.Remove", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("PersistentId.New", StringComparison.OrdinalIgnoreCase) ||
            IsMutatingContainer(nodeType) ||
            IsBuiMutation(nodeType);

        private static bool IsMutatingContainer(string nodeType) =>
            nodeType.Equals("Container.Insert", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("Container.Remove", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("Inventory.TryInsert", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("Inventory.TryRemove", StringComparison.OrdinalIgnoreCase);

        private static bool IsContainerQuery(string nodeType) =>
            nodeType.Equals("Container.Has", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("Container.Contents", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("Inventory.Find", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("Inventory.Contains", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("Entity.GetHeldItem", StringComparison.OrdinalIgnoreCase);

        private static bool IsBuiMutation(string nodeType) =>
            nodeType.Equals("Bui.Set", StringComparison.OrdinalIgnoreCase);

        private static bool IsBuiQuery(string nodeType) =>
            nodeType.Equals("Ui.Rows", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("Bui.Field", StringComparison.OrdinalIgnoreCase);

        private static bool IsGameplayContainer(string nodeType) =>
            IsMutatingContainer(nodeType) || IsContainerQuery(nodeType);

        private static bool IsDirectNative(string nodeType) =>
            IsGameplayContainer(nodeType) || IsBuiMutation(nodeType) || IsBuiQuery(nodeType);

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

                if (nodeType.Equals("Native.Call", StringComparison.OrdinalIgnoreCase) ||
                    nodeType.Equals("Graph.Call", StringComparison.OrdinalIgnoreCase) ||
                    IsContainerQuery(nodeType) ||
                    IsBuiQuery(nodeType))
                {
                    return LowerCall(node, pin);
                }

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
                if (nodeType.Equals("Entity.GetComponent", StringComparison.OrdinalIgnoreCase) ||
                    nodeType.Equals("Entity.TryGetComponent", StringComparison.OrdinalIgnoreCase))
                {
                    var entityPin = node.FindPin("Entity", PinDirection.Input);
                    var entityExpr = entityPin != null ? LowerPinExpression(entityPin) : new AstLiteralExpression(null, EntityType.EntityUid, node.Id);
                    var compTypeName = node.Properties.GetValueOrDefault("ComponentType", "Component");
                    var compType = ResolveComponent(node, compTypeName);
                    if (pin.Name.Equals("Found", StringComparison.OrdinalIgnoreCase))
                    {
                        return new AstHasComponentExpression(entityExpr, compType, node.Id);
                    }

                    return new AstGetComponentExpression(entityExpr, compType, node.Id);
                }

                if (nodeType.Equals("Entity.HasComponent", StringComparison.OrdinalIgnoreCase))
                {
                    var entityPin = node.FindPin("Entity", PinDirection.Input);
                    var entityExpr = entityPin != null ? LowerPinExpression(entityPin) : new AstLiteralExpression(null, EntityType.EntityUid, node.Id);
                    var compTypeName = node.Properties.GetValueOrDefault("ComponentType", "Component");
                    return new AstHasComponentExpression(entityExpr, ResolveComponent(node, compTypeName), node.Id);
                }

                if (nodeType.StartsWith("Event.", StringComparison.OrdinalIgnoreCase))
                {
                    var slot = pin.Name.ToLowerInvariant() switch
                    {
                        "entity" => 0,
                        "component" => 1,
                        _ => 2
                    };
                    var type = slot == 0 ? (AstraType)EntityType.EntityUid : PrimitiveType.String;
                    if (slot != 0)
                    {
                        _typeRegistry.TryGetType(pin.DataType, out var pinType);
                        type = pinType ?? PrimitiveType.String;
                    }

                    return new AstEventContextExpression(slot, type, node.Id);
                }

                if (nodeType.Equals("Native.GetMember", StringComparison.OrdinalIgnoreCase))
                {
                    var targetPin = node.FindPin("Target", PinDirection.Input);
                    var target = targetPin != null ? LowerPinExpression(targetPin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                    var member = node.Properties.GetValueOrDefault("Member", pin.Name);
                    if (string.IsNullOrEmpty(member))
                    {
                        _diagnostics.ReportError(DiagnosticCodes.InvalidMember, $"Node '{node.Name}' does not name a member.", node.Id, pin.Id);
                    }

                    _typeRegistry.TryGetType(pin.DataType, out var memberType);
                    return new AstMemberReadExpression(target, member, memberType ?? PrimitiveType.String, node.Id);
                }

                if (nodeType.Equals("Schema.GetField", StringComparison.OrdinalIgnoreCase))
                {
                    var componentPin = node.FindPin("Component", PinDirection.Input) ?? node.FindPin("Target", PinDirection.Input);
                    var component = componentPin != null ? LowerPinExpression(componentPin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                    var fieldName = node.Properties.GetValueOrDefault("Field", pin.Name);
                    var schemaName = node.Properties.GetValueOrDefault("Schema", string.Empty);
                    ReportMissingField(node, schemaName, fieldName, pin.Id);
                    _typeRegistry.TryGetType(pin.DataType, out var fieldType);
                    return new AstFieldReadExpression(component, FieldKey(schemaName, fieldName), fieldType ?? PrimitiveType.Int32, node.Id);
                }

                if (nodeType.Equals("Schema.Make", StringComparison.OrdinalIgnoreCase))
                {
                    var schemaName = node.Properties.GetValueOrDefault("Schema", string.Empty);
                    if (!string.IsNullOrEmpty(schemaName) && !_typeRegistry.TryGetType(schemaName, out _))
                    {
                        _diagnostics.ReportError(DiagnosticCodes.UnknownSchema, $"Schema '{schemaName}' is not declared.", node.Id, pin.Id);
                    }

                    _typeRegistry.TryGetType(pin.DataType, out var structType);
                    return new AstStructMakeExpression(SchemaKey(schemaName), structType ?? PrimitiveType.String, node.Id);
                }

                if (nodeType.Equals("Schema.Copy", StringComparison.OrdinalIgnoreCase))
                {
                    var sourcePin = node.FindPin("Value", PinDirection.Input);
                    var source = sourcePin != null ? LowerPinExpression(sourcePin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                    _typeRegistry.TryGetType(pin.DataType, out var copyType);
                    return new AstStructCopyExpression(source, copyType ?? source.Type, node.Id);
                }

                if (nodeType.Equals("List.Create", StringComparison.OrdinalIgnoreCase))
                {
                    _typeRegistry.TryGetType(pin.DataType, out var listType);
                    return new AstListMakeExpression(listType ?? PrimitiveType.String, node.Id);
                }

                if (nodeType.Equals("List.Get", StringComparison.OrdinalIgnoreCase))
                {
                    var collectionPin = node.FindPin("List", PinDirection.Input);
                    var indexPin = node.FindPin("Index", PinDirection.Input);
                    var collection = collectionPin != null ? LowerPinExpression(collectionPin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                    var index = indexPin != null ? LowerPinExpression(indexPin) : new AstLiteralExpression(0, PrimitiveType.Int32, node.Id);
                    _typeRegistry.TryGetType(pin.DataType, out var elementType);
                    return new AstCollectionGetExpression(collection, index, elementType ?? PrimitiveType.String, node.Id);
                }

                if (nodeType.Equals("List.Count", StringComparison.OrdinalIgnoreCase))
                {
                    var collectionPin = node.FindPin("List", PinDirection.Input);
                    var collection = collectionPin != null ? LowerPinExpression(collectionPin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                    return new AstCollectionLengthExpression(collection, node.Id);
                }

                if (IsStoredMutation(nodeType) && pin.Direction == PinDirection.Output)
                {
                    _typeRegistry.TryGetType(pin.DataType, out var resultType);
                    return new AstVariableReadExpression(SymbolId.Empty, ResultName(node), resultType ?? PrimitiveType.String, node.Id);
                }

                if (nodeType.Equals("Nullable.HasValue", StringComparison.OrdinalIgnoreCase))
                {
                    var valuePin = node.FindPin("Value", PinDirection.Input);
                    var value = valuePin != null ? LowerPinExpression(valuePin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                    return new AstHasValueExpression(value, node.Id);
                }

                if (nodeType.Equals("Nullable.GetValue", StringComparison.OrdinalIgnoreCase))
                {
                    var valuePin = node.FindPin("Value", PinDirection.Input);
                    var value = valuePin != null ? LowerPinExpression(valuePin) : new AstLiteralExpression(null, PrimitiveType.Void, node.Id);
                    if (valuePin != null && !string.IsNullOrEmpty(valuePin.DataType) && !valuePin.DataType.EndsWith('?'))
                    {
                        _diagnostics.ReportError(DiagnosticCodes.UnsafeNullableDereference, $"Get Value on non-nullable '{valuePin.DataType}'.", node.Id, pin.Id);
                    }

                    return value;
                }

                if ((IsForNode(nodeType) && pin.Name.Equals("Index", StringComparison.OrdinalIgnoreCase)) ||
                    (IsForEachNode(nodeType) && (pin.Name.Equals("Item", StringComparison.OrdinalIgnoreCase) || pin.Name.Equals("Current", StringComparison.OrdinalIgnoreCase))))
                {
                    var prefix = IsForNode(nodeType) ? "Index" : "Item";
                    var name = "$" + prefix + ":" + node.Id.Value.ToString("N");
                    return new AstVariableReadExpression(SymbolId.Empty, name, pin.Name.Equals("Index", StringComparison.OrdinalIgnoreCase) ? PrimitiveType.Int32 : EntityType.EntityUid, node.Id);
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

        private AstNativeCallExpression LowerCall(NodeDocument node, PinDocument? resultPin = null)
        {
            var nodeType = node.NodeType;
            var descriptor = nodeType.Equals("Graph.Call", StringComparison.OrdinalIgnoreCase)
                ? "Graph.Call"
                : IsDirectNative(nodeType)
                    ? nodeType
                    : node.Properties.GetValueOrDefault("Method", string.Empty);
            var args = new List<AstExpression>();
            if (descriptor == "Graph.Call")
            {
                args.Add(new AstLiteralExpression(node.Properties.GetValueOrDefault("Function", string.Empty), PrimitiveType.String, node.Id));
            }

            foreach (var pin in node.Pins.Where(p => p.Kind == PinKind.Data && p.Direction == PinDirection.Input))
            {
                args.Add(LowerPinExpression(pin));
            }

            if (string.IsNullOrEmpty(descriptor))
            {
                _diagnostics.ReportError(DiagnosticCodes.UnavailableBinding, $"Node '{node.Name}' does not name a binding.", node.Id);
            }

            _typeRegistry.TryGetType(resultPin?.DataType ?? string.Empty, out var returnType);
            return new AstNativeCallExpression(descriptor, args, returnType ?? PrimitiveType.Void, node.Id);
        }

        private AstraType ResolveComponent(NodeDocument node, string compTypeName)
        {
            if (_typeRegistry.TryGetType(compTypeName, out var compType) && compType is not null)
            {
                return compType;
            }

            if (!compTypeName.Equals("Component", StringComparison.Ordinal))
            {
                _diagnostics.ReportError(DiagnosticCodes.UnknownSchema, $"Unknown schema or component '{compTypeName}'.", node.Id);
            }

            return PrimitiveType.Void;
        }

        private static bool IsForNode(string nodeType) =>
            nodeType.Equals("Flow.For", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("For", StringComparison.OrdinalIgnoreCase);

        private static bool IsForEachNode(string nodeType) =>
            nodeType.Equals("Flow.ForEach", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("ForEach", StringComparison.OrdinalIgnoreCase);

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
