using NHibernaut.Server;

namespace NHibernaut.App.ViewModels;

public sealed class StatementViewModel
{
    public StatementViewModel(StatementDto dto) => Dto = dto;

    public StatementDto Dto { get; }
    public Guid Id => Guid.Parse(Dto.Id);
    public string Kind => Dto.Kind;
    public double DurationMs => Dto.DurationMs;
    public int? RowsRead => Dto.RowsRead;
    public bool IsSlow => Dto.DurationMs > 200;

    public string SqlFirstLine
    {
        get
        {
            var line = (Dto.Sql ?? "").Split('\n')[0].Trim();
            return line.Length > 200 ? line[..200] : line;
        }
    }
}
