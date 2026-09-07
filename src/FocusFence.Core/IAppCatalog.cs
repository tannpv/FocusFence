namespace FocusFence.Core;

public sealed record CatalogApp(string Name, string Path, string Source, string? Restriction);
public interface IAppCatalog { IReadOnlyList<CatalogApp> Discover(); }
