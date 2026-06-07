using System.Data.Common;
using Microsoft.Data.Sqlite;
using NHibernate.Driver;

namespace NHibernaut.Tests.Infrastructure;

// NHibernate 5.6.1 ships no Microsoft.Data.Sqlite driver (only SQLite20Driver for
// System.Data.SQLite). This minimal DriverBase subclass wires NHibernate to
// Microsoft.Data.Sqlite, whose bundled SQLitePCLRaw native libs work cross-platform
// (incl. osx-arm64) — the reason we prefer it for the test SessionFactory.
public class MicrosoftDataSqliteDriver : DriverBase
{
    public override DbConnection CreateConnection() => new SqliteConnection();

    public override DbCommand CreateCommand() => new SqliteCommand();

    public override bool UseNamedPrefixInSql => true;

    public override bool UseNamedPrefixInParameter => true;

    public override string NamedPrefix => "@";
}
