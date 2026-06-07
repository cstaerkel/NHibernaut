using System;
using System.Collections.Generic;

namespace NHibernaut.Core.Model;

/// <summary>An anti-pattern finding raised by an <c>IAlertDetector</c>.</summary>
public sealed class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable detector identifier, e.g. <c>SelectNPlusOne</c>.</summary>
    public string Type { get; set; } = string.Empty;

    public AlertSeverity Severity { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>A concrete, actionable remediation suggestion.</summary>
    public string? Suggestion { get; set; }

    public List<Guid> RelatedStatementIds { get; } = new();
}
