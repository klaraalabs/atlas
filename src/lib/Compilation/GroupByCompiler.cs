using System.Reflection;
using Atlas.Ast;

namespace Atlas.Compilation;

/// <summary>
/// Compiles GROUP BY queries with aggregates into executable expressions.
/// </summary>
public class GroupByCompiler<TEntity> where TEntity : class
{
    private readonly Func<string, bool>? _fieldValidator;

    public GroupByCompiler(Func<string, bool>? fieldValidator = null)
    {
        _fieldValidator = fieldValidator;
    }

    /// <summary>
    /// Executes a GROUP BY query with aggregates.
    /// </summary>
    public List<Dictionary<string, object?>> Execute(IQueryable<TEntity> source, List<string> groupByFields, List<SelectField> selectFields)
    {
        // Validate group by fields
        foreach (var field in groupByFields)
        {
            if (_fieldValidator is not null && !_fieldValidator(field.ToLowerInvariant()))
            {
                throw new AtlasSecurityException($"Field '{field}' is not allowed for grouping.");
            }
        }

        // We need to use dynamic grouping since the key type is runtime-determined
        var results = new List<Dictionary<string, object?>>();

        // For simplicity, we'll materialize and group in memory for complex cases
        // EF Core has limited support for dynamic GroupBy with projections
        var data = source.ToList();
        var groups = GroupData(data, groupByFields);

        foreach (var group in groups)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            // Add group key values
            for (var i = 0; i < groupByFields.Count; i++)
            {
                var field = groupByFields[i];
                var value = group.Key[i];
                row[field.ToLowerInvariant()] = value;
            }

            // Add aggregates
            foreach (var selectField in selectFields)
            {
                if (selectField.Aggregate is null)
                {
                    continue; // Group key fields are already added
                }

                var aggregateValue = GroupByCompiler<TEntity>.ComputeAggregate(group.Items, selectField);
                row[selectField.OutputKey] = aggregateValue;
            }

            results.Add(row);
        }

        return results;
    }

    private List<(object?[] Key, List<TEntity> Items)> GroupData(List<TEntity> data, List<string> groupByFields)
    {
        // Group by composite key (tuple of field values)
        var groups = data
            .GroupBy(e => GroupByCompiler<TEntity>.GetCompositeKey(e, groupByFields), new CompositeKeyComparer())
            .Select(g => (g.Key, Items: g.ToList()))
            .ToList();

        return groups;
    }

    private static object?[] GetCompositeKey(TEntity entity, List<string> fields)
    {
        return fields.Select(f => GroupByCompiler<TEntity>.GetFieldValue(entity, f)).ToArray();
    }

    private static object? GetFieldValue(object? obj, string fieldPath)
    {
        if (obj is null)
        {
            return null;
        }

        // Handle composite key (object array)
        if (obj is object?[])
        {
            // This is called when extracting from the group key
            // We need to track which field index corresponds to which field
            // For now, return the entire key - we'll handle this differently
            return obj;
        }

        var parts = fieldPath.Split('.');
        object? current = obj;

        foreach (var part in parts)
        {
            if (current is null)
            {
                return null;
            }

            var property = current.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                return null;
            }

            current = property.GetValue(current);
        }

        return current;
    }

    private static object? ComputeAggregate(List<TEntity> items, SelectField selectField)
    {
        if (selectField.Aggregate is null)
        {
            throw new AtlasCompilationException($"Expected aggregate function for field '{selectField.Field}'");
        }

        // Get values for the field
        var values = items
            .Select(e => GroupByCompiler<TEntity>.GetFieldValue(e, selectField.Field))
            .Where(v => v is not null)
            .ToList();

        return selectField.Aggregate.Value switch
        {
            AggregateFunc.Count => items.Count,
            AggregateFunc.Sum => ComputeSum(values),
            AggregateFunc.Avg => ComputeAvg(values),
            AggregateFunc.Min => ComputeMin(values),
            AggregateFunc.Max => ComputeMax(values),
            _ => throw new AtlasCompilationException($"Unknown aggregate function: {selectField.Aggregate}")
        };
    }

    private static decimal? ComputeSum(List<object?> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        return values.Aggregate(0m, (acc, v) => acc + Convert.ToDecimal(v));
    }

    private static object? ComputeAvg(List<object?> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sum = values.Aggregate(0m, (acc, v) => acc + Convert.ToDecimal(v));
        return sum / values.Count;
    }

    private static object? ComputeMin(List<object?> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        return values.Cast<IComparable>().Min();
    }

    private static object? ComputeMax(List<object?> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        return values.Cast<IComparable>().Max();
    }

    private class CompositeKeyComparer : IEqualityComparer<object?[]>
    {
        public bool Equals(object?[]? x, object?[]? y)
        {
            if (x is null && y is null)
            {
                return true;
            }
            if (x is null || y is null)
            {
                return false;
            }
            if (x.Length != y.Length)
            {
                return false;
            }

            for (int i = 0; i < x.Length; i++)
            {
                if (!Equals(x[i], y[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(object?[] obj)
        {
            if (obj is null)
            {
                return 0;
            }

            var hash = new HashCode();
            foreach (var item in obj)
            {
                hash.Add(item);
            }
            return hash.ToHashCode();
        }
    }
}
