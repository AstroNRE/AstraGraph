using System.Text;
using AstraGraph.Core;
using AstraGraph.Persistence;

namespace AstraGraph.HotReload;

/// <summary>
/// Writes published graphs and revision records under the live and history directories.
/// </summary>
public sealed class RevisionArchive
{
    private readonly StorageLayout _layout;

    public RevisionArchive(StorageLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _layout.EnsureDirectories();
    }

    public void Save(GraphDocument document, RevisionRecord record)
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
        AtomicFileStore.WriteAllTextAtomic(
            Path.Combine(_layout.HistoryDirectory, $"{document.Id.Value:N}-{record.RevisionId.Value:N}.txt"),
            note);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var cleaned = new string(chars).Trim();
        return string.IsNullOrEmpty(cleaned) ? "graph" : cleaned;
    }
}
