namespace NHibernaut.App.Services;

/// <summary>Applies an <see cref="AppTheme"/> to the running application's visual theme.</summary>
public interface IThemeService
{
    void Apply(AppTheme theme);
}
