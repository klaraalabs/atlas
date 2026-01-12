using System.Text.Json.Nodes;

namespace Atlas.Compilation;

/// <summary>
/// Transforms flat dot-notation dictionaries into nested object structures.
/// </summary>
public static class ResultTransformer
{
    /// <summary>
    /// Transforms a flat dictionary with dot-notation keys into a nested structure.
    /// </summary>
    /// <example>
    /// Input: { "id": "...", "balance.amount": 100, "balance.currency": "USD" }
    /// Output: { "id": "...", "balance": { "amount": 100, "currency": "USD" } }
    /// If all nested values are null, the parent becomes null:
    /// Input: { "id": "...", "balance.amount": null, "balance.currency": null }
    /// Output: { "id": "...", "balance": null }
    /// </example>
    public static Dictionary<string, object?> ToNested(Dictionary<string, object?> flat)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in flat)
        {
            SetNestedValue(result, kvp.Key, kvp.Value);
        }

        // Collapse nested objects where all values are null
        CollapseNullObjects(result);

        return result;
    }

    /// <summary>
    /// Transforms a list of flat dictionaries into nested structures.
    /// </summary>
    public static List<Dictionary<string, object?>> ToNested(IEnumerable<Dictionary<string, object?>> flatList)
    {
        return flatList.Select(ToNested).ToList();
    }

    private static void SetNestedValue(Dictionary<string, object?> dict, string path, object? value)
    {
        var parts = path.Split('.');

        if (parts.Length == 1)
        {
            // Simple key, just set it
            dict[path] = value;
            return;
        }

        // Navigate/create nested dictionaries
        var current = dict;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];

            if (!current.TryGetValue(part, out var existing))
            {
                var nested = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[part] = nested;
                current = nested;
            }
            else if (existing is Dictionary<string, object?> existingDict)
            {
                current = existingDict;
            }
            else
            {
                // Conflict: existing value is not a dictionary
                // Create a wrapper with the existing value
                var nested = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[part] = nested;
                current = nested;
            }
        }

        // Set the final value
        current[parts[^1]] = value;
    }

    /// <summary>
    /// Recursively collapses nested dictionaries where all values are null into a single null.
    /// </summary>
    private static void CollapseNullObjects(Dictionary<string, object?> dict)
    {
        var keysToCollapse = new List<string>();

        foreach (var kvp in dict)
        {
            if (kvp.Value is Dictionary<string, object?> nested)
            {
                // First, recursively process nested dictionaries
                CollapseNullObjects(nested);

                // Then check if all values in this nested dict are null
                if (IsAllNull(nested))
                {
                    keysToCollapse.Add(kvp.Key);
                }
            }
        }

        // Replace all-null dictionaries with null
        foreach (var key in keysToCollapse)
        {
            dict[key] = null;
        }
    }

    /// <summary>
    /// Checks if all values in a dictionary are null (recursively for nested dicts).
    /// </summary>
    private static bool IsAllNull(Dictionary<string, object?> dict)
    {
        foreach (var kvp in dict)
        {
            if (kvp.Value is Dictionary<string, object?> nested)
            {
                if (!IsAllNull(nested))
                {
                    return false;
                }
            }
            else if (kvp.Value is not null)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Converts a nested dictionary to a JsonObject for clean serialization.
    /// </summary>
    public static JsonObject ToJsonObject(Dictionary<string, object?> nested)
    {
        var obj = new JsonObject();

        foreach (var kvp in nested)
        {
            obj[kvp.Key] = ToJsonNode(kvp.Value);
        }

        return obj;
    }

    private static JsonNode? ToJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            Dictionary<string, object?> dict => ToJsonObject(dict),
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            decimal d => JsonValue.Create(d),
            double dbl => JsonValue.Create(dbl),
            float f => JsonValue.Create(f),
            DateTime dt => JsonValue.Create(dt),
            DateTimeOffset dto => JsonValue.Create(dto),
            Guid g => JsonValue.Create(g),
            _ => JsonValue.Create(value.ToString())
        };
    }
}
