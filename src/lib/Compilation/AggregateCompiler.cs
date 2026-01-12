using System.Linq.Expressions;
using System.Reflection;
using Atlas.Ast;

namespace Atlas.Compilation;

/// <summary>
/// Compiles aggregate queries into LINQ expressions.
/// </summary>
public class AggregateCompiler<TEntity>
{
    private readonly ParameterExpression _parameter;
    private readonly Func<string, bool>? _fieldValidator;

    public AggregateCompiler(Func<string, bool>? fieldValidator = null)
    {
        _parameter = Expression.Parameter(typeof(TEntity), "e");
        _fieldValidator = fieldValidator;
    }

    /// <summary>
    /// Executes an aggregate query and returns a single result dictionary.
    /// </summary>
    public Dictionary<string, object?> Execute(IQueryable<TEntity> source, IEnumerable<SelectField> fields)
    {
        var fieldList = fields.ToList();
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fieldList)
        {
            if (!field.Aggregate.HasValue)
            {
                throw new AtlasCompilationException(
                    $"Field '{field.Field}' must have an aggregate function in aggregate queries.");
            }

            // Validate field (except for count(*))
            if (field.Field != "*" && _fieldValidator is not null && !_fieldValidator(field.Field))
            {
                throw new AtlasSecurityException($"Field '{field.Field}' is not allowed.");
            }

            var value = ComputeAggregate(source, field);
            result[field.OutputKey] = value;
        }

        return result;
    }

    private object? ComputeAggregate(IQueryable<TEntity> source, SelectField field)
    {
        return field.Aggregate switch
        {
            AggregateFunc.Count => ComputeCount(source, field.Field),
            AggregateFunc.Sum => ComputeSum(source, field.Field),
            AggregateFunc.Avg => ComputeAvg(source, field.Field),
            AggregateFunc.Min => ComputeMin(source, field.Field),
            AggregateFunc.Max => ComputeMax(source, field.Field),
            _ => throw new AtlasCompilationException($"Unknown aggregate function: {field.Aggregate}")
        };
    }

    private object? ComputeCount(IQueryable<TEntity> source, string fieldPath)
    {
        if (fieldPath == "*")
        {
            // COUNT(*)
            return source.Count();
        }

        // COUNT(field) - count non-null values
        var selector = BuildSelector<object?>(fieldPath);
        return source.Select(selector).Count(x => x != null);
    }

    private object? ComputeSum(IQueryable<TEntity> source, string fieldPath)
    {
        var memberType = GetMemberType(fieldPath);

        if (memberType == typeof(decimal) || memberType == typeof(decimal?))
        {
            var selector = BuildSelector<decimal?>(fieldPath);
            return source.Select(selector).Sum();
        }
        if (memberType == typeof(double) || memberType == typeof(double?))
        {
            var selector = BuildSelector<double?>(fieldPath);
            return source.Select(selector).Sum();
        }
        if (memberType == typeof(float) || memberType == typeof(float?))
        {
            var selector = BuildSelector<float?>(fieldPath);
            return source.Select(selector).Sum();
        }
        if (memberType == typeof(int) || memberType == typeof(int?))
        {
            var selector = BuildSelector<int?>(fieldPath);
            return source.Select(selector).Sum();
        }
        if (memberType == typeof(long) || memberType == typeof(long?))
        {
            var selector = BuildSelector<long?>(fieldPath);
            return source.Select(selector).Sum();
        }

        throw new AtlasCompilationException($"SUM is not supported for type '{memberType.Name}'");
    }

    private object? ComputeAvg(IQueryable<TEntity> source, string fieldPath)
    {
        var memberType = GetMemberType(fieldPath);

        if (memberType == typeof(decimal) || memberType == typeof(decimal?))
        {
            var selector = BuildSelector<decimal?>(fieldPath);
            return source.Select(selector).Average();
        }
        if (memberType == typeof(double) || memberType == typeof(double?))
        {
            var selector = BuildSelector<double?>(fieldPath);
            return source.Select(selector).Average();
        }
        if (memberType == typeof(float) || memberType == typeof(float?))
        {
            var selector = BuildSelector<float?>(fieldPath);
            return source.Select(selector).Average();
        }
        if (memberType == typeof(int) || memberType == typeof(int?))
        {
            var selector = BuildSelector<int?>(fieldPath);
            return source.Select(selector).Average();
        }
        if (memberType == typeof(long) || memberType == typeof(long?))
        {
            var selector = BuildSelector<long?>(fieldPath);
            return source.Select(selector).Average();
        }

        throw new AtlasCompilationException($"AVG is not supported for type '{memberType.Name}'");
    }

    private object? ComputeMin(IQueryable<TEntity> source, string fieldPath)
    {
        var selector = BuildSelector<object?>(fieldPath);
        return source.Select(selector).Min();
    }

    private object? ComputeMax(IQueryable<TEntity> source, string fieldPath)
    {
        var selector = BuildSelector<object?>(fieldPath);
        return source.Select(selector).Max();
    }

    private Expression<Func<TEntity, TResult>> BuildSelector<TResult>(string fieldPath)
    {
        var member = BuildMemberAccess(fieldPath);
        var converted = Expression.Convert(member, typeof(TResult));
        return Expression.Lambda<Func<TEntity, TResult>>(converted, _parameter);
    }

    private Type GetMemberType(string fieldPath)
    {
        var parts = fieldPath.Split('.');
        var currentType = typeof(TEntity);

        foreach (var part in parts)
        {
            var property = currentType.GetProperty(part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                throw new AtlasCompilationException(
                    $"Property '{part}' not found on type '{currentType.Name}' (field path: '{fieldPath}')");
            }

            currentType = property.PropertyType;
        }

        return currentType;
    }

    private Expression BuildMemberAccess(string fieldPath)
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

        return current;
    }
}
