namespace Atlas.Ast;

/// <summary>
/// A node that groups multiple filter conditions with a logical operator (AND/OR).
/// </summary>
public class GroupNode : WhereNode
{
    /// <summary>
    /// The logical operator combining the child nodes.
    /// </summary>
    public LogicalOp Op { get; set; }

    /// <summary>
    /// The child nodes to combine.
    /// </summary>
    public List<WhereNode> Nodes { get; set; } = [];

    public GroupNode() { }

    public GroupNode(LogicalOp op, params WhereNode[] nodes)
    {
        Op = op;
        Nodes = [.. nodes];
    }

    public override TResult Accept<TResult>(IWhereNodeVisitor<TResult> visitor)
        => visitor.Visit(this);
}
