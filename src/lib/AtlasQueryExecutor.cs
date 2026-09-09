using System.Reflection;
using Atlas.Ast;
using Atlas.Compilation;
using Atlas.Query;
using Atlas.Security;
using Microsoft.EntityFrameworkCore;

namespace Atlas;

/// <summary>
/// Main entry point for compiling and executing Atlas queries against EF Core.
/// </summary>
public class AtlasQueryExecutor<TEntity> where TEntity : class
{
    private readonly AtlasPolicy<TEntity> _policy;
    private readonly QueryValidator<TEntity> _validator;

    public AtlasQueryExecutor(AtlasPolicy<TEntity> policy)
    {
        _policy = policy;
        _validator = new QueryValidator<TEntity>(policy);
    }

    /// <summary>
    /// Gets schema information for this entity.
    /// </summary>
    public AtlasEntitySchema GetEntitySchema()
    {
        var fields = new List<AtlasFieldSchema>();
        var entityType = typeof(TEntity);

        foreach (var fieldPath in _policy.AllowedFields)
        {
            var fieldInfo = GetFieldInfo(entityType, fieldPath);
            fields.Add(new AtlasFieldSchema
            {
                Path = fieldPath,
                Type = fieldInfo.Type.Name,
                IsNullable = fieldInfo.IsNullable,
                IsCollection = fieldInfo.IsCollection,
                IsNavigation = fieldPath.Contains('.')
            });
        }

        return new AtlasEntitySchema
        {
            TypeName = entityType.Name,
            Fields = fields.OrderBy(f => f.Path).ToList(),
            AllowedOperators = _policy.AllowedOperators.Select(o => o.ToString().ToLowerInvariant()).ToList(),
            MaxLimit = _policy.MaxLimitValue,
            DefaultLimit = _policy.DefaultLimitValue,
            MaxWhereDepth = _policy.MaxWhereDepthValue,
            MaxSelectFields = _policy.MaxSelectFieldsValue,
            MaxNavigationDepth = _policy.MaxNavigationDepthValue
        };
    }

    private static (Type Type, bool IsNullable, bool IsCollection) GetFieldInfo(Type rootType, string fieldPath)
    {
        var parts = fieldPath.Split('.');
        var currentType = rootType;

        foreach (var part in parts)
        {
            var prop = currentType.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null)
            {
                return (typeof(object), true, false);
            }
            currentType = prop.PropertyType;
        }

        var isNullable = !currentType.IsValueType || Nullable.GetUnderlyingType(currentType) != null;
        var isCollection = currentType != typeof(string) &&
                           currentType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        var displayType = Nullable.GetUnderlyingType(currentType) ?? currentType;
        if (isCollection && currentType.IsGenericType)
        {
            displayType = currentType.GetGenericArguments()[0];
        }

        return (displayType, isNullable, isCollection);
    }

    /// <summary>
    /// Compiles and executes the query, returning dictionaries with the selected fields.
    /// Results are transformed to nested objects (e.g., "balance.amount" becomes { balance: { amount: ... } }).
    /// Supports aggregate functions (count, sum, avg, min, max) and GROUP BY.
    /// </summary>
    public Task<AtlasResult> ExecuteAsync(
        DbContext dbContext,
        AtlasQuery query,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(dbContext.Set<TEntity>(), query, cancellationToken);

    /// <summary>
    /// Compiles and executes the query against a caller-supplied source.
    /// Use this overload for request-scoped authorization, tenancy, and soft-delete filters.
    /// </summary>
    public async Task<AtlasResult> ExecuteAsync(
        IQueryable<TEntity> source,
        AtlasQuery query,
        CancellationToken cancellationToken = default)
    {
        // Validate against policy
        _validator.Validate(query);

        // Parse select fields to detect aggregates
        var selectFields = SelectFieldParser.ParseAll(query.Select);

        // Check if this is a GROUP BY query
        if (query.GroupBy is { Count: > 0 })
        {
            return await ExecuteGroupByAsync(source, query, selectFields, cancellationToken);
        }

        // Check if this is an aggregate query (without GROUP BY)
        if (SelectFieldParser.HasAggregates(selectFields))
        {
            return await ExecuteAggregateAsync(source, query, selectFields, cancellationToken);
        }

        // Regular query - build the queryable pipeline
        var queryable = BuildQueryable(source, query, selectFields);

        // Execute and transform to nested structure
        var flatData = await queryable.ToListAsync(cancellationToken);
        var data = ResultTransformer.ToNested(flatData);

        // Apply distinct after transformation (dictionaries need content-based comparison)
        if (query.Distinct)
        {
            data = data.Distinct(Helpers.DictionaryEqualityComparer.Instance).ToList();
        }

        return new AtlasResult
        {
            Data = data,
            Offset = query.Offset,
            Limit = _policy.GetEffectiveLimit(query.Limit)
        };
    }

    /// <summary>
    /// Executes a GROUP BY query with aggregates.
    /// </summary>
    private async Task<AtlasResult> ExecuteGroupByAsync(
        IQueryable<TEntity> source,
        AtlasQuery query,
        List<SelectField> selectFields,
        CancellationToken cancellationToken)
    {
        var q = ApplyFilters(source, query);

        // Include navigation properties needed for group by and select fields
        var allFields = query.GroupBy!
            .Concat(selectFields.Select(f => f.Field))
            .Distinct()
            .ToList();
        q = NavigationIncluder.IncludeNavigations(q, allFields);

        // Execute GROUP BY with aggregates
        var groupByCompiler = new GroupByCompiler<TEntity>(_policy.IsFieldAllowed);
        var groupByFields = query.GroupBy!.Select(f => f.ToLowerInvariant()).ToList();

        // Run on thread pool since we're doing in-memory grouping
        var flatData = await Task.Run(() => groupByCompiler.Execute(q, groupByFields, selectFields), cancellationToken);
        var data = ResultTransformer.ToNested(flatData);

        return new AtlasResult
        {
            Data = data,
            Offset = 0,
            Limit = flatData.Count
        };
    }

    /// <summary>
    /// Executes an aggregate query (e.g., sum, count, avg).
    /// </summary>
    private async Task<AtlasResult> ExecuteAggregateAsync(
        IQueryable<TEntity> source,
        AtlasQuery query,
        List<SelectField> selectFields,
        CancellationToken cancellationToken)
    {
        var q = ApplyFilters(source, query);

        // Execute aggregates
        var aggregateCompiler = new AggregateCompiler<TEntity>(_policy.IsFieldAllowed);

        // Run on thread pool to allow async pattern even though aggregates are sync
        var result = await Task.Run(() => aggregateCompiler.Execute(q, selectFields), cancellationToken);

        return new AtlasResult
        {
            Data = [result],
            Offset = 0,
            Limit = 1
        };
    }

    /// <summary>
    /// Compiles and executes the query with a count of total matching records.
    /// </summary>
    public Task<AtlasResultWithCount> ExecuteWithCountAsync(
        DbContext dbContext,
        AtlasQuery query,
        CancellationToken cancellationToken = default)
        => ExecuteWithCountAsync(dbContext.Set<TEntity>(), query, cancellationToken);

    /// <summary>
    /// Compiles and executes the query against a caller-supplied source and returns the total count.
    /// The count includes Atlas and policy filters, but excludes pagination.
    /// </summary>
    public async Task<AtlasResultWithCount> ExecuteWithCountAsync(
        IQueryable<TEntity> source,
        AtlasQuery query,
        CancellationToken cancellationToken = default)
    {
        // Validate against policy
        _validator.Validate(query);

        // Parse select fields
        var selectFields = SelectFieldParser.ParseAll(query.Select);

        // Check if this is a GROUP BY query
        if (query.GroupBy is { Count: > 0 })
        {
            var groupResult = await ExecuteGroupByAsync(source, query, selectFields, cancellationToken);
            return new AtlasResultWithCount
            {
                Data = groupResult.Data,
                Offset = 0,
                Limit = groupResult.Limit,
                TotalCount = groupResult.Data.Count
            };
        }

        // Aggregates don't support count metadata (they return single row)
        if (SelectFieldParser.HasAggregates(selectFields))
        {
            var aggResult = await ExecuteAggregateAsync(source, query, selectFields, cancellationToken);
            return new AtlasResultWithCount
            {
                Data = aggResult.Data,
                Offset = 0,
                Limit = 1,
                TotalCount = 1
            };
        }

        var baseQuery = ApplyFilters(source, query);

        // Get total count before pagination
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // Build full queryable with projection and pagination
        var queryable = BuildQueryable(source, query, selectFields);
        var flatData = await queryable.ToListAsync(cancellationToken);
        var data = ResultTransformer.ToNested(flatData);

        // Apply distinct after transformation (dictionaries need content-based comparison)
        if (query.Distinct)
        {
            data = data.Distinct(Helpers.DictionaryEqualityComparer.Instance).ToList();
        }

        return new AtlasResultWithCount
        {
            Data = data,
            Offset = query.Offset,
            Limit = _policy.GetEffectiveLimit(query.Limit),
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// Builds the IQueryable pipeline without executing it.
    /// </summary>
    public IQueryable<Dictionary<string, object?>> BuildQueryable(
        IQueryable<TEntity> source,
        AtlasQuery query,
        List<SelectField>? selectFields = null)
    {
        selectFields ??= SelectFieldParser.ParseAll(query.Select);
        var q = ApplyFilters(source, query);

        // 1. Apply ORDER BY
        if (query.OrderBy is { Count: > 0 })
        {
            var orderByCompiler = new OrderByCompiler<TEntity>(_policy.IsFieldAllowed);
            var orderByClauses = query.OrderBy.Select(o =>
                (o.Field.ToLowerInvariant(), o.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase)));
            q = orderByCompiler.Apply(q, orderByClauses);
        }
        else
        {
            // check if "Id" is in the TEntity and use it, otherwise skip ordering (note: OFFSET without ORDER BY may yield non-deterministic results)
            var idProp = typeof(TEntity).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (idProp != null)
            {
                q = q.OrderBy(e => EF.Property<object>(e, "Id"));
            }
        }

        // 2. Apply SKIP/TAKE
        if (query.Offset > 0)
        {
            q = q.Skip(query.Offset);
        }

        var effectiveLimit = _policy.GetEffectiveLimit(query.Limit);
        q = q.Take(effectiveLimit);

        // 3. Apply SELECT projection using parsed select fields
        var projectionCompiler = new ProjectionCompiler<TEntity>(_policy.IsFieldAllowed);
        var simpleFields = selectFields.Select(f => f.Field).ToList();
        var projection = projectionCompiler.Compile(simpleFields);

        return q.Select(projection);
    }

    private IQueryable<TEntity> ApplyFilters(IQueryable<TEntity> source, AtlasQuery query)
    {
        var q = source;

        foreach (var globalFilter in _policy.GlobalFilters)
        {
            q = q.Where(globalFilter);
        }

        var whereNode = query.Where is not null
            ? WhereClauseParser.Parse(query.Where)
            : null;

        if (whereNode is not null)
        {
            var predicateCompiler = new PredicateCompiler<TEntity>(_policy.IsFieldAllowed);
            var predicate = predicateCompiler.Compile(whereNode);
            if (predicate is not null)
            {
                q = q.Where(predicate);
            }
        }

        return q;
    }
}

/// <summary>
/// Result from an Atlas query execution.
/// </summary>
public class AtlasResult
{
    public List<Dictionary<string, object?>> Data { get; init; } = [];
    public int Offset { get; init; }
    public int Limit { get; init; }
}

/// <summary>
/// Result from an Atlas query execution with total count.
/// </summary>
public class AtlasResultWithCount : AtlasResult
{
    public int TotalCount { get; init; }
}
