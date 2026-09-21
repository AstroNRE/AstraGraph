using System;
using System.Collections.Generic;
using System.IO;
using AstraGraph.Core;
using AstraGraph.Persistence.Audit;

namespace AstraGraph.Persistence.Discovery;

/// <summary>
/// Discovers and resolves graph documents across Project Resources, Live, and Overrides tiers
/// enforcing strict precedence order and atomic crash-recovery semantics.
/// </summary>
public sealed class BootstrapLoader
{
    private readonly StorageLayout _layout;
    private readonly AuditLogger _auditLogger;

    public StorageLayout Layout => _layout;
    public AuditLogger Audit => _auditLogger;

    public BootstrapLoader(StorageLayout layout, AuditLogger? auditLogger = null)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _auditLogger = auditLogger ?? new AuditLogger(layout);
        _layout.EnsureDirectories();
    }

    /// <summary>
    /// Executes startup crash recovery by purging abandoned temporary files
    /// and checking storage integrity.
    /// </summary>
    public int RecoverOnStartup()
    {
        var cleaned = _layout.CleanOrphanTempFiles();
        if (cleaned > 0)
        {
            _auditLogger.Append(new AuditRecord(
                DateTimeOffset.UtcNow,
                "CrashRecovery",
                null,
                null,
                "System",
                $"Cleaned {cleaned} orphaned temporary files on startup.",
                null,
                true));
        }
        return cleaned;
    }

    /// <summary>
    /// Discovers all graphs across Overrides, Live, and Project tiers and applies
    /// the precedence resolution order: Live Override > Live > Project Resource.
    /// </summary>
    public IReadOnlyList<DiscoveredGraph> DiscoverAll()
    {
        _layout.EnsureDirectories();

        var overrides = ScanDirectory(_layout.OverridesDirectory, GraphOrigin.Override);
        var liveGraphs = ScanDirectory(_layout.LiveDirectory, GraphOrigin.Live);
        var projectGraphs = ScanDirectory(_layout.ProjectRoot, GraphOrigin.ProjectResource);

        // Precedence map by GraphId and RelativePath
        var resolved = new Dictionary<GraphId, DiscoveredGraph>();
        var resolvedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Overrides have highest precedence
        foreach (var og in overrides)
        {
            resolved[og.Id] = og;
            resolvedPaths.Add(og.RelativePath);
        }

        // 2. Live additions
        foreach (var lg in liveGraphs)
        {
            if (!resolved.ContainsKey(lg.Id) && !resolvedPaths.Contains(lg.RelativePath))
            {
                resolved[lg.Id] = lg;
                resolvedPaths.Add(lg.RelativePath);
            }
        }

        // 3. Project resources (if not superseded by an override)
        foreach (var pg in projectGraphs)
        {
            if (!resolved.ContainsKey(pg.Id) && !resolvedPaths.Contains(pg.RelativePath))
            {
                resolved[pg.Id] = pg;
                resolvedPaths.Add(pg.RelativePath);
            }
        }

        return new List<DiscoveredGraph>(resolved.Values);
    }

    /// <summary>
    /// Atomically saves a live graph to data/AstraGraph/Live.
    /// </summary>
    public DiscoveredGraph SaveLive(GraphDocument document, string relativePath, string author = "System")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(relativePath);

        var fullPath = _layout.GetLivePath(relativePath);
        var json = GraphSerializer.Serialize(document);

        AtomicFileStore.WriteAllTextAtomic(
            fullPath,
            json,
            _layout.BackupsDirectory,
            validator: tempFile =>
            {
                var content = File.ReadAllText(tempFile);
                _ = GraphSerializer.Deserialize(content);
            });

        _auditLogger.Append(new AuditRecord(
            DateTimeOffset.UtcNow,
            "SaveLive",
            document.Id.Value,
            null,
            author,
            $"Saved live graph '{relativePath}'",
            AstraHash.ComputeSemanticHash(document),
            true));

        return new DiscoveredGraph(document.Id, relativePath, fullPath, GraphOrigin.Live, document);
    }

    /// <summary>
    /// Atomically saves an override graph to data/AstraGraph/Overrides.
    /// </summary>
    public DiscoveredGraph SaveOverride(
        GraphDocument document,
        string relativePath,
        string? baseRelativePath = null,
        string author = "System")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(relativePath);

        var fullPath = _layout.GetOverridePath(relativePath);
        var json = GraphSerializer.Serialize(document);

        AtomicFileStore.WriteAllTextAtomic(
            fullPath,
            json,
            _layout.BackupsDirectory,
            validator: tempFile =>
            {
                var content = File.ReadAllText(tempFile);
                _ = GraphSerializer.Deserialize(content);
            });

        _auditLogger.Append(new AuditRecord(
            DateTimeOffset.UtcNow,
            "SaveOverride",
            document.Id.Value,
            null,
            author,
            $"Saved override graph '{relativePath}' (base: {baseRelativePath ?? "none"})",
            AstraHash.ComputeSemanticHash(document),
            true));

        return new DiscoveredGraph(document.Id, relativePath, fullPath, GraphOrigin.Override, document, baseRelativePath);
    }

    /// <summary>
    /// Promotes a live or override graph to a project-owned resource in Resources/AstraGraph/{Category}.
    /// </summary>
    public DiscoveredGraph PromoteToProject(
        DiscoveredGraph discovered,
        GraphCategory targetCategory,
        string destinationFilename,
        bool removeSourceFromLive = true,
        string author = "Admin")
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(destinationFilename);

        var categoryDir = _layout.GetProjectCategoryPath(targetCategory);
        var targetPath = Path.Combine(categoryDir, destinationFilename);
        var relativePath = Path.Combine(targetCategory.ToString(), destinationFilename);

        var json = GraphSerializer.Serialize(discovered.Document);

        AtomicFileStore.WriteAllTextAtomic(
            targetPath,
            json,
            _layout.BackupsDirectory,
            validator: tempFile =>
            {
                var content = File.ReadAllText(tempFile);
                _ = GraphSerializer.Deserialize(content);
            });

        if (removeSourceFromLive && File.Exists(discovered.FullPath))
        {
            try
            {
                File.Delete(discovered.FullPath);
            }
            catch (IOException)
            {
            }
        }

        _auditLogger.Append(new AuditRecord(
            DateTimeOffset.UtcNow,
            "PromoteToProject",
            discovered.Id.Value,
            null,
            author,
            $"Promoted graph from {discovered.FullPath} to {targetPath}",
            AstraHash.ComputeSemanticHash(discovered.Document),
            true));

        return new DiscoveredGraph(
            discovered.Id,
            relativePath,
            targetPath,
            GraphOrigin.ProjectResource,
            discovered.Document);
    }

    private static List<DiscoveredGraph> ScanDirectory(string rootPath, GraphOrigin origin)
    {
        var result = new List<DiscoveredGraph>();
        if (!Directory.Exists(rootPath))
            return result;

        var fullRoot = Path.GetFullPath(rootPath);
        foreach (var file in Directory.EnumerateFiles(fullRoot, "*.agraph", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(file);
                var doc = GraphSerializer.Deserialize(json);
                var relPath = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');
                result.Add(new DiscoveredGraph(doc.Id, relPath, file, origin, doc));
            }
            catch (Exception)
            {
                // Ignore malformed files during discovery
            }
        }

        return result;
    }
}
