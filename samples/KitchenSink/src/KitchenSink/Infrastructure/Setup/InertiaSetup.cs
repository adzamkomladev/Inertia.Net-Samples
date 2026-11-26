using KitchenSink.Data;
using KitchenSink.Features;
using KitchenSink.Features.Crm;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Infrastructure.Setup;

public static class InertiaSetup
{
    public static WebApplicationBuilder AddKitchenSinkInertia(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;

        builder.Services.AddProblemDetails();
        builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
        builder.Services.AddInertiaDevTools();
        builder.Services.AddInertia(o =>
        {
            o.Share("name", config["App:Name"] ?? "Inertia Kitchen Sink");

            o.Share("auth", context =>
            {
                if (context.User.Identity?.IsAuthenticated != true || context.User.UserId() is not { } id)
                {
                    return Prop(() => new AuthDto());
                }

                static async Task<AuthDto> Load(AppDb db, int userId, CancellationToken cancellationToken)
                {
                    var user = await db.Users.AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                    return new AuthDto { User = user?.ToSharedDto() };
                }

                return Prop<AppDb, AuthDto>((db, cancellationToken) => Load(db, id, cancellationToken));
            });

            o.Share("sidebarOpen", context =>
                !context.Request.Cookies.TryGetValue("sidebar_state", out var cookie) || cookie == "true");
            o.Share("queryMethod", config.GetValue("Demo:QueryMethod", true));

            o.Share("demoResetAt", context =>
            {
                if (context.User.Identity?.IsAuthenticated != true)
                {
                    return Prop(() => (DateTime?)null);
                }

                static async Task<DateTime?> Load(AppDb db, CancellationToken cancellationToken) =>
                    await DemoSeeder.ResetAtAsync(db, cancellationToken);

                return Prop<AppDb, DateTime?>(Load).Live(DemoEvents.Reset);
            });

            o.Pages.Directory = "ClientApp/pages";
            // The default version is the Vite manifest hash. The Location Event demo simulates a deployment for
            // one request; the protocol middleware then answers a current client with a 409 naming the new version.
            o.VersionResolver = context =>
                context.Request.Path.StartsWithSegments("/features/events/location-event/deploy")
                    ? "new-asset-version-hash"
                    : null;
            // The Big Integers page switches per request; the global flag writes $bigint for long values.
            o.PreserveBigIntegers = true;
            o.State.UseSession();
            o.Ssr.Mode = SsrMode.Disabled;
        });

        builder.Services.AddInertiaLive().AddSignalR();

        return builder;
    }
}
