using System;
using System.Collections.Generic;
using System.Linq;
using NHibernaut.Core;
using NHibernaut.Core.Analysis;
using NHibernaut.Core.Model;
using Xunit;

namespace NHibernaut.Tests;

public class M5_SqlNormalizerTests
{
    [Theory]
    [InlineData("SELECT * FROM t WHERE id = @p0", "SELECT * FROM t WHERE id = ?")]
    [InlineData("SELECT * FROM t WHERE id = 5", "SELECT * FROM t WHERE id = ?")]
    [InlineData("SELECT * FROM t WHERE name = 'abc'", "SELECT * FROM t WHERE name = ?")]
    [InlineData("select   *  from   t", "select * from t")]
    public void Normalizes_literals_params_and_whitespace(string input, string expected)
        => Assert.Equal(expected, SqlNormalizer.Normalize(input));

    [Fact]
    public void Same_shape_with_different_values_normalizes_equal()
        => Assert.Equal(SqlNormalizer.Normalize("WHERE x = 1"), SqlNormalizer.Normalize("WHERE x = 2"));

    [Fact]
    public void Identifiers_with_digits_are_preserved()
        => Assert.Contains("col1", SqlNormalizer.Normalize("select col1 from t"));
}

public class M5_DetectorTests
{
    private static readonly NHibernautOptions Opt = new();

    private static ProfiledStatement Select(string sql, int rows = 0, double ms = 0, DateTimeOffset at = default)
        => new()
        {
            Sql = sql,
            NormalizedSql = SqlNormalizer.Normalize(sql),
            Kind = StatementKind.Select,
            RowsRead = rows,
            DurationMs = ms,
            StartedAt = at
        };

    private static List<Alert> Run(IAlertDetector d, ProfiledSession s) => d.Detect(s, Opt).ToList();

    [Fact]
    public void SelectNPlusOne_fires_on_repeated_shape()
    {
        var s = new ProfiledSession();
        for (var i = 0; i < 10; i++) s.Statements.Add(Select($"select * from rack where server_id = {i}"));
        var alerts = Run(new SelectNPlusOneDetector(), s);
        var a = Assert.Single(alerts);
        Assert.Equal("SelectNPlusOne", a.Type);
        Assert.Equal(AlertSeverity.Warning, a.Severity);
        Assert.Equal(10, a.RelatedStatementIds.Count);
    }

    [Fact]
    public void SelectNPlusOne_does_not_fire_below_threshold()
    {
        var s = new ProfiledSession();
        for (var i = 0; i < 5; i++) s.Statements.Add(Select($"select * from rack where server_id = {i}"));
        Assert.Empty(Run(new SelectNPlusOneDetector(), s));
    }

    [Fact]
    public void SelectNPlusOne_fires_on_collection_init_role_when_no_query_shape()
    {
        var s = new ProfiledSession();
        for (var i = 0; i < 10; i++) s.CollectionInits.Add(new CollectionInit { Role = "Server.Racks" });
        var a = Assert.Single(Run(new SelectNPlusOneDetector(), s));
        Assert.Equal("SelectNPlusOne", a.Type);
    }

    [Fact]
    public void TooManyQueries_fires_above_threshold()
    {
        var s = new ProfiledSession();
        for (var i = 0; i < 51; i++) s.Statements.Add(Select($"select {i}"));
        var a = Assert.Single(Run(new TooManyQueriesDetector(), s));
        Assert.Equal(AlertSeverity.Warning, a.Severity);
    }

    [Fact]
    public void UnboundedResultSet_fires_for_unlimited_large_select()
    {
        var s = new ProfiledSession();
        s.Statements.Add(Select("select * from widgets", rows: 150));
        var a = Assert.Single(Run(new UnboundedResultSetDetector(), s));
        Assert.Equal("UnboundedResultSet", a.Type);
    }

    [Fact]
    public void UnboundedResultSet_ignores_limited_query()
    {
        var s = new ProfiledSession();
        s.Statements.Add(Select("select * from widgets limit 10", rows: 150));
        Assert.Empty(Run(new UnboundedResultSetDetector(), s));
    }

    [Fact]
    public void TooManyRows_fires_above_threshold()
    {
        var s = new ProfiledSession();
        s.Statements.Add(Select("select * from widgets", rows: 1001));
        Assert.Single(Run(new TooManyRowsDetector(), s));
    }

    [Fact]
    public void TooManyJoins_fires_above_threshold()
    {
        var s = new ProfiledSession();
        s.Statements.Add(Select("select * from a join b join c join d join e join f join g"));
        var a = Assert.Single(Run(new TooManyJoinsDetector(), s));
        Assert.Equal(AlertSeverity.Info, a.Severity);
    }

    [Fact]
    public void SlowQuery_fires_above_threshold()
    {
        var s = new ProfiledSession();
        s.Statements.Add(Select("select * from widgets", ms: 250));
        Assert.Single(Run(new SlowQueryDetector(), s));
    }

    [Fact]
    public void DuplicateQuery_fires_for_identical_sql_and_params()
    {
        var s = new ProfiledSession();
        for (var i = 0; i < 2; i++)
        {
            var st = new ProfiledStatement { Sql = "select * from t where id = @p0", Kind = StatementKind.Select };
            st.Parameters.Add(new ParamCapture { Name = "@p0", Value = 7 });
            s.Statements.Add(st);
        }
        var a = Assert.Single(Run(new DuplicateQueryDetector(), s));
        Assert.Equal(AlertSeverity.Info, a.Severity);
    }

    [Fact]
    public void CrossThreadSession_fires_when_multiple_threads()
    {
        var s = new ProfiledSession();
        s.ThreadIds.Add(1);
        s.ThreadIds.Add(2);
        var a = Assert.Single(Run(new CrossThreadSessionDetector(), s));
        Assert.Equal(AlertSeverity.Error, a.Severity);
    }

    [Fact]
    public void WriteWithoutTransaction_fires_for_uncovered_write()
    {
        var s = new ProfiledSession();
        s.Statements.Add(new ProfiledStatement { Sql = "insert into t ...", Kind = StatementKind.Insert, StartedAt = DateTimeOffset.UtcNow });
        // no transactions
        var a = Assert.Single(Run(new WriteWithoutTransactionDetector(), s));
        Assert.Equal(AlertSeverity.Warning, a.Severity);
    }

    [Fact]
    public void WriteWithoutTransaction_ignores_covered_write()
    {
        var now = DateTimeOffset.UtcNow;
        var s = new ProfiledSession();
        s.Statements.Add(new ProfiledStatement { Sql = "insert into t ...", Kind = StatementKind.Insert, StartedAt = now });
        s.Transactions.Add(new ProfiledTransaction { BeganAt = now.AddSeconds(-1), CompletedAt = now.AddSeconds(1), Outcome = TransactionOutcome.Commit });
        Assert.Empty(Run(new WriteWithoutTransactionDetector(), s));
    }

    [Fact]
    public void TooManyWrites_fires_above_threshold()
    {
        var s = new ProfiledSession();
        for (var i = 0; i < 51; i++) s.Writes.Add(new EntityWrite { Kind = WriteKind.Insert, EntityType = "T" });
        Assert.Single(Run(new TooManyWritesDetector(), s));
    }

    [Fact]
    public void SuperfluousUpdate_fires_for_no_change_update()
    {
        var s = new ProfiledSession();
        s.Writes.Add(new EntityWrite { Kind = WriteKind.Update, EntityType = "T", NoActualChange = true });
        var a = Assert.Single(Run(new SuperfluousUpdateDetector(), s));
        Assert.Equal(AlertSeverity.Info, a.Severity);
    }
}

public class M5_FailSafeTests
{
    private sealed class ThrowingDetector : IAlertDetector
    {
        public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Throwing_detector_does_not_surface_and_other_detectors_still_run()
    {
        var pipeline = new AnalysisPipeline(new IAlertDetector[]
        {
            new ThrowingDetector(),
            new TooManyQueriesDetector()
        });

        var s = new ProfiledSession();
        for (var i = 0; i < 51; i++) s.Statements.Add(new ProfiledStatement { Sql = $"select {i}", Kind = StatementKind.Select });

        var ex = Record.Exception(() => pipeline.Analyze(s, new NHibernautOptions()));
        Assert.Null(ex); // the throwing detector was swallowed
        Assert.Contains(s.Alerts, a => a.Type == "TooManyQueries"); // the healthy detector still ran
    }
}
