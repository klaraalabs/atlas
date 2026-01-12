using Atlas.Ast;
using Atlas.Compilation;
using Atlas.Query;

namespace Atlas.Security;

/// <summary>
/// Validates an AtlasQuery against a policy before compilation.
/// </summary>
public class QueryValidator<TEntity>
{
    private readonly AtlasPolicy<TEntity> _policy;

    public QueryValidator(AtlasPolicy<TEntity> policy)
    {
        _policy = policy;
    }

    /// <summary>
    /// Validates the query and throws if invalid.
    /// </summary>
    public void Validate(AtlasQuery query)
    {
        ValidateSelectFields(query.Select);

        if (query.Where is not null)
        {
            var whereNode = WhereClauseParser.Parse(query.Where);
            if (whereNode is not null)
            {
                ValidateWhereNode(whereNode, depth: 1);
            }
        }

        if (query.OrderBy is not null)
        {
            ValidateOrderByFields(query.OrderBy);
        }

        if (query.GroupBy is not null)
        {
            ValidateGroupByFields(query.GroupBy);
        }
    }

    private void ValidateSelectFields(IEnumerable<string> fields)
    {
        var fieldList = fields.ToList();

        // Check max select fields
        if (fieldList.Count > _policy.MaxSelectFieldsValue)
        {
            throw new AtlasSecurityException(
                $"Too many fields selected ({fieldList.Count}). Maximum allowed: {_policy.MaxSelectFieldsValue}.");
        }

        foreach (var fieldSpec in fieldList)
        {
            // Parse to handle aggregate functions like balance.amount.sum
            var selectField = SelectFieldParser.Parse(fieldSpec);

            // count(*) is always allowed
            if (selectField.Field == "*")
            {
                continue;
            }

            // Check navigation depth
            var depth = selectField.Field.Split('.').Length;
            if (depth > _policy.MaxNavigationDepthValue)
            {
                throw new AtlasSecurityException(
                    $"Field '{selectField.Field}' exceeds maximum navigation depth ({depth} > {_policy.MaxNavigationDepthValue}).");
            }

            if (!_policy.IsFieldAllowed(selectField.Field))
            {
                throw CreateFieldNotAllowedException(selectField.Field, "selection");
            }
        }
    }

    private void ValidateWhereNode(WhereNode node, int depth, string? collectionPrefix = null)
    {
        // Check depth limit
        if (depth > _policy.MaxWhereDepthValue)
        {
            throw new AtlasSecurityException(
                $"WHERE clause nesting too deep (depth {depth}). Maximum allowed: {_policy.MaxWhereDepthValue}.");
        }

        switch (node)
        {
            case GroupNode group:
                foreach (var child in group.Nodes)
                {
                    ValidateWhereNode(child, depth + 1, collectionPrefix);
                }
                break;

            case FilterNode filter:
                // Prefix field with collection path if we're inside a collection condition
                var fieldPath = string.IsNullOrEmpty(collectionPrefix)
                    ? filter.Field
                    : $"{collectionPrefix}.{filter.Field}";

                if (!_policy.IsFieldAllowed(fieldPath))
                {
                    throw CreateFieldNotAllowedException(fieldPath, "filtering");
                }

                if (!_policy.IsOperatorAllowed(filter.Op))
                {
                    throw CreateOperatorNotAllowedException(filter.Op);
                }
                break;

            case CollectionNode collection:
                if (!_policy.IsFieldAllowed(collection.Field))
                {
                    throw CreateFieldNotAllowedException(collection.Field, "collection filtering");
                }

                // Validate the inner condition with the collection path as prefix
                if (collection.Condition is not null)
                {
                    ValidateWhereNode(collection.Condition, depth + 1, collection.Field);
                }
                break;
        }
    }

    private void ValidateOrderByFields(IEnumerable<OrderByClause> orderByClauses)
    {
        foreach (var clause in orderByClauses)
        {
            var field = clause.Field.ToLowerInvariant();
            if (!_policy.IsFieldAllowed(field))
            {
                throw CreateFieldNotAllowedException(field, "ordering");
            }
        }
    }

    private void ValidateGroupByFields(IEnumerable<string> groupByFields)
    {
        foreach (var fieldSpec in groupByFields)
        {
            var field = fieldSpec.ToLowerInvariant();
            if (!_policy.IsFieldAllowed(field))
            {
                throw CreateFieldNotAllowedException(field, "grouping");
            }
        }
    }

    private AtlasSecurityException CreateFieldNotAllowedException(string field, string operation)
    {
        var allowedFields = _policy.AllowedFields;
        var allowedList = allowedFields.Count > 0
            ? string.Join(", ", allowedFields.OrderBy(f => f).Take(20))
            : "(none)";

        var message = $"Field '{field}' is not allowed for {operation}. " +
                      $"Allowed fields: {allowedList}";

        if (allowedFields.Count > 20)
        {
            message += $" (and {allowedFields.Count - 20} more)";
        }

        return new AtlasSecurityException(message);
    }

    private AtlasSecurityException CreateOperatorNotAllowedException(CompareOp op)
    {
        var allowedOps = _policy.AllowedOperators;
        var allowedList = allowedOps.Count > 0
            ? string.Join(", ", allowedOps.Select(o => o.ToString().ToLowerInvariant()))
            : "(all operators allowed by default)";

        return new AtlasSecurityException(
            $"Operator '{op.ToString().ToLowerInvariant()}' is not allowed. Allowed operators: {allowedList}");
    }
}
