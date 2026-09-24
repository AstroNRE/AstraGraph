namespace AstraGraph.Core;

/// <summary>
/// Mutable list carried through the VM. Elements may be structs.
/// </summary>
public sealed class AstraList
{
    private readonly List<AstraValue> _items;

    public AstraList()
    {
        _items = [];
    }

    public AstraList(IReadOnlyList<AstraValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    public IReadOnlyList<AstraValue> Items => _items;

    public int Count => _items.Count;

    public AstraValue Get(int index) =>
        (uint)index >= (uint)_items.Count ? AstraValue.Null : _items[index];

    public void Add(AstraValue value) => _items.Add(value);

    public void Set(int index, AstraValue value)
    {
        if ((uint)index < (uint)_items.Count)
        {
            _items[index] = value;
        }
    }

    public void RemoveAt(int index)
    {
        if ((uint)index < (uint)_items.Count)
        {
            _items.RemoveAt(index);
        }
    }

    public AstraList Copy()
    {
        var copy = new AstraList();
        foreach (var item in _items)
        {
            copy._items.Add(AstraValues.CopyValue(item));
        }

        return copy;
    }
}
