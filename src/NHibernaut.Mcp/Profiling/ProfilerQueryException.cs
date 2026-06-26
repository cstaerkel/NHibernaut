using System;

namespace NHibernaut.Mcp.Profiling;

public sealed class ProfilerQueryException : Exception
{
    public ProfilerQueryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
