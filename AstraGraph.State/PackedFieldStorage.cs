using AstraGraph.Core;

namespace AstraGraph.State;

/// <summary>
/// Packed contiguous slot storage for a single instance of a dynamic component schema.
/// Tracks per-field dirty status without heap allocations.
/// </summary>
public sealed class PackedFieldStorage
{
    private readonly AstraValue[] _slots;
    private ulong _dirtyMask;

    public SchemaType Schema { get; }

    public int FieldCount => _slots.Length;

    public bool HasAnyDirty => _dirtyMask != 0;

    public PackedFieldStorage(SchemaType schema, AstraValue[]? initialValues = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _slots = new AstraValue[schema.FieldCount];

        if (initialValues != null)
        {
            var len = Math.Min(initialValues.Length, _slots.Length);
            Array.Copy(initialValues, _slots, len);
        }

        // Initially marked clean
        _dirtyMask = 0;
    }

    public AstraValue GetField(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), $"Slot index {slotIndex} out of bounds for schema '{Schema.Name}'.");
        }
        return _slots[slotIndex];
    }

    public void SetField(int slotIndex, AstraValue value)
    {
        if ((uint)slotIndex >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), $"Slot index {slotIndex} out of bounds for schema '{Schema.Name}'.");
        }

        if (!_slots[slotIndex].Equals(value))
        {
            _slots[slotIndex] = value;
            if (slotIndex < 64)
            {
                _dirtyMask |= (1UL << slotIndex);
            }
        }
    }

    public AstraValue GetField(FieldId fieldId)
    {
        var idx = Schema.GetFieldIndex(fieldId);
        if (idx < 0) throw new KeyNotFoundException($"Field ID '{fieldId}' not found in schema '{Schema.Name}'.");
        return GetField(idx);
    }

    public void SetField(FieldId fieldId, AstraValue value)
    {
        var idx = Schema.GetFieldIndex(fieldId);
        if (idx < 0) throw new KeyNotFoundException($"Field ID '{fieldId}' not found in schema '{Schema.Name}'.");
        SetField(idx, value);
    }

    public bool IsDirty(int slotIndex)
    {
        if (slotIndex >= 64) return true;
        return (_dirtyMask & (1UL << slotIndex)) != 0;
    }

    public void ClearDirty()
    {
        _dirtyMask = 0;
    }

    public ReadOnlySpan<AstraValue> GetAllSlots() => _slots;
}
