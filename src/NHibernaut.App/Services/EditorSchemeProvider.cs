namespace NHibernaut.App.Services;

/// <summary>Holds the dashboard's editorLinkScheme (from /api/config), set once on connect.</summary>
public sealed class EditorSchemeProvider { public string Scheme { get; set; } = "vscode"; }
