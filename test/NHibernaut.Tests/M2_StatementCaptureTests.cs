using System;
using System.Linq;
using System.Threading.Tasks;
using NHibernate;
using NHibernate.Linq;
using NHibernaut.Core;
using NHibernaut.Core.Model;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

// Milestone 2: full flat SQL capture (timing, params, rows, kind, exceptions, transactions)
// against a real SessionFactory, grouped by NHibernate session id.
public class M2_StatementCaptureTests
{
    private static void Seed(ISessionFactory sf, params string[] names)
    {
        using var session = sf.OpenSession();
        using var tx = session.BeginTransaction();
        foreach (var n in names) session.Save(new Widget { Name = n });
        tx.Commit();
    }

    [Fact]
    public void Flat_select_captures_statement_with_timing_params_rows_and_session_grouping()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        Seed(sf, "alpha", "beta", "gamma");
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            var hits = session.Query<Widget>().Where(w => w.Name == "alpha").ToList();
            Assert.Single(hits);
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        var stmt = Assert.Single(profiled!.Statements);
        Assert.Equal(StatementKind.Select, stmt.Kind);
        Assert.Contains("widgets", stmt.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(stmt.DurationMs >= 0);
        Assert.Equal(1, stmt.RowsRead);
        Assert.Equal(sid, stmt.SessionId);
        Assert.Contains(stmt.Parameters, p => Equals(p.Value, "alpha"));
    }

    [Fact]
    public async Task Async_query_path_captures_statement()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        Seed(sf, "x", "y");
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            var all = await session.Query<Widget>().ToListAsync();
            Assert.Equal(2, all.Count);
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        var stmt = Assert.Single(profiled!.Statements);
        Assert.Equal(StatementKind.Select, stmt.Kind);
        Assert.Equal(2, stmt.RowsRead);
    }

    [Fact]
    public void Insert_captures_statement_with_insert_kind()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            using var tx = session.BeginTransaction();
            session.Save(new Widget { Name = "new" });
            tx.Commit();
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.Contains(profiled!.Statements, s => s.Kind == StatementKind.Insert);
    }

    [Fact]
    public void Transaction_commit_outcome_is_captured()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            using var tx = session.BeginTransaction();
            session.Save(new Widget { Name = "tx" });
            tx.Commit();
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        var transaction = Assert.Single(profiled!.Transactions);
        Assert.Equal(TransactionOutcome.Commit, transaction.Outcome);
    }

    [Fact]
    public void Failing_statement_records_exception_and_rethrows()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        NHibernautRuntime.Store.Clear();

        using var session = sf.OpenSession();
        var sid = session.GetSessionImplementation().SessionId;

        Assert.ThrowsAny<Exception>(() =>
            session.CreateSQLQuery("SELECT * FROM does_not_exist").List());

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.Contains(profiled!.Statements, s => s.Exception != null);
    }
}
