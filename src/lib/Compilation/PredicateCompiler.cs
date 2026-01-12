using System.Linq.Expressions;
using System.Reflection;
using Atlas.Ast;

namespace Atlas.Compilation;

/// <summary>
/// Compiles a WhereNode AST into an Expression&lt;Func&lt;T, bool&gt;&gt;.
/// </summary>
public class PredicateCompiler<TEntity> : IWhereNodeVisitor<Expression>
{
    private readonly ParameterExpression _parameter;
    private readonly Func<string, bool>? _fieldValidator;

    public PredicateCompiler(Func<string, bool>? fieldValidator = null)
    {
        _parameter = Expression.Parameter(typeof(TEntity), "e");
        _fieldValidator = fieldValidator;
    }

    /// <summary>
    /// Compiles a WhereNode tree into a predicate expression.
    /// </summary>
    public Expression<Func<TEntity, bool>>? Compile(WhereNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var body = node.Accept(this);
        return Expression.Lambda<Func<TEntity, bool>>(body, _parameter);
    }

    public Expression Visit(GroupNode node)
    {
        if (node.Nodes.Count == 0)
        {
            // Empty AND = true, empty OR = false
            return Expression.Constant(node.Op == LogicalOp.And);
        }

        var expressions = node.Nodes.Select(n => n.Accept(this)).ToList();

        return node.Op switch
        {
            LogicalOp.And => expressions.Aggregate(Expression.AndAlso),
            LogicalOp.Or => expressions.Aggregate(Expression.OrElse),
            _ => throw new InvalidOperationException($"Unknown logical operator: {node.Op}")
        };
    }

    public Expression Visit(FilterNode node)
    {
        // Validate field if validator provided
        if (_fieldValidator is not null && !_fieldValidator(node.Field))
        {
            throw new AtlasSecurityException($"Field '{node.Field}' is not allowed.");
        }

        var (memberAccess, nullChecks) = BuildMemberAccessWithNullChecks(node.Field);
        var comparison = BuildComparison(memberAccess, node.Op, node.Value);

        // Combine null checks with comparison: nav1 != null && nav2 != null && comparison
        if (nullChecks.Count > 0)
        {
            var combined = nullChecks.Aggregate(Expression.AndAlso);
            return Expression.AndAlso(combined, comparison);
        }

        return comparison;
    }

    public Expression Visit(CollectionNode node)
    {
        // Validate collection field if validator provided
        if (_fieldValidator is not null && !_fieldValidator(node.Field))
        {
            throw new AtlasSecurityException($"Collection field '{node.Field}' is not allowed.");
        }

        // Build member access to the collection property
        var collectionMember = BuildMemberAccess(node.Field);
        var collectionType = collectionMember.Type;

        // Get the element type of the collection
        var elementType = GetCollectionElementType(collectionType);
        if (elementType is null)
        {
            throw new AtlasCompilationException(
                $"Field '{node.Field}' is not a collection. Collection operators (any/all/none) require an IEnumerable<T> property.");
        }

        // Build the inner predicate for the element type
        Expression? innerPredicate = null;
        if (node.Condition is not null)
        {
            // Create a parameter for the collection element
            var elementParam = Expression.Parameter(elementType, "x");
            var innerCompiler = new ElementPredicateCompiler(elementParam);
            var innerBody = innerCompiler.Compile(node.Condition);
            innerPredicate = Expression.Lambda(innerBody, elementParam);
        }

        // Get the appropriate Enumerable method (Any, All)
        var method = node.Op switch
        {
            CollectionOp.Any => GetEnumerableMethod("Any", elementType, innerPredicate is not null),
            CollectionOp.All => GetEnumerableMethod("All", elementType, withPredicate: true),
            CollectionOp.None => GetEnumerableMethod("Any", elementType, innerPredicate is not null),
            _ => throw new AtlasCompilationException($"Unknown collection operator: {node.Op}")
        };

        // Build the method call expression
        Expression result;
        if (innerPredicate is not null)
        {
            result = Expression.Call(method, collectionMember, innerPredicate);
        }
        else if (node.Op == CollectionOp.Any || node.Op == CollectionOp.None)
        {
            // Any() without predicate - just check if collection has elements
            result = Expression.Call(method, collectionMember);
        }
        else
        {
            // All() requires a predicate
            throw new AtlasCompilationException("Collection operator 'all' requires a condition.");
        }

        // For 'none', negate the result
        if (node.Op == CollectionOp.None)
        {
            result = Expression.Not(result);
        }

        // Handle null collection: collection != null && (Any/All/None)
        var nullCheck = Expression.NotEqual(collectionMember, Expression.Constant(null, collectionType));
        return Expression.AndAlso(nullCheck, result);
    }

    private static Type? GetCollectionElementType(Type type)
    {
        // Check for IEnumerable<T>
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        // Check implemented interfaces
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static MethodInfo GetEnumerableMethod(string methodName, Type elementType, bool withPredicate)
    {
        var methods = typeof(Enumerable).GetMethods()
            .Where(m => m.Name == methodName && m.IsGenericMethodDefinition);

        var method = withPredicate
            ? methods.First(m => m.GetParameters().Length == 2)
            : methods.First(m => m.GetParameters().Length == 1);

        return method.MakeGenericMethod(elementType);
    }

    /// <summary>
    /// Builds a member access expression for a dot-separated field path,
    /// also returning null checks for any navigation properties traversed.
    /// </summary>
    private (Expression Member, List<Expression> NullChecks) BuildMemberAccessWithNullChecks(string fieldPath)
    {
        var parts = fieldPath.Split('.');
        Expression current = _parameter;
        var nullChecks = new List<Expression>();

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var property = current.Type.GetProperty(part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                throw new AtlasCompilationException(
                    $"Property '{part}' not found on type '{current.Type.Name}' (field path: '{fieldPath}')");
            }

            current = Expression.Property(current, property);

            // Add null check for reference types that are not the final property
            // (navigation properties that could be null)
            var isLastProperty = i == parts.Length - 1;
            var isReferenceType = !property.PropertyType.IsValueType;
            var isNullableValueType = Nullable.GetUnderlyingType(property.PropertyType) != null;

            if (!isLastProperty && (isReferenceType || isNullableValueType))
            {
                nullChecks.Add(Expression.NotEqual(current, Expression.Constant(null, property.PropertyType)));
            }
        }

        return (current, nullChecks);
    }

    /// <summary>
    /// Builds a member access expression for a dot-separated field path (without null checks).
    /// </summary>
    private Expression BuildMemberAccess(string fieldPath)
    {
        var (member, _) = BuildMemberAccessWithNullChecks(fieldPath);
        return member;
    }

    /// <summary>
    /// Builds a comparison expression for the given operator.
    /// </summary>
    private Expression BuildComparison(Expression member, CompareOp op, object? value)
    {
        return op switch
        {
            CompareOp.Eq => BuildEquality(member, value, equal: true),
            CompareOp.Neq => BuildEquality(member, value, equal: false),
            CompareOp.Gt => BuildBinaryComparison(member, value, Expression.GreaterThan),
            CompareOp.Gte => BuildBinaryComparison(member, value, Expression.GreaterThanOrEqual),
            CompareOp.Lt => BuildBinaryComparison(member, value, Expression.LessThan),
            CompareOp.Lte => BuildBinaryComparison(member, value, Expression.LessThanOrEqual),
            CompareOp.Contains => PredicateCompiler<TEntity>.BuildStringMethod(member, value, "Contains"),
            CompareOp.StartsWith => PredicateCompiler<TEntity>.BuildStringMethod(member, value, "StartsWith"),
            CompareOp.EndsWith => PredicateCompiler<TEntity>.BuildStringMethod(member, value, "EndsWith"),
            CompareOp.In => BuildInExpression(member, value),
            CompareOp.IsNull => Expression.Equal(member, Expression.Constant(null, member.Type)),
            CompareOp.IsNotNull => Expression.NotEqual(member, Expression.Constant(null, member.Type)),
            _ => throw new AtlasCompilationException($"Unknown comparison operator: {op}")
        };
    }

    private BinaryExpression BuildEquality(Expression member, object? value, bool equal)
    {
        var constant = PredicateCompiler<TEntity>.CreateConstantExpression(value, member.Type);
        return equal
            ? Expression.Equal(member, constant)
            : Expression.NotEqual(member, constant);
    }

    private BinaryExpression BuildBinaryComparison(Expression member, object? value, Func<Expression, Expression, BinaryExpression> factory)
    {
        var constant = PredicateCompiler<TEntity>.CreateConstantExpression(value, member.Type);
        return factory(member, constant);
    }

    private static BinaryExpression BuildStringMethod(Expression member, object? value, string methodName)
    {
        if (member.Type != typeof(string))
        {
            throw new AtlasCompilationException(
                $"String operation '{methodName}' cannot be applied to non-string type '{member.Type.Name}'");
        }

        var method = typeof(string).GetMethod(methodName, [typeof(string)])!;
        var constant = Expression.Constant(value?.ToString() ?? string.Empty);

        // Handle null: member != null && member.Method(value)
        var nullCheck = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        var methodCall = Expression.Call(member, method, constant);
        return Expression.AndAlso(nullCheck, methodCall);
    }

    private Expression BuildInExpression(Expression member, object? value)
    {
        if (value is not IEnumerable<object> list)
        {
            throw new AtlasCompilationException("'In' operator requires an array value.");
        }

        var values = list.ToList();
        if (values.Count == 0)
        {
            return Expression.Constant(false);
        }

        // Build: member == v1 || member == v2 || ...
        var comparisons = values
            .Select(v => BuildEquality(member, v, equal: true))
            .ToList();

        return comparisons.Aggregate(Expression.OrElse);
    }

    private static ConstantExpression CreateConstantExpression(object? value, Type targetType)
    {
        if (value is null)
        {
            return Expression.Constant(null, targetType);
        }

        var convertedValue = ConvertValue(value, targetType);
        return Expression.Constant(convertedValue, targetType);
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null)
        {
            return null;
        }

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value.GetType() == underlyingType)
        {
            return value;
        }

        // Special case for Guid
        if (underlyingType == typeof(Guid) && value is string guidStr)
        {
            return Guid.Parse(guidStr);
        }

        // Special case for enums
        if (underlyingType.IsEnum)
        {
            return value is string enumStr
                ? Enum.Parse(underlyingType, enumStr, ignoreCase: true)
                : Enum.ToObject(underlyingType, value);
        }

        return Convert.ChangeType(value, underlyingType);
    }
}

/// <summary>
/// Compiles predicates for collection elements (used inside Any/All/None).
/// </summary>
internal class ElementPredicateCompiler : IWhereNodeVisitor<Expression>
{
    private readonly ParameterExpression _parameter;

    public ElementPredicateCompiler(ParameterExpression parameter)
    {
        _parameter = parameter;
    }

    public Expression Compile(WhereNode node)
    {
        return node.Accept(this);
    }

    public Expression Visit(GroupNode node)
    {
        if (node.Nodes.Count == 0)
        {
            return Expression.Constant(node.Op == LogicalOp.And);
        }

        var expressions = node.Nodes.Select(n => n.Accept(this)).ToList();

        return node.Op switch
        {
            LogicalOp.And => expressions.Aggregate(Expression.AndAlso),
            LogicalOp.Or => expressions.Aggregate(Expression.OrElse),
            _ => throw new InvalidOperationException($"Unknown logical operator: {node.Op}")
        };
    }

    public Expression Visit(FilterNode node)
    {
        var (memberAccess, nullChecks) = BuildMemberAccessWithNullChecks(node.Field);
        var comparison = BuildComparison(memberAccess, node.Op, node.Value);

        if (nullChecks.Count > 0)
        {
            var combined = nullChecks.Aggregate(Expression.AndAlso);
            return Expression.AndAlso(combined, comparison);
        }

        return comparison;
    }

    public Expression Visit(CollectionNode node)
    {
        // Nested collection queries are not supported for now
        throw new AtlasCompilationException(
            $"Nested collection operators are not supported. Cannot use '{node.Op}' inside another collection predicate.");
    }

    private (Expression Member, List<Expression> NullChecks) BuildMemberAccessWithNullChecks(string fieldPath)
    {
        var parts = fieldPath.Split('.');
        Expression current = _parameter;
        var nullChecks = new List<Expression>();

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var property = current.Type.GetProperty(part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                throw new AtlasCompilationException(
                    $"Property '{part}' not found on type '{current.Type.Name}' (field path: '{fieldPath}')");
            }

            current = Expression.Property(current, property);

            var isLastProperty = i == parts.Length - 1;
            var isReferenceType = !property.PropertyType.IsValueType;
            var isNullableValueType = Nullable.GetUnderlyingType(property.PropertyType) != null;

            if (!isLastProperty && (isReferenceType || isNullableValueType))
            {
                nullChecks.Add(Expression.NotEqual(current, Expression.Constant(null, property.PropertyType)));
            }
        }

        return (current, nullChecks);
    }

    private Expression BuildComparison(Expression member, CompareOp op, object? value)
    {
        return op switch
        {
            CompareOp.Eq => BuildEquality(member, value, equal: true),
            CompareOp.Neq => BuildEquality(member, value, equal: false),
            CompareOp.Gt => BuildBinaryComparison(member, value, Expression.GreaterThan),
            CompareOp.Gte => BuildBinaryComparison(member, value, Expression.GreaterThanOrEqual),
            CompareOp.Lt => BuildBinaryComparison(member, value, Expression.LessThan),
            CompareOp.Lte => BuildBinaryComparison(member, value, Expression.LessThanOrEqual),
            CompareOp.Contains => BuildStringMethod(member, value, "Contains"),
            CompareOp.StartsWith => BuildStringMethod(member, value, "StartsWith"),
            CompareOp.EndsWith => BuildStringMethod(member, value, "EndsWith"),
            CompareOp.In => BuildInExpression(member, value),
            CompareOp.IsNull => Expression.Equal(member, Expression.Constant(null, member.Type)),
            CompareOp.IsNotNull => Expression.NotEqual(member, Expression.Constant(null, member.Type)),
            _ => throw new AtlasCompilationException($"Unknown comparison operator: {op}")
        };
    }

    private BinaryExpression BuildEquality(Expression member, object? value, bool equal)
    {
        var constant = CreateConstantExpression(value, member.Type);
        return equal
            ? Expression.Equal(member, constant)
            : Expression.NotEqual(member, constant);
    }

    private BinaryExpression BuildBinaryComparison(Expression member, object? value, Func<Expression, Expression, BinaryExpression> factory)
    {
        var constant = CreateConstantExpression(value, member.Type);
        return factory(member, constant);
    }

    private static BinaryExpression BuildStringMethod(Expression member, object? value, string methodName)
    {
        if (member.Type != typeof(string))
        {
            throw new AtlasCompilationException(
                $"String operation '{methodName}' cannot be applied to non-string type '{member.Type.Name}'");
        }

        var method = typeof(string).GetMethod(methodName, [typeof(string)])!;
        var constant = Expression.Constant(value?.ToString() ?? string.Empty);

        var nullCheck = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        var methodCall = Expression.Call(member, method, constant);
        return Expression.AndAlso(nullCheck, methodCall);
    }

    private Expression BuildInExpression(Expression member, object? value)
    {
        if (value is not IEnumerable<object> list)
        {
            throw new AtlasCompilationException("'In' operator requires an array value.");
        }

        var values = list.ToList();
        if (values.Count == 0)
        {
            return Expression.Constant(false);
        }

        var comparisons = values
            .Select(v => BuildEquality(member, v, equal: true))
            .ToList();

        return comparisons.Aggregate(Expression.OrElse);
    }

    private static ConstantExpression CreateConstantExpression(object? value, Type targetType)
    {
        if (value is null)
        {
            return Expression.Constant(null, targetType);
        }

        var convertedValue = ConvertValue(value, targetType);
        return Expression.Constant(convertedValue, targetType);
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null)
        {
            return null;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value.GetType() == underlyingType)
        {
            return value;
        }

        if (underlyingType == typeof(Guid) && value is string guidStr)
        {
            return Guid.Parse(guidStr);
        }

        if (underlyingType.IsEnum)
        {
            return value is string enumStr
                ? Enum.Parse(underlyingType, enumStr, ignoreCase: true)
                : Enum.ToObject(underlyingType, value);
        }

        return Convert.ChangeType(value, underlyingType);
    }
}
