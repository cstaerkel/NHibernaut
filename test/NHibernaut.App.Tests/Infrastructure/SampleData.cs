using System;
using System.Collections.Generic;
using NHibernaut.Core.Model;
using NHibernaut.Server;

namespace NHibernaut.App.Tests.Infrastructure;

/// <summary>Shared factory for sample DTOs used across test classes.</summary>
public static class SampleData
{
    public static SessionDetailDto Session(Guid id)
    {
        var summary = new SessionSummaryDto(
            Id: id.ToString(),
            StartedAt: DateTimeOffset.UtcNow,
            EndedAt: DateTimeOffset.UtcNow,
            IsSealed: true,
            StatementCount: 0,
            TotalDurationMs: 0,
            TotalRowsRead: 0,
            WriteCount: 0,
            AlertCount: 0,
            MaxSeverity: null,
            ThreadCount: 1);

        return new SessionDetailDto(
            Summary: summary,
            Statements: Array.Empty<StatementDto>(),
            Connections: Array.Empty<ConnectionDto>(),
            Transactions: Array.Empty<TransactionDto>(),
            EntityLoads: Array.Empty<EntityLoadDto>(),
            Writes: Array.Empty<EntityWriteDto>(),
            CollectionInits: Array.Empty<CollectionInitDto>(),
            Alerts: Array.Empty<AlertDto>(),
            EntityCountsByType: new Dictionary<string, int>());
    }
}
