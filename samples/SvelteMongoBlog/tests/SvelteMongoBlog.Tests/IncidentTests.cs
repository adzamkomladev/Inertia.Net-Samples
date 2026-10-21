using System.Net;
using MongoDB.Driver;
using SvelteMongoBlog.Features.Incidents;
using SvelteMongoBlog.Data;

namespace SvelteMongoBlog.Tests;

public sealed class IncidentTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Guards_log_and_resolve_incidents()
    {
        var (guard, guardId) = await app.SharedUserAsync(UserRole.Guard);
        var (lot, _) = await app.SeedLotAsync("Incident deck", spotCount: 1);

        using var created = await app.SendAsync(guard, "/guard/incidents", new { lotId = lot.Id, kind = "theft", plateNo = "cp-99", description = "Hub caps missing" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var incident = await app.QueryAsync(db => db.SecurityIncidents.Find(i => i.LotId == lot.Id).SingleAsync());
        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Equal("theft", incident.Kind);
        Assert.Equal("CP-99", incident.PlateNo);
        Assert.Equal(guardId, incident.ReporterUserId);
        Assert.Equal("Hub caps missing", incident.Description);

        using var resolved = await app.SendAsync(guard, $"/guard/incidents/{incident.Id}/resolve?note=patrol", new { });
        Assert.Equal(HttpStatusCode.Redirect, resolved.StatusCode);
        incident = await app.QueryAsync(db => db.SecurityIncidents.Find(i => i.Id == incident.Id).SingleAsync());
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal("patrol", incident.Note);
        Assert.NotNull(incident.ResolvedAt);

        // Resolving an already-resolved incident changes nothing.
        using var again = await app.SendAsync(guard, $"/guard/incidents/{incident.Id}/resolve?note=duplicate", new { });
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Equal("patrol", await app.QueryAsync(db => db.SecurityIncidents.Find(i => i.Id == incident.Id).Project(i => i.Note).SingleAsync()));
    }

    [Fact]
    public async Task Unknown_incident_kinds_are_refused()
    {
        var (guard, _) = await app.SharedUserAsync(UserRole.Guard);
        var (lot, _) = await app.SeedLotAsync("Bad kind deck", spotCount: 1);
        using var response = await app.SendAsync(guard, "/guard/incidents", new { lotId = lot.Id, kind = "vandalism", description = "Not a kind" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); // back with a kind error
        Assert.Equal(0, await app.QueryAsync(db => db.SecurityIncidents.CountDocumentsAsync(i => i.LotId == lot.Id)));
    }
}
