using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Atlas.Compilation;

/// <summary>
/// Caches compiled expressions to avoid recompilation of identical queries.
/// Thread-safe for concurrent access.
/// </summary>
public class ExpressionCache
{
    private readonly ConcurrentDictionary<string, object> _cache = new();
    private readonly int _maxSize;
    private int _hits;
    private int _misses;

    public ExpressionCache(int maxSize = 1000)
    {
        _maxSize = maxSize;
    }

    /// <summary>
    /// Gets or adds a cached expression.
    /// </summary>
    public Expression<Func<TEntity, TResult>> GetOrAdd<TEntity, TResult>(
        string cacheKey,
        Func<Expression<Func<TEntity, TResult>>> factory)
    {
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            Interlocked.Increment(ref _hits);
            return (Expression<Func<TEntity, TResult>>)cached;
        }

        Interlocked.Increment(ref _misses);

        // Evict oldest entries if cache is full (simple strategy)
        if (_cache.Count >= _maxSize)
        {
            // Remove ~10% of entries
            var keysToRemove = _cache.Keys.Take(_maxSize / 10).ToList();
            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }

        var expression = factory();
        _cache.TryAdd(cacheKey, expression);
        return expression;
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public (int Hits, int Misses, int Size, double HitRate) GetStats()
    {
        var hits = _hits;
        var misses = _misses;
        var total = hits + misses;
        var hitRate = total > 0 ? (double)hits / total : 0;
        return (hits, misses, _cache.Count, hitRate);
    }

    /// <summary>
    /// Clears all cached expressions.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _hits = 0;
        _misses = 0;
    }
}

/// <summary>
/// Global expression cache shared across executors.
/// </summary>
public static class GlobalExpressionCache
{
    private static readonly ExpressionCache _instance = new(maxSize: 2000);

    public static ExpressionCache Instance => _instance;

    /// <summary>
    /// Generates a cache key for a projection based on entity type and selected fields.
    /// </summary>
    public static string GetProjectionKey<TEntity>(IEnumerable<string> fields)
    {
        var sortedFields = string.Join(",", fields.OrderBy(f => f));
        return $"proj:{typeof(TEntity).FullName}:{sortedFields}";
    }

    /// <summary>
    /// Generates a cache key for a predicate based on entity type and where clause structure.
    /// Note: Values are not included in the key since they differ between queries.
    /// </summary>
    public static string GetPredicateKey<TEntity>(string whereClauseHash)
    {
        return $"pred:{typeof(TEntity).FullName}:{whereClauseHash}";
    }
}
