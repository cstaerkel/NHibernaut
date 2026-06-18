using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using NHibernaut.Server;

namespace NHibernaut.App.ViewModels;

public partial class WaterfallBarViewModel : ViewModelBase
{
    public WaterfallBarViewModel(StatementDto dto, double leftPercent, double widthPercent)
    {
        StatementId = Guid.Parse(dto.Id);
        Kind = dto.Kind;
        IsSlow = dto.DurationMs > 200;
        Tooltip = $"{dto.Kind} · {dto.DurationMs:F0} ms · {dto.RowsRead?.ToString() ?? "-"} rows\n{dto.Sql}";
        Left = leftPercent;
        Width = widthPercent;
    }

    public Guid StatementId { get; }
    public string Kind { get; }
    public bool IsSlow { get; }
    public string Tooltip { get; }
    public double Left { get; }
    public double Width { get; }

    [ObservableProperty] private bool _isHighlighted;

    // Star factors for declarative proportional layout (no code-behind, no converter).
    public GridLength LeftStar  => new(Left, GridUnitType.Star);
    public GridLength WidthStar => new(Width, GridUnitType.Star);
    public GridLength RightStar => new(Math.Max(100 - Left - Width, 0), GridUnitType.Star);

    // Color key consumed by KindToBrushConverter (slow wins over kind).
    public string ColorKey => IsSlow ? "Slow" : Kind;
}
