using NHibernaut.Server;

namespace NHibernaut.App.ViewModels;

public sealed class AggregateRowViewModel
{
    private readonly AggregateRowDto _dto;

    public AggregateRowViewModel(AggregateRowDto dto) => _dto = dto;

    public string Shape => _dto.NormalizedSql;
    public int Calls => _dto.ExecutionCount;
    public double TotalMs => _dto.TotalDurationMs;
    public double AvgMs => _dto.AvgDurationMs;
    public int MaxRows => _dto.MaxRowsRead;
    public int Sessions => _dto.SessionCount;
    public int NPlusOne => _dto.NPlusOneIncidence;
    public string NPlusOneDisplay => NPlusOne > 0 ? NPlusOne.ToString() : "—";
}
