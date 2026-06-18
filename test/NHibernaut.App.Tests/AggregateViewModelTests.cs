using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NHibernaut.App.Tests.Infrastructure;
using NHibernaut.App.ViewModels;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class AggregateViewModelTests
{
    private static AggregateRowDto MakeRow(string sql, int execCount, double totalMs, double avgMs, int maxRows, int sessionCount, int nPlusOne) =>
        new(sql, execCount, totalMs, avgMs, maxRows, sessionCount, nPlusOne);

    [Fact]
    public async Task LoadAsync_maps_rows_and_preserves_server_order()
    {
        // Arrange: three rows already sorted by server (descending total ms)
        var rows = new List<AggregateRowDto>
        {
            MakeRow("SELECT a FROM t1", execCount: 10, totalMs: 500.0, avgMs: 50.0, maxRows: 100, sessionCount: 2, nPlusOne: 3),
            MakeRow("SELECT b FROM t2", execCount: 5,  totalMs: 200.0, avgMs: 40.0, maxRows: 50,  sessionCount: 1, nPlusOne: 0),
            MakeRow("SELECT c FROM t3", execCount: 1,  totalMs: 10.0,  avgMs: 10.0, maxRows: 0,   sessionCount: 1, nPlusOne: 0),
        };
        var client = new FakeDashboardClient(aggregate: rows);
        var vm = new AggregateViewModel();

        // Act
        await vm.LoadAsync(client);

        // Assert: same order preserved
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("SELECT a FROM t1", vm.Rows[0].Shape);
        Assert.Equal("SELECT b FROM t2", vm.Rows[1].Shape);
        Assert.Equal("SELECT c FROM t3", vm.Rows[2].Shape);

        // First row field mappings
        Assert.Equal(10, vm.Rows[0].Calls);
        Assert.Equal(500.0, vm.Rows[0].TotalMs);
        Assert.Equal(50.0, vm.Rows[0].AvgMs);
        Assert.Equal(100, vm.Rows[0].MaxRows);
        Assert.Equal(2, vm.Rows[0].Sessions);
        Assert.Equal(3, vm.Rows[0].NPlusOne);
        Assert.Equal("3", vm.Rows[0].NPlusOneDisplay);        // non-zero: show number

        // Second row: NPlusOne == 0 → display "—"
        Assert.Equal(0, vm.Rows[1].NPlusOne);
        Assert.Equal("—", vm.Rows[1].NPlusOneDisplay);

        // Third row: also 0
        Assert.Equal("—", vm.Rows[2].NPlusOneDisplay);
    }

    [Fact]
    public async Task LoadAsync_clears_previous_rows_before_loading()
    {
        var row = MakeRow("SELECT x FROM t", 1, 10.0, 10.0, 0, 1, 0);
        var client = new FakeDashboardClient(aggregate: new List<AggregateRowDto> { row });
        var vm = new AggregateViewModel();

        await vm.LoadAsync(client);
        Assert.Single(vm.Rows);

        // Load again with empty
        var emptyClient = new FakeDashboardClient(aggregate: Array.Empty<AggregateRowDto>());
        await vm.LoadAsync(emptyClient);
        Assert.Empty(vm.Rows);
    }
}
