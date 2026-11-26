namespace KitchenSink.Features.Showcase;

public static class PrefetchingFeature
{
    public static IEndpointRouteBuilder MapPrefetching(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/prefetching").RequireAuthorization();

        group.MapGet("/link-prefetch", () => Inertia.Net.Inertia.Render("Features/Prefetching/LinkPrefetch"));
        group.MapGet("/stale-while-revalidate", () => Inertia.Net.Inertia.Render("Features/Prefetching/StaleWhileRevalidate"));
        group.MapGet("/manual-prefetch", () => Inertia.Net.Inertia.Render("Features/Prefetching/ManualPrefetch"));
        group.MapGet("/cache-management", () => Inertia.Net.Inertia.Render("Features/Prefetching/CacheManagement"));

        return endpoints;
    }
}
