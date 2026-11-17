using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TechLandier.Data;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

/// Runwick jobs against the real PostgreSQL store: staged with the business transaction, and rendered by their handlers.
public sealed class JobsTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Staged_job_commits_with_the_transaction_and_disappears_with_a_rollback()
    {
        var before = await app.RunwickJobsAsync("mail.ticket-reply");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IJobOutbox>().StageAsync(db, new TicketReplyEmail(Guid.NewGuid()));
            await tx.RollbackAsync();
        }
        Assert.Equal(before, await app.RunwickJobsAsync("mail.ticket-reply"));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IJobOutbox>().StageAsync(db, new TicketReplyEmail(Guid.NewGuid()));
            await tx.CommitAsync();
        }
        Assert.Equal(before + 1, await app.RunwickJobsAsync("mail.ticket-reply"));
    }

    [Fact]
    public async Task Staging_outside_a_transaction_is_refused()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IJobOutbox>().StageAsync(db, new TicketReplyEmail(Guid.NewGuid())));
    }

    [Fact]
    public async Task Installment_paid_email_names_the_order_and_ignores_waived_installments()
    {
        var p = await app.SeedProductAsync("Plan Phone", price: 90m, stock: 3);
        var (client, uid) = await app.SignUpAsync("plan-mail@example.test");
        using (await app.SendAsync(client, "/checkout", new
        {
            productId = p.ProductId, qty = 1, zoneId = p.ZoneId, paymentMethod = "card", recipientName = "Ama Mensah", phone = "+233 24 000 0001",
            streetLabel = "GA-183-5287 Nyaneba Avenue", insurance = false, bnplCount = 3, checkoutAttemptId = Guid.NewGuid().ToString(),
        })) { }

        var (orderNumber, first) = await app.QueryAsync(async db =>
        {
            var ordered = await db.Installments.Include(i => i.Plan).ThenInclude(x => x.Order)
                .Where(i => i.Plan.UserId == uid).OrderBy(i => i.Number).ToListAsync();
            ordered[2].Status = InstallmentStatuses.Waived;
            await db.SaveChangesAsync();
            return (ordered[0].Plan.Order.Number, ordered[0].Id);
        });
        var log = new RecordingLogger();
        await using var scope = app.Services.CreateAsyncScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var mailer = new Mailer(new HttpClient(), config, scope.ServiceProvider.GetRequiredService<IHostEnvironment>(), log);
        await new InstallmentPaidEmailHandler(scope.ServiceProvider.GetRequiredService<AppDb>(), mailer, scope.ServiceProvider.GetRequiredService<EmailContexts>())
            .HandleAsync(new InstallmentPaidEmail(first), CancellationToken.None);

        var text = Assert.Single(log.Messages);
        Assert.Contains(orderNumber, text);
        Assert.DoesNotContain("your plan", text);
        // One installment is left to pay; the waived one is not owed.
        var perInstallment = await app.QueryAsync(async db => await db.Installments.Where(i => i.Id == first).Select(i => i.Amount).SingleAsync());
        Assert.Contains($"${perInstallment:0.00} remaining", text);
    }

    sealed class RecordingLogger : ILogger<Mailer>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
