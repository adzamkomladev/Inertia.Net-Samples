using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Inkwell.Infrastructure;

public sealed class StorageOptions
{
    /// <summary>S3 endpoint, e.g. http://localhost:9000 (RustFS).</summary>
    public string ServiceUrl { get; set; } = "http://localhost:9000";
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    /// <summary>Public-read bucket for images.</summary>
    public string MediaBucket { get; set; } = "media";
    /// <summary>Public base URL of the media bucket, e.g. https://s3.example.com/media (no trailing slash).</summary>
    public string PublicBaseUrl { get; set; } = "";
    public bool ForcePathStyle { get; set; } = true;
}

/// <summary>S3-compatible object storage (RustFS) via AWSSDK.S3.</summary>
public sealed class Storage(IOptions<StorageOptions> options) : IDisposable
{
    private readonly StorageOptions o = options.Value;

    private readonly AmazonS3Client s3 = new(
        new BasicAWSCredentials(options.Value.AccessKey, options.Value.SecretKey),
        new AmazonS3Config
        {
            ServiceURL = options.Value.ServiceUrl,
            ForcePathStyle = options.Value.ForcePathStyle,
            AuthenticationRegion = options.Value.Region,
            // SDK v4 adds CRC checksums by default, which S3-compatible servers often reject.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });

    /// <summary>Public URL of an object in the media bucket.</summary>
    public string MediaUrl(string key)
    {
        var baseUrl = o.PublicBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            var endpoint = new UriBuilder(o.ServiceUrl);
            if (o.ForcePathStyle)
                endpoint.Path = $"{endpoint.Path.TrimEnd('/')}/{o.MediaBucket}";
            else
                endpoint.Host = $"{o.MediaBucket}.{endpoint.Host}";
            baseUrl = endpoint.Uri.AbsoluteUri;
        }
        return $"{baseUrl.TrimEnd('/')}/{string.Join("/", key.Split('/').Select(Uri.EscapeDataString))}";
    }

    /// <summary>Uploads to the media bucket (public read).</summary>
    public async Task UploadMediaAsync(string key, Stream content, string contentType, CancellationToken ct) =>
        await PutAsync(o.MediaBucket, key, content, contentType, ct);

    public async Task DeleteMediaAsync(string key, CancellationToken ct) => await s3.DeleteObjectAsync(o.MediaBucket, key, ct);

    /// <summary>Creates the media bucket when missing and applies the public-read policy to the media bucket.</summary>
    public async Task EnsureBucketsAsync(CancellationToken ct)
    {
        var existing = (await s3.ListBucketsAsync(ct)).Buckets?.Select(b => b.BucketName).ToHashSet() ?? [];
        foreach (var bucket in new[] { o.MediaBucket }.Where(b => !existing.Contains(b)))
        {
            try { await s3.PutBucketAsync(bucket, ct); }
            catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists") { }
        }

        var policy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = new { AWS = new[] { "*" } },
                    Action = new[] { "s3:GetObject" },
                    Resource = new[] { $"arn:aws:s3:::{o.MediaBucket}/*" },
                },
            },
        });
        await s3.PutBucketPolicyAsync(new PutBucketPolicyRequest { BucketName = o.MediaBucket, Policy = policy }, ct);
    }

    private async Task PutAsync(string bucket, string key, Stream content, string contentType, CancellationToken ct) =>
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            UseChunkEncoding = false,
        }, ct);

    public void Dispose() => s3.Dispose();
}

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddStorage(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<StorageOptions>(config.GetSection("Storage"));
        services.Configure<StorageOptions>(o =>
        {
            o.ServiceUrl = config["S3_ENDPOINT"] ?? o.ServiceUrl;
            o.AccessKey = config["S3_ACCESS_KEY"] ?? o.AccessKey;
            o.SecretKey = config["S3_SECRET_KEY"] ?? o.SecretKey;
            o.Region = config["S3_REGION"] ?? o.Region;
            o.MediaBucket = config["S3_BUCKET"] ?? o.MediaBucket;
            o.PublicBaseUrl = config["S3_PUBLIC_URL"] ?? o.PublicBaseUrl;
            o.ForcePathStyle = config.GetValue<bool?>("STORAGE_FORCED_PATH_STYLE") ?? o.ForcePathStyle;
        });
        services.AddSingleton<Storage>();
        return services;
    }
}
