using System.IO;

namespace AstraGraph.Persistence;

/// <summary>
/// Categorization of project-owned AstraGraph documents.
/// </summary>
public enum GraphCategory
{
    Systems,
    Behaviors,
    Libraries,
    Schemas,
    Mechanics
}

/// <summary>
/// Defines and manages the storage directory layout for both project-owned resources
/// and runtime live/override data as specified by the AstraGraph persistence architecture.
/// </summary>
public sealed class StorageLayout
{
    public string ProjectRoot { get; }
    public string DataRoot { get; }

    public string LiveDirectory => Path.Combine(DataRoot, "Live");
    public string OverridesDirectory => Path.Combine(DataRoot, "Overrides");
    public string HistoryDirectory => Path.Combine(DataRoot, "History");
    public string CacheDirectory => Path.Combine(DataRoot, "Cache");
    public string StateDirectory => Path.Combine(DataRoot, "State");
    public string BackupsDirectory => Path.Combine(DataRoot, "Backups");
    public string AuditDirectory => Path.Combine(DataRoot, "Audit");
    public string DraftsDirectory => Path.Combine(DataRoot, "Drafts");

    public StorageLayout(string projectRoot = "Resources/AstraGraph", string dataRoot = "data/AstraGraph")
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
        DataRoot = Path.GetFullPath(dataRoot);
    }

    /// <summary>
    /// Creates all standard directories if they do not exist.
    /// </summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ProjectRoot);
        Directory.CreateDirectory(Path.Combine(ProjectRoot, nameof(GraphCategory.Systems)));
        Directory.CreateDirectory(Path.Combine(ProjectRoot, nameof(GraphCategory.Behaviors)));
        Directory.CreateDirectory(Path.Combine(ProjectRoot, nameof(GraphCategory.Libraries)));
        Directory.CreateDirectory(Path.Combine(ProjectRoot, nameof(GraphCategory.Schemas)));
        Directory.CreateDirectory(Path.Combine(ProjectRoot, nameof(GraphCategory.Mechanics)));

        Directory.CreateDirectory(LiveDirectory);
        Directory.CreateDirectory(OverridesDirectory);
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(AuditDirectory);
        Directory.CreateDirectory(DraftsDirectory);
    }

    /// <summary>
    /// Gets directory path for a project category.
    /// </summary>
    public string GetProjectCategoryPath(GraphCategory category)
    {
        return Path.Combine(ProjectRoot, category.ToString());
    }

    /// <summary>
    /// Gets path to a live graph file.
    /// </summary>
    public string GetLivePath(string relativePath)
    {
        return Path.Combine(LiveDirectory, relativePath);
    }

    /// <summary>
    /// Gets path to an override graph file.
    /// </summary>
    public string GetOverridePath(string relativePath)
    {
        return Path.Combine(OverridesDirectory, relativePath);
    }

    /// <summary>
    /// Gets path to a historical immutable revision.
    /// </summary>
    public string GetHistoryPath(Guid graphId, Guid revisionId)
    {
        var graphFolder = Path.Combine(HistoryDirectory, graphId.ToString("D"));
        return Path.Combine(graphFolder, $"{revisionId:D}.agraph");
    }

    /// <summary>
    /// Gets path to a compiled cache file.
    /// </summary>
    public string GetCachePath(string cacheKey)
    {
        return Path.Combine(CacheDirectory, $"{cacheKey}.agcache");
    }

    /// <summary>
    /// Gets path to a persistent state file.
    /// </summary>
    public string GetStatePath(string snapshotName)
    {
        return Path.Combine(StateDirectory, $"{snapshotName}.agstate");
    }

    /// <summary>
    /// Gets path to an audit log file for a given UTC date.
    /// </summary>
    public string GetAuditLogPath(DateTime utcDate)
    {
        return Path.Combine(AuditDirectory, $"audit_{utcDate:yyyyMMdd}.jsonl");
    }

    /// <summary>
    /// Scans the entire data root and cleans up orphan temporary files (*.tmp*),
    /// which may have been left behind by sudden system crashes during write.
    /// </summary>
    public int CleanOrphanTempFiles()
    {
        if (!Directory.Exists(DataRoot))
            return 0;

        var cleanedCount = 0;
        foreach (var file in Directory.EnumerateFiles(DataRoot, "*.tmp*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
                cleanedCount++;
            }
            catch (IOException)
            {
                // File might be locked by another process
            }
        }

        return cleanedCount;
    }
}
