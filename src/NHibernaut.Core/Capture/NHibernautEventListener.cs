using System;
using System.Threading;
using System.Threading.Tasks;
using NHibernate.Event;
using NHibernate.Persister.Entity;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Capture;

/// <summary>
/// Single listener (registered for several listener types) that records object-level capture:
/// entity hydration, writes, and lazy collection initialization. It recovers the session from each
/// event (so it holds no shared session state) and attributes work to the currently-executing
/// statement via <see cref="CaptureContext.CurrentStatement"/>.
/// </summary>
internal sealed class NHibernautEventListener :
    IPostLoadEventListener,
    IPostInsertEventListener,
    IPostUpdateEventListener,
    IPostDeleteEventListener,
    IInitializeCollectionEventListener
{
    public void OnPostLoad(PostLoadEvent @event) => NHibernautRuntime.SafeExecute(() =>
        RecordLoad(@event.Session.SessionId, EntityName(@event.Persister, @event.Entity), @event.Id));

    public void OnPostInsert(PostInsertEvent @event) => NHibernautRuntime.SafeExecute(() =>
        RecordWrite(@event.Session.SessionId, WriteKind.Insert, EntityName(@event.Persister, @event.Entity), @event.Id));

    public Task OnPostInsertAsync(PostInsertEvent @event, CancellationToken cancellationToken)
    {
        OnPostInsert(@event);
        return Task.CompletedTask;
    }

    public void OnPostUpdate(PostUpdateEvent @event) => NHibernautRuntime.SafeExecute(() =>
        RecordWrite(@event.Session.SessionId, WriteKind.Update, EntityName(@event.Persister, @event.Entity), @event.Id, IsNoChange(@event)));

    public Task OnPostUpdateAsync(PostUpdateEvent @event, CancellationToken cancellationToken)
    {
        OnPostUpdate(@event);
        return Task.CompletedTask;
    }

    public void OnPostDelete(PostDeleteEvent @event) => NHibernautRuntime.SafeExecute(() =>
        RecordWrite(@event.Session.SessionId, WriteKind.Delete, EntityName(@event.Persister, @event.Entity), @event.Id));

    public Task OnPostDeleteAsync(PostDeleteEvent @event, CancellationToken cancellationToken)
    {
        OnPostDelete(@event);
        return Task.CompletedTask;
    }

    public void OnInitializeCollection(InitializeCollectionEvent @event) => NHibernautRuntime.SafeExecute(() =>
        RecordCollectionInit(@event.Session.SessionId, @event.Collection?.Role));

    public Task OnInitializeCollectionAsync(InitializeCollectionEvent @event, CancellationToken cancellationToken)
    {
        OnInitializeCollection(@event);
        return Task.CompletedTask;
    }

    private static void RecordLoad(Guid sessionId, string entityType, object? id)
    {
        if (!NHibernautRuntime.IsSampled(sessionId)) return;
        var capture = CaptureContext.CurrentStatement.Value;
        var session = NHibernautRuntime.Store.GetOrCreateSession(sessionId);
        lock (session.SyncRoot)
        {
            var statement = ResolveStatement(capture, sessionId, session);
            session.EntityLoads.Add(new EntityLoad
            {
                EntityType = entityType,
                Id = id,
                StatementId = statement?.Id
            });
            if (statement is not null) statement.EntityLoadCount++;
        }
    }

    private static void RecordWrite(Guid sessionId, WriteKind kind, string entityType, object? id, bool noActualChange = false)
    {
        if (!NHibernautRuntime.IsSampled(sessionId)) return;
        var capture = CaptureContext.CurrentStatement.Value;
        var session = NHibernautRuntime.Store.GetOrCreateSession(sessionId);
        lock (session.SyncRoot)
        {
            var statement = ResolveStatement(capture, sessionId, session);
            session.Writes.Add(new EntityWrite
            {
                Kind = kind,
                EntityType = entityType,
                Id = id,
                StatementId = statement?.Id,
                NoActualChange = noActualChange
            });
        }
    }

    // Best-effort: an update is "superfluous" when old and new state are element-wise equal.
    private static bool IsNoChange(PostUpdateEvent @event)
    {
        var oldState = @event.OldState;
        var state = @event.State;
        if (oldState is null || state is null || oldState.Length != state.Length) return false;
        for (var i = 0; i < state.Length; i++)
            if (!Equals(oldState[i], state[i])) return false;
        return true;
    }

    private static void RecordCollectionInit(Guid sessionId, string? role)
    {
        if (!NHibernautRuntime.IsSampled(sessionId)) return;
        var capture = CaptureContext.CurrentStatement.Value;
        var session = NHibernautRuntime.Store.GetOrCreateSession(sessionId);
        lock (session.SyncRoot)
        {
            var statement = ResolveStatement(capture, sessionId, session);
            session.CollectionInits.Add(new CollectionInit
            {
                Role = role ?? string.Empty,
                StatementId = statement?.Id
            });
            if (statement is not null) statement.CollectionInitCount++;
        }
    }

    // Attribute to the active statement if one is set for this session; otherwise fall back to the
    // most-recently-started statement — NHibernate's two-phase load fires PostLoad/InitializeCollection
    // *after* the reader (and thus CurrentStatement) has closed, so the just-run statement is the best
    // heuristic owner. Must be called under the session lock.
    private static ProfiledStatement? ResolveStatement(StatementCapture? capture, Guid sessionId, ProfiledSession session)
    {
        if (capture is not null && capture.SessionId == sessionId)
            return capture.Statement;

        return session.Statements.Count > 0 ? session.Statements[session.Statements.Count - 1] : null;
    }

    private static string EntityName(IEntityPersister? persister, object? entity)
        => persister?.EntityName ?? entity?.GetType().FullName ?? "?";
}
