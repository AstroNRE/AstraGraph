namespace AstraGraph.State;

/// <summary>
/// High-performance sparse-set data structure providing O(1) presence checks,
/// additions, removals, and cache-friendly dense iteration over entity IDs.
/// </summary>
public sealed class SparseSet
{
    private const int PageSize = 4096;
    private int[][] _sparsePages = [];
    private int[] _dense = [];
    private int _count;

    public int Count => _count;

    public ReadOnlySpan<int> Dense => _dense.AsSpan(0, _count);

    public bool Contains(int entity)
    {
        if (entity < 0) return false;

        var pageIndex = entity / PageSize;
        var offset = entity % PageSize;

        if (pageIndex >= _sparsePages.Length) return false;

        var page = _sparsePages[pageIndex];
        if (page is null) return false;

        var denseIndex = page[offset];
        return denseIndex >= 0 && denseIndex < _count && _dense[denseIndex] == entity;
    }

    public int Add(int entity)
    {
        if (entity < 0) throw new ArgumentOutOfRangeException(nameof(entity), "Entity ID must be non-negative.");

        if (Contains(entity))
        {
            var p = entity / PageSize;
            var o = entity % PageSize;
            return _sparsePages[p][o];
        }

        var pageIndex = entity / PageSize;
        var offset = entity % PageSize;

        EnsurePage(pageIndex);

        if (_count >= _dense.Length)
        {
            var newCap = _dense.Length == 0 ? 16 : _dense.Length * 2;
            Array.Resize(ref _dense, newCap);
        }

        var denseIndex = _count++;
        _dense[denseIndex] = entity;
        _sparsePages[pageIndex][offset] = denseIndex;

        return denseIndex;
    }

    public bool Remove(int entity)
    {
        if (!Contains(entity)) return false;

        var pageIndex = entity / PageSize;
        var offset = entity % PageSize;

        var denseIndex = _sparsePages[pageIndex][offset];
        var lastEntity = _dense[_count - 1];

        // Move last element to the deleted slot
        _dense[denseIndex] = lastEntity;
        var lastPage = lastEntity / PageSize;
        var lastOffset = lastEntity % PageSize;
        _sparsePages[lastPage][lastOffset] = denseIndex;

        // Invalidate old slot
        _sparsePages[pageIndex][offset] = -1;
        _count--;

        return true;
    }

    public void Clear()
    {
        for (var i = 0; i < _sparsePages.Length; i++)
        {
            if (_sparsePages[i] != null)
            {
                Array.Fill(_sparsePages[i], -1);
            }
        }
        _count = 0;
    }

    private void EnsurePage(int pageIndex)
    {
        if (pageIndex >= _sparsePages.Length)
        {
            Array.Resize(ref _sparsePages, pageIndex + 1);
        }

        if (_sparsePages[pageIndex] is null)
        {
            var page = new int[PageSize];
            Array.Fill(page, -1);
            _sparsePages[pageIndex] = page;
        }
    }
}
