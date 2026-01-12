using System.Linq.Expressions;
using System.Reflection;

namespace Atlas.Compilation;

/// <summary>
/// Compiles a list of field paths into a Select projection that returns a dynamic object.
/// </summary>
public class ProjectionCompiler<TEntity>
{
    private readonly ParameterExpression _parameter;
    private readonly Func<string, bool>? _fieldValidator;

    public ProjectionCompiler(Func<string, bool>? fieldValidator = null)
    {
        _parameter = Expression.Parameter(typeof(TEntity), "e");
        _fieldValidator = fieldValidator;
    }

    /// <summary>
    /// Compiles select fields into a projection that returns a Dictionary&lt;string, object?&gt;.
    /// Groups collection fields together to produce array of objects instead of parallel arrays.
    /// </summary>
    public Expression<Func<TEntity, Dictionary<string, object?>>> Compile(IEnumerable<string> fields)
    {
        var fieldList = fields.ToList();

        if (fieldList.Count == 0)
        {
            throw new AtlasCompilationException("At least one field must be selected.");
        }

        // Validate all fields
        foreach (var field in fieldList)
        {
            if (_fieldValidator is not null && !_fieldValidator(field))
            {
                throw new AtlasSecurityException($"Field '{field}' is not allowed.");
            }
        }

        // Separate collection fields from simple fields
        // Collection fields are like "orders.amount", "orders.status" - group by collection path
        var simpleFields = new List<string>();
        var collectionGroups = new Dictionary<string, List<string>>(); // collectionPath -> subfields

        foreach (var field in fieldList)
        {
            var collectionInfo = GetCollectionFieldInfo(field);
            if (collectionInfo is not null)
            {
                var (collectionPath, subField) = collectionInfo.Value;
                if (!collectionGroups.TryGetValue(collectionPath, out List<string>? value))
                {
                    value = new List<string>();
                    collectionGroups[collectionPath] = value;
                }

                value.Add(subField);
            }
            else
            {
                simpleFields.Add(field);
            }
        }

        // Build Dictionary initialization
        var dictionaryType = typeof(Dictionary<string, object?>);
        var addMethod = dictionaryType.GetMethod("Add", [typeof(string), typeof(object)])!;

        var bindings = new List<ElementInit>();

        // Add simple (non-collection) fields
        foreach (var field in simpleFields)
        {
            var memberAccess = BuildMemberAccess(field);
            var boxed = Expression.Convert(memberAccess, typeof(object));
            var key = Expression.Constant(field);
            bindings.Add(Expression.ElementInit(addMethod, key, boxed));
        }

        // Add collection field groups as arrays of objects
        foreach (var (collectionPath, subFields) in collectionGroups)
        {
            var collectionProjection = BuildCollectionObjectProjection(collectionPath, subFields);
            var boxed = Expression.Convert(collectionProjection, typeof(object));
            var key = Expression.Constant(collectionPath);
            bindings.Add(Expression.ElementInit(addMethod, key, boxed));
        }

        var newDict = Expression.New(dictionaryType);
        var initDict = Expression.ListInit(newDict, bindings);

        return Expression.Lambda<Func<TEntity, Dictionary<string, object?>>>(initDict, _parameter);
    }

    /// <summary>
    /// Checks if a field path accesses a collection property and returns the collection path and subfield.
    /// Returns null if not a collection field.
    /// </summary>
    private (string CollectionPath, string SubField)? GetCollectionFieldInfo(string fieldPath)
    {
        var parts = fieldPath.Split('.');
        Expression current = _parameter;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            var property = current.Type.GetProperty(part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                return null;
            }

            var elementType = GetCollectionElementType(property.PropertyType);
            if (elementType is not null)
            {
                // This is a collection - return the path up to here and the remaining path
                var collectionPath = string.Join(".", parts.Take(i + 1));
                var subField = string.Join(".", parts.Skip(i + 1));
                return (collectionPath, subField);
            }

            current = Expression.Property(current, property);
        }

        return null;
    }

    /// <summary>
    /// Builds a projection that returns collection.Select(x => new Dictionary { {subField1, x.Prop1}, ... }).ToArray()
    /// </summary>
    private MethodCallExpression BuildCollectionObjectProjection(string collectionPath, List<string> subFields)
    {
        // Navigate to the collection property
        var parts = collectionPath.Split('.');
        Expression collection = _parameter;
        Type? elementType = null;

        foreach (var part in parts)
        {
            var property = collection.Type.GetProperty(part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) ?? throw new AtlasCompilationException(
                    $"Property '{part}' not found on type '{collection.Type.Name}' (field path: '{collectionPath}')");

            collection = Expression.Property(collection, property);
            elementType = GetCollectionElementType(property.PropertyType);
        }

        if (elementType is null)
        {
            throw new AtlasCompilationException($"'{collectionPath}' is not a collection.");
        }

        // Build the Select lambda: x => new Dictionary<string, object?> { {"field1", x.Field1}, ... }
        var param = Expression.Parameter(elementType, "x");
        var dictionaryType = typeof(Dictionary<string, object?>);
        var addMethod = dictionaryType.GetMethod("Add", [typeof(string), typeof(object)])!;

        var elementBindings = new List<ElementInit>();
        foreach (var subField in subFields)
        {
            // Navigate through the subfield path
            Expression memberAccess = param;
            foreach (var subPart in subField.Split('.'))
            {
                var property = memberAccess.Type.GetProperty(subPart,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (property is null)
                {
                    throw new AtlasCompilationException(
                        $"Property '{subPart}' not found on type '{memberAccess.Type.Name}' (field path: '{collectionPath}.{subField}')");
                }

                memberAccess = Expression.Property(memberAccess, property);
            }

            var boxed = Expression.Convert(memberAccess, typeof(object));
            var key = Expression.Constant(subField);
            elementBindings.Add(Expression.ElementInit(addMethod, key, boxed));
        }

        var newDict = Expression.New(dictionaryType);
        var initDict = Expression.ListInit(newDict, elementBindings);
        var lambda = Expression.Lambda(initDict, param);

        // Get Enumerable.Select method
        var selectMethod = typeof(Enumerable)
            .GetMethods()
            .First(m => m.Name == "Select" && m.GetParameters().Length == 2)
            .MakeGenericMethod(elementType, dictionaryType);

        // Build: collection.Select(x => new Dictionary { ... })
        var selectCall = Expression.Call(selectMethod, collection, lambda);

        // Convert to array: .ToArray()
        var toArrayMethod = typeof(Enumerable)
            .GetMethod("ToArray")!
            .MakeGenericMethod(dictionaryType);

        return Expression.Call(toArrayMethod, selectCall);
    }

    /// <summary>
    /// Compiles select fields into an anonymous type projection.
    /// Returns IQueryable so EF Core can translate to SQL.
    /// </summary>
    public Expression<Func<TEntity, TResult>> CompileTyped<TResult>(IEnumerable<string> fields)
        where TResult : new()
    {
        var fieldList = fields.ToList();
        var resultType = typeof(TResult);

        var bindings = new List<MemberBinding>();

        foreach (var field in fieldList)
        {
            if (_fieldValidator is not null && !_fieldValidator(field))
            {
                throw new AtlasSecurityException($"Field '{field}' is not allowed.");
            }

            var memberAccess = BuildMemberAccess(field);

            // Find matching property on result type (use last segment of path)
            var resultPropertyName = field.Contains('.')
                ? field.Split('.').Last()
                : field;

            var resultProperty = resultType.GetProperty(resultPropertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (resultProperty is not null)
            {
                var converted = Expression.Convert(memberAccess, resultProperty.PropertyType);
                bindings.Add(Expression.Bind(resultProperty, converted));
            }
        }

        var newExpr = Expression.New(resultType);
        var memberInit = Expression.MemberInit(newExpr, bindings);

        return Expression.Lambda<Func<TEntity, TResult>>(memberInit, _parameter);
    }

    /// <summary>
    /// Builds a member access expression for a dot-separated field path.
    /// For simple navigation properties only - collection fields are handled by BuildCollectionObjectProjection.
    /// </summary>
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

    /// <summary>
    /// Gets the element type if the type is a collection, otherwise null.
    /// </summary>
    private static Type? GetCollectionElementType(Type type)
    {
        // Skip strings - they implement IEnumerable<char> but aren't collections
        if (type == typeof(string))
        {
            return null;
        }

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
}
