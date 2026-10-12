using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace VuePgBlog.Infrastructure;

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
        _bucket = s["Bucket"] ?? "uploads";
        _publicBaseUrl = string.IsNullOrWhiteSpace(s["PublicBaseUrl"]) ? null : s["PublicBaseUrl"]!.Trim().TrimEnd('/');
        var serviceUrl = s["ServiceUrl"];
        var publicUrl = string.IsNullOrWhiteSpace(s["PublicServiceUrl"]) ? serviceUrl : s["PublicServiceUrl"]!.Trim();
        _http = publicUrl?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true;

        var creds = new BasicAWSCredentials(s["AccessKey"] ?? "", s["SecretKey"] ?? "");
        _s3 = new AmazonS3Client(creds, S3Config(s, serviceUrl));
        _signer = publicUrl == serviceUrl ? _s3 : new AmazonS3Client(creds, S3Config(s, publicUrl));
    }

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

    public string PresignPut(string key, string contentType, TimeSpan ttl) => _signer.GetPreSignedURL(new GetPreSignedUrlRequest
    {
        BucketName = _bucket,
        Key = key,
        Verb = HttpVerb.PUT,
        ContentType = contentType,
        Expires = DateTime.UtcNow.Add(ttl),
        Protocol = _http ? Protocol.HTTP : Protocol.HTTPS,
    });

    /// Public URL when the bucket (or a CDN in front of it) is public, otherwise a 1h presigned GET.
    public string Url(string key) => _publicBaseUrl is not null
        ? $"{_publicBaseUrl}/{key}"
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

    public Task UploadFileAsync(string bucket, string key, string filePath, CancellationToken ct) =>
        _s3.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, FilePath = filePath }, ct);

    public async Task<List<string>> ListKeysAsync(string prefix, CancellationToken ct, string? bucket = null)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = bucket ?? _bucket, Prefix = prefix };
        ListObjectsV2Response response;
        do
        {
            response = await _s3.ListObjectsV2Async(request, ct);
            keys.AddRange(response.S3Objects?.Select(o => o.Key) ?? []);
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);
        return keys;
    }

    public void Dispose()
    {
        if (!ReferenceEquals(_signer, _s3)) _signer.Dispose();
        _s3.Dispose();
    }
}
