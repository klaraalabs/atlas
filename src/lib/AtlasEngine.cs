using System.Reflection;
using Atlas.Query;
using Atlas.Security;
using Microsoft.EntityFrameworkCore;

namespace Atlas;

/// <summary>
/// A multi-entity Atlas engine that routes queries to the appropriate executor based on the entity name.
/// </summary>
public class AtlasEngine
{
    private readonly Dictionary<string, IAtlasExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an entity with its executor.
    /// </summary>
    public AtlasEngine Register<TEntity>(string entityName, AtlasQueryExecutor<TEntity> executor)
        where TEntity : class
    {
        _executors[entityName] = new TypedExecutorWrapper<TEntity>(executor);
        return this;
    }

    /// <summary>
    /// Registers an entity with a custom policy configuration.
    /// </summary>
    public AtlasEngine Register<TEntity>(string entityName, Action<AtlasBuilder<TEntity>> configure)
        where TEntity : class
    {
        var builder = Atlas.For<TEntity>();
        configure(builder);
        _executors[entityName] = new TypedExecutorWrapper<TEntity>(builder.BuildExecutor());
        return this;
    }

    /// <summary>
    /// Registers all entities from a DbContext with permissive defaults (all fields, all operators).
    /// Entity names are derived from DbSet property names (e.g., "Users" -> "users").
    /// </summary>
    public AtlasEngine RegisterFromDbContext<TDbContext>() where TDbContext : DbContext
    {
        return RegisterFromDbContext<TDbContext>(options => { });
    }

    /// <summary>
    /// Registers all entities from a DbContext with custom default options.
    /// </summary>
    public AtlasEngine RegisterFromDbContext<TDbContext>(Action<AtlasEngineOptions> configureOptions)
        where TDbContext : DbContext
    {
        var options = new AtlasEngineOptions();
        configureOptions(options);

        var dbContextType = typeof(TDbContext);
        var dbSetProperties = dbContextType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType &&
                        p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        foreach (var prop in dbSetProperties)
        {
            var entityType = prop.PropertyType.GetGenericArguments()[0];
            var entityName = options.EntityNameTransform(prop.Name);

            // Skip if already registered (allows overriding specific entities)
            if (_executors.ContainsKey(entityName))
            {
                continue;
            }

            // Create executor using reflection
            var executorType = typeof(AtlasQueryExecutor<>).MakeGenericType(entityType);
            var policyType = typeof(AtlasPolicy<>).MakeGenericType(entityType);

            // Create policy with defaults
            var createMethod = policyType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
            var policy = createMethod.Invoke(null, null)!;

            // Apply default configuration
            var allowAllFieldsMethod = policyType.GetMethod("AllowAllFields")!;
            allowAllFieldsMethod.Invoke(policy, [2]); // Default navigation depth of 2

            var allowAllOpsMethod = policyType.GetMethod("AllowAllOperators")!;
            allowAllOpsMethod.Invoke(policy, null);

            if (options.DefaultMaxLimit.HasValue)
            {
                var maxLimitMethod = policyType.GetMethod("MaxLimit")!;
                maxLimitMethod.Invoke(policy, [options.DefaultMaxLimit.Value]);
            }

            if (options.DefaultLimit.HasValue)
            {
                var defaultLimitMethod = policyType.GetMethod("DefaultLimit")!;
                defaultLimitMethod.Invoke(policy, [options.DefaultLimit.Value]);
            }

            // Create executor
            var executor = Activator.CreateInstance(executorType, policy)!;

            // Wrap and register
            var wrapperType = typeof(TypedExecutorWrapper<>).MakeGenericType(entityType);
            var wrapper = (IAtlasExecutor)Activator.CreateInstance(wrapperType, executor)!;
            _executors[entityName] = wrapper;
        }

        return this;
    }

    /// <summary>
    /// Executes a query, routing to the appropriate executor based on the entity property.
    /// </summary>
    public async Task<AtlasResult> ExecuteAsync(
        DbContext dbContext,
        AtlasQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.Entity))
        {
            throw new AtlasQueryException("The 'entity' property is required.");
        }

        if (!_executors.TryGetValue(query.Entity, out var executor))
        {
            var available = string.Join(", ", _executors.Keys.OrderBy(k => k));
            throw new AtlasQueryException($"Unknown entity '{query.Entity}'. Available entities: {available}");
        }

        return await executor.ExecuteAsync(dbContext, query, cancellationToken);
    }

    /// <summary>
    /// Executes a query with total count, routing to the appropriate executor.
    /// </summary>
    public async Task<AtlasResultWithCount> ExecuteWithCountAsync(
        DbContext dbContext,
        AtlasQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.Entity))
        {
            throw new AtlasQueryException("The 'entity' property is required.");
        }

        if (!_executors.TryGetValue(query.Entity, out var executor))
        {
            var available = string.Join(", ", _executors.Keys.OrderBy(k => k));
            throw new AtlasQueryException($"Unknown entity '{query.Entity}'. Available entities: {available}");
        }

        return await executor.ExecuteWithCountAsync(dbContext, query, cancellationToken);
    }

    /// <summary>
    /// Gets the list of registered entity names.
    /// </summary>
    public IEnumerable<string> RegisteredEntities => _executors.Keys;

    /// <summary>
    /// Gets schema information for all registered entities.
    /// Useful for client discovery and documentation.
    /// </summary>
    public AtlasSchema GetSchema()
    {
        var entities = new Dictionary<string, AtlasEntitySchema>();

        foreach (var (name, executor) in _executors)
        {
            entities[name] = executor.GetEntitySchema();
        }

        return new AtlasSchema { Entities = entities };
    }

    /// <summary>
    /// Gets schema information for a specific entity.
    /// </summary>
    public AtlasEntitySchema? GetEntitySchema(string entityName)
    {
        if (_executors.TryGetValue(entityName, out var executor))
        {
            return executor.GetEntitySchema();
        }
        return null;
    }
}

/// <summary>
/// Internal interface for type-erased executor access.
/// </summary>
internal interface IAtlasExecutor
{
    Task<AtlasResult> ExecuteAsync(DbContext dbContext, AtlasQuery query, CancellationToken cancellationToken);
    Task<AtlasResultWithCount> ExecuteWithCountAsync(DbContext dbContext, AtlasQuery query, CancellationToken cancellationToken);
    AtlasEntitySchema GetEntitySchema();
}

/// <summary>
/// Wraps a typed executor to implement the type-erased interface.
/// </summary>
internal class TypedExecutorWrapper<TEntity> : IAtlasExecutor
    where TEntity : class
{
    private readonly AtlasQueryExecutor<TEntity> _executor;

    public TypedExecutorWrapper(AtlasQueryExecutor<TEntity> executor)
    {
        _executor = executor;
    }

    public Task<AtlasResult> ExecuteAsync(DbContext dbContext, AtlasQuery query, CancellationToken cancellationToken)
        => _executor.ExecuteAsync(dbContext, query, cancellationToken);

    public Task<AtlasResultWithCount> ExecuteWithCountAsync(DbContext dbContext, AtlasQuery query, CancellationToken cancellationToken)
        => _executor.ExecuteWithCountAsync(dbContext, query, cancellationToken);

    public AtlasEntitySchema GetEntitySchema()
        => _executor.GetEntitySchema();
}

/// <summary>
/// Options for configuring auto-registration from a DbContext.
/// </summary>
public class AtlasEngineOptions
{
    /// <summary>
    /// Transform function for entity names. Default converts to lowercase (e.g., "Users" -> "users").
    /// </summary>
    public Func<string, string> EntityNameTransform { get; set; } = name => name.ToLowerInvariant();

    /// <summary>
    /// Default maximum limit for all entities.
    /// </summary>
    public int? DefaultMaxLimit { get; set; } = 100;

    /// <summary>
    /// Default page limit for all entities.
    /// </summary>
    public int? DefaultLimit { get; set; } = 25;
}
