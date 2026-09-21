using System.Globalization;
using AstraGraph.Core;

namespace AstraGraph.HotReload;

/// <summary>
/// Generates deterministic migration plans between schema revisions based on stable GUIDs.
/// </summary>
public static class StateMigrationPlanner
{
    public static SchemaMigrationPlan CreatePlan(SchemaType sourceSchema, SchemaType targetSchema)
    {
        ArgumentNullException.ThrowIfNull(sourceSchema);
        ArgumentNullException.ThrowIfNull(targetSchema);

        var steps = new List<FieldMigrationStep>();

        // 1. Target fields: check existing or new
        foreach (var targetField in targetSchema.Fields)
        {
            var sourceField = sourceSchema.FindField(targetField.Id);

            if (sourceField != null)
            {
                if (targetField.Type.Equals(sourceField.Type))
                {
                    // Exact match (including renames!)
                    steps.Add(new FieldMigrationStep(
                        targetField.Id,
                        targetField.Name,
                        FieldMigrationAction.Preserve,
                        sourceField.Id,
                        sourceField.Name));
                }
                else if (targetField.Type.IsAssignableFrom(sourceField.Type))
                {
                    // Safe numeric widening
                    steps.Add(new FieldMigrationStep(
                        targetField.Id,
                        targetField.Name,
                        FieldMigrationAction.WideningConvert,
                        sourceField.Id,
                        sourceField.Name));
                }
                else
                {
                    // Incompatible type change
                    steps.Add(new FieldMigrationStep(
                        targetField.Id,
                        targetField.Name,
                        FieldMigrationAction.Incompatible,
                        sourceField.Id,
                        sourceField.Name));
                }
            }
            else
            {
                // Newly added field: initialize with default value
                var defaultVal = ParseDefaultValue(targetField);
                steps.Add(new FieldMigrationStep(
                    targetField.Id,
                    targetField.Name,
                    FieldMigrationAction.InitializeDefault,
                    DefaultValue: defaultVal));
            }
        }

        // 2. Source fields: check dropped
        foreach (var sourceField in sourceSchema.Fields)
        {
            if (targetSchema.FindField(sourceField.Id) is null)
            {
                steps.Add(new FieldMigrationStep(
                    sourceField.Id,
                    sourceField.Name,
                    FieldMigrationAction.Drop,
                    sourceField.Id,
                    sourceField.Name));
            }
        }

        return new SchemaMigrationPlan(sourceSchema, targetSchema, steps);
    }

    private static AstraValue ParseDefaultValue(SchemaField field)
    {
        if (string.IsNullOrEmpty(field.DefaultValue))
        {
            return AstraValue.Null;
        }

        if (field.Type is PrimitiveType prim)
        {
            switch (prim.Kind)
            {
                case PrimitiveKind.Bool:
                    if (bool.TryParse(field.DefaultValue, out var b)) return AstraValue.FromBool(b);
                    break;
                case PrimitiveKind.Int32:
                case PrimitiveKind.Int64:
                    if (long.TryParse(field.DefaultValue, CultureInfo.InvariantCulture, out var i)) return AstraValue.FromInt64(i);
                    break;
                case PrimitiveKind.Float32:
                case PrimitiveKind.Float64:
                    if (double.TryParse(field.DefaultValue, CultureInfo.InvariantCulture, out var d)) return AstraValue.FromDouble(d);
                    break;
                case PrimitiveKind.String:
                    return AstraValue.FromString(field.DefaultValue);
            }
        }

        return AstraValue.FromString(field.DefaultValue);
    }
}
