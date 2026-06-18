namespace NHibernaut.App.Services;

public interface IEditorLinkService
{
    /// <summary>Build an editor deep-link from the first file:line frame in a stack trace.</summary>
    bool TryBuildLink(string? stackTrace, string scheme, out string? link);
    /// <summary>Open the link in the OS default handler (best-effort, never throws).</summary>
    void Open(string link);
}
