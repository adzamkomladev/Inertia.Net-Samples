namespace TechLandier.Features;

/// <summary>
/// Tags and lifetimes for the storefront and admin props that are expensive enough to cache. Catalog and order
/// mutations remove their tag; the storefront pages then rebuild on the next request instead of waiting for the TTL.
/// </summary>
public static class StoreCache
{
    public const string Catalog = "catalog";
    public const string Home = "home";
    public const string Sales = "sales";

    public static readonly TimeSpan CatalogFreshFor = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CatalogStaleFor = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RailFreshFor = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RailStaleFor = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RelatedFreshFor = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan RelatedStaleFor = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan SalesFreshFor = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SalesStaleFor = TimeSpan.FromSeconds(30);

    public static string Product(Guid productId) => $"product:{productId}";
}
