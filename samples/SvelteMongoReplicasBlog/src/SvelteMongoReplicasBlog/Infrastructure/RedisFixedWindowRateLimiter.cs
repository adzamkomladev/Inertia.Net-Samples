using System.Threading.RateLimiting;
using System.Diagnostics;
using StackExchange.Redis;

namespace SvelteMongoReplicasBlog.Infrastructure;

// One atomic counter and expiry per policy/identity, shared by every app process.
public sealed class RedisFixedWindowRateLimiter(IConnectionMultiplexer redis, string key, int permitLimit) : RateLimiter
{
    const string Script = """
        local count = tonumber(redis.call('GET', KEYS[1]) or '0')
        if count + tonumber(ARGV[1]) > tonumber(ARGV[2]) then
            return {0, redis.call('PTTL', KEYS[1])}
        end
        count = redis.call('INCRBY', KEYS[1], ARGV[1])
        if count == tonumber(ARGV[1]) then redis.call('PEXPIRE', KEYS[1], 60000) end
        return {1, redis.call('PTTL', KEYS[1])}
        """;

    long lastActivity = Stopwatch.GetTimestamp();
    public override TimeSpan? IdleDuration => Stopwatch.GetElapsedTime(Interlocked.Read(ref lastActivity));
    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        if (permitCount == 0) return new Lease(true, TimeSpan.Zero);
        Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp());
        try { return Result(redis.GetDatabase().ScriptEvaluate(Script, [key], [permitCount, permitLimit])); }
        catch (RedisException) { return new Lease(false, TimeSpan.FromSeconds(1)); }
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        if (permitCount == 0) return new Lease(true, TimeSpan.Zero);
        Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp());
        try
        {
            var result = await redis.GetDatabase().ScriptEvaluateAsync(Script, [key], [permitCount, permitLimit]).WaitAsync(cancellationToken);
            return Result(result);
        }
        catch (RedisException) { return new Lease(false, TimeSpan.FromSeconds(1)); }
    }

    static Lease Result(RedisResult result)
    {
        var values = (RedisResult[])result!;
        return new Lease((long)values[0] == 1, TimeSpan.FromMilliseconds(Math.Max(1, (long)values[1])));
    }

    sealed class Lease(bool acquired, TimeSpan retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => acquired;
        public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter.Name];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = metadataName == MetadataName.RetryAfter.Name ? retryAfter : null;
            return metadata is not null;
        }
    }
}
