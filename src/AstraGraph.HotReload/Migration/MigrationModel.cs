using AstraGraph.Core;
using AstraGraph.State;

namespace AstraGraph.HotReload;

public enum FieldMigrationAction
{
    Preserve,
    InitializeDefault,
    WideningConvert,
    Drop,
    Incompatible
}

public sealed record FieldMigrationStep(
    FieldId TargetFieldId,
    string TargetFieldName,
    FieldMigrationAction Action,
    FieldId? SourceFieldId = null,
    string? SourceFieldName = null,
    AstraValue? DefaultValue = null);

/// <summary>
/// Executable migration plan for transforming a dynamic component instance between two schema revisions.
/// </summary>
public sealed class SchemaMigrationPlan
{
    public SchemaType SourceSchema { get; }
    public SchemaType TargetSchema { get; }
    public IReadOnlyList<FieldMigrationStep> Steps { get; }
    public bool CanAutoMigrate { get; }

    public SchemaMigrationPlan(SchemaType sourceSchema, SchemaType targetSchema, IReadOnlyList<FieldMigrationStep> steps)
    {
        SourceSchema = sourceSchema;
        TargetSchema = targetSchema;
        Steps = steps;
        CanAutoMigrate = !steps.Any(s => s.Action == FieldMigrationAction.Incompatible);
    }

    public PackedFieldStorage Execute(PackedFieldStorage oldStorage)
    {
        ArgumentNullException.ThrowIfNull(oldStorage);

        if (!CanAutoMigrate)
        {
            throw new InvalidOperationException($"Cannot auto-migrate schema '{SourceSchema.Name}' to '{TargetSchema.Name}' due to incompatible field definitions.");
        }

        var newValues = new AstraValue[TargetSchema.FieldCount];

        for (var i = 0; i < TargetSchema.FieldCount; i++)
        {
            var targetField = TargetSchema.Fields[i];
            var step = Steps.FirstOrDefault(s => s.TargetFieldId == targetField.Id);

            if (step != null)
            {
                switch (step.Action)
                {
                    case FieldMigrationAction.Preserve:
                        newValues[i] = oldStorage.GetField(step.SourceFieldId!.Value);
                        break;

                    case FieldMigrationAction.WideningConvert:
                        var rawVal = oldStorage.GetField(step.SourceFieldId!.Value);
                        newValues[i] = WidenValue(rawVal, targetField.Type);
                        break;

                    case FieldMigrationAction.InitializeDefault:
                        newValues[i] = step.DefaultValue ?? AstraValue.Null;
                        break;

                    default:
                        newValues[i] = AstraValue.Null;
                        break;
                }
            }
        }

        return new PackedFieldStorage(TargetSchema, newValues);
    }

    private static AstraValue WidenValue(AstraValue value, AstraType targetType)
    {
        if (targetType is PrimitiveType prim)
        {
            if (prim.Kind == PrimitiveKind.Float64)
            {
                return AstraValue.FromDouble(value.AsDouble());
            }
            if (prim.Kind == PrimitiveKind.Int64)
            {
                return AstraValue.FromInt64(value.AsInt64());
            }
        }
        return value;
    }
}
