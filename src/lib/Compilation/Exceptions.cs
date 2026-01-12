namespace Atlas.Compilation;

/// <summary>
/// Exception thrown when Atlas fails to compile a query.
/// </summary>
public class AtlasCompilationException : Exception
{
    public AtlasCompilationException(string message) : base(message) { }
    public AtlasCompilationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a query violates security policies.
/// </summary>
public class AtlasSecurityException : Exception
{
    public AtlasSecurityException(string message) : base(message) { }
    public AtlasSecurityException(string message, Exception innerException) : base(message, innerException) { }
}
