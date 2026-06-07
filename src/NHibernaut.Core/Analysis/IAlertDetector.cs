using System.Collections.Generic;
using System.Linq;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Analysis;

/// <summary>
/// An independently testable anti-pattern detector. Given a session snapshot and the active options,
/// it yields zero or more alerts. Detectors must be pure (no side effects) and tolerant of partial data.
/// </summary>
public interface IAlertDetector
{
    IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options);
}

/// <summary>
/// Runs all detectors over a session and writes the resulting alerts onto it. Each detector is
/// isolated: one that throws is swallowed (reported via <see cref="NHibernautRuntime.InternalError"/>)
/// and never blocks the others or the host. Analysis is idempotent — alerts are recomputed each call,
/// so re-sealing a session (multi-transaction sessions) doesn't accumulate duplicates.
/// </summary>
public sealed class AnalysisPipeline
{
    private readonly IReadOnlyList<IAlertDetector> _detectors;

    public AnalysisPipeline()
        : this(DefaultDetectors())
    {
    }

    public AnalysisPipeline(IEnumerable<IAlertDetector> detectors)
    {
        _detectors = detectors.ToList();
    }

    public static IReadOnlyList<IAlertDetector> DefaultDetectors() => new IAlertDetector[]
    {
        new SelectNPlusOneDetector(),
        new TooManyQueriesDetector(),
        new UnboundedResultSetDetector(),
        new TooManyRowsDetector(),
        new TooManyJoinsDetector(),
        new SlowQueryDetector(),
        new DuplicateQueryDetector(),
        new CrossThreadSessionDetector(),
        new WriteWithoutTransactionDetector(),
        new TooManyWritesDetector(),
        new SuperfluousUpdateDetector()
    };

    public void Analyze(ProfiledSession session, NHibernautOptions options)
    {
        lock (session.SyncRoot)
        {
            session.Alerts.Clear();
            foreach (var detector in _detectors)
            {
                var alerts = NHibernautRuntime.SafeExecute(
                    () => detector.Detect(session, options).ToList(),
                    new List<Alert>());
                session.Alerts.AddRange(alerts);
            }
        }
    }
}
