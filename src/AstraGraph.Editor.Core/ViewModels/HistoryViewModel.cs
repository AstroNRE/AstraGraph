using System;
using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core.ViewModels;

public sealed record RevisionItem(
    RevisionId Id,
    RevisionId? ParentId,
    string Author,
    string Message,
    DateTimeOffset Timestamp,
    string SemanticHash,
    bool IsActive);

public sealed class HistoryViewModel
{
    private readonly List<RevisionItem> _revisions = [];
    private RevisionId _activeRevisionId;

    public IReadOnlyList<RevisionItem> Revisions => _revisions;
    public RevisionId ActiveRevisionId => _activeRevisionId;
    public RevisionItem? SelectedRevision { get; set; }

    public bool CanRollback => SelectedRevision != null && SelectedRevision.Id != _activeRevisionId;

    public event Action? OnChanged;

    public void SetHistory(IEnumerable<(RevisionId Id, RevisionId? ParentId, string Author, string Message, DateTimeOffset Timestamp, string SemanticHash)> history, RevisionId activeRevisionId)
    {
        ArgumentNullException.ThrowIfNull(history);

        _activeRevisionId = activeRevisionId;
        _revisions.Clear();

        foreach (var r in history.OrderByDescending(h => h.Timestamp))
        {
            var isActive = r.Id == activeRevisionId;
            _revisions.Add(new RevisionItem(r.Id, r.ParentId, r.Author, r.Message, r.Timestamp, r.SemanticHash, isActive));
        }

        SelectedRevision = _revisions.FirstOrDefault(r => r.IsActive) ?? _revisions.FirstOrDefault();
        OnChanged?.Invoke();
    }

    public void SelectRevision(RevisionId revisionId)
    {
        SelectedRevision = _revisions.FirstOrDefault(r => r.Id == revisionId);
        OnChanged?.Invoke();
    }
}
