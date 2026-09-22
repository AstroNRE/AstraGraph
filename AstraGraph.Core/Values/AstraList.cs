namespace AstraGraph.Core;

/// <summary>
/// Typed list carried through the VM. Native collections are boxed into this at member reads.
/// </summary>
public sealed class AstraList
{
    public AstraList(IReadOnlyList<AstraValue> items)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public IReadOnlyList<AstraValue> Items { get; }

    public int Count => Items.Count;

    public AstraValue Get(int index) =>
        (uint)index >= (uint)Items.Count ? AstraValue.Null : Items[index];
}
