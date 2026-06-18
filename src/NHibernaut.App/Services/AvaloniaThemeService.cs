using Avalonia;
using Avalonia.Styling;

namespace NHibernaut.App.Services;

/// <summary>Applies the requested theme to the live Avalonia <see cref="Application"/>.</summary>
public sealed class AvaloniaThemeService : IThemeService
{
    public void Apply(AppTheme theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
