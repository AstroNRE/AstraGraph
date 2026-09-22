using System.Text;
using AstraGraph.Core;
using AstraGraph.Persistence;

namespace AstraGraph.HotReload;

/// <summary>
/// Writes published graphs and revision records under the live and history directories.
/// </summary>
public sealed class RevisionArchive
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private readonly StorageLayout _layout;

    public RevisionArchive(StorageLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _layout.EnsureDirectories();
    }

    public void Save(GraphDocument document, RevisionRecord record, string bindingCatalogHash = "")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(record);

        var fileName = Sanitize(document.Name) + ".agraph";
        var json = GraphSerializer.Serialize(document);
        var bytes = Encoding.UTF8.GetBytes(json);
        AtomicFileStore.WriteAllBytesAtomic(_layout.GetLivePath(fileName), bytes, _layout.BackupsDirectory);

        var historyName = $"{document.Id.Value:N}-{record.RevisionId.Value:N}.agraph";
        AtomicFileStore.WriteAllBytesAtomic(
            Path.Combine(_layout.HistoryDirectory, historyName),
            bytes,
            backupDirectory: null);

        var note = $"{record.RevisionId}|{record.ParentRevisionId}|{record.Author}|{record.Message}";
        var stem = $"{document.Id.Value:N}-{record.RevisionId.Value:N}";
        AtomicFileStore.WriteAllTextAtomic(
            Path.Combine(_layout.HistoryDirectory, stem + ".txt"),
            note);
        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            graphId = document.Id.ToString(),
            revisionId = record.RevisionId.ToString(),
            parentRevisionId = record.ParentRevisionId?.ToString(),
            semanticHash = record.SemanticHash,
            schemaHash = SchemaHash(record.SchemaSnapshot),
            bindingCatalogHash,
            compatibilityProfile = "robust-api-v1",
            author = record.Author,
            timestamp = record.Timestamp,
            message = record.Message
        }, JsonOptions);
        AtomicFileStore.WriteAllTextAtomic(
            Path.Combine(_layout.HistoryDirectory, stem + ".revision.json"),
            metadata);
    }

    private static string SchemaHash(SchemaType? schema)
    {
        if (schema == null)
        {
            return "";
        }

        return string.Join(",", schema.Fields.Select(field => field.Name + ":" + field.Type));
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var cleaned = new string(chars).Trim();
        return string.IsNullOrEmpty(cleaned) ? "graph" : cleaned;
    }
}
