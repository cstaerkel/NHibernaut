using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using NHibernaut.App.Services;
using NHibernaut.Server;

namespace NHibernaut.App.ViewModels;

/// <summary>A single row in the Parameters list.</summary>
public sealed record ParamRow(string Name, string Value, string DbType);

public sealed partial class StatementDetailViewModel : ViewModelBase
{
    private readonly IEditorLinkService? _editorLinks;

    public string Sql { get; }
    public string? StackTrace { get; }
    public string? SourceLink { get; }
    public bool HasSource => SourceLink is not null;

    public ObservableCollection<ParamRow> Parameters { get; } = new();
    public ObservableCollection<string> Writes { get; } = new();
    public ObservableCollection<string> Hydrated { get; } = new();
    public ObservableCollection<string> Initialized { get; } = new();

    public StatementDetailViewModel(
        StatementDto statement,
        SessionDetailDto parentDetail,
        string scheme,
        IEditorLinkService? editorLinks)
    {
        _editorLinks = editorLinks;
        Sql = statement.Sql;
        StackTrace = statement.StackTrace;

        // Parameters
        foreach (var p in statement.Parameters)
            Parameters.Add(new ParamRow(p.Name ?? "", p.Value ?? "", p.DbType ?? ""));

        // Writes: filter, group by Kind, verb-map, chip-cap at 24
        var writes = parentDetail.Writes
            .Where(w => w.StatementId == statement.Id)
            .ToList();

        var writesByKind = writes
            .GroupBy(w => w.Kind)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (kind, list) in writesByKind)
        {
            var verb = VerbFor(kind);
            var chips = list
                .Take(24)
                .Select(w => $"{ShortType(w.EntityType)} #{w.Id}")
                .ToList();
            var overflow = list.Count > 24 ? $" (+{list.Count - 24} more)" : "";
            Writes.Add($"{verb}: {string.Join(", ", chips)}{overflow}");
        }

        // Hydrated: filter, group by EntityType, cap 8 ids
        var loads = parentDetail.EntityLoads
            .Where(e => e.StatementId == statement.Id)
            .ToList();

        var loadsByType = loads
            .GroupBy(e => e.EntityType)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (entityType, list) in loadsByType)
        {
            var shortName = ShortType(entityType);
            var ids = list.Take(8).Select(e => $"#{e.Id}").ToList();
            var overflow = list.Count > 8 ? $", +{list.Count - 8} more" : "";
            Hydrated.Add($"{list.Count} × {shortName} ({string.Join(", ", ids)}{overflow})");
        }

        // Initialized: filter, shorten role to last 2 segments
        var inits = parentDetail.CollectionInits
            .Where(c => c.StatementId == statement.Id)
            .ToList();

        foreach (var init in inits)
            Initialized.Add(ShortRole(init.Role));

        // Source link
        if (editorLinks is not null && editorLinks.TryBuildLink(statement.StackTrace, scheme, out var lnk))
            SourceLink = lnk;
    }

    [RelayCommand]
    private void OpenSource()
    {
        if (HasSource && _editorLinks is not null)
            _editorLinks.Open(SourceLink!);
    }

    private static string ShortType(string? entityType)
    {
        if (string.IsNullOrEmpty(entityType)) return "";
        var dot = entityType.LastIndexOf('.');
        return dot >= 0 ? entityType[(dot + 1)..] : entityType;
    }

    private static string ShortRole(string? role)
    {
        if (string.IsNullOrEmpty(role)) return "";
        var parts = role.Split('.');
        return parts.Length >= 2
            ? string.Join(".", parts[^2], parts[^1])
            : role;
    }

    private static string VerbFor(string kind) => kind switch
    {
        "Insert" => "created",
        "Update" => "updated",
        "Delete" => "deleted",
        _ => kind.ToLowerInvariant(),
    };
}
