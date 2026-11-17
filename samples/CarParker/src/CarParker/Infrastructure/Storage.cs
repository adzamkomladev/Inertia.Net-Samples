using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace CarParker.Infrastructure;

/// Any S3-compatible store (AWS S3, Cloudflare R2, MinIO). Browsers upload straight to it with presigned PUTs.
public sealed class Storage : IDisposable
{
    readonly AmazonS3Client _s3;
    readonly AmazonS3Client _signer; // presigns only; points at the browser-reachable endpoint (Storage:PublicServiceUrl)
    readonly string _bucket;
    readonly string? _publicBaseUrl;
    readonly bool _http;

    public Storage(IConfiguration config)
    {
        var s = config.GetSection("Storage");
        // Uploads are served publicly; a separate public bucket (S3_PUBLIC_BUCKET) takes them so they land where S3_PUBLIC_URL points.
        _bucket = string.IsNullOrWhiteSpace(s["PublicBucket"]) ? s["Bucket"] ?? "uploads" : s["PublicBucket"]!.Trim();
        var serviceUrl = s["ServiceUrl"];
        var publicUrl = string.IsNullOrWhiteSpace(s["PublicServiceUrl"]) ? serviceUrl : s["PublicServiceUrl"]!.Trim();
        _publicBaseUrl = string.IsNullOrWhiteSpace(s["PublicBaseUrl"])
            ? DefaultPublicBase(publicUrl, _bucket, s.GetValue("ForcePathStyle", !string.IsNullOrEmpty(serviceUrl)))
            : s["PublicBaseUrl"]!.Trim().TrimEnd('/');
        _http = publicUrl?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true;

        var creds = new BasicAWSCredentials(s["AccessKey"] ?? "", s["SecretKey"] ?? "");
        _s3 = new AmazonS3Client(creds, S3Config(s, serviceUrl));
        _signer = publicUrl == serviceUrl ? _s3 : new AmazonS3Client(creds, S3Config(s, publicUrl));

        // Without any endpoint the SDK talks to AWS itself, where objects live on the regional / bucket-subdomain hosts.
        var region = string.IsNullOrWhiteSpace(s["Region"]) ? "us-east-1" : s["Region"]!.Trim();
        var origins = OriginsOf(_publicBaseUrl, publicUrl).ToList();
        // Virtual-hosted addressing presigns against https://{bucket}.{host}, which differs from both the endpoint and a CDN base.
        if (!s.GetValue("ForcePathStyle", !string.IsNullOrEmpty(serviceUrl)) && Uri.TryCreate(publicUrl, UriKind.Absolute, out var endpoint))
            origins.Add($"{endpoint.Scheme}://{_bucket}.{endpoint.Authority}");
        if (string.IsNullOrWhiteSpace(publicUrl)) origins.AddRange([$"https://{_bucket}.s3.{region}.amazonaws.com", $"https://s3.{region}.amazonaws.com"]);
        BrowserOrigins = origins.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// Origins the browser talks to for uploads and image reads (public base, presign endpoint); allowed by the CSP.
    public IReadOnlyList<string> BrowserOrigins { get; private init; } = [];

    static IEnumerable<string> OriginsOf(params string?[] urls) =>
        urls.Where(u => !string.IsNullOrWhiteSpace(u) && Uri.TryCreate(u, UriKind.Absolute, out _))
            .Select(u => new Uri(u!).GetLeftPart(UriPartial.Authority)).Distinct(StringComparer.OrdinalIgnoreCase);

    static AmazonS3Config S3Config(IConfigurationSection s, string? serviceUrl)
    {
        var c = new AmazonS3Config
        {
            ForcePathStyle = s.GetValue("ForcePathStyle", !string.IsNullOrEmpty(s["ServiceUrl"])),
            // SDK v4 adds CRC checksums by default, which S3-compatible servers (RustFS, R2...) often reject.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (!string.IsNullOrEmpty(serviceUrl)) c.ServiceURL = serviceUrl;
        if (!string.IsNullOrEmpty(s["Region"])) c.AuthenticationRegion = s["Region"];
        if (!string.IsNullOrEmpty(s["Region"]) && string.IsNullOrEmpty(serviceUrl)) c.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(s["Region"]);
        return c;
    }

    /// A presigned PUT for exactly `size` bytes: Content-Length is a signed header, so the store rejects a body of any other length.
    public string PresignPut(string key, string contentType, long size, TimeSpan ttl)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(ttl),
            Protocol = _http ? Protocol.HTTP : Protocol.HTTPS,
        };
        request.Headers["Content-Length"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return _signer.GetPreSignedURL(request);
    }

    /// The first `count` bytes of an object (a ranged GET), for checking what a file really is.
    public async Task<byte[]> ReadHeadAsync(string key, int count, CancellationToken ct)
    {
        using var response = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = key, ByteRange = new ByteRange(0, count - 1) }, ct);
        await using var stream = response.ResponseStream;
        var buffer = new byte[count];
        var read = 0;
        int n;
        while (read < count && (n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct)) > 0) read += n;
        return buffer[..read];
    }

    /// The documented S3_PUBLIC_URL default: the browser-facing endpoint plus the bucket (path-style), or the bucket
    /// subdomain for virtual-hosted stores. Null only on AWS without an endpoint, where reads fall back to presigned GETs.
    static string? DefaultPublicBase(string? endpoint, string bucket, bool pathStyle)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        var uri = new UriBuilder(endpoint.Trim());
        if (pathStyle) uri.Path = $"{uri.Path.TrimEnd('/')}/{bucket}";
        else uri.Host = $"{bucket}.{uri.Host}";
        return uri.Uri.AbsoluteUri.TrimEnd('/');
    }

    /// Public URL of an object: S3_PUBLIC_URL (bucket or CDN base), else endpoint + bucket; a 1h presigned GET only without either.
    public string Url(string key) => _publicBaseUrl is not null
        ? $"{_publicBaseUrl}/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}"
        : _signer.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddHours(1),
            Protocol = _http ? Protocol.HTTP : Protocol.HTTPS,
        });

    public async Task<(long Size, string? ContentType)?> HeadAsync(string key, CancellationToken ct)
    {
        try
        {
            var meta = await _s3.GetObjectMetadataAsync(_bucket, key, ct);
            return (meta.ContentLength, meta.Headers.ContentType);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken ct, string? bucket = null)
    {
        foreach (var chunk in keys.Chunk(1000))
            await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = bucket ?? _bucket,
                Objects = chunk.Select(k => new KeyVersion { Key = k }).ToList(),
            }, ct);
    }


    public void Dispose()
    {
        if (!ReferenceEquals(_signer, _s3)) _signer.Dispose();
        _s3.Dispose();
    }
}
