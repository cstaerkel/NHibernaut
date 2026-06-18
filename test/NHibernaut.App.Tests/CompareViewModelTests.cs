using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NHibernaut.App.Tests.Infrastructure;
using NHibernaut.App.ViewModels;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class CompareViewModelTests
{
    // SessionSummaryDto: Id, StartedAt, EndedAt, IsSealed, StatementCount, TotalDurationMs,
    //                    TotalRowsRead, WriteCount, AlertCount, MaxSeverity, ThreadCount
    private static SessionSummaryDto MakeSummary(
        string id,
        int statementCount = 0,
        double totalDurationMs = 0,
        int totalRowsRead = 0,
        int writeCount = 0,
        int alertCount = 0) =>
        new(id, DateTimeOffset.UtcNow, null, true, statementCount, totalDurationMs, totalRowsRead, writeCount, alertCount, null, 1);

    private static SessionDetailDto MakeDetail(SessionSummaryDto summary) =>
        new(
            Summary: summary,
            Statements: Array.Empty<StatementDto>(),
            Connections: Array.Empty<ConnectionDto>(),
            Transactions: Array.Empty<TransactionDto>(),
            EntityLoads: Array.Empty<EntityLoadDto>(),
            Writes: Array.Empty<EntityWriteDto>(),
            CollectionInits: Array.Empty<CollectionInitDto>(),
            Alerts: Array.Empty<AlertDto>(),
            EntityCountsByType: new Dictionary<string, int>());

    [Fact]
    public async Task LoadAsync_populates_options()
    {
        var idA = Guid.NewGuid().ToString();
        var idB = Guid.NewGuid().ToString();
        var sessions = new List<SessionSummaryDto>
        {
            MakeSummary(idA, statementCount: 5),
            MakeSummary(idB, statementCount: 10),
        };
        var client = new FakeDashboardClient(sessions: sessions);
        var vm = new CompareViewModel();

        await vm.LoadAsync(client);

        Assert.Equal(2, vm.Options.Count);
        Assert.Equal(idA, vm.Options[0].Dto.Id);
        Assert.Equal(idB, vm.Options[1].Dto.Id);
        Assert.Null(vm.SelectedA);
        Assert.Null(vm.SelectedB);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Selecting_A_and_B_computes_metric_deltas()
    {
        // Session A: 10 queries, 500ms total, 100 rows, 2 writes, 1 alert
        // Session B:  8 queries, 600ms total,  80 rows, 3 writes, 0 alerts
        // B - A deltas: queries = -2 (Better), totalMs = +100 (Worse), rows = -20 (Better), writes = +1 (Worse), alerts = -1 (Better)
        var guidA = Guid.NewGuid();
        var guidB = Guid.NewGuid();
        var summaryA = MakeSummary(guidA.ToString(), statementCount: 10, totalDurationMs: 500, totalRowsRead: 100, writeCount: 2, alertCount: 1);
        var summaryB = MakeSummary(guidB.ToString(), statementCount: 8,  totalDurationMs: 600, totalRowsRead: 80,  writeCount: 3, alertCount: 0);
        var detailA = MakeDetail(summaryA);
        var detailB = MakeDetail(summaryB);

        var sessions = new List<SessionSummaryDto> { summaryA, summaryB };
        var details = new Dictionary<Guid, SessionDetailDto>
        {
            [guidA] = detailA,
            [guidB] = detailB,
        };
        var client = new FakeDashboardClient(sessions: sessions, details: details);
        var vm = new CompareViewModel();
        await vm.LoadAsync(client);

        // Select A then B — CompareAsync fires synchronously since GetSessionDetailAsync returns Task.FromResult
        vm.SelectedA = vm.Options[0]; // guidA session
        vm.SelectedB = vm.Options[1]; // guidB session

        Assert.Equal(5, vm.Rows.Count);

        // Queries: A=10, B=8, Delta=-2 → Better
        var queries = vm.Rows[0];
        Assert.Equal("Queries", queries.Metric);
        Assert.Equal(10, queries.A);
        Assert.Equal(8, queries.B);
        Assert.Equal(-2, queries.Delta);
        Assert.True(queries.IsBetter);
        Assert.False(queries.IsWorse);
        Assert.Equal("Better", queries.State);

        // Total ms: A=500, B=600, Delta=+100 → Worse
        var totalMs = vm.Rows[1];
        Assert.Equal("Total ms", totalMs.Metric);
        Assert.Equal(100, totalMs.Delta);
        Assert.True(totalMs.IsWorse);
        Assert.False(totalMs.IsBetter);
        Assert.Equal("Worse", totalMs.State);

        // Rows read: A=100, B=80, Delta=-20 → Better
        var rowsRead = vm.Rows[2];
        Assert.Equal("Rows read", rowsRead.Metric);
        Assert.Equal(-20, rowsRead.Delta);
        Assert.True(rowsRead.IsBetter);

        // Writes: A=2, B=3, Delta=+1 → Worse
        var writes = vm.Rows[3];
        Assert.Equal("Writes", writes.Metric);
        Assert.Equal(1, writes.Delta);
        Assert.True(writes.IsWorse);

        // Alerts: A=1, B=0, Delta=-1 → Better
        var alerts = vm.Rows[4];
        Assert.Equal("Alerts", alerts.Metric);
        Assert.Equal(-1, alerts.Delta);
        Assert.True(alerts.IsBetter);
    }

    [Fact]
    public async Task RefreshOptions_preserves_selection_by_id_and_rebuilds_rows()
    {
        var guidA = Guid.NewGuid();
        var guidB = Guid.NewGuid();
        var summaryA = MakeSummary(guidA.ToString(), statementCount: 10);
        var summaryB = MakeSummary(guidB.ToString(), statementCount: 8);
        var sessions = new List<SessionSummaryDto> { summaryA, summaryB };
        var details = new Dictionary<Guid, SessionDetailDto>
        {
            [guidA] = MakeDetail(summaryA),
            [guidB] = MakeDetail(summaryB),
        };
        var client = new FakeDashboardClient(sessions: sessions, details: details);
        var vm = new CompareViewModel();
        await vm.LoadAsync(client);

        vm.SelectedA = vm.Options[0]; // guidA
        vm.SelectedB = vm.Options[1]; // guidB
        Assert.Equal(5, vm.Rows.Count);

        var originalA = vm.SelectedA;
        var originalB = vm.SelectedB;

        // Refresh with the SAME ids present (new client returning fresh instances of the same sessions).
        var refreshedClient = new FakeDashboardClient(sessions: sessions, details: details);
        await vm.RefreshOptionsAsync(refreshedClient);

        // Selections preserved by id (re-resolved to the new Option instances).
        Assert.NotNull(vm.SelectedA);
        Assert.NotNull(vm.SelectedB);
        Assert.Equal(guidA, vm.SelectedA!.Id);
        Assert.Equal(guidB, vm.SelectedB!.Id);
        // They point at the NEW option instances, not the stale ones.
        Assert.NotSame(originalA, vm.SelectedA);
        Assert.NotSame(originalB, vm.SelectedB);
        Assert.Same(vm.Options[0], vm.SelectedA);
        Assert.Same(vm.Options[1], vm.SelectedB);

        // Rows still reflect the comparison (re-assigning the selections re-ran CompareAsync).
        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("Queries", vm.Rows[0].Metric);
        Assert.Equal(10, vm.Rows[0].A);
        Assert.Equal(8, vm.Rows[0].B);
    }

    [Fact]
    public async Task RefreshOptions_drops_selection_when_id_no_longer_present()
    {
        var guidA = Guid.NewGuid();
        var guidB = Guid.NewGuid();
        var summaryA = MakeSummary(guidA.ToString(), statementCount: 10);
        var summaryB = MakeSummary(guidB.ToString(), statementCount: 8);
        var details = new Dictionary<Guid, SessionDetailDto>
        {
            [guidA] = MakeDetail(summaryA),
            [guidB] = MakeDetail(summaryB),
        };
        var client = new FakeDashboardClient(sessions: new List<SessionSummaryDto> { summaryA, summaryB }, details: details);
        var vm = new CompareViewModel();
        await vm.LoadAsync(client);
        vm.SelectedA = vm.Options[0];
        vm.SelectedB = vm.Options[1];

        // Refresh with only A still present → B's selection is dropped.
        var refreshedClient = new FakeDashboardClient(sessions: new List<SessionSummaryDto> { summaryA }, details: details);
        await vm.RefreshOptionsAsync(refreshedClient);

        Assert.NotNull(vm.SelectedA);
        Assert.Equal(guidA, vm.SelectedA!.Id);
        Assert.Null(vm.SelectedB);
        Assert.Empty(vm.Rows); // only one side selected
    }

    [Fact]
    public async Task Selecting_only_A_leaves_rows_empty()
    {
        var guidA = Guid.NewGuid();
        var summaryA = MakeSummary(guidA.ToString(), statementCount: 5);
        var sessions = new List<SessionSummaryDto> { summaryA };
        var client = new FakeDashboardClient(sessions: sessions);
        var vm = new CompareViewModel();
        await vm.LoadAsync(client);

        vm.SelectedA = vm.Options[0];

        // SelectedB is still null → rows should be empty
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void DeltaDisplay_shows_plus_for_positive_and_dash_for_negative()
    {
        var better = new CompareRow("X", 10, 8);
        Assert.Equal("-2", better.DeltaDisplay);
        Assert.Equal("Better", better.State);

        var worse = new CompareRow("Y", 5, 10);
        Assert.Equal("+5", worse.DeltaDisplay);
        Assert.Equal("Worse", worse.State);

        var same = new CompareRow("Z", 7, 7);
        Assert.Equal("0", same.DeltaDisplay);
        Assert.Equal("Same", same.State);
    }

    [Fact]
    public async Task Superseded_compare_does_not_double_populate()
    {
        // Three sessions so we can re-select B and supersede an in-flight compare.
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var g3 = Guid.NewGuid();
        var s1 = MakeSummary(g1.ToString(), statementCount: 10);
        var s2 = MakeSummary(g2.ToString(), statementCount: 8);
        var s3 = MakeSummary(g3.ToString(), statementCount: 6);
        var details = new Dictionary<Guid, SessionDetailDto>
        {
            [g1] = MakeDetail(s1),
            [g2] = MakeDetail(s2),
            [g3] = MakeDetail(s3),
        };

        // Gate holds every GetSessionDetailAsync call until released, so both compares stay in flight.
        var gate = new TaskCompletionSource();
        var client = new FakeDashboardClient(sessions: new List<SessionSummaryDto> { s1, s2, s3 }, details: details)
        {
            DetailGate = gate.Task,
        };
        var vm = new CompareViewModel();
        await vm.LoadAsync(client);

        // First compare: A=session1, B=session2 — both detail fetches block on the gate.
        vm.SelectedA = vm.Options[0];
        vm.SelectedB = vm.Options[1]; // assignment triggers CompareAsync (gen 1)

        // Second compare supersedes before the first completes: switch B to session3 (gen 2, still gated).
        vm.SelectedB = vm.Options[2];

        // Release the gate → both gated runs resume; only the newest generation may populate.
        gate.SetResult();

        // Allow the continuations to drain.
        await Task.Yield();
        await Task.Delay(20);

        // Exactly 5 rows (not 10): the superseded gen-1 run returns early on the generation check.
        Assert.Equal(5, vm.Rows.Count);

        // The surviving rows reflect the newest pair (B = session3, 6 queries → Queries delta = 6 - 10 = -4).
        var queries = vm.Rows[0];
        Assert.Equal("Queries", queries.Metric);
        Assert.Equal(10, queries.A);
        Assert.Equal(6, queries.B);
    }

    [Fact]
    public async Task LoadAsync_clears_previous_selections_and_rows()
    {
        var guidA = Guid.NewGuid();
        var guidB = Guid.NewGuid();
        var summaryA = MakeSummary(guidA.ToString(), statementCount: 10);
        var summaryB = MakeSummary(guidB.ToString(), statementCount: 8);
        var sessions = new List<SessionSummaryDto> { summaryA, summaryB };
        var details = new Dictionary<Guid, SessionDetailDto>
        {
            [guidA] = MakeDetail(summaryA),
            [guidB] = MakeDetail(summaryB),
        };
        var vm = new CompareViewModel();
        await vm.LoadAsync(new FakeDashboardClient(sessions: sessions, details: details));

        vm.SelectedA = vm.Options[0];
        vm.SelectedB = vm.Options[1];
        Assert.Equal(5, vm.Rows.Count);

        // Reloading for a new connection must drop the prior comparison, not carry it across.
        await vm.LoadAsync(new FakeDashboardClient(sessions: sessions, details: details));

        Assert.Null(vm.SelectedA);
        Assert.Null(vm.SelectedB);
        Assert.Empty(vm.Rows);
        Assert.Equal(2, vm.Options.Count);
    }
}
