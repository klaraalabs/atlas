using System.Text.Json;
using Atlas.Ast;

namespace Atlas.Query;

/// <summary>
/// Parses JSON WhereClause into the internal AST representation.
/// </summary>
public static class WhereClauseParser
{
    private static readonly Dictionary<string, CompareOp> CompareOpMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"] = CompareOp.Eq,
        ["neq"] = CompareOp.Neq,
        ["gt"] = CompareOp.Gt,
        ["gte"] = CompareOp.Gte,
        ["lt"] = CompareOp.Lt,
        ["lte"] = CompareOp.Lte,
        ["contains"] = CompareOp.Contains,
        ["startsWith"] = CompareOp.StartsWith,
        ["endsWith"] = CompareOp.EndsWith,
        ["in"] = CompareOp.In,
        ["isNull"] = CompareOp.IsNull,
        ["isNotNull"] = CompareOp.IsNotNull
    };

    private static readonly Dictionary<string, CollectionOp> CollectionOpMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["any"] = CollectionOp.Any,
        ["all"] = CollectionOp.All,
        ["none"] = CollectionOp.None
    };

    /// <summary>
    /// Parses a WhereClause into a WhereNode AST.
    /// </summary>
    public static WhereNode? Parse(WhereClause? clause)
    {
        if (clause is null)
        {
            return null;
        }

        var op = clause.Op.ToLowerInvariant();

        // Check if it's a logical group
        if (op is "and" or "or")
        {
            var logicalOp = op == "and" ? LogicalOp.And : LogicalOp.Or;
            var nodes = clause.Nodes?
                .Select(Parse)
                .Where(n => n is not null)
                .Cast<WhereNode>()
                .ToList() ?? [];

            return new GroupNode(logicalOp, [.. nodes]);
        }

        // Check if it's a collection operator (any/all/none)
        if (CollectionOpMap.TryGetValue(op, out var collectionOp))
        {
            if (string.IsNullOrEmpty(clause.Field))
            {
                throw new AtlasQueryException($"Collection operation '{clause.Op}' requires a 'field' property specifying the collection.");
            }

            return new CollectionNode
            {
                Field = clause.Field.ToLowerInvariant(),
                Op = collectionOp,
                Condition = Parse(clause.Condition)
            };
        }

        // It's a filter node
        if (string.IsNullOrEmpty(clause.Field))
        {
            throw new AtlasQueryException($"Filter operation '{clause.Op}' requires a 'field' property.");
        }

        if (!CompareOpMap.TryGetValue(clause.Op, out var compareOp))
        {
            throw new AtlasQueryException($"Unknown comparison operator: '{clause.Op}'");
        }

        var value = ConvertValue(clause.Value, clause.Type);
        return new FilterNode
        {
            Field = clause.Field.ToLowerInvariant(),
            Op = compareOp,
            Value = value
        };
    }

    private static object? ConvertValue(object? value, string? typeHint)
    {
        if (value is null)
        {
            return null;
        }

        // Handle JsonElement from System.Text.Json
        if (value is JsonElement jsonElement)
        {
            return typeHint?.ToLowerInvariant() switch
            {
                "guid" => Guid.Parse(jsonElement.GetString()!),
                "decimal" => jsonElement.GetDecimal(),
                "int" or "integer" => jsonElement.GetInt32(),
                "long" => jsonElement.GetInt64(),
                "double" => jsonElement.GetDouble(),
                "bool" or "boolean" => jsonElement.GetBoolean(),
                "datetime" => DateTime.Parse(jsonElement.GetString()!),
                "datetimeoffset" => DateTimeOffset.Parse(jsonElement.GetString()!),
                "string" => jsonElement.GetString(),
                _ => ConvertJsonElement(jsonElement)
            };
        }

        return value;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var i) => i,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            _ => element.GetRawText()
        };
    }
}
