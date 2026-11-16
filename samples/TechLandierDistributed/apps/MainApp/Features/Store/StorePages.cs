using Inertia.Net;

namespace TechLandier.Features;

// Page records live in MainApp: TechLandier.Core deliberately has no Inertia.Net dependency.
public sealed record ProductFilters(string Q, string Category, decimal? Min, decimal? Max, bool? InStock, string Sort);

public sealed record ProductsPage(
    [property: InertiaPropType(typeof(Paged<ProductCard>))] InertiaProp Products,
    ProductFilters Filters,
    [property: InertiaPropType(typeof(CategoryTile[]))] InertiaProp Categories) : IInertiaPage
{
    public static string Component => "products/Index";
}

public sealed record DealsPage(
    [property: InertiaPropType(typeof(Paged<ProductCard>))] InertiaProp Products,
    string Sort) : IInertiaPage
{
    public static string Component => "deals/Index";
}
