using System.Text.Json;

namespace Atlas.Helpers;

/// <summary>
/// Compares dictionaries by their content (key-value pairs) rather than reference.
/// Used for implementing DISTINCT on projected results.
/// </summary>
public class DictionaryEqualityComparer : IEqualityComparer<Dictionary<string, object?>>
{
    public static readonly DictionaryEqualityComparer Instance = new();

    public bool Equals(Dictionary<string, object?>? x, Dictionary<string, object?>? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;
        if (x.Count != y.Count)
            return false;

        foreach (var kvp in x)
        {
            if (!y.TryGetValue(kvp.Key, out var yValue))
                return false;

            if (!ValuesEqual(kvp.Value, yValue))
                return false;
        }

        return true;
    }

    public int GetHashCode(Dictionary<string, object?> obj)
    {
        var hash = new HashCode();

        // Sort keys for consistent hashing
        foreach (var key in obj.Keys.OrderBy(k => k))
        {
            hash.Add(key);
            hash.Add(GetValueHashCode(obj[key]));
        }

        return hash.ToHashCode();
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;

        // Handle nested dictionaries
        if (a is Dictionary<string, object?> dictA && b is Dictionary<string, object?> dictB)
        {
            return Instance.Equals(dictA, dictB);
        }

        // Handle lists/arrays
        if (a is IList<object?> listA && b is IList<object?> listB)
        {
            if (listA.Count != listB.Count)
                return false;
            for (int i = 0; i < listA.Count; i++)
            {
                if (!ValuesEqual(listA[i], listB[i]))
                    return false;
            }
            return true;
        }

        // Handle List<Dictionary<string, object?>>
        if (a is List<Dictionary<string, object?>> dictListA &&
            b is List<Dictionary<string, object?>> dictListB)
        {
            if (dictListA.Count != dictListB.Count)
                return false;
            for (int i = 0; i < dictListA.Count; i++)
            {
                if (!Instance.Equals(dictListA[i], dictListB[i]))
                    return false;
            }
            return true;
        }

        return a.Equals(b);
    }

    private static int GetValueHashCode(object? value)
    {
        if (value is null)
            return 0;

        if (value is Dictionary<string, object?> dict)
        {
            return Instance.GetHashCode(dict);
        }

        if (value is IList<object?> list)
        {
            var hash = new HashCode();
            foreach (var item in list)
            {
                hash.Add(GetValueHashCode(item));
            }
            return hash.ToHashCode();
        }

        if (value is List<Dictionary<string, object?>> dictList)
        {
            var hash = new HashCode();
            foreach (var item in dictList)
            {
                hash.Add(Instance.GetHashCode(item));
            }
            return hash.ToHashCode();
        }

        return value.GetHashCode();
    }
}
