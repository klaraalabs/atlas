using System.Linq.Expressions;
using System.Reflection;
using Atlas.Ast;

namespace Atlas.Security;

/// <summary>
/// Defines security policies for querying an entity type.
/// </summary>
public class AtlasPolicy<TEntity>
{
    private readonly HashSet<string> _allowedFields = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<CompareOp> _allowedOperators = [];
    private readonly List<Expression<Func<TEntity, bool>>> _globalFilters = [];
    private int _maxLimit = 100;
    private int _defaultLimit = 25;
    private int _maxWhereDepth = 5;
    private int _maxSelectFields = 50;
    private int _maxNavigationDepth = 3;

    /// <summary>
    /// Creates a new policy for the entity type.
    /// </summary>
    public static AtlasPolicy<TEntity> Create() => new();

    /// <summary>
    /// Allows specific fields to be queried and selected.
    /// </summary>
    public AtlasPolicy<TEntity> AllowFields(params Expression<Func<TEntity, object?>>[] fieldSelectors)
    {
        foreach (var selector in fieldSelectors)
        {
            var path = GetFieldPath(selector);
            _allowedFields.Add(path);
        }
        return this;
    }

    /// <summary>
    /// Allows specific field paths by string.
    /// </summary>
    public AtlasPolicy<TEntity> AllowFields(params string[] fieldPaths)
    {
        foreach (var path in fieldPaths)
        {
            _allowedFields.Add(path);
        }
        return this;
    }

    /// <summary>
    /// Allows all public properties of the entity, including navigation properties up to the specified depth.
    /// </summary>
    /// <param name="navigationDepth">How deep to traverse navigation properties. Default is 2 (e.g., "user.name").</param>
    public AtlasPolicy<TEntity> AllowAllFields(int navigationDepth = 2)
    {
        AllowFieldsRecursive(typeof(TEntity), "", navigationDepth, []);
        return this;
    }

    private void AllowFieldsRecursive(Type type, string prefix, int remainingDepth, HashSet<Type> visited)
    {
        if (remainingDepth < 0 || !visited.Add(type))
        {
            return;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            var fieldPath = string.IsNullOrEmpty(prefix)
                ? prop.Name.ToLowerInvariant()
                : $"{prefix}.{prop.Name.ToLowerInvariant()}";

            _allowedFields.Add(fieldPath);

            // Check if this is a navigation property (complex type, not a primitive/string/etc.)
            if (remainingDepth > 0 && IsNavigationType(prop.PropertyType))
            {
                var navType = GetUnderlyingType(prop.PropertyType);
                if (navType is not null)
                {
                    AllowFieldsRecursive(navType, fieldPath, remainingDepth - 1, visited);
                }
            }
        }

        visited.Remove(type);
    }

    private static bool IsNavigationType(Type type)
    {
        // Skip primitives, strings, common value types
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
            type == typeof(Guid) || type == typeof(DateOnly) || type == typeof(TimeOnly))
        {
            return false;
        }

        // Handle nullable value types
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return IsNavigationType(underlying);
        }

        // It's a complex type (entity or collection)
        return true;
    }

    private static Type? GetUnderlyingType(Type type)
    {
        // Handle collections (IEnumerable<T>, ICollection<T>, List<T>, etc.)
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(IEnumerable<>) || genericDef == typeof(ICollection<>) ||
                genericDef == typeof(IList<>) || genericDef == typeof(List<>) ||
                genericDef == typeof(HashSet<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        // Check if it implements IEnumerable<T>
        var enumerable = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is not null && type != typeof(string))
        {
            return enumerable.GetGenericArguments()[0];
        }

        // It's a direct navigation property
        return type;
    }

    /// <summary>
    /// Restricts which comparison operators can be used.
    /// </summary>
    public AtlasPolicy<TEntity> AllowOperators(params CompareOp[] operators)
    {
        foreach (var op in operators)
        {
            _allowedOperators.Add(op);
        }
        return this;
    }

    /// <summary>
    /// Allows all comparison operators.
    /// </summary>
    public AtlasPolicy<TEntity> AllowAllOperators()
    {
        foreach (CompareOp op in Enum.GetValues<CompareOp>())
        {
            _allowedOperators.Add(op);
        }
        return this;
    }

    /// <summary>
    /// Sets the maximum allowed limit for queries.
    /// </summary>
    public AtlasPolicy<TEntity> MaxLimit(int maxLimit)
    {
        _maxLimit = maxLimit;
        return this;
    }

    /// <summary>
    /// Sets the default limit when none is specified.
    /// </summary>
    public AtlasPolicy<TEntity> DefaultLimit(int defaultLimit)
    {
        _defaultLimit = defaultLimit;
        return this;
    }

    /// <summary>
    /// Sets the maximum nesting depth for WHERE clauses (default: 5).
    /// </summary>
    public AtlasPolicy<TEntity> MaxWhereDepth(int maxDepth)
    {
        _maxWhereDepth = maxDepth;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of fields that can be selected (default: 50).
    /// </summary>
    public AtlasPolicy<TEntity> MaxSelectFields(int maxFields)
    {
        _maxSelectFields = maxFields;
        return this;
    }

    /// <summary>
    /// Sets the maximum navigation depth for field paths (default: 3).
    /// E.g., "user.balance.currency" has depth 3.
    /// </summary>
    public AtlasPolicy<TEntity> MaxNavigationDepth(int maxDepth)
    {
        _maxNavigationDepth = maxDepth;
        return this;
    }

    /// <summary>
    /// Adds a global filter that is always applied to queries (row-level security).
    /// Use this for tenant isolation, soft deletes, or other mandatory filters.
    /// </summary>
    public AtlasPolicy<TEntity> WhereAlways(Expression<Func<TEntity, bool>> filter)
    {
        _globalFilters.Add(filter);
        return this;
    }

    /// <summary>
    /// Checks if a field path is allowed.
    /// </summary>
    public bool IsFieldAllowed(string fieldPath)
    {
        // If no fields are explicitly allowed, deny all
        if (_allowedFields.Count == 0)
        {
            return false;
        }

        return _allowedFields.Contains(fieldPath);
    }

    /// <summary>
    /// Checks if an operator is allowed.
    /// </summary>
    public bool IsOperatorAllowed(CompareOp op)
    {
        // If no operators are explicitly allowed, allow all
        if (_allowedOperators.Count == 0)
        {
            return true;
        }

        return _allowedOperators.Contains(op);
    }

    /// <summary>
    /// Gets the effective limit, clamped to max.
    /// </summary>
    public int GetEffectiveLimit(int requestedLimit)
    {
        if (requestedLimit <= 0)
        {
            return _defaultLimit;
        }

        return Math.Min(requestedLimit, _maxLimit);
    }

    /// <summary>
    /// Gets the set of allowed fields.
    /// </summary>
    public IReadOnlySet<string> AllowedFields => _allowedFields;

    /// <summary>
    /// Gets the set of allowed operators.
    /// </summary>
    public IReadOnlySet<CompareOp> AllowedOperators => _allowedOperators;

    /// <summary>
    /// Gets the maximum limit.
    /// </summary>
    public int MaxLimitValue => _maxLimit;

    /// <summary>
    /// Gets the default limit.
    /// </summary>
    public int DefaultLimitValue => _defaultLimit;

    /// <summary>
    /// Gets the maximum WHERE clause nesting depth.
    /// </summary>
    public int MaxWhereDepthValue => _maxWhereDepth;

    /// <summary>
    /// Gets the maximum number of select fields.
    /// </summary>
    public int MaxSelectFieldsValue => _maxSelectFields;

    /// <summary>
    /// Gets the maximum navigation depth.
    /// </summary>
    public int MaxNavigationDepthValue => _maxNavigationDepth;

    /// <summary>
    /// Gets the global filters (row-level security).
    /// </summary>
    public IReadOnlyList<Expression<Func<TEntity, bool>>> GlobalFilters => _globalFilters;

    private static string GetFieldPath(Expression<Func<TEntity, object?>> selector)
    {
        var expression = selector.Body;

        // Handle boxing conversions
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            expression = unary.Operand;
        }

        var parts = new List<string>();

        while (expression is MemberExpression member)
        {
            parts.Add(member.Member.Name.ToLowerInvariant());
            expression = member.Expression!;
        }

        parts.Reverse();
        return string.Join(".", parts);
    }
}
