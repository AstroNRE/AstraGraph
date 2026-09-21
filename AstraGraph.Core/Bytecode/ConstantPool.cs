namespace AstraGraph.Core;

public enum ConstantKind : byte
{
    Null = 0,
    Bool = 1,
    Int64 = 2,
    Double = 3,
    String = 4,
    Guid = 5
}

public sealed record ConstantEntry(ConstantKind Kind, object? Value);

/// <summary>
/// Deduplicated pool of constants (strings, numbers, Guids) for compact bytecode encoding.
/// </summary>
public sealed class ConstantPool
{
    private readonly List<ConstantEntry> _entries = [];
    private readonly Dictionary<string, int> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<long, int> _integers = [];
    private readonly Dictionary<double, int> _doubles = [];
    private readonly Dictionary<Guid, int> _guids = [];

    public IReadOnlyList<ConstantEntry> Entries => _entries;
    public int Count => _entries.Count;

    public ConstantEntry this[int index] => _entries[index];

    public int GetOrAddString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_strings.TryGetValue(value, out var idx)) return idx;

        idx = _entries.Count;
        _entries.Add(new ConstantEntry(ConstantKind.String, value));
        _strings[value] = idx;
        return idx;
    }

    public int GetOrAddInt64(long value)
    {
        if (_integers.TryGetValue(value, out var idx)) return idx;

        idx = _entries.Count;
        _entries.Add(new ConstantEntry(ConstantKind.Int64, value));
        _integers[value] = idx;
        return idx;
    }

    public int GetOrAddDouble(double value)
    {
        if (_doubles.TryGetValue(value, out var idx)) return idx;

        idx = _entries.Count;
        _entries.Add(new ConstantEntry(ConstantKind.Double, value));
        _doubles[value] = idx;
        return idx;
    }

    public int GetOrAddGuid(Guid value)
    {
        if (_guids.TryGetValue(value, out var idx)) return idx;

        idx = _entries.Count;
        _entries.Add(new ConstantEntry(ConstantKind.Guid, value));
        _guids[value] = idx;
        return idx;
    }

    public int GetOrAddBool(bool value)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Kind == ConstantKind.Bool && (bool)_entries[i].Value! == value)
                return i;
        }

        var idx = _entries.Count;
        _entries.Add(new ConstantEntry(ConstantKind.Bool, value));
        return idx;
    }

    public void AddEntry(ConstantEntry entry)
    {
        _entries.Add(entry);
    }
}
