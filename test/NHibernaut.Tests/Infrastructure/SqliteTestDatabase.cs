using System;
using Microsoft.Data.Sqlite;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Mapping.ByCode;
using NHibernate.Tool.hbm2ddl;
using NHibernaut.Core;
using NHibernaut.Tests.Domain;

namespace NHibernaut.Tests.Infrastructure;

// Owns a shared-cache in-memory SQLite database for the lifetime of a test.
//
// SQLite ":memory:" databases are per-connection and vanish when that connection closes;
// NHibernate opens/closes a connection per session, so the schema would disappear mid-test.
// Fix (Spec 0.6): use a uniquely-named shared-cache in-memory DB and hold ONE keep-alive
// connection open for the database's whole lifetime. All NHibernate sessions reuse the same
// underlying DB; the unique name isolates tests from each other.
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public string ConnectionString { get; }

    public SqliteTestDatabase()
    {
        ConnectionString = $"Data Source=nhi_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    /// <summary>Builds an NHibernate configuration for this database. Maps Widget by default.</summary>
    public Configuration BuildConfiguration(Action<ModelMapper>? configureMappings = null)
    {
        var cfg = new Configuration();
        cfg.DataBaseIntegration(db =>
        {
            db.Driver<MicrosoftDataSqliteDriver>();
            db.Dialect<SQLiteDialect>();
            db.ConnectionString = ConnectionString;
            db.LogSqlInConsole = false;
            // Microsoft.Data.Sqlite doesn't implement GetSchema("DataTypes"), which NHibernate's
            // SchemaMetadataUpdater calls during BuildSessionFactory when keyword auto-import is on.
            // Disable it (irrelevant for the profiler's purpose: exercising capture, not DDL fidelity).
            db.KeywordsAutoImport = Hbm2DDLKeyWords.None;
        });

        var mapper = new ModelMapper();
        if (configureMappings is null)
            mapper.AddMapping<WidgetMap>();
        else
            configureMappings(mapper);
        cfg.AddMapping(mapper.CompileMappingForAllExplicitlyAddedEntities());

        return cfg;
    }

    /// <summary>
    /// Creates the schema on the keep-alive connection so DDL hits the shared DB directly and is
    /// never routed through the profiler (keeping captured-statement counts deterministic).
    /// </summary>
    public void CreateSchema(Configuration cfg)
    {
        new SchemaExport(cfg).Execute(false, true, false, _keepAlive, null);
    }

    /// <summary>
    /// Builds the schema, enables NHibernaut capture, and returns a profiled SessionFactory.
    /// Sessions opened from it route through the profiler and write into <c>NHibernautRuntime.Store</c>.
    /// </summary>
    public ISessionFactory BuildProfiledSessionFactory(
        Action<ModelMapper>? configureMappings = null,
        Action<NHibernautOptions>? configureOptions = null)
    {
        var cfg = BuildConfiguration(configureMappings);
        CreateSchema(cfg);
        cfg.EnableNHibernaut(configureOptions);
        return cfg.BuildSessionFactory();
    }

    public void Dispose() => _keepAlive.Dispose();
}
