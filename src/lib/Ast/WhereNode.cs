namespace Atlas.Ast;

/// <summary>
/// Base class for all nodes in the boolean expression tree.
/// </summary>
public abstract class WhereNode
{
    /// <summary>
    /// Accepts a visitor for tree traversal.
    /// </summary>
    public abstract TResult Accept<TResult>(IWhereNodeVisitor<TResult> visitor);
}

/// <summary>
/// Visitor interface for WhereNode tree traversal.
/// </summary>
public interface IWhereNodeVisitor<TResult>
{
    TResult Visit(GroupNode node);
    TResult Visit(FilterNode node);
    TResult Visit(CollectionNode node);
}
