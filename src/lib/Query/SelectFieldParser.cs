using Atlas.Ast;

namespace Atlas.Query;

/// <summary>
/// Parses select field strings into SelectField objects.
/// Supports simple fields and aggregate functions.
/// </summary>
/// <remarks>
/// Aggregate syntax: field.path.func (e.g., "balance.amount.sum", "balance.amount.max")
/// Count syntax: "count" for count(*), or "field.count" for count of non-null values
/// Alias syntax: "balance.amount.sum:totalAmount"
/// </remarks>
public static class SelectFieldParser
{
    private static readonly HashSet<string> AggregateFuncs = new(StringComparer.OrdinalIgnoreCase)
    {
        "count", "sum", "avg", "min", "max"
    };

    private static readonly Dictionary<string, AggregateFunc> FuncMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["count"] = AggregateFunc.Count,
        ["sum"] = AggregateFunc.Sum,
        ["avg"] = AggregateFunc.Avg,
        ["min"] = AggregateFunc.Min,
        ["max"] = AggregateFunc.Max
    };

    /// <summary>
    /// Parses a select field string into a SelectField object.
    /// </summary>
    public static SelectField Parse(string fieldSpec)
    {
        if (string.IsNullOrWhiteSpace(fieldSpec))
        {
            throw new AtlasQueryException("Select field cannot be empty.");
        }

        fieldSpec = fieldSpec.Trim();

        // Check for alias suffix (e.g., "balance.amount.sum:totalAmount")
        string? alias = null;
        var colonIndex = fieldSpec.LastIndexOf(':');
        if (colonIndex > 0 && colonIndex < fieldSpec.Length - 1)
        {
            alias = fieldSpec[(colonIndex + 1)..].Trim();
            fieldSpec = fieldSpec[..colonIndex].Trim();
        }

        // Special case: just "count" means count(*)
        if (fieldSpec.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            return new SelectField
            {
                Field = "*",
                Aggregate = AggregateFunc.Count,
                Alias = alias
            };
        }

        // Split by dots and check if last segment is an aggregate function
        var parts = fieldSpec.Split('.');
        var lastPart = parts[^1];

        if (parts.Length > 1 && AggregateFuncs.Contains(lastPart))
        {
            // It's an aggregate: e.g., "balance.amount.sum"
            var fieldPath = string.Join(".", parts[..^1]).ToLowerInvariant();
            var func = FuncMap[lastPart];

            return new SelectField
            {
                Field = fieldPath,
                Aggregate = func,
                Alias = alias
            };
        }

        // Simple field (no aggregate)
        return new SelectField
        {
            Field = fieldSpec.ToLowerInvariant(),
            Alias = alias
        };
    }

    /// <summary>
    /// Parses multiple select field strings.
    /// </summary>
    public static List<SelectField> ParseAll(IEnumerable<string> fieldSpecs)
    {
        return fieldSpecs.Select(Parse).ToList();
    }

    /// <summary>
    /// Checks if any of the fields contain aggregates.
    /// </summary>
    public static bool HasAggregates(IEnumerable<SelectField> fields)
    {
        return fields.Any(f => f.Aggregate.HasValue);
    }
}
