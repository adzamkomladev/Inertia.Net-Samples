namespace VuePgBlog.Features;

public static class Support
{
    public static void MapSupport(this IEndpointRouteBuilder app)
    {
        // Public guest complaint form lives on /support/policy.
        app.MapPost("/support", SupportService.GuestComplaintAsync);

        var account = app.MapGroup("/account/support").RequireAuthorization("verified");
        account.MapGet("/", SupportService.TicketsAsync);
        account.MapPost("/", SupportService.CreateTicketAsync);
        account.MapPost("/{id:guid}/reply", SupportService.ReplyAsync);

        var admin = app.MapGroup("/admin/support").RequireAuthorization("admin");
        admin.MapGet("/", SupportService.AdminTicketsAsync);
        admin.MapGet("/{number}", SupportService.TicketDetailAsync);
        admin.MapPost("/{id:guid}/reply", SupportService.AdminReplyAsync);
        admin.MapPost("/{id:guid}", SupportService.DecideTicketAsync);
    }
}
