using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Compilation;

/// <summary>
/// Automatically includes navigation properties based on field paths.
/// </summary>
public static class NavigationIncluder
{
    /// <summary>
    /// Adds Include statements for all navigation properties needed by the given field paths.
    /// </summary>
    public static IQueryable<TEntity> IncludeNavigations<TEntity>(
        IQueryable<TEntity> source,
        IEnumerable<string> fieldPaths) where TEntity : class
    {
        var navigations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fieldPath in fieldPaths)
        {
            var parts = fieldPath.Split('.');
            if (parts.Length <= 1)
            {
                continue; // No navigation needed for top-level fields
            }

            // Build navigation paths for each level except the last (which is the property)
            var currentType = typeof(TEntity);
            var navigationPath = new List<string>();

            for (var i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i];
                var property = currentType.GetProperty(part,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (property is null)
                {
                    break;
                }

                // Check if it's a navigation property (reference type, not string, not primitive collection)
                if (IsNavigationProperty(property))
                {
                    navigationPath.Add(property.Name);
                    navigations.Add(string.Join(".", navigationPath));
                }

                currentType = GetElementTypeOrSelf(property.PropertyType);
            }
        }

        // Apply includes
        foreach (var nav in navigations.OrderBy(n => n.Length))
        {
            source = source.Include(nav);
        }

        return source;
    }

    private static bool IsNavigationProperty(PropertyInfo property)
    {
        var type = property.PropertyType;

        // Not a navigation if it's a value type
        if (type.IsValueType)
        {
            return false;
        }

        // Not a navigation if it's string
        if (type == typeof(string))
        {
            return false;
        }

        // It's either a reference navigation or collection navigation
        return true;
    }

    private static Type GetElementTypeOrSelf(Type type)
    {
        // For collections, return the element type
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(ICollection<>) ||
                genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(IList<>) ||
                genericDef == typeof(List<>) ||
                genericDef == typeof(HashSet<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return type;
    }
}
