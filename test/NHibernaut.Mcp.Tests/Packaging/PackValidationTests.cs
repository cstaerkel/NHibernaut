using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace NHibernaut.Mcp.Tests.Packaging;

public sealed class PackValidationTests
{
    [Fact]
    public void Mcp_project_is_packaged_as_dotnet_tool_with_readme_metadata()
    {
        var project = XDocument.Load(ProjectPath());
        var root = project.Root ?? throw new InvalidOperationException("Project XML is missing a root element.");

        Assert.Equal("true", Value(root, "PackAsTool"));
        Assert.Equal("nhibernaut-mcp", Value(root, "ToolCommandName"));
        Assert.Equal("NHibernaut.Mcp", Value(root, "PackageId"));

        var readme = root.Descendants("None")
            .SingleOrDefault(element =>
                string.Equals((string?)element.Attribute("Include"), @"..\..\README.md", StringComparison.Ordinal));

        Assert.NotNull(readme);
        Assert.Equal("true", (string?)readme!.Attribute("Pack"));
        Assert.Equal(@"\", (string?)readme.Attribute("PackagePath"));
    }

    private static string Value(XElement root, string name)
        => root.Descendants(name).Single().Value;

    private static string ProjectPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solution = Path.Combine(directory.FullName, "NHibernaut.sln");
            if (File.Exists(solution))
            {
                return Path.Combine(directory.FullName, "src", "NHibernaut.Mcp", "NHibernaut.Mcp.csproj");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing NHibernaut.sln.");
    }
}
