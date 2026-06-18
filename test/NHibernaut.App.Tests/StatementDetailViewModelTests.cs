using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using NHibernaut.App.Services;
using NHibernaut.App.ViewModels;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class StatementDetailViewModelTests
{
    // Helpers ----------------------------------------------------------------

    private static EditorLinkService EditorLinks() =>
        new(NullLogger<EditorLinkService>.Instance);

    private static StatementDto MakeStatement(
        string id,
        string? stackTrace = null,
        IReadOnlyList<ParamDto>? parameters = null) =>
        new(
            Id: id,
            SessionId: Guid.NewGuid().ToString(),
            Sql: "SELECT 1",
            NormalizedSql: null,
            Kind: "Select",
            StartedAt: DateTimeOffset.UtcNow,
            DurationMs: 10,
            RowsAffected: null,
            RowsRead: null,
            Exception: null,
            StackTrace: stackTrace,
            EntityLoadCount: 0,
            CollectionInitCount: 0,
            Parameters: parameters ?? Array.Empty<ParamDto>());

    private static SessionDetailDto MakeDetail(
        IReadOnlyList<StatementDto> statements,
        IReadOnlyList<EntityWriteDto>? writes = null,
        IReadOnlyList<EntityLoadDto>? loads = null,
        IReadOnlyList<CollectionInitDto>? collectionInits = null) =>
        new(
            Summary: new SessionSummaryDto(
                Id: Guid.NewGuid().ToString(),
                StartedAt: DateTimeOffset.UtcNow,
                EndedAt: DateTimeOffset.UtcNow,
                IsSealed: true,
                StatementCount: statements.Count,
                TotalDurationMs: 0,
                TotalRowsRead: 0,
                WriteCount: 0,
                AlertCount: 0,
                MaxSeverity: null,
                ThreadCount: 1),
            Statements: statements,
            Connections: Array.Empty<ConnectionDto>(),
            Transactions: Array.Empty<TransactionDto>(),
            EntityLoads: loads ?? Array.Empty<EntityLoadDto>(),
            Writes: writes ?? Array.Empty<EntityWriteDto>(),
            CollectionInits: collectionInits ?? Array.Empty<CollectionInitDto>(),
            Alerts: Array.Empty<AlertDto>(),
            EntityCountsByType: new Dictionary<string, int>());

    // Tests ------------------------------------------------------------------

    [Fact]
    public void Writes_verb_mapping_and_grouping()
    {
        var stmtId = Guid.NewGuid().ToString();
        var stmt = MakeStatement(stmtId);
        var writes = new[]
        {
            new EntityWriteDto("Insert", "App.Domain.Blog", "1", stmtId, false),
            new EntityWriteDto("Update", "App.Domain.Post", "10", stmtId, false),
            new EntityWriteDto("Update", "App.Domain.Post", "11", stmtId, false),
        };
        var detail = MakeDetail(new[] { stmt }, writes: writes);

        var vm = new StatementDetailViewModel(stmt, detail, "vscode", null);

        Assert.Equal(2, vm.Writes.Count);
        Assert.Contains("created", vm.Writes[0]);
        Assert.Contains("Blog #1", vm.Writes[0]);
        Assert.Contains("updated", vm.Writes[1]);
        Assert.Contains("Post #10", vm.Writes[1]);
        Assert.Contains("Post #11", vm.Writes[1]);
    }

    [Fact]
    public void Writes_does_not_include_other_statements_writes()
    {
        var stmtId = Guid.NewGuid().ToString();
        var otherId = Guid.NewGuid().ToString();
        var stmt = MakeStatement(stmtId);
        var writes = new[]
        {
            new EntityWriteDto("Insert", "App.Domain.Blog", "1", otherId, false),
        };
        var detail = MakeDetail(new[] { stmt }, writes: writes);

        var vm = new StatementDetailViewModel(stmt, detail, "vscode", null);

        Assert.Empty(vm.Writes);
    }

    [Fact]
    public void Hydrated_groups_by_entity_type()
    {
        var stmtId = Guid.NewGuid().ToString();
        var stmt = MakeStatement(stmtId);
        var loads = new[]
        {
            new EntityLoadDto("App.Domain.Blog", "1", stmtId),
            new EntityLoadDto("App.Domain.Blog", "2", stmtId),
            new EntityLoadDto("App.Domain.Blog", "3", stmtId),
        };
        var detail = MakeDetail(new[] { stmt }, loads: loads);

        var vm = new StatementDetailViewModel(stmt, detail, "vscode", null);

        Assert.Single(vm.Hydrated);
        Assert.Contains("3 × Blog", vm.Hydrated[0]);
        Assert.Contains("#1", vm.Hydrated[0]);
        Assert.Contains("#2", vm.Hydrated[0]);
        Assert.Contains("#3", vm.Hydrated[0]);
    }

    [Fact]
    public void Source_link_built_from_stack_trace()
    {
        var stmtId = Guid.NewGuid().ToString();
        // Real CaptureContext format: TypeName.MethodName (file:line)
        var stackTrace = "App.Controllers.BlogController.Create (C:\\src\\App\\Controllers\\BlogController.cs:55)";
        var stmt = MakeStatement(stmtId, stackTrace: stackTrace);
        var detail = MakeDetail(new[] { stmt });

        var vm = new StatementDetailViewModel(stmt, detail, "vscode", EditorLinks());

        Assert.True(vm.HasSource);
        Assert.NotNull(vm.SourceLink);
        Assert.StartsWith("vscode://file/", vm.SourceLink);
        Assert.Contains("BlogController.cs:55", vm.SourceLink);
    }

    [Fact]
    public void No_source_link_when_stack_trace_is_null()
    {
        var stmtId = Guid.NewGuid().ToString();
        var stmt = MakeStatement(stmtId, stackTrace: null);
        var detail = MakeDetail(new[] { stmt });

        var vm = new StatementDetailViewModel(stmt, detail, "vscode", EditorLinks());

        Assert.False(vm.HasSource);
        Assert.Null(vm.SourceLink);
    }

    [Fact]
    public void No_source_link_when_editor_links_is_null()
    {
        var stmtId = Guid.NewGuid().ToString();
        var stackTrace = "App.Controllers.Foo (C:\\src\\Foo.cs:1)";
        var stmt = MakeStatement(stmtId, stackTrace: stackTrace);
        var detail = MakeDetail(new[] { stmt });

        var vm = new StatementDetailViewModel(stmt, detail, "vscode", editorLinks: null);

        Assert.False(vm.HasSource);
        Assert.Null(vm.SourceLink);
    }

    [Fact]
    public void Empty_collections_when_no_writes_or_loads()
    {
        var stmtId = Guid.NewGuid().ToString();
        var stmt = MakeStatement(stmtId);
        var detail = MakeDetail(new[] { stmt });

        var vm = new StatementDetailViewModel(stmt, detail, "vscode", null);

        Assert.Empty(vm.Writes);
        Assert.Empty(vm.Hydrated);
        Assert.Empty(vm.Initialized);
    }

    [Fact]
    public void Initialized_shows_short_role()
    {
        var stmtId = Guid.NewGuid().ToString();
        var stmt = MakeStatement(stmtId);
        var inits = new[]
        {
            new CollectionInitDto("App.Domain.Blog.Posts", stmtId),
        };
        var detail = MakeDetail(new[] { stmt }, collectionInits: inits);

        var vm = new StatementDetailViewModel(stmt, detail, "vscode", null);

        Assert.Single(vm.Initialized);
        Assert.Equal("Blog.Posts", vm.Initialized[0]);
    }
}
