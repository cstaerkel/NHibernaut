using System;
using System.Collections;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace NHibernaut.Core.Capture;

/// <summary>
/// Decorates a real <see cref="DbDataReader"/> to count rows read and to finalize the owning
/// statement's capture (duration + rows) when the reader closes. All accessors delegate to the
/// inner reader; only <c>Read</c>/<c>ReadAsync</c> and the close/dispose paths add behavior.
/// </summary>
public sealed class ProfilingDbDataReader : DbDataReader
{
    private readonly DbDataReader _inner;
    private readonly StatementCapture _capture;
    private bool _finalized;

    internal ProfilingDbDataReader(DbDataReader inner, StatementCapture capture)
    {
        _inner = inner;
        _capture = capture;
    }

    public override bool Read()
    {
        var read = _inner.Read();
        if (read) CountRow();
        return read;
    }

    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (read) CountRow();
        return read;
    }

    private void CountRow()
    {
        // Best-effort, capped to avoid pathological counting on huge result sets.
        if (_capture.RowsRead < NHibernautRuntime.Options.MaxCapturedRows)
            _capture.RowsRead++;
    }

    public override void Close()
    {
        _inner.Close();
        FinalizeCapture();
    }

    public override async Task CloseAsync()
    {
        await _inner.CloseAsync().ConfigureAwait(false);
        FinalizeCapture();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            FinalizeCapture();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        FinalizeCapture();
    }

    private void FinalizeCapture()
    {
        if (_finalized) return;
        _finalized = true;
        Capturer.CompleteReader(_capture);
    }

    // ---- pure delegation below ----

    public override object this[int ordinal] => _inner[ordinal];
    public override object this[string name] => _inner[name];
    public override int Depth => _inner.Depth;
    public override int FieldCount => _inner.FieldCount;
    public override bool HasRows => _inner.HasRows;
    public override bool IsClosed => _inner.IsClosed;
    public override int RecordsAffected => _inner.RecordsAffected;
    public override int VisibleFieldCount => _inner.VisibleFieldCount;

    public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => _inner.GetChar(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);
    public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);
    public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);
    public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);
    public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);
    public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);
    public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);
    public override string GetName(int ordinal) => _inner.GetName(ordinal);
    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);
    public override string GetString(int ordinal) => _inner.GetString(ordinal);
    public override object GetValue(int ordinal) => _inner.GetValue(ordinal);
    public override int GetValues(object[] values) => _inner.GetValues(values);
    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);
    public override T GetFieldValue<T>(int ordinal) => _inner.GetFieldValue<T>(ordinal);
    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
        => _inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
        => _inner.IsDBNullAsync(ordinal, cancellationToken);
    public override bool NextResult() => _inner.NextResult();
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        => _inner.NextResultAsync(cancellationToken);
    public override IEnumerator GetEnumerator() => _inner.GetEnumerator();
    public override System.Data.DataTable? GetSchemaTable() => _inner.GetSchemaTable();
}
