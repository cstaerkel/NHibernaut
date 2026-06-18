using System.Globalization;

namespace NHibernaut.App.ViewModels;

public sealed record CompareRow(string Metric, double A, double B)
{
    public double Delta => B - A;
    public bool IsBetter => Delta < 0;   // lower is better for all compared metrics
    public bool IsWorse => Delta > 0;
    public string State => IsBetter ? "Better" : IsWorse ? "Worse" : "Same";
    public string ADisplay => A.ToString("0.##", CultureInfo.InvariantCulture);
    public string BDisplay => B.ToString("0.##", CultureInfo.InvariantCulture);
    public string DeltaDisplay => (Delta > 0 ? "+" : "") + Delta.ToString("0.##", CultureInfo.InvariantCulture);
}
