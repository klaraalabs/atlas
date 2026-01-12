namespace Atlas.Ast;

/// <summary>
/// Comparison operators for field filtering.
/// </summary>
public enum CompareOp
{
    /// <summary>Equals (==)</summary>
    Eq,

    /// <summary>Not equals (!=)</summary>
    Neq,

    /// <summary>Greater than (&gt;)</summary>
    Gt,

    /// <summary>Greater than or equal (&gt;=)</summary>
    Gte,

    /// <summary>Less than (&lt;)</summary>
    Lt,

    /// <summary>Less than or equal (&lt;=)</summary>
    Lte,

    /// <summary>String contains</summary>
    Contains,

    /// <summary>String starts with</summary>
    StartsWith,

    /// <summary>String ends with</summary>
    EndsWith,

    /// <summary>Value is in list</summary>
    In,

    /// <summary>Value is null</summary>
    IsNull,

    /// <summary>Value is not null</summary>
    IsNotNull
}
