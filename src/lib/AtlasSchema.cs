namespace Atlas;

/// <summary>
/// Schema information for all registered entities in an Atlas engine.
/// </summary>
public class AtlasSchema
{
    /// <summary>
    /// Map of entity name to its schema.
    /// </summary>
    public Dictionary<string, AtlasEntitySchema> Entities { get; init; } = new();
}

/// <summary>
/// Schema information for a single entity.
/// </summary>
public class AtlasEntitySchema
{
    /// <summary>
    /// The CLR type name of the entity.
    /// </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>
    /// Available fields that can be selected/filtered.
    /// </summary>
    public List<AtlasFieldSchema> Fields { get; init; } = [];

    /// <summary>
    /// Allowed comparison operators.
    /// </summary>
    public List<string> AllowedOperators { get; init; } = [];

    /// <summary>
    /// Maximum records per query.
    /// </summary>
    public int MaxLimit { get; init; }

    /// <summary>
    /// Default records per query.
    /// </summary>
    public int DefaultLimit { get; init; }

    /// <summary>
    /// Maximum WHERE clause nesting depth.
    /// </summary>
    public int MaxWhereDepth { get; init; }

    /// <summary>
    /// Maximum fields in SELECT.
    /// </summary>
    public int MaxSelectFields { get; init; }

    /// <summary>
    /// Maximum navigation property depth (e.g., user.balance.currency = 3).
    /// </summary>
    public int MaxNavigationDepth { get; init; }
}

/// <summary>
/// Schema information for a single field.
/// </summary>
public class AtlasFieldSchema
{
    /// <summary>
    /// The field path (e.g., "id", "balance.amount").
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// The CLR type name of the field.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Whether the field is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Whether the field is a collection.
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    /// Whether the field is a navigation property.
    /// </summary>
    public bool IsNavigation { get; init; }
}
