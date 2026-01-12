using Atlas.Security;

namespace Atlas;

/// <summary>
/// Fluent builder for configuring Atlas query execution.
/// </summary>
public static class Atlas
{
    /// <summary>
    /// Creates a new Atlas configuration for an entity type.
    /// </summary>
    public static AtlasBuilder<TEntity> For<TEntity>() where TEntity : class
        => new();
}

/// <summary>
/// Builder for configuring Atlas for a specific entity type.
/// </summary>
public class AtlasBuilder<TEntity> where TEntity : class
{
    private readonly AtlasPolicy<TEntity> _policy = AtlasPolicy<TEntity>.Create();

    /// <summary>
    /// Allows specific fields to be queried.
    /// </summary>
    public AtlasBuilder<TEntity> AllowFields(params System.Linq.Expressions.Expression<Func<TEntity, object?>>[] fields)
    {
        _policy.AllowFields(fields);
        return this;
    }

    /// <summary>
    /// Allows specific field paths by string.
    /// </summary>
    public AtlasBuilder<TEntity> AllowFields(params string[] fieldPaths)
    {
        _policy.AllowFields(fieldPaths);
        return this;
    }

    /// <summary>
    /// Allows all public properties.
    /// </summary>
    public AtlasBuilder<TEntity> AllowAllFields()
    {
        _policy.AllowAllFields();
        return this;
    }

    /// <summary>
    /// Restricts which operators can be used.
    /// </summary>
    public AtlasBuilder<TEntity> AllowOperators(params Ast.CompareOp[] operators)
    {
        _policy.AllowOperators(operators);
        return this;
    }

    /// <summary>
    /// Allows all comparison operators.
    /// </summary>
    public AtlasBuilder<TEntity> AllowAllOperators()
    {
        _policy.AllowAllOperators();
        return this;
    }

    /// <summary>
    /// Sets the maximum limit for queries.
    /// </summary>
    public AtlasBuilder<TEntity> MaxLimit(int limit)
    {
        _policy.MaxLimit(limit);
        return this;
    }

    /// <summary>
    /// Sets the default limit for queries.
    /// </summary>
    public AtlasBuilder<TEntity> DefaultLimit(int limit)
    {
        _policy.DefaultLimit(limit);
        return this;
    }

    /// <summary>
    /// Builds the policy.
    /// </summary>
    public AtlasPolicy<TEntity> BuildPolicy() => _policy;

    /// <summary>
    /// Creates an executor with the configured policy.
    /// </summary>
    public AtlasQueryExecutor<TEntity> BuildExecutor() => new(_policy);
}
