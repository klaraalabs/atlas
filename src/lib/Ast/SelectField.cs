namespace Atlas.Ast;

/// <summary>
/// Supported aggregate functions.
/// </summary>
public enum AggregateFunc
{
    Count,
    Sum,
    Avg,
    Min,
    Max
}

/// <summary>
/// Represents a field selection, optionally with an aggregate function.
/// </summary>
public class SelectField
{
    /// <summary>
    /// The field path (e.g., "balance.amount").
    /// For Count without a field, this can be "*" or empty.
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Optional aggregate function to apply.
    /// </summary>
    public AggregateFunc? Aggregate { get; set; }

    /// <summary>
    /// Optional alias for the result (e.g., "totalAmount").
    /// If not set, a default name is generated.
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>
    /// Gets the output key name for this field.
    /// Matches input format: "balance.amount.sum" or just "count" for count(*)
    /// </summary>
    public string OutputKey => Alias ?? (Aggregate.HasValue
        ? (Field == "*" ? "count" : $"{Field}.{Aggregate.Value.ToString().ToLowerInvariant()}")
        : Field);

    public static SelectField Simple(string field) => new() { Field = field };

    public static SelectField WithAggregate(string field, AggregateFunc func, string? alias = null)
        => new() { Field = field, Aggregate = func, Alias = alias };
}
