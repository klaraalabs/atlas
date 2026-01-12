namespace Atlas.Query;

/// <summary>
/// Exception thrown when an Atlas query is invalid.
/// </summary>
public class AtlasQueryException : Exception
{
    public AtlasQueryException(string message) : base(message) { }
    public AtlasQueryException(string message, Exception innerException) : base(message, innerException) { }
}
