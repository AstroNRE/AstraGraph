using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Runtime.Integration;
using AstraGraph.Runtime.Security;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public class ConsumerHostConfigurationTests
{
    [Test]
    public void FindManifest_ExplicitPath_IsUsedExactly()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-manifest-" + Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "AstraGraph", "Compatibility.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, "{}");

        var found = EngineCompatibilityService.FindManifest(manifest);
        Assert.That(found, Is.EqualTo(Path.GetFullPath(manifest)));
    }

    [Test]
    public void FindManifest_MissingExplicitPath_DoesNotSearchElsewhere()
    {
        var missing = Path.Combine(Path.GetTempPath(), "astra-missing-" + Guid.NewGuid().ToString("N"), "Compatibility.json");
        var error = Assert.Throws<EngineCompatibilityException>(() => EngineCompatibilityService.FindManifest(missing));
        Assert.That(error!.Message, Does.Contain(Path.GetFullPath(missing)));
    }

    [Test]
    public void FindManifest_DiscoversStandardSubmoduleLayout()
    {
        var consumer = Path.Combine(Path.GetTempPath(), "astra-consumer-" + Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(consumer, "AstraGraph", "Compatibility.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        Directory.CreateDirectory(Path.Combine(consumer, "RobustToolbox"));
        File.WriteAllText(manifest, "{}");

        var found = EngineCompatibilityService.FindManifest(searchRoots: [consumer]);
        Assert.That(found, Is.EqualTo(Path.GetFullPath(manifest)));
    }

    [Test]
    public void ResolveRobustToolboxRoot_UsesExplicitRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-roots-" + Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "a", "Compatibility.json");
        var robust = Path.Combine(root, "b", "RobustToolbox");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        Directory.CreateDirectory(robust);
        File.WriteAllText(manifest, "{}");

        var resolved = EngineCompatibilityService.ResolveRobustToolboxRoot(manifest, robust);
        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(robust)));
    }

    [Test]
    public void ResolveEngineIdentity_WithoutGitOrCommit_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-identity-" + Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "Compatibility.json");
        var robust = Path.Combine(root, "RobustToolbox");
        Directory.CreateDirectory(robust);
        File.WriteAllText(manifest, "{}");

        var error = Assert.Throws<EngineCompatibilityException>(() =>
            EngineCompatibilityService.ResolveEngineIdentity(manifest, explicitCommit: null, robust));
        Assert.That(error!.Message, Does.Contain("Engine identity could not be verified"));
    }

    [Test]
    public void ResolveEngineIdentity_UsesExplicitCommitWithoutGit()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-identity-" + Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "Compatibility.json");
        Directory.CreateDirectory(root);
        File.Copy(EngineCompatibilityService.FindManifest()!, manifest);
        var expected = EngineCompatibilityService.Load(manifest).Manifest.TestedRobustCommit;

        var identity = EngineCompatibilityService.ResolveEngineIdentity(manifest, expected, explicitRoot: null);
        Assert.That(identity, Is.EqualTo(expected));
        Assert.DoesNotThrow(() => EngineCompatibilityService.Load(manifest).EnsureCompatible(
            new EngineCompatibilityReport(identity, EngineCompatibilityService.Load(manifest).Manifest.EngineApiVersion, ["type-event-subscribe"])));
    }
}

#if NET10_0_OR_GREATER
[TestFixture]
public class ServerHostCompositionTests
{
    [Test]
    public void Server_UsesConsumerStorageLayout()
    {
        var consumer = Path.Combine(Path.GetTempPath(), "astra-storage-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(consumer, "Resources", "AstraGraph");
        var data = Path.Combine(consumer, "data", "AstraGraph");
        var system = new AstraGraph.Robust.Server.ServerAstraGraphSystem();
        system.Configure(new AstraServerHostOptions
        {
            Storage = new StorageLayout(project, data),
            PermissionProvider = PolicyPermissionProvider.AllowLocalAuthor("dev")
        });

        system.EnsureInitialized();

        Assert.That(system.StorageLayout.ProjectRoot, Is.EqualTo(Path.GetFullPath(project)));
        Assert.That(system.StorageLayout.DataRoot, Is.EqualTo(Path.GetFullPath(data)));
        Assert.That(system.StorageLayout.ProjectRoot, Does.Not.StartWith(AppContext.BaseDirectory));
        Assert.That(Directory.Exists(project), Is.True);
        Assert.That(Directory.Exists(data), Is.True);
    }

    [Test]
    public void Server_AdminDirectory_PublisherCanPublish()
    {
        var system = Compose(new PublisherDirectory());
        var session = new DirectorySession(Guid.NewGuid(), "publisher");
        Assert.That(system.PermissionProvider.CanEnterAstra(session), Is.True);
        Assert.That(system.PermissionProvider.HasPermission(session, AstraPermission.PublishServer), Is.True);
    }

    [Test]
    public void Server_AdminDirectory_DeveloperCannotPublish()
    {
        var system = Compose(new DeveloperDirectory());
        var session = new DirectorySession(Guid.NewGuid(), "developer");
        Assert.That(system.PermissionProvider.CanEnterAstra(session), Is.True);
        Assert.That(system.PermissionProvider.HasPermission(session, AstraPermission.Compile), Is.True);
        Assert.That(system.PermissionProvider.HasPermission(session, AstraPermission.PublishServer), Is.False);
    }

    [Test]
    public void Server_ExplicitManifest_IsTheConfiguredFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-server-manifest-" + Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "AstraGraph", "Compatibility.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.Copy(EngineCompatibilityService.FindManifest()!, manifest);
        var system = new AstraGraph.Robust.Server.ServerAstraGraphSystem();
        system.Configure(new AstraServerHostOptions
        {
            Storage = new StorageLayout(Path.Combine(root, "Resources", "AstraGraph"), Path.Combine(root, "data", "AstraGraph")),
            CompatibilityManifestPath = manifest,
            PermissionProvider = PolicyPermissionProvider.AllowLocalAuthor("dev")
        });

        system.EnsureInitialized();
        Assert.That(system.HostOptions.CompatibilityManifestPath, Is.EqualTo(Path.GetFullPath(manifest)));
    }

    [Test]
    public void Server_UnknownEngineIdentity_FailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-server-id-" + Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "Compatibility.json");
        var robust = Path.Combine(root, "engine");
        Directory.CreateDirectory(robust);
        File.Copy(EngineCompatibilityService.FindManifest()!, manifest);
        var system = new AstraGraph.Robust.Server.ServerAstraGraphSystem();
        system.Configure(new AstraServerHostOptions
        {
            Storage = new StorageLayout(Path.Combine(root, "Resources", "AstraGraph"), Path.Combine(root, "data", "AstraGraph")),
            CompatibilityManifestPath = manifest,
            RobustToolboxRoot = robust,
            PermissionProvider = PolicyPermissionProvider.AllowLocalAuthor("dev")
        });

        var error = Assert.Throws<EngineCompatibilityException>(() => system.ExecuteBootstrap());
        Assert.That(error!.Message, Does.Contain("Engine identity could not be verified"));
    }

    [Test]
    public void Server_ExplicitEngineIdentity_PassesWithoutGit()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-server-sha-" + Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "Compatibility.json");
        Directory.CreateDirectory(root);
        File.Copy(EngineCompatibilityService.FindManifest()!, manifest);
        var expected = EngineCompatibilityService.Load(manifest).Manifest.TestedRobustCommit;
        var system = new AstraGraph.Robust.Server.ServerAstraGraphSystem();
        system.Configure(new AstraServerHostOptions
        {
            Storage = new StorageLayout(Path.Combine(root, "Resources", "AstraGraph"), Path.Combine(root, "data", "AstraGraph")),
            CompatibilityManifestPath = manifest,
            RobustCommit = expected,
            PermissionProvider = PolicyPermissionProvider.AllowLocalAuthor("dev")
        });

        Assert.DoesNotThrow(() => system.ExecuteBootstrap());
        Assert.That(system.AuthoringService, Is.Not.Null);
    }

    [Test]
    public void Configure_AfterInitialization_Throws()
    {
        var system = Compose(new DeveloperDirectory());
        Assert.Throws<InvalidOperationException>(() => system.Configure(new AstraServerHostOptions
        {
            Storage = new StorageLayout("Resources/AstraGraph", "data/AstraGraph"),
            AdminDirectory = new DeveloperDirectory()
        }));
    }

    private static AstraGraph.Robust.Server.ServerAstraGraphSystem Compose(IAstraAdminDirectory directory)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-admin-" + Guid.NewGuid().ToString("N"));
        var system = new AstraGraph.Robust.Server.ServerAstraGraphSystem();
        system.Configure(new AstraServerHostOptions
        {
            Storage = new StorageLayout(Path.Combine(root, "Resources", "AstraGraph"), Path.Combine(root, "data", "AstraGraph")),
            AdminDirectory = directory
        });
        system.EnsureInitialized();
        return system;
    }

    private sealed class PublisherDirectory : IAstraAdminDirectory
    {
        public bool TryGetAdmin(string userId, out uint adminFlags, out string? rank, out bool isSandbox)
        {
            adminFlags = SS14AdminFlagsConstants.AdminFlagAstraGraph;
            rank = "Publisher";
            isSandbox = false;
            return true;
        }
    }

    private sealed class DeveloperDirectory : IAstraAdminDirectory
    {
        public bool TryGetAdmin(string userId, out uint adminFlags, out string? rank, out bool isSandbox)
        {
            adminFlags = SS14AdminFlagsConstants.AdminFlagAstraGraph;
            rank = "Developer";
            isSandbox = false;
            return true;
        }
    }

    private sealed class DirectorySession : global::Robust.Shared.Player.ICommonSession
    {
        public DirectorySession(Guid userId, string name)
        {
            UserId = new global::Robust.Shared.Network.NetUserId(userId);
            Name = name;
            Data = new global::Robust.Shared.Player.SessionData(UserId, name);
            State = new global::Robust.Shared.GameStates.SessionState { UserId = UserId, Name = name };
            ViewSubscriptions = [];
        }

        public global::Robust.Shared.Enums.SessionStatus Status => global::Robust.Shared.Enums.SessionStatus.InGame;
        public global::Robust.Shared.GameObjects.EntityUid? AttachedEntity => null;
        public global::Robust.Shared.Network.NetUserId UserId { get; }
        public string Name { get; }
        public short Ping => 0;
        public global::Robust.Shared.Network.INetChannel Channel { get; set; } = null!;
        public global::Robust.Shared.Network.LoginType AuthType => global::Robust.Shared.Network.LoginType.LoggedIn;
        public HashSet<global::Robust.Shared.GameObjects.EntityUid> ViewSubscriptions { get; }
        public DateTime ConnectedTime { get; set; }
        public global::Robust.Shared.GameStates.SessionState State { get; }
        public global::Robust.Shared.Player.SessionData Data { get; }
        public bool ClientSide { get; set; }
    }
}
#endif
