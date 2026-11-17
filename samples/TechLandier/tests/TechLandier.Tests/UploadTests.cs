using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TechLandier.Data;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

/// Uploads against an in-process S3 stand-in: presigned PUTs bound the body length and confirm checks what arrived.
public sealed class UploadTests : IClassFixture<UploadTests.UploadFactory>
{
    readonly UploadFactory app;
    public UploadTests(UploadFactory app) => this.app = app;

    [Fact]
    public async Task Presigned_put_signs_the_exact_content_length()
    {
        var (client, _) = await app.SignUpAsync("upload-length@example.test");
        var presign = await PresignAsync(client, "image/png", 1234);
        var url = new Uri(presign.GetProperty("url").GetString()!);
        var signed = Uri.UnescapeDataString(System.Web.HttpUtility.ParseQueryString(url.Query)["X-Amz-SignedHeaders"] ?? "");
        Assert.Contains("content-length", signed.Split(';'));
        Assert.Contains("content-type", signed.Split(';'));
    }

    [Fact]
    public async Task Confirm_accepts_a_real_image()
    {
        var (client, _) = await app.SignUpAsync("upload-ok@example.test");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
        var assetId = await UploadAsync(client, "image/png", png);

        using var confirm = await ConfirmAsync(client, assetId);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(AssetStatus.Ready, await app.QueryAsync(async db => (await db.Assets.SingleAsync(a => a.Id == assetId)).Status));
    }

    [Fact]
    public async Task Confirm_rejects_a_body_that_is_not_an_image_and_discards_the_asset()
    {
        var (client, _) = await app.SignUpAsync("upload-fake@example.test");
        var html = "<html><script>alert(1)</script></html>"u8.ToArray();
        var assetId = await UploadAsync(client, "image/png", html);

        using var confirm = await ConfirmAsync(client, assetId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, confirm.StatusCode);
        Assert.False(await app.QueryAsync(async db => await db.Assets.AnyAsync(a => a.Id == assetId)));
    }

    [Fact]
    public async Task Confirm_rejects_a_body_longer_than_declared()
    {
        var (client, _) = await app.SignUpAsync("upload-long@example.test");
        byte[] declared = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var presign = await PresignAsync(client, "image/png", declared.Length);
        var assetId = presign.GetProperty("assetId").GetGuid();
        // A client that ignores the signed length and sends more than it declared.
        app.Objects.Store(await KeyAsync(assetId), [.. declared, .. new byte[500]], "image/png");

        using var confirm = await ConfirmAsync(client, assetId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, confirm.StatusCode);
    }

    [Fact]
    public async Task Pdf_uploads_are_no_longer_accepted()
    {
        var (client, _) = await app.SignUpAsync("upload-pdf@example.test");
        using var response = await app.SendAsync(client, "/uploads/presign", new { contentType = "application/pdf", size = 1000, fileName = "a.pdf" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Unverified_accounts_cannot_upload()
    {
        var (client, userId) = await app.SignUpAsync("upload-unverified@example.test", verified: false);
        using var response = await app.SendAsync(client, "/uploads/presign", new { contentType = "image/png", size = 1000, fileName = "a.png" });
        Assert.DoesNotContain("assetId", await response.Content.ReadAsStringAsync());
        Assert.False(await app.QueryAsync(async db => await db.Assets.AnyAsync(a => a.OwnerId == userId)));
    }

    [Theory]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData("image/gif", new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, true)]
    [InlineData("image/png", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, false)]
    [InlineData("image/jpeg", new byte[] { (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l' }, false)]
    [InlineData("image/webp", new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, true)]
    [InlineData("image/webp", new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'A', (byte)'V', (byte)'I', (byte)' ' }, false)]
    [InlineData("application/pdf", new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F' }, false)]
    public void Signatures_must_match_the_declared_type(string contentType, byte[] head, bool expected) =>
        Assert.Equal(expected, UploadsValidators.MatchesSignature(contentType, head));

    async Task<JsonElement> PresignAsync(HttpClient client, string contentType, long size)
    {
        using var response = await app.SendAsync(client, "/uploads/presign", new { contentType, size, fileName = "file" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    async Task<Guid> UploadAsync(HttpClient client, string contentType, byte[] body)
    {
        var presign = await PresignAsync(client, contentType, body.Length);
        var assetId = presign.GetProperty("assetId").GetGuid();
        app.Objects.Store(await KeyAsync(assetId), body, contentType);
        return assetId;
    }

    async Task<HttpResponseMessage> ConfirmAsync(HttpClient client, Guid assetId) =>
        await app.SendAsync(client, $"/uploads/{assetId}/confirm", new { });

    async Task<string> KeyAsync(Guid assetId) => await app.QueryAsync(async db => (await db.Assets.AsNoTracking().SingleAsync(a => a.Id == assetId)).Key);

    /// The app pointed at a minimal S3 API (HEAD, ranged GET, PUT, delete) served from memory.
    public sealed class UploadFactory : AppFactory
    {
        public FakeObjects Objects { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Storage:ServiceUrl", Objects.Url);
            builder.UseSetting("Storage:AccessKey", "test");
            builder.UseSetting("Storage:SecretKey", "test-secret");
            builder.UseSetting("Storage:Bucket", "uploads");
            builder.UseSetting("Storage:ForcePathStyle", "true");
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await Objects.DisposeAsync();
        }
    }

    public sealed class FakeObjects : IAsyncDisposable
    {
        readonly ConcurrentDictionary<string, (byte[] Body, string Type)> objects = new();
        readonly WebApplication server;

        public string Url { get; }

        public FakeObjects()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            server = builder.Build();
            server.Run(async ctx =>
            {
                // Path-style: /{bucket}/{key}. Everything else (bucket-level calls, DeleteObjects) just succeeds.
                var parts = ctx.Request.Path.Value!.TrimStart('/').Split('/', 2);
                var key = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                if (key.Length == 0 || !objects.TryGetValue(key, out var found))
                {
                    ctx.Response.StatusCode = ctx.Request.Method == "POST" ? 200 : 404;
                    return;
                }
                ctx.Response.ContentType = found.Type;
                var body = found.Body;
                if (ctx.Request.Method == "HEAD")
                {
                    ctx.Response.ContentLength = body.Length;
                    return;
                }
                if (ctx.Request.Headers.Range.ToString() is { Length: > 0 } range && range.StartsWith("bytes="))
                {
                    var bounds = range["bytes=".Length..].Split('-');
                    var from = int.Parse(bounds[0]);
                    var to = Math.Min(int.Parse(bounds[1]), body.Length - 1);
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers.ContentRange = $"bytes {from}-{to}/{body.Length}";
                    await ctx.Response.Body.WriteAsync(body.AsMemory(from, to - from + 1));
                    return;
                }
                await ctx.Response.Body.WriteAsync(body);
            });
            server.Start();
            Url = server.Urls.First();
        }

        public void Store(string key, byte[] body, string contentType) => objects[key] = (body, contentType);

        public async ValueTask DisposeAsync() => await server.DisposeAsync();
    }
}
