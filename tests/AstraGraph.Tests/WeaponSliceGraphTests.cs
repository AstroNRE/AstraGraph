using AstraGraph.Core;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class WeaponSliceGraphTests
{
    [Test]
    public void WeaponProfile_Analyzes()
    {
        var path = Path.Combine(NightCity(), "Resources", "AstraGraph", "Systems", "WeaponProfile.agraph");
        if (!File.Exists(path))
        {
            Assert.Ignore("Night City checkout is not beside this library.");
        }

        var document = GraphSerializer.Deserialize(File.ReadAllText(path));
        var analyzed = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
    }

    [Test]
    public void WeaponBench_AnalyzesAndIsStoredLeftToRight()
    {
        var document = WeaponBench();
        var analyzed = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());

        var root = NightCity();
        if (!Directory.Exists(root))
        {
            return;
        }

        var path = Path.Combine(root, "Resources", "AstraGraph", "Systems", "WeaponBench.agraph");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, GraphSerializer.Serialize(document));
    }

    private static GraphDocument WeaponBench()
    {
        var graph = new BenchGraph();
        var open = graph.Event("Open", "BoundUIOpenedEvent", "AstraHtmlUiComponent", 40, 640);
        var message = graph.Event("Message", "AstraBuiUiMessage", "AstraHtmlUiComponent", 40, 80);
        var action = graph.Member("Action", "Action", "string", 360);
        var isInstall = graph.Equal("IsInstall", "InstallPart", 360);
        var installGate = graph.Branch("Install?", 680);
        var partId = graph.Call("Bui.Field", "PartId", 960, ("Payload", "string", null), ("Field", "string", "id"), ("Value", "string", null));
        var part = graph.Call("Inventory.Find", "Part", 960, ("Owner", "EntityUid", null), ("Prototype", "string", null), ("Item", "int64", null));
        var hasPart = graph.NotZero("HasPart", 1240);
        var partGate = graph.Branch("Part?", 1240);
        var gun = graph.Call("Entity.GetHeldItem", "Gun", 1520, ("Holder", "EntityUid", null), ("Container", "string", "gun"), ("Item", "int64", null));
        var hasGun = graph.NotZero("HasGun", 1800);
        var gunGate = graph.Branch("Gun?", 1800);
        var partComp = graph.Component("PartData", "WeaponPart", 2080);
        var partDataGate = graph.Branch("PartData?", 2080);
        var slot = graph.Field("Slot", "WeaponPart", "Slot", "string", 2360);
        var barrel = graph.Equal("IsBarrel", "barrel", 2360);
        var slotGate = graph.Branch("Barrel?", 2640);
        var assembly = graph.Component("Assembly", "WeaponAssembly", 2920);
        var assemblyGate = graph.Branch("Assembly?", 2920);
        var prototype = graph.Field("Prototype", "WeaponPart", "Prototype", "string", 3200);
        var rate = graph.Field("Rate", "WeaponPart", "FireRate", "float32", 3480);
        var speed = graph.Field("Speed", "WeaponPart", "ProjectileSpeed", "float32", 3760);
        var setBarrel = graph.Set("SetBarrel", "WeaponAssembly", "Barrel", 3200);
        var setRate = graph.Set("SetRate", "WeaponAssembly", "FireRate", 3480);
        var setSpeed = graph.Set("SetSpeed", "WeaponAssembly", "ProjectileSpeed", 3760);
        var insert = graph.Call("Container.Insert", "Insert", 4040, ("Owner", "EntityUid", null), ("Container", "string", "barrel"), ("Item", "EntityUid", null), ("Success", "bool", null));
        var created = graph.Data("List.Create", "Empty", 4320, ("List", "object", null));
        var clear = graph.Assign("Clear", "Rows", 4320);
        var contents = graph.Call("Container.Contents", "Contents", 4600, ("Owner", "EntityUid", null), ("Container", "string", "parts"), ("Contents", "List<EntityUid>", null));
        var each = graph.ForEach("EachPart", 4600);
        var rowComp = graph.Component("RowData", "WeaponPart", 4880);
        var rowGate = graph.Branch("Row?", 4880);
        var label = graph.Field("Label", "WeaponPart", "Label", "string", 5160);
        var rowProto = graph.Field("RowPrototype", "WeaponPart", "Prototype", "string", 5440);
        var made = graph.Data("Schema.Make", "Row", 5160, ("Value", "object", null));
        made.Properties["Schema"] = "PartRow";
        var setId = graph.Set("SetId", "PartRow", "Id", 5440);
        var setText = graph.Set("SetText", "PartRow", "Text", 5720);
        var setOff = graph.Set("SetOff", "PartRow", "Disabled", 6000);
        setOff.Pins.First(pin => pin.Name == "Value").DefaultValue = "false";
        var readRows = graph.Read("ReadRows", "Rows", 6000);
        var add = graph.Call("List.Add", "Add", 6280, ("List", "object", null), ("Item", "object", null), ("ListOut", "object", null));
        var store = graph.Assign("Store", "Rows", 6280);
        var readBuilt = graph.Read("ReadBuilt", "Rows", 6560);
        var rows = graph.Call("Ui.Rows", "RowsJson", 6560, ("List", "object", null), ("IdField", "string", "Id"), ("TextField", "string", "Text"), ("DisabledField", "string", "Disabled"), ("Rows", "string", null));
        var publish = graph.Call("Bui.Set", "Publish", 6840, ("Owner", "EntityUid", null), ("Name", "string", "Parts"), ("Value", "string", null), ("Success", "bool", null));

        graph.Exec(message, installGate);
        graph.Exec(installGate, "True", partGate);
        graph.Exec(partGate, "True", gunGate);
        graph.Exec(gunGate, "True", partDataGate);
        graph.Exec(partDataGate, "True", slotGate);
        graph.Exec(slotGate, "True", assemblyGate);
        graph.Exec(assemblyGate, "True", setBarrel);
        graph.Exec(setBarrel, setRate);
        graph.Exec(setRate, setSpeed);
        graph.Exec(setSpeed, insert);
        graph.Exec(insert, clear);
        graph.Exec(installGate, "False", clear);
        graph.Exec(partGate, "False", clear);
        graph.Exec(gunGate, "False", clear);
        graph.Exec(partDataGate, "False", clear);
        graph.Exec(slotGate, "False", clear);
        graph.Exec(assemblyGate, "False", clear);
        graph.Exec(open, clear);
        graph.Exec(clear, each);
        graph.Exec(each, "Body", rowGate);
        graph.Exec(rowGate, "True", setId);
        graph.Exec(setId, setText);
        graph.Exec(setText, setOff);
        graph.Exec(setOff, add);
        graph.Exec(add, store);
        graph.Exec(each, "Out", publish);

        graph.DataWire(message, "Event", action, "Target");
        graph.DataWire(action, "Value", isInstall, "A");
        graph.DataWire(isInstall, "Result", installGate, "Condition");
        graph.DataWire(message, "Event", partId, "Payload");
        graph.DataWire(message, "Entity", part, "Owner");
        graph.DataWire(partId, "Value", part, "Prototype");
        graph.DataWire(part, "Item", hasPart, "A");
        graph.DataWire(hasPart, "Result", partGate, "Condition");
        graph.DataWire(message, "Entity", gun, "Holder");
        graph.DataWire(gun, "Item", hasGun, "A");
        graph.DataWire(hasGun, "Result", gunGate, "Condition");
        graph.DataWire(part, "Item", partComp, "Entity");
        graph.DataWire(partComp, "Found", partDataGate, "Condition");
        graph.DataWire(partComp, "Component", slot, "Component");
        graph.DataWire(slot, "Value", barrel, "A");
        graph.DataWire(barrel, "Result", slotGate, "Condition");
        graph.DataWire(gun, "Item", assembly, "Entity");
        graph.DataWire(assembly, "Found", assemblyGate, "Condition");
        graph.DataWire(partComp, "Component", prototype, "Component");
        graph.DataWire(partComp, "Component", rate, "Component");
        graph.DataWire(partComp, "Component", speed, "Component");
        graph.DataWire(assembly, "Component", setBarrel, "Target");
        graph.DataWire(prototype, "Value", setBarrel, "Value");
        graph.DataWire(setBarrel, "Result", setRate, "Target");
        graph.DataWire(rate, "Value", setRate, "Value");
        graph.DataWire(setRate, "Result", setSpeed, "Target");
        graph.DataWire(speed, "Value", setSpeed, "Value");
        graph.DataWire(gun, "Item", insert, "Owner");
        graph.DataWire(part, "Item", insert, "Item");
        graph.DataWire(created, "List", clear, "Value");
        graph.DataWire(open, "Entity", contents, "Owner");
        graph.DataWire(contents, "Contents", each, "Collection");
        graph.DataWire(each, "Current", rowComp, "Entity");
        graph.DataWire(rowComp, "Found", rowGate, "Condition");
        graph.DataWire(rowComp, "Component", label, "Component");
        graph.DataWire(rowComp, "Component", rowProto, "Component");
        graph.DataWire(made, "Value", setId, "Target");
        graph.DataWire(rowProto, "Value", setId, "Value");
        graph.DataWire(setId, "Result", setText, "Target");
        graph.DataWire(label, "Value", setText, "Value");
        graph.DataWire(setText, "Result", setOff, "Target");
        graph.DataWire(readRows, "Value", add, "List");
        graph.DataWire(setOff, "Result", add, "Item");
        graph.DataWire(add, "List", store, "Value");
        graph.DataWire(readBuilt, "Value", rows, "List");
        graph.DataWire(rows, "Rows", publish, "Value");
        graph.DataWire(open, "Entity", publish, "Owner");

        return graph.Document();
    }

    private static string NightCity()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "..", "night-city");
            if (Directory.Exists(Path.Combine(candidate, "RobustToolbox")))
            {
                return Path.GetFullPath(candidate);
            }

            if (Directory.Exists(Path.Combine(dir.FullName, "RobustToolbox")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Resources")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return @"D:\projects\night-city";
    }

    private sealed class BenchGraph
    {
        private readonly List<NodeDocument> _nodes = [];
        private readonly List<ConnectionDocument> _connections = [];
        private int _row;

        public NodeDocument Event(string name, string eventType, string component, int x) =>
            Place(Node(name, "Event." + name, new Dictionary<string, string> { ["eventType"] = eventType, ["componentType"] = component },
                Exec("Out", false), Data("Entity", "EntityUid", false), Data("Event", "object", false)), x, 80);

        public NodeDocument Branch(string name, int x) =>
            Place(Node(name, "Core.Branch", null, Exec("In", true), Exec("True", false), Exec("False", false), Data("Condition", "bool", true)), x, 80);

        public NodeDocument Assign(string name, string variable, int x) =>
            Place(Node(name, "Core.VariableAssign", new Dictionary<string, string> { ["VariableName"] = variable },
                Exec("In", true), Exec("Out", false), Data("Value", "object", true)), x, 80);

        public NodeDocument Read(string name, string variable, int x) =>
            Place(Node(name, "Core.VariableRead", new Dictionary<string, string> { ["VariableName"] = variable },
                Data("Value", "object", false)), x, 300);

        public NodeDocument ForEach(string name, int x) =>
            Place(Node(name, "Flow.ForEach", null, Exec("In", true), Exec("Out", false), Exec("Body", false), Data("Collection", "List<EntityUid>", true), Data("Current", "EntityUid", false)), x, 80);

        public NodeDocument Component(string name, string type, int x) =>
            Place(Node(name, "Entity.TryGetComponent", new Dictionary<string, string> { ["ComponentType"] = type },
                Data("Entity", "EntityUid", true), Data("Found", "bool", false), Data("Component", "component", false)), x, 300);

        public NodeDocument Field(string name, string schema, string field, string type, int x) =>
            Place(Node(name, "Schema.GetField", new Dictionary<string, string> { ["Schema"] = schema, ["Field"] = field },
                Data("Component", "component", true), Data("Value", type, false)), x, 300);

        public NodeDocument Set(string name, string schema, string field, int x) =>
            Place(Node(name, "Schema.SetField", new Dictionary<string, string> { ["Schema"] = schema, ["Field"] = field },
                Exec("In", true), Exec("Out", false), Data("Target", "object", true), Data("Value", "object", true), Data("Result", "object", false)), x, 80);

        public NodeDocument Member(string name, string member, string type, int x) =>
            Place(Node(name, "Native.GetMember", new Dictionary<string, string> { ["Member"] = member },
                Data("Target", "object", true), Data("Value", type, false)), x, 300);

        public NodeDocument Equal(string name, string literal, int x)
        {
            var node = Node(name, "cmp.equal", null, Data("A", "string", true), Data("B", "string", true, literal), Data("Result", "bool", false));
            return Place(node, x, 300);
        }

        public NodeDocument NotZero(string name, int x)
        {
            var node = Node(name, "cmp.notequal", null, Data("A", "int64", true), Data("B", "int64", true, "0"), Data("Result", "bool", false));
            return Place(node, x, 300);
        }

        public NodeDocument Data(string type, string name, int x, params (string Name, string Type, string? Default)[] pins)
        {
            var specs = pins.Select(pin => pin.Default == null && pins.Count(item => item.Name == pin.Name) >= 0
                ? Data(pin.Name, pin.Type, pin.Default != null, pin.Default)
                : Data(pin.Name, pin.Type, false)).ToArray();
            return Place(Node(name, type, null, specs), x, 300);
        }

        public NodeDocument Call(string type, string name, int x, params (string Name, string Type, string? Default)[] pins)
        {
            var specs = new List<PinDocument>();
            if (type is "Container.Insert" or "Bui.Set" or "List.Add")
            {
                specs.Add(Exec("In", true));
                specs.Add(Exec("Out", false));
            }

            foreach (var pin in pins)
            {
                var output = pin.Name is "Item" or "Contents" or "Value" or "Rows" or "Success" or "ListOut";
                if (pin.Name == "List" && type == "List.Add")
                {
                    output = false;
                }

                if (pin.Name == "ListOut")
                {
                    specs.Add(new PinDocument { Name = "List", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = pin.Type });
                    continue;
                }

                specs.Add(output
                    ? Data(pin.Name, pin.Type, false)
                    : Data(pin.Name, pin.Type, true, pin.Default));
            }

            return Place(Node(name, type, null, [.. specs]), x, type.StartsWith("Bui") || type.StartsWith("Container") || type.StartsWith("List") ? 80 : 300);
        }

        public void Exec(NodeDocument from, NodeDocument to) => Exec(from, from.Pins.First(pin => pin.Kind == PinKind.Execution && pin.Direction == PinDirection.Output).Name, to);

        public void Exec(NodeDocument from, string fromPin, NodeDocument to) =>
            Wire(from, fromPin, to, "In");

        public void DataWire(NodeDocument from, string fromPin, NodeDocument to, string toPin) =>
            Wire(from, fromPin, to, toPin);

        public GraphDocument Document() => new()
        {
            Id = GraphId.Parse("c1000000-0000-4000-8000-0000000000c1"),
            Name = "WeaponBench",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Metadata = new GraphMetadata { Description = "Bench lists parts and installs a barrel the server accepts. The client only sends the part id." },
            Variables = [new GraphVariableDocument { Name = "Rows", TypeName = "object" }],
            Nodes = _nodes,
            Connections = _connections,
            EditorLayout = new EditorLayoutDocument
            {
                NodePositions = _nodes.ToDictionary(node => node.Id.Value.ToString(), node => new NodePosition(node.PositionX, node.PositionY))
            },
            Schemas =
            [
                Schema("11aa11aa-11aa-41aa-81aa-11aa11aa11aa", "WeaponAssembly", true,
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11ab", "Barrel", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11ac", "FireRate", "float32", "6"),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11ad", "ProjectileSpeed", "float32", "40")),
                Schema("22bb22bb-22bb-42bb-82bb-22bb22bb22bb", "WeaponPart", true,
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b1", "Prototype", "string", ""),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b2", "Slot", "string", "barrel"),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b3", "Label", "string", ""),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b4", "FireRate", "float32", "6"),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b5", "ProjectileSpeed", "float32", "40")),
                Schema("33cc33cc-33cc-43cc-83cc-33cc33cc33cc", "PartRow", false,
                    ("33cc33cc-33cc-43cc-83cc-33cc33cc33c1", "Id", "string", ""),
                    ("33cc33cc-33cc-43cc-83cc-33cc33cc33c2", "Text", "string", ""),
                    ("33cc33cc-33cc-43cc-83cc-33cc33cc33c3", "Disabled", "bool", "false"))
            ]
        };

        private NodeDocument Place(NodeDocument node, int x, int y)
        {
            node.PositionX = x;
            node.PositionY = y + (_row++ % 2 == 0 ? 0 : 0);
            return node;
        }

        private void Wire(NodeDocument from, string fromPin, NodeDocument to, string toPin)
        {
            _connections.Add(new ConnectionDocument
            {
                FromNode = from.Id,
                FromPin = from.FindPin(fromPin, PinDirection.Output)!.Id,
                ToNode = to.Id,
                ToPin = to.FindPin(toPin, PinDirection.Input)!.Id
            });
        }

        private NodeDocument Node(string name, string type, Dictionary<string, string>? properties, params PinDocument[] pins)
        {
            var node = new NodeDocument
            {
                Name = name,
                NodeType = type,
                Properties = properties ?? [],
                Pins = pins.ToList()
            };
            _nodes.Add(node);
            return node;
        }

        private static PinDocument Exec(string name, bool input) => new()
        {
            Name = name,
            Direction = input ? PinDirection.Input : PinDirection.Output,
            Kind = PinKind.Execution
        };

        private static PinDocument Data(string name, string type, bool input, string? value = null) => new()
        {
            Name = name,
            Direction = input ? PinDirection.Input : PinDirection.Output,
            Kind = PinKind.Data,
            DataType = type,
            DefaultValue = value
        };

        private static ComponentSchemaDocument Schema(string id, string name, bool component, params (string Id, string Name, string Type, string Default)[] fields) => new()
        {
            Id = SchemaId.Parse(id),
            Name = name,
            Kind = component ? "Component" : "Struct",
            Fields = fields.Select(field => new ComponentFieldDocument
            {
                Id = FieldId.Parse(field.Id),
                Name = field.Name,
                TypeName = field.Type,
                DefaultValue = field.Default
            }).ToList()
        };
    }
}
