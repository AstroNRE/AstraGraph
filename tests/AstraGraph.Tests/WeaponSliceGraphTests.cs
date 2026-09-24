using AstraGraph.Core;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class WeaponSliceGraphTests
{
    [Test]
    public void WeaponProfile_Analyzes()
    {
        var path = Path.Combine(NightCity(), "Resources", "AstraGraph", "Systems", "Weapons", "Modular", "WeaponProfile.agraph");
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

        var path = Path.Combine(root, "Resources", "AstraGraph", "Systems", "Weapons", "Modular", "WeaponBench.agraph");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, GraphSerializer.Serialize(document));
    }

    private static GraphDocument WeaponBench()
    {
        var graph = new BenchGraph();
        var open = graph.Event("Open", "BoundUIOpenedEvent", "AstraHtmlUiComponent", 40, 640);
        var message = graph.Event("Message", "AstraBuiUiMessage", "AstraHtmlUiComponent", 40, 80);
        var action = graph.Member("Action", "Action", "string", 360);
        var isInstall = graph.Equal("IsInstall", "InstallPart", 520);
        var installGate = graph.Branch("Install?", 680);
        var payload = graph.Member("Payload", "Payload", "string", 820);
        var partId = graph.Call("Bui.Field", "PartId", 1040, ("Payload", "string", null), ("Field", "string", "id"), ("Value", "string", null));
        var part = graph.Call("Inventory.Find", "Part", 1260, ("Owner", "EntityUid", null), ("Prototype", "string", null), ("Item", "int64", null));
        var hasPart = graph.NotZero("HasPart", 1480);
        var partGate = graph.Branch("Part?", 1480);
        var noPart = graph.Call("Bui.Set", "NoPart", 1680, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Select a barrel in the list"), ("Success", "bool", null));
        var gun = graph.Call("Entity.GetHeldItem", "Gun", 1900, ("Holder", "EntityUid", null), ("Container", "string", "gun"), ("Item", "int64", null));
        var hasGun = graph.NotZero("HasGun", 2120);
        var gunGate = graph.Branch("Gun?", 2120);
        var noGun = graph.Call("Bui.Set", "NoGun", 2320, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Put the pistol in the gun slot"), ("Success", "bool", null));
        var partComp = graph.Component("PartData", "WeaponPart", 2400, "int64");
        var partDataGate = graph.Branch("PartData?", 2400);
        var slot = graph.Field("Slot", "WeaponPart", "Slot", "string", 2540);
        var barrel = graph.Equal("IsBarrel", "barrel", 2780);
        var slotGate = graph.Branch("Barrel?", 2960);
        var assembly = graph.Component("Assembly", "WeaponAssembly", 3240, "int64");
        var assemblyGate = graph.Branch("Assembly?", 3240);
        var prototype = graph.Field("Prototype", "WeaponPart", "Prototype", "string", 3520);
        var rate = graph.Field("Rate", "WeaponPart", "FireRate", "float32", 3800);
        var speed = graph.Field("Speed", "WeaponPart", "ProjectileSpeed", "float32", 4080);
        var setBarrel = graph.Set("SetBarrel", "WeaponAssembly", "Barrel", 3520);
        var setRate = graph.Set("SetRate", "WeaponAssembly", "FireRate", 3800);
        var setSpeed = graph.Set("SetSpeed", "WeaponAssembly", "ProjectileSpeed", 4080);
        var insert = graph.Call("Container.Insert", "Insert", 4360, ("Owner", "int64", null), ("Container", "string", "barrel"), ("Item", "int64", null), ("Success", "bool", null));
        var afterInstall = graph.Refresh("Done", 4640);
        graph.UseRow(560);
        var onOpen = graph.Refresh("Open", 360);

        graph.Exec(message, installGate);
        graph.Exec(installGate, "True", partGate);
        graph.Exec(partGate, "False", noPart);
        graph.Exec(partGate, "True", gunGate);
        graph.Exec(gunGate, "False", noGun);
        graph.Exec(gunGate, "True", partDataGate);
        graph.Exec(partDataGate, "True", slotGate);
        graph.Exec(slotGate, "True", assemblyGate);
        graph.Exec(assemblyGate, "True", setBarrel);
        graph.Exec(setBarrel, setRate);
        graph.Exec(setRate, setSpeed);
        graph.Exec(setSpeed, insert);
        graph.Exec(insert, afterInstall);
        graph.Exec(open, onOpen);

        graph.DataWire(message, "Event", action, "Target");
        graph.DataWire(action, "Value", isInstall, "A");
        graph.DataWire(isInstall, "Result", installGate, "Condition");
        graph.DataWire(message, "Event", payload, "Target");
        graph.DataWire(payload, "Value", partId, "Payload");
        graph.DataWire(message, "Entity", part, "Owner");
        graph.DataWire(message, "Entity", noPart, "Owner");
        graph.DataWire(message, "Entity", noGun, "Owner");
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
        graph.WireRefresh(afterInstall, open);
        graph.WireRefresh(onOpen, open);

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
        private readonly Dictionary<string, NodePosition> _positions = [];
        private readonly HashSet<(int X, int Y)> _used = [];
        private int _row;

        public void UseRow(int y) => _row = y;

        public NodeDocument Refresh(string prefix, int x)
        {
            var created = Data("List.Create", prefix + "Empty", x, ("List", "List<PartRow>", null));
            var clear = Assign(prefix + "Clear", "Rows", x, "List<PartRow>");
            var contents = Call("Container.Contents", prefix + "Contents", x + 280, ("Owner", "EntityUid", null), ("Container", "string", "storagebase"), ("Contents", "List<EntityUid>", null));
            var each = ForEach(prefix + "Each", x + 280);
            var rowComp = Component(prefix + "Row", "WeaponPart", x + 560);
            var rowGate = Branch(prefix + "Row?", x + 560);
            var made = Data("Schema.Make", prefix + "Make", x + 840, ("Value", "PartRow", null));
            made.Properties["Schema"] = "PartRow";
            var setId = Set(prefix + "Id", "PartRow", "Id", x + 840);
            var rowProto = Field(prefix + "Prototype", "WeaponPart", "Prototype", "string", x + 1120);
            var setText = Set(prefix + "Text", "PartRow", "Text", x + 1120);
            var label = Field(prefix + "Label", "WeaponPart", "Label", "string", x + 1400);
            var setOff = Set(prefix + "Off", "PartRow", "Disabled", x + 1400, "false", "bool");
            var readRows = Read(prefix + "Read", "Rows", x + 1680, "List<PartRow>");
            var add = Call("List.Add", prefix + "Add", x + 1680, ("List", "List<PartRow>", null), ("Item", "PartRow", null), ("ListOut", "List<PartRow>", null));
            var store = Assign(prefix + "Store", "Rows", x + 1960, "List<PartRow>");
            var readBuilt = Read(prefix + "Built", "Rows", x + 2100, "List<PartRow>");
            var rows = Call("Ui.Rows", prefix + "Json", x + 2380, ("List", "List<PartRow>", null), ("IdField", "string", "Id"), ("TextField", "string", "Text"), ("DisabledField", "string", "Disabled"), ("Rows", "string", null));
            var publish = Call("Bui.Set", prefix + "Publish", x + 2520, ("Owner", "EntityUid", null), ("Name", "string", "Parts"), ("Value", "string", null), ("Success", "bool", null));
            var gun = Call("Entity.GetHeldItem", prefix + "Gun", x + 2740, ("Holder", "EntityUid", null), ("Container", "string", "gun"), ("Item", "int64", null));
            var inGun = Call("Entity.GetHeldItem", prefix + "InGun", x + 2960, ("Holder", "int64", null), ("Container", "string", "barrel"), ("Item", "int64", null));
            var hasBarrel = NotZero(prefix + "HasBarrel", x + 3180);
            var barrelGate = Branch(prefix + "Installed?", x + 3180);
            var installedComp = Component(prefix + "Installed", "WeaponPart", x + 3400, "int64");
            var installedGate = Branch(prefix + "InstalledPart?", x + 3400);
            var plainPublish = Call("Bui.Set", prefix + "Plain", x + 3620, ("Owner", "EntityUid", null), ("Name", "string", "Parts"), ("Value", "string", null), ("Success", "bool", null));
            var installedMade = Data("Schema.Make", prefix + "InstalledMake", x + 3620, ("Value", "PartRow", null));
            installedMade.Properties["Schema"] = "PartRow";
            var installedId = Set(prefix + "InstalledId", "PartRow", "Id", x + 3840);
            var installedProto = Field(prefix + "InstalledPrototype", "WeaponPart", "Prototype", "string", x + 4060);
            var installedText = Set(prefix + "InstalledText", "PartRow", "Text", x + 4060);
            var installedLabel = Field(prefix + "InstalledLabel", "WeaponPart", "Label", "string", x + 4280);
            var installedOff = Set(prefix + "InstalledOff", "PartRow", "Disabled", x + 4280, "true", "bool");
            var installedRead = Read(prefix + "InstalledRead", "Rows", x + 4500, "List<PartRow>");
            var installedAdd = Call("List.Add", prefix + "InstalledAdd", x + 4500, ("List", "List<PartRow>", null), ("Item", "PartRow", null), ("ListOut", "List<PartRow>", null));
            var installedStore = Assign(prefix + "InstalledStore", "Rows", x + 4720, "List<PartRow>");
            var installedBuilt = Read(prefix + "InstalledBuilt", "Rows", x + 4940, "List<PartRow>");
            var installedJson = Call("Ui.Rows", prefix + "InstalledJson", x + 5160, ("List", "List<PartRow>", null), ("IdField", "string", "Id"), ("TextField", "string", "Text"), ("DisabledField", "string", "Disabled"), ("Rows", "string", null));
            var installedPublish = Call("Bui.Set", prefix + "InstalledPublish", x + 5380, ("Owner", "EntityUid", null), ("Name", "string", "Parts"), ("Value", "string", null), ("Success", "bool", null));

            Exec(clear, each);
            Exec(each, "Body", rowGate);
            Exec(rowGate, "True", setId);
            Exec(setId, setText);
            Exec(setText, setOff);
            Exec(setOff, add);
            Exec(add, store);
            Exec(each, "Out", barrelGate);
            Exec(barrelGate, "False", publish);
            Exec(barrelGate, "True", installedGate);
            Exec(installedGate, "False", plainPublish);
            Exec(installedGate, "True", installedId);
            Exec(installedId, installedText);
            Exec(installedText, installedOff);
            Exec(installedOff, installedAdd);
            Exec(installedAdd, installedStore);
            Exec(installedStore, installedPublish);

            DataWire(created, "List", clear, "Value");
            DataWire(contents, "Contents", each, "Collection");
            DataWire(each, "Current", rowComp, "Entity");
            DataWire(rowComp, "Found", rowGate, "Condition");
            DataWire(rowComp, "Component", label, "Component");
            DataWire(rowComp, "Component", rowProto, "Component");
            DataWire(made, "Value", setId, "Target");
            DataWire(rowProto, "Value", setId, "Value");
            DataWire(setId, "Result", setText, "Target");
            DataWire(label, "Value", setText, "Value");
            DataWire(setText, "Result", setOff, "Target");
            DataWire(readRows, "Value", add, "List");
            DataWire(setOff, "Result", add, "Item");
            DataWire(add, "List", store, "Value");
            DataWire(readBuilt, "Value", rows, "List");
            DataWire(rows, "Rows", publish, "Value");
            DataWire(rows, "Rows", plainPublish, "Value");
            DataWire(gun, "Item", inGun, "Holder");
            DataWire(inGun, "Item", hasBarrel, "A");
            DataWire(hasBarrel, "Result", barrelGate, "Condition");
            DataWire(inGun, "Item", installedComp, "Entity");
            DataWire(installedComp, "Found", installedGate, "Condition");
            DataWire(installedComp, "Component", installedProto, "Component");
            DataWire(installedComp, "Component", installedLabel, "Component");
            DataWire(installedMade, "Value", installedId, "Target");
            DataWire(installedProto, "Value", installedId, "Value");
            DataWire(installedId, "Result", installedText, "Target");
            DataWire(installedLabel, "Value", installedText, "Value");
            DataWire(installedText, "Result", installedOff, "Target");
            DataWire(installedRead, "Value", installedAdd, "List");
            DataWire(installedOff, "Result", installedAdd, "Item");
            DataWire(installedAdd, "List", installedStore, "Value");
            DataWire(installedBuilt, "Value", installedJson, "List");
            DataWire(installedJson, "Rows", installedPublish, "Value");
            _refreshes[clear] = (contents, publish, gun, plainPublish, installedPublish);
            return clear;
        }

        public void WireRefresh(NodeDocument clear, NodeDocument bench)
        {
            var (contents, publish, gun, plainPublish, installedPublish) = _refreshes[clear];
            DataWire(bench, "Entity", contents, "Owner");
            DataWire(bench, "Entity", publish, "Owner");
            DataWire(bench, "Entity", gun, "Holder");
            DataWire(bench, "Entity", plainPublish, "Owner");
            DataWire(bench, "Entity", installedPublish, "Owner");
        }

        private readonly Dictionary<NodeDocument, (NodeDocument Contents, NodeDocument Publish, NodeDocument Gun, NodeDocument PlainPublish, NodeDocument InstalledPublish)> _refreshes = [];

        public NodeDocument Event(string name, string eventType, string component, int x, int y) =>
            Place(Node(name, "Event." + name, new Dictionary<string, string> { ["eventType"] = eventType, ["componentType"] = component },
                Exec("Out", false), Data("Entity", "EntityUid", false), Data("Event", "object", false)), x, y);

        public NodeDocument Branch(string name, int x) =>
            Place(Node(name, "Core.Branch", null, Exec("In", true), Exec("True", false), Exec("False", false), Data("Condition", "bool", true)), x, Y(80));

        public NodeDocument Assign(string name, string variable, int x, string valueType = "object") =>
            Place(Node(name, "Core.VariableAssign", new Dictionary<string, string> { ["VariableName"] = variable },
                Exec("In", true), Exec("Out", false), Data("Value", valueType, true)), x, Y(80));

        public NodeDocument Read(string name, string variable, int x, string valueType = "object") =>
            Place(Node(name, "Core.VariableRead", new Dictionary<string, string> { ["VariableName"] = variable },
                Data("Value", valueType, false)), x, Y(300));

        public NodeDocument ForEach(string name, int x) =>
            Place(Node(name, "Flow.ForEach", null, Exec("In", true), Exec("Out", false), Exec("Body", false), Data("Collection", "List<EntityUid>", true), Data("Current", "EntityUid", false)), x, Y(80));

        public NodeDocument Component(string name, string type, int x, string entityType = "EntityUid") =>
            Place(Node(name, "Entity.TryGetComponent", new Dictionary<string, string> { ["ComponentType"] = type },
                Data("Entity", entityType, true), Data("Found", "bool", false), Data("Component", "component", false)), x, Y(300));

        public NodeDocument Field(string name, string schema, string field, string type, int x) =>
            Place(Node(name, "Schema.GetField", new Dictionary<string, string> { ["Schema"] = schema, ["Field"] = field },
                Data("Component", "component", true), Data("Value", type, false)), x, Y(300));

        public NodeDocument Set(string name, string schema, string field, int x, string? valueDefault = null, string valueType = "object") =>
            Place(Node(name, "Schema.SetField", new Dictionary<string, string> { ["Schema"] = schema, ["Field"] = field },
                Exec("In", true), Exec("Out", false), Data("Target", "object", true), Data("Value", valueType, true, valueDefault), Data("Result", "object", false)), x, Y(80));

        public NodeDocument Member(string name, string member, string type, int x) =>
            Place(Node(name, "Native.GetMember", new Dictionary<string, string> { ["Member"] = member },
                Data("Target", "object", true), Data("Value", type, false)), x, Y(300));

        public NodeDocument Equal(string name, string literal, int x)
        {
            var node = Node(name, "cmp.equal", null, Data("A", "string", true), Data("B", "string", true, literal), Data("Result", "bool", false));
            return Place(node, x, Y(300));
        }

        public NodeDocument NotZero(string name, int x)
        {
            var node = Node(name, "cmp.notequal", null, Data("A", "int64", true), Data("B", "int64", true, "0"), Data("Result", "bool", false));
            return Place(node, x, Y(300));
        }

        public NodeDocument Data(string type, string name, int x, params (string Name, string Type, string? Default)[] pins)
        {
            var specs = pins.Select(pin => Data(pin.Name, pin.Type, false, pin.Default)).ToArray();
            return Place(Node(name, type, null, specs), x, Y(300));
        }

        private int Y(int y) => y + _row;

        public NodeDocument Call(string type, string name, int x, params (string Name, string Type, string? Default)[] pins)
        {
            var specs = new List<PinDocument>();
            if (type is "Container.Insert" or "Bui.Set" or "List.Add")
            {
                specs.Add(Exec("In", true));
                specs.Add(Exec("Out", false));
            }

            var outputs = type switch
            {
                "Inventory.Find" or "Entity.GetHeldItem" => new[] { "Item" },
                "Container.Contents" => new[] { "Contents" },
                "Bui.Field" => new[] { "Value" },
                "Ui.Rows" => new[] { "Rows" },
                "Container.Insert" or "Bui.Set" => new[] { "Success" },
                "List.Add" => new[] { "ListOut" },
                _ => Array.Empty<string>()
            };
            foreach (var pin in pins)
            {
                if (pin.Name == "ListOut")
                {
                    specs.Add(new PinDocument { Name = "List", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = pin.Type });
                    continue;
                }

                var output = outputs.Contains(pin.Name);
                specs.Add(output
                    ? Data(pin.Name, pin.Type, false)
                    : Data(pin.Name, pin.Type, true, pin.Default));
            }

            var execution = type is "Container.Insert" or "Bui.Set" or "List.Add";
            return Place(Node(name, type, null, [.. specs]), x, Y(execution ? 80 : 300));
        }

        public void Exec(NodeDocument from, NodeDocument to) => Exec(from, from.Pins.First(pin => pin.Kind == PinKind.Execution && pin.Direction == PinDirection.Output).Name, to);

        public void Exec(NodeDocument from, string fromPin, NodeDocument to) =>
            Wire(from, fromPin, to, "In");

        public void DataWire(NodeDocument from, string fromPin, NodeDocument to, string toPin) =>
            Wire(from, fromPin, to, toPin);

        public GraphDocument Document() => new()
        {
            Id = GraphId.FromString("c1000000-0000-4000-8000-0000000000c1"),
            Name = "WeaponBench",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Metadata = new GraphMetadata { Description = "Bench lists parts and installs a barrel the server accepts. The client only sends the part id." },
            Variables = [new GraphVariableDocument { Name = "Rows", TypeName = "List<PartRow>" }],
            Nodes = _nodes,
            Connections = _connections,
            EditorLayout = new EditorLayoutDocument { NodePositions = _positions },
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
            while (!_used.Add((x, y)))
            {
                y += 220;
            }

            _positions[node.Id.Value.ToString("D")] = new NodePosition(x, y);
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
            Id = SchemaId.FromString(id),
            Name = name,
            Kind = component ? "Component" : "Struct",
            Fields = fields.Select(field => new ComponentFieldDocument
            {
                Id = FieldId.FromString(field.Id),
                Name = field.Name,
                TypeName = field.Type,
                DefaultValue = field.Default
            }).ToList()
        };
    }
}
