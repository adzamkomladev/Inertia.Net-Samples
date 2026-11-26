namespace KitchenSink.Features.Showcase;

public static class LayoutsFeature
{
    public static IEndpointRouteBuilder MapLayouts(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/layouts").RequireAuthorization();

        group.MapGet("/persistent-layouts", () => Inertia.Net.Inertia.Render("Features/Layouts/PersistentLayouts"));
        group.MapGet("/persistent-layouts/page-2", () => Inertia.Net.Inertia.Render("Features/Layouts/PersistentLayoutsPageTwo"));
        group.MapGet("/nested-layouts", () => Inertia.Net.Inertia.Render("Features/Layouts/NestedLayouts"));
        group.MapGet("/head", () => Inertia.Net.Inertia.Render("Features/Layouts/Head"));
        group.MapGet("/layout-props", () => Inertia.Net.Inertia.Render("Features/Layouts/LayoutProps"));

        return endpoints;
    }
}
