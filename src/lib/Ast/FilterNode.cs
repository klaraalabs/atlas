namespace Atlas.Ast;

/// <summary>
/// A leaf node representing a single field comparison.
/// </summary>
public class FilterNode : WhereNode
{
    /// <summary>
    /// The field path to filter on (e.g., "balance.amount").
    /// </summary>
    public required string Field { get; set; }

    /// <summary>
    /// The comparison operator.
    /// </summary>
    public required CompareOp Op { get; set; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    public object? Value { get; set; }

    public override TResult Accept<TResult>(IWhereNodeVisitor<TResult> visitor)
        => visitor.Visit(this);
}
