using System.Net;
using System.Text.Json;
using Npgsql;

namespace TechLandier.Tests;

public sealed class RunwickTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Demo_catalog_and_single_trigger_are_open_and_durable()
    {
        using var client = app.Client();

        using var catalog = await client.GetAsync("/demo/jobs");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Contains("demo.sleep.100ms", await catalog.Content.ReadAsStringAsync());

        using var response = await client.PostAsync("/demo/jobs/100ms", content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jobId = Guid.Parse(json.RootElement.GetProperty("jobId").GetString()!);

        Assert.True(await WaitForSucceededAsync(jobId, TimeSpan.FromSeconds(10)), "Runwick did not finish the demo job.");
    }

    [Fact]
    public async Task Batch_trigger_accepts_one_hundred_jobs()
    {
        using var client = app.Client();
        using var response = await client.PostAsync("/demo/jobs/100ms/batch?count=100", content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(100, json.RootElement.GetProperty("requested").GetInt32());
        Assert.Equal(100, json.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(100, json.RootElement.GetProperty("jobs").GetArrayLength());
    }

    private async Task<bool> WaitForSucceededAsync(Guid jobId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("SELECT state FROM runwick.jobs WHERE job_id = @id", connection);
            command.Parameters.AddWithValue("id", jobId);
            if (await command.ExecuteScalarAsync() is "succeeded") return true;
            await Task.Delay(100);
        }
        return false;
    }
}
