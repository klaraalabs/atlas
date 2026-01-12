using System.Linq.Expressions;
using System.Reflection;

namespace Atlas.Compilation;

/// <summary>
/// Compiles order-by clauses into IQueryable extensions.
/// </summary>
public class OrderByCompiler<TEntity>
{
    private readonly ParameterExpression _parameter;
    private readonly Func<string, bool>? _fieldValidator;

    public OrderByCompiler(Func<string, bool>? fieldValidator = null)
    {
        _parameter = Expression.Parameter(typeof(TEntity), "e");
        _fieldValidator = fieldValidator;
    }

    /// <summary>
    /// Applies ordering to the query based on order-by specifications.
    /// </summary>
    public IQueryable<TEntity> Apply(IQueryable<TEntity> query, IEnumerable<(string Field, bool Descending)> orderByClauses)
    {
        var clauses = orderByClauses.ToList();
        if (clauses.Count == 0)
        {
            return query;
        }

        IOrderedQueryable<TEntity>? ordered = null;

        foreach (var (field, descending) in clauses)
        {
            if (_fieldValidator is not null && !_fieldValidator(field))
            {
                throw new AtlasSecurityException($"Field '{field}' is not allowed for ordering.");
            }

            var keySelector = BuildKeySelector(field);

            if (ordered is null)
            {
                ordered = descending
                    ? ApplyOrderByDescending(query, keySelector)
                    : ApplyOrderBy(query, keySelector);
            }
            else
            {
                ordered = descending
                    ? ApplyThenByDescending(ordered, keySelector)
                    : ApplyThenBy(ordered, keySelector);
            }
        }

        return ordered ?? query;
    }

    private LambdaExpression BuildKeySelector(string fieldPath)
    {
        var parts = fieldPath.Split('.');
        Expression current = _parameter;

        foreach (var part in parts)
        {
            var property = current.Type.GetProperty(part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                throw new AtlasCompilationException(
                    $"Property '{part}' not found on type '{current.Type.Name}' (field path: '{fieldPath}')");
            }

            current = Expression.Property(current, property);
        }

        return Expression.Lambda(current, _parameter);
    }

    private static IOrderedQueryable<TEntity> ApplyOrderBy(IQueryable<TEntity> query, LambdaExpression keySelector)
    {
        return (IOrderedQueryable<TEntity>)InvokeQueryableMethod("OrderBy", query, keySelector);
    }

    private static IOrderedQueryable<TEntity> ApplyOrderByDescending(IQueryable<TEntity> query, LambdaExpression keySelector)
    {
        return (IOrderedQueryable<TEntity>)InvokeQueryableMethod("OrderByDescending", query, keySelector);
    }

    private static IOrderedQueryable<TEntity> ApplyThenBy(IOrderedQueryable<TEntity> query, LambdaExpression keySelector)
    {
        return (IOrderedQueryable<TEntity>)InvokeQueryableMethod("ThenBy", query, keySelector);
    }

    private static IOrderedQueryable<TEntity> ApplyThenByDescending(IOrderedQueryable<TEntity> query, LambdaExpression keySelector)
    {
        return (IOrderedQueryable<TEntity>)InvokeQueryableMethod("ThenByDescending", query, keySelector);
    }

    private static IQueryable InvokeQueryableMethod(string methodName, IQueryable query, LambdaExpression keySelector)
    {
        var method = typeof(Queryable)
            .GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TEntity), keySelector.ReturnType);

        return (IQueryable)method.Invoke(null, [query, keySelector])!;
    }
}
