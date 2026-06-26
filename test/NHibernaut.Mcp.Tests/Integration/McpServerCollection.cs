using Xunit;

namespace NHibernaut.Mcp.Tests.Integration;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class McpServerCollection : ICollectionFixture<McpServerCollectionFixture>
{
    public const string Name = "MCP server integration tests";
}

public sealed class McpServerCollectionFixture
{
}
