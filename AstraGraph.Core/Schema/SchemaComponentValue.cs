namespace AstraGraph.Core;

/// <summary>
/// Graph-facing view of one schema component. Field identity stays inside the store.
/// </summary>
public sealed class SchemaComponentValue
{
    private readonly Func<string, AstraValue> _read;
    private readonly Action<string, AstraValue>? _write;

    public SchemaComponentValue(string schemaName, bool found, Func<string, AstraValue> read, Action<string, AstraValue>? write = null)
    {
        SchemaName = schemaName;
        Found = found;
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write;
    }

    public string SchemaName { get; }

    public bool Found { get; }

    public AstraValue Read(string fieldName) => _read(fieldName);

    public bool TryWrite(string fieldName, AstraValue value)
    {
        if (_write == null || !Found)
        {
            return false;
        }

        _write(fieldName, value);
        return true;
    }

    public static SchemaComponentValue Missing(string schemaName) =>
        new(schemaName, false, _ => AstraValue.Null);
}
