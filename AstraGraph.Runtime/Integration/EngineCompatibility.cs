using System.Text.Json;

namespace AstraGraph.Runtime.Integration;

public sealed class EngineCompatibilityException : Exception
{
    public EngineCompatibilityException(string message) : base(message)
    {
    }
}

public sealed class EngineCompatibilityManifest
{
    public string AstraRuntimeVersion { get; init; } = "1.0.0";
    public string EngineFamily { get; init; } = "SpaceStation14";
    public string CompatibilityProfile { get; init; } = "Gameplay";
    public string TestedSs14Commit { get; init; } = "";
    public string TestedRobustCommit { get; init; } = "";
    public string EngineApiVersion { get; init; } = "1";
    public string AdapterApiVersion { get; init; } = "1";
    public bool DynamicNativeSystemOrdering { get; init; }
    public string[] RequiredEngineHooks { get; init; } = [];
    public string[] RequiredFeatures { get; init; } = [];
    public string[] UnsupportedFeatures { get; init; } = [];
    public string LastVerifiedDate { get; init; } = "";

    public const string NativeOrderingWarning =
        "DynamicNativeSystemOrdering is false. Before/After against native systems is recorded and not applied.";
}

public sealed record EngineCompatibilityReport(
    string RobustCommit,
    string EngineApiVersion,
    IReadOnlyCollection<string> AvailableHooks);

public interface IEngineCompatibilityService
{
    EngineCompatibilityManifest Manifest { get; }
    void EnsureCompatible(EngineCompatibilityReport report);
}

public sealed class EngineCompatibilityService : IEngineCompatibilityService
{
    public EngineCompatibilityService(EngineCompatibilityManifest manifest)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    public EngineCompatibilityManifest Manifest { get; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string? FindManifest(string fileName = "Compatibility.json")
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && cursor != null; depth++)
        {
            var candidate = Path.Combine(cursor.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            cursor = cursor.Parent;
        }

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        return File.Exists(cwd) ? cwd : null;
    }

    public static EngineCompatibilityService Load(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<EngineCompatibilityManifest>(json, JsonOptions)
            ?? throw new EngineCompatibilityException($"Compatibility manifest '{path}' is empty.");
        return new EngineCompatibilityService(manifest);
    }

    public void EnsureCompatible(EngineCompatibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!string.Equals(Manifest.EngineFamily, "RobustToolbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineCompatibilityException($"Engine family '{Manifest.EngineFamily}' is not RobustToolbox.");
        }

        if (string.IsNullOrWhiteSpace(Manifest.CompatibilityProfile))
        {
            throw new EngineCompatibilityException("Compatibility profile is missing.");
        }

        if (Manifest.DynamicNativeSystemOrdering)
        {
            throw new EngineCompatibilityException(EngineCompatibilityManifest.NativeOrderingWarning);
        }

        foreach (var feature in Manifest.UnsupportedFeatures)
        {
            if (Manifest.RequiredEngineHooks.Contains(feature, StringComparer.Ordinal) ||
                Manifest.RequiredFeatures.Contains(feature, StringComparer.Ordinal))
            {
                throw new EngineCompatibilityException($"Feature '{feature}' is both required and unsupported.");
            }
        }

        if (!string.Equals(report.RobustCommit, Manifest.TestedRobustCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineCompatibilityException(
                $"Robust commit '{report.RobustCommit}' does not match tested commit '{Manifest.TestedRobustCommit}'.");
        }

        if (!string.Equals(report.EngineApiVersion, Manifest.EngineApiVersion, StringComparison.Ordinal))
        {
            throw new EngineCompatibilityException(
                $"Engine API '{report.EngineApiVersion}' does not match manifest API '{Manifest.EngineApiVersion}'.");
        }

        var missing = Manifest.RequiredEngineHooks
            .Concat(Manifest.RequiredFeatures)
            .Where(hook => !report.AvailableHooks.Contains(hook, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new EngineCompatibilityException(
                "AstraGraph Compatibility Check\n\n" +
                $"Profile: {Manifest.CompatibilityProfile}\n" +
                "Engine API: unsupported\n\n" +
                "Missing:\n- " + string.Join("\n- ", missing) +
                "\n\nAstraGraph startup aborted.");
        }
    }

    public static string? ReadCheckedOutCommit(string repositoryPath)
    {
        var gitDir = Path.Combine(repositoryPath, ".git");
        if (!Directory.Exists(gitDir) && !File.Exists(gitDir))
        {
            return null;
        }

        var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
        if (head.StartsWith("ref:", StringComparison.Ordinal))
        {
            var refPath = head["ref:".Length..].Trim();
            var refFile = Path.Combine(gitDir, refPath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(refFile) ? File.ReadAllText(refFile).Trim() : null;
        }

        return head.Length >= 7 ? head : null;
    }
}
