namespace Atlas.Ast;

/// <summary>
/// Collection predicate operators for querying collection navigation properties.
/// </summary>
public enum CollectionOp
{
    /// <summary>
    /// At least one element in the collection matches the condition.
    /// </summary>
    Any,

    /// <summary>
    /// All elements in the collection match the condition.
    /// </summary>
    All,

    /// <summary>
    /// No elements in the collection match the condition (equivalent to !Any).
    /// </summary>
    None
}
