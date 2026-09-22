namespace AstraGraph.Core;

/// <summary>
/// Graph-facing view of one schema component. Field identity stays inside the store.
/// </summary>
public sealed class SchemaComponentValue
{
    private readonly Func<string, AstraValue> _read;

    public SchemaComponentValue(string schemaName, bool found, Func<string, AstraValue> read)
    {
        SchemaName = schemaName;
        Found = found;
        _read = read ?? throw new ArgumentNullException(nameof(read));
    }

    public string SchemaName { get; }

    public bool Found { get; }

    public AstraValue Read(string fieldName) => _read(fieldName);

    public static SchemaComponentValue Missing(string schemaName) =>
        new(schemaName, false, _ => AstraValue.Null);
}
