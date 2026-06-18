using System;
using System.Collections.Generic;
using NHibernaut.App.ViewModels;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class SessionDetailViewModelTests
{
    // Helpers ----------------------------------------------------------------

    private static StatementDto MakeStatement(
        string id,
        string? normalizedSql,
        long startedAtMs,
        double durationMs,
        string kind = "Select") =>
        new(
            Id: id,
            SessionId: Guid.NewGuid().ToString(),
            Sql: $"SELECT 1 -- {id}",
            NormalizedSql: normalizedSql,
            Kind: kind,
            StartedAt: DateTimeOffset.FromUnixTimeMilliseconds(startedAtMs),
            DurationMs: durationMs,
            RowsAffected: null,
            RowsRead: null,
            Exception: null,
            StackTrace: null,
            EntityLoadCount: 0,
            CollectionInitCount: 0,
            Parameters: Array.Empty<ParamDto>());

    private static SessionDetailDto MakeDetail(
        IReadOnlyList<StatementDto> statements,
        IReadOnlyList<AlertDto>? alerts = null) =>
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
                AlertCount: alerts?.Count ?? 0,
                MaxSeverity: null,
                ThreadCount: 1),
            Statements: statements,
            Connections: Array.Empty<ConnectionDto>(),
            Transactions: Array.Empty<TransactionDto>(),
            EntityLoads: Array.Empty<EntityLoadDto>(),
            Writes: Array.Empty<EntityWriteDto>(),
            CollectionInits: Array.Empty<CollectionInitDto>(),
            Alerts: alerts ?? Array.Empty<AlertDto>(),
            EntityCountsByType: new Dictionary<string, int>());

    // Tests ------------------------------------------------------------------

    [Fact]
    public void Waterfall_scales_left_and_width_by_start_and_duration()
    {
        // t0=1000ms, s1 starts at 1000ms duration=200ms → left=0, width=200/(1200-1000)*100 = 100
        // s2 starts at 1100ms duration=100ms → left=(1100-1000)/(1200-1000)*100=50, width=100/200*100=50
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();
        var s1 = MakeStatement(id1, "s1sql", startedAtMs: 1000, durationMs: 200);
        var s2 = MakeStatement(id2, "s2sql", startedAtMs: 1100, durationMs: 100);

        var vm = new SessionDetailViewModel();
        vm.Build(MakeDetail(new[] { s1, s2 }));

        Assert.Equal(2, vm.Bars.Count);

        // Bar 0: starts at t0, so Left should be ~0
        Assert.Equal(0.0, vm.Bars[0].Left, precision: 6);
        // Width = 200 / 200 * 100 = 100
        Assert.Equal(100.0, vm.Bars[0].Width, precision: 6);

        // Bar 1: Left = (1100 - 1000) / 200 * 100 = 50
        Assert.Equal(50.0, vm.Bars[1].Left, precision: 6);
        // Width = 100 / 200 * 100 = 50
        Assert.Equal(50.0, vm.Bars[1].Width, precision: 6);

        // Star surface: LeftStar/WidthStar mirror Left/Width; RightStar is the clamped remainder.
        foreach (var bar in vm.Bars)
        {
            Assert.Equal(bar.Left, bar.LeftStar.Value, precision: 6);
            Assert.Equal(bar.Width, bar.WidthStar.Value, precision: 6);
            Assert.Equal(Math.Max(100 - bar.Left - bar.Width, 0), bar.RightStar.Value, precision: 6);
        }
    }

    [Fact]
    public void Waterfall_rightstar_clamps_to_zero_when_left_plus_width_exceeds_100()
    {
        // The only way left% + width% can exceed 100 is the 0.6 width floor on a tiny-duration
        // statement pinned to the far right edge: a long first statement defines t1, and a
        // zero-duration final statement starts exactly at t1 (left = 100) → width floored to 0.6
        // → left + width = 100.6 > 100 → RightStar clamps to 0.
        // span = t1 - t0 = (1000 + 500) - 1000 = 500ms.
        // Bar 1 (final, zero-dur at 1500): left = (1500-1000)/500*100 = 100, width floored to 0.6.
        var s1 = MakeStatement(Guid.NewGuid().ToString(), "s1sql", startedAtMs: 1000, durationMs: 500);
        var s2 = MakeStatement(Guid.NewGuid().ToString(), "s2sql", startedAtMs: 1500, durationMs: 0);

        var vm = new SessionDetailViewModel();
        vm.Build(MakeDetail(new[] { s1, s2 }));

        Assert.Equal(2, vm.Bars.Count);

        var finalBar = vm.Bars[1];
        Assert.Equal(100.0, finalBar.Left, precision: 6);
        Assert.Equal(0.6, finalBar.Width, precision: 6);              // floored
        Assert.True(finalBar.Left + finalBar.Width > 100);            // precondition: overflows
        Assert.Equal(0.0, finalBar.RightStar.Value, precision: 6);     // clamped to 0
        Assert.Equal(finalBar.Left, finalBar.LeftStar.Value, precision: 6);
        Assert.Equal(finalBar.Width, finalBar.WidthStar.Value, precision: 6);
    }

    [Fact]
    public void Waterfall_zero_duration_width_uses_floor()
    {
        // A zero-duration statement must still render a visible sliver (width floor = 0.6),
        // never 0. A second statement gives the span a positive denominator.
        var s1 = MakeStatement(Guid.NewGuid().ToString(), "s1sql", startedAtMs: 1000, durationMs: 0);
        var s2 = MakeStatement(Guid.NewGuid().ToString(), "s2sql", startedAtMs: 1000, durationMs: 100);

        var vm = new SessionDetailViewModel();
        vm.Build(MakeDetail(new[] { s1, s2 }));

        Assert.Equal(0.6, vm.Bars[0].Width, precision: 6);
        Assert.Equal(0.6, vm.Bars[0].WidthStar.Value, precision: 6);
    }

    [Fact]
    public void ColorKey_slow_wins_over_kind()
    {
        // Slow Select (>200ms) → "Slow"; normal Select (≤200ms) → "Select".
        var slow = MakeStatement(Guid.NewGuid().ToString(), "slowsql", startedAtMs: 1000, durationMs: 250, kind: "Select");
        var normal = MakeStatement(Guid.NewGuid().ToString(), "normalsql", startedAtMs: 1300, durationMs: 100, kind: "Select");

        var vm = new SessionDetailViewModel();
        vm.Build(MakeDetail(new[] { slow, normal }));

        Assert.Equal("Slow", vm.Bars[0].ColorKey);
        Assert.True(vm.Bars[0].IsSlow);

        Assert.Equal("Select", vm.Bars[1].ColorKey);
        Assert.False(vm.Bars[1].IsSlow);
    }

    [Fact]
    public void Consecutive_same_shape_statements_fold_into_one_group()
    {
        var norm = "SELECT ? FROM thing";
        var s1 = MakeStatement(Guid.NewGuid().ToString(), norm, 1000, 10);
        var s2 = MakeStatement(Guid.NewGuid().ToString(), norm, 1020, 10);
        var s3 = MakeStatement(Guid.NewGuid().ToString(), norm, 1040, 10);
        var s4 = MakeStatement(Guid.NewGuid().ToString(), "SELECT ? FROM other", 1060, 10);

        var vm = new SessionDetailViewModel();
        vm.Build(MakeDetail(new[] { s1, s2, s3, s4 }));

        Assert.Equal(2, vm.Groups.Count);

        Assert.True(vm.Groups[0].IsGroup);
        Assert.Equal(3, vm.Groups[0].Count);
        // Groups are collapsed by default.
        Assert.True(vm.Groups[0].IsFolded);
        Assert.False(vm.Groups[0].ShowItems);   // folded group hides its rows

        Assert.False(vm.Groups[1].IsGroup);
        Assert.Equal(1, vm.Groups[1].Count);
        Assert.True(vm.Groups[1].ShowItems);     // single-item group always shows its row
    }

    [Fact]
    public void HighlightAlert_sets_isHighlighted_on_related_bars()
    {
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();
        var id3 = Guid.NewGuid().ToString();
        var s1 = MakeStatement(id1, "s1", 1000, 10);
        var s2 = MakeStatement(id2, "s2", 1020, 10);
        var s3 = MakeStatement(id3, "s3", 1040, 10);

        var alertDto = new AlertDto(
            Id: Guid.NewGuid().ToString(),
            Type: "NPlusOne",
            Severity: "Warning",
            Title: "N+1",
            Description: "Repeated loads",
            Suggestion: null,
            RelatedStatementIds: new[] { id2 });

        var vm = new SessionDetailViewModel();
        vm.Build(MakeDetail(new[] { s1, s2, s3 }, new[] { alertDto }));

        Assert.Single(vm.Alerts);
        var alert = vm.Alerts[0];

        // Execute the highlight command
        vm.HighlightAlertCommand.Execute(alert);

        Assert.False(vm.Bars[0].IsHighlighted);
        Assert.True(vm.Bars[1].IsHighlighted);
        Assert.False(vm.Bars[2].IsHighlighted);
    }

    [Fact]
    public void SelectStatement_sets_selected()
    {
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();
        var s1 = MakeStatement(id1, "s1", 1000, 10);
        var s2 = MakeStatement(id2, "s2", 1020, 10);

        var vm = new SessionDetailViewModel();
        vm.Build(MakeDetail(new[] { s1, s2 }));

        var targetId = Guid.Parse(id2);
        vm.SelectStatementCommand.Execute(targetId);

        Assert.NotNull(vm.SelectedStatement);
        Assert.Equal(targetId, vm.SelectedStatement!.Id);
    }
}
