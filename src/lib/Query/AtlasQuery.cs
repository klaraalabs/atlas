using System.Text.Json.Serialization;
using Atlas.Ast;

namespace Atlas.Query;

/// <summary>
/// The HTTP query model that clients send to Atlas.
/// </summary>
public class AtlasQuery
{
    /// <summary>
    /// The entity to query (e.g., "users", "orders").
    /// </summary>
    [JsonPropertyName("entity")]
    public string? Entity { get; set; }

    /// <summary>
    /// Fields to select (e.g., ["id", "name", "balance.amount"]).
    /// </summary>
    [JsonPropertyName("select")]
    public List<string> Select { get; set; } = [];

    /// <summary>
    /// The filter condition tree.
    /// </summary>
    [JsonPropertyName("where")]
    public WhereClause? Where { get; set; }

    /// <summary>
    /// Fields to group by for aggregate queries.
    /// </summary>
    [JsonPropertyName("groupBy")]
    public List<string>? GroupBy { get; set; }

    /// <summary>
    /// Ordering specifications.
    /// </summary>
    [JsonPropertyName("orderBy")]
    public List<OrderByClause>? OrderBy { get; set; }

    /// <summary>
    /// Number of records to skip.
    /// </summary>
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    /// <summary>
    /// Maximum number of records to return.
    /// </summary>
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 25;

    /// <summary>
    /// Whether to return distinct results only.
    /// </summary>
    [JsonPropertyName("distinct")]
    public bool Distinct { get; set; }
}

/// <summary>
/// Represents a WHERE clause in the JSON query.
/// Can be either a group (and/or), a field filter, or a collection predicate (any/all/none).
/// </summary>
public class WhereClause
{
    /// <summary>
    /// The operator: "and", "or", a comparison operator like "eq", "gt", etc.,
    /// or a collection operator like "any", "all", "none".
    /// </summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = string.Empty;

    /// <summary>
    /// Child nodes for group operations (and/or).
    /// </summary>
    [JsonPropertyName("nodes")]
    public List<WhereClause>? Nodes { get; set; }

    /// <summary>
    /// Field path for filter operations, or collection property path for any/all/none.
    /// </summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>
    /// Type hint for the value (e.g., "guid", "decimal", "string").
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>
    /// Condition for collection predicates (any/all/none).
    /// Applied to each element in the collection.
    /// </summary>
    [JsonPropertyName("condition")]
    public WhereClause? Condition { get; set; }
}

/// <summary>
/// Represents an ORDER BY clause.
/// </summary>
public class OrderByClause
{
    /// <summary>
    /// The field path to order by.
    /// </summary>
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Sort direction: "asc" or "desc".
    /// </summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "asc";
}
