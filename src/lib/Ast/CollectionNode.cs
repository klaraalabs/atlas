namespace Atlas.Ast;

/// <summary>
/// Represents a collection predicate (any/all/none) on a collection navigation property.
/// Example: orders.any(amount > 100)
/// </summary>
public class CollectionNode : WhereNode
{
    /// <summary>
    /// The collection navigation property path (e.g., "orders").
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// The collection operator (any, all, none).
    /// </summary>
    public required CollectionOp Op { get; init; }

    /// <summary>
    /// The condition to evaluate on collection elements.
    /// If null for Any/None, checks if collection is empty/non-empty.
    /// </summary>
    public WhereNode? Condition { get; init; }

    public override TResult Accept<TResult>(IWhereNodeVisitor<TResult> visitor)
    {
        return visitor.Visit(this);
    }
}
