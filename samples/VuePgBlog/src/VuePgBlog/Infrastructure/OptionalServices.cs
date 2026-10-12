using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace VuePgBlog.Infrastructure;

/// <summary>Stable per-sample/per-environment names for shared optional backends.</summary>
public sealed class OptionalServiceNamespace
{
    public OptionalServiceNamespace(string sampleFolderName, string environmentName, string? meilisearchIndex = null)
    {
        SampleFolderName = RequiredPart(sampleFolderName, nameof(sampleFolderName));
        EnvironmentName = RequiredPart(environmentName, nameof(environmentName));
        if (!string.IsNullOrWhiteSpace(meilisearchIndex))
        {
            if (meilisearchIndex.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
                throw new ArgumentException("MEILISEARCH_INDEX must contain only ASCII letters, numbers, hyphens or underscores.", nameof(meilisearchIndex));
            PostsIndex = meilisearchIndex;
        }
    }

    private string? PostsIndex { get; }

    public string SampleFolderName { get; }
    public string EnvironmentName { get; }

    public string RedisPrefix => $"{SampleFolderName}:{EnvironmentName}:";
    public string RedisKey(string key) => RedisPrefix + RequiredPart(key, nameof(key));
    public string MeilisearchIndex(string indexName) =>
        indexName == "posts" && PostsIndex is not null ? PostsIndex
            : $"{SampleFolderName}-{EnvironmentName}-{RequiredPart(indexName, nameof(indexName))}";

    static string RequiredPart(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

/// <summary>Parsed Redis URL. Its string representation deliberately never includes credentials.</summary>
public sealed class RedisUrlSettings(
    string host,
    int port,
    int database,
    bool useTls,
    string? username,
    string? password)
{
    public string Host { get; } = host;
    public int Port { get; } = port;
    public int Database { get; } = database;
    public bool UseTls { get; } = useTls;
    public string? Username { get; } = username;
    public string? Password { get; } = password;

    public static RedisUrlSettings Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "redis" && uri.Scheme != "rediss")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new FormatException("Redis URL must be an absolute redis:// or rediss:// URL.");

        var databaseText = uri.AbsolutePath.Trim('/');
        var database = 0;
        if (databaseText.Length > 0
            && (!int.TryParse(databaseText, NumberStyles.None, CultureInfo.InvariantCulture, out database) || database < 0))
            throw new FormatException("Redis URL database path must be a non-negative integer.");

        var username = (string?)null;
        var password = (string?)null;
        var userInfo = uri.GetComponents(UriComponents.UserInfo, UriFormat.UriEscaped);
        if (userInfo.Length > 0)
        {
            var separator = userInfo.IndexOf(':');
            if (separator < 0)
                password = Uri.UnescapeDataString(userInfo);
            else
            {
                username = Uri.UnescapeDataString(userInfo[..separator]);
                password = Uri.UnescapeDataString(userInfo[(separator + 1)..]);
            }
        }

        var useTls = uri.Scheme == "rediss";
        var query = uri.Query.TrimStart('?');
        if (query.Length > 0)
        {
            foreach (var parameter in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = parameter.Split('=', 2);
                var key = Uri.UnescapeDataString(pieces[0]);
                var setting = pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : "";
                if (!string.Equals(key, "ssl", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(key, "tls", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("Redis URL contains an unsupported query parameter.");
                if (!bool.TryParse(setting, out useTls))
                    throw new FormatException("Redis URL TLS query value must be true or false.");
            }
        }

        var port = uri.IsDefaultPort ? 6379 : uri.Port;
        return new RedisUrlSettings(uri.Host, port, database, useTls, username, password);
    }

    public static RedisUrlSettings? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration["Redis:Url"];
        if (string.IsNullOrWhiteSpace(value)) value = configuration["REDIS_URL"];
        return string.IsNullOrWhiteSpace(value) ? null : Parse(value);
    }

    public override string ToString() => $"redis{(UseTls ? "s" : "")}://{Host}:{Port}/{Database} (credentials redacted)";
}

/// <summary>Optional Meilisearch endpoint and API key loaded from application configuration.</summary>
public sealed class MeilisearchConnectionSettings
{
    public MeilisearchConnectionSettings(Uri endpoint, string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
            throw new ArgumentException("Meilisearch endpoint must be an absolute HTTP(S) URL without credentials, query, or fragment.", nameof(endpoint));
        Endpoint = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    public Uri Endpoint { get; }
    public string? ApiKey { get; }

    public static MeilisearchConnectionSettings? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration["Meilisearch:Url"];
        if (string.IsNullOrWhiteSpace(value)) value = configuration["MEILISEARCH_URL"];
        if (string.IsNullOrWhiteSpace(value)) return null;
        var apiKey = configuration["Meilisearch:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = configuration["MEILISEARCH_API_KEY"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
            throw new FormatException("Meilisearch URL must be an absolute HTTP(S) URL.");
        return new MeilisearchConnectionSettings(endpoint, apiKey);
    }

    public override string ToString() => $"Meilisearch {Endpoint} (API key redacted)";
}

public readonly record struct MeilisearchTaskId(long Value);

public enum MeilisearchTaskState
{
    Enqueued,
    Processing,
    Succeeded,
    Failed,
    Canceled,
}

public sealed record MeilisearchTaskResult(MeilisearchTaskId Id, MeilisearchTaskState State)
{
    public string? ErrorCode { get; init; }
}
public sealed record MeilisearchSearchResult(IReadOnlyList<string> DocumentIds, long EstimatedTotalHits);

public sealed class MeilisearchTaskFailedException(MeilisearchTaskId id, MeilisearchTaskState state, string? errorCode = null)
    : Exception($"Meilisearch task {id.Value} ended with state {state}.")
{
    public string? ErrorCode { get; } = errorCode;
    public MeilisearchTaskId TaskId { get; } = id;
    public MeilisearchTaskState State { get; } = state;
}

/// <summary>
/// Minimal Meilisearch HTTP helper. Enqueue methods return task IDs suitable for durable job records;
/// waiting is explicit so a worker can fence later writes until an indexing task succeeds.
/// </summary>
public sealed class MeilisearchTaskClient
{
    readonly HttpClient http;
    readonly Uri endpoint;
    readonly string? apiKey;

    public MeilisearchTaskClient(HttpClient http, MeilisearchConnectionSettings settings)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        ArgumentNullException.ThrowIfNull(settings);
        endpoint = settings.Endpoint;
        apiKey = settings.ApiKey;
    }

    /// <summary>Creates the named index when absent; returns its task ID, or null when it exists.</summary>
    public async Task<MeilisearchTaskId?> EnsureIndexAsync(
        string indexUid,
        string primaryKey = "id",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKey);
        var escapedUid = Uri.EscapeDataString(indexUid);
        using (var get = CreateRequest(HttpMethod.Get, $"indexes/{escapedUid}"))
        using (var existing = await http.SendAsync(get, cancellationToken))
        {
            if (existing.IsSuccessStatusCode) return null;
            if (existing.StatusCode != System.Net.HttpStatusCode.NotFound) EnsureSuccess(existing);
        }

        using var request = CreateRequest(HttpMethod.Post, "indexes");
        request.Content = JsonContent.Create(new { uid = indexUid, primaryKey });
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict && await IsAlreadyExistsConflictAsync(response, cancellationToken)) return null;
        EnsureSuccess(response);
        return await ReadEnqueuedTaskIdAsync(response, cancellationToken);
    }

    static async Task<bool> IsAlreadyExistsConflictAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && string.Equals(code.GetString(), "index_already_exists", StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    public async Task ConfirmIndexCreationAsync(MeilisearchTaskId taskId, string indexUid, CancellationToken cancellationToken = default)
    {
        try { await WaitForSuccessAsync(taskId, cancellationToken: cancellationToken); }
        catch (MeilisearchTaskFailedException exception) when (exception.ErrorCode == "index_already_exists")
        {
            using var request = CreateRequest(HttpMethod.Get, $"indexes/{Uri.EscapeDataString(indexUid)}");
            using var response = await http.SendAsync(request, cancellationToken);
            EnsureSuccess(response);
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("primaryKey", out var key) || key.GetString() != "id")
                throw new InvalidDataException("The existing Meilisearch index has an incompatible primary key.");
        }
    }

    public async Task<MeilisearchTaskId> EnqueueSearchSettingsAsync(
        string indexUid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexUid);
        using var request = CreateRequest(HttpMethod.Patch, $"indexes/{Uri.EscapeDataString(indexUid)}/settings");
        request.Content = JsonContent.Create(new
        {
            searchableAttributes = new[] { "title", "body", "excerpt", "authorName", "topics" },
            filterableAttributes = new[] { "authorId", "topics" },
            sortableAttributes = new[] { "publishedAt" },
        });
        using var response = await http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        return await ReadEnqueuedTaskIdAsync(response, cancellationToken);
    }

    public async Task<MeilisearchSearchResult> SearchAsync(
        string indexUid,
        string query,
        int limit,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexUid);
        ArgumentNullException.ThrowIfNull(query);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        using var request = CreateRequest(HttpMethod.Post, $"indexes/{Uri.EscapeDataString(indexUid)}/search");
        request.Content = JsonContent.Create(new
        {
            q = query,
            limit,
            offset,
            attributesToRetrieve = new[] { "id" },
            showRankingScore = false,
        });
        using var response = await http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var ids = new List<string>();
        if (root.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in hits.EnumerateArray())
            {
                if (hit.ValueKind == JsonValueKind.Object && hit.TryGetProperty("id", out var id))
                {
                    if (id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString())) ids.Add(id.GetString()!);
                }
            }
        }
        var total = root.TryGetProperty("estimatedTotalHits", out var count) && count.TryGetInt64(out var value) ? value : ids.Count;
        return new MeilisearchSearchResult(ids, total);
    }

    public async Task<MeilisearchTaskId> EnqueueUpsertAsync<TDocument>(
        string indexUid,
        IReadOnlyCollection<TDocument> documents,
        string? primaryKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexUid);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0) throw new ArgumentException("At least one document is required.", nameof(documents));

        var path = $"indexes/{Uri.EscapeDataString(indexUid)}/documents";
        if (!string.IsNullOrWhiteSpace(primaryKey)) path += $"?primaryKey={Uri.EscapeDataString(primaryKey)}";
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(documents);
        using var response = await http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        return await ReadEnqueuedTaskIdAsync(response, cancellationToken);
    }

    public async Task<MeilisearchTaskId> EnqueueDeleteAsync(
        string indexUid,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        using var request = CreateRequest(HttpMethod.Delete,
            $"indexes/{Uri.EscapeDataString(indexUid)}/documents/{Uri.EscapeDataString(documentId)}");
        using var response = await http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        return await ReadEnqueuedTaskIdAsync(response, cancellationToken);
    }

    public async Task<MeilisearchTaskId> EnqueueDeleteBatchAsync(
        string indexUid,
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexUid);
        ArgumentNullException.ThrowIfNull(documentIds);
        if (documentIds.Count == 0) throw new ArgumentException("At least one document ID is required.", nameof(documentIds));
        using var request = CreateRequest(HttpMethod.Post, $"indexes/{Uri.EscapeDataString(indexUid)}/documents/delete-batch");
        request.Content = JsonContent.Create(documentIds);
        using var response = await http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        return await ReadEnqueuedTaskIdAsync(response, cancellationToken);
    }

    public async Task<MeilisearchTaskResult> GetTaskAsync(
        MeilisearchTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        if (taskId.Value < 0) throw new ArgumentOutOfRangeException(nameof(taskId));
        using var request = CreateRequest(HttpMethod.Get, $"tasks/{taskId.Value.ToString(CultureInfo.InvariantCulture)}");
        using var response = await http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var returnedId = ReadTaskId(root);
        if (returnedId != taskId.Value)
            throw new InvalidDataException("Meilisearch returned a task ID different from the requested ID.");
        if (!root.TryGetProperty("status", out var statusValue) || statusValue.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Meilisearch task response did not contain a status.");

        var state = statusValue.GetString()?.ToLowerInvariant() switch
        {
            "enqueued" => MeilisearchTaskState.Enqueued,
            "processing" => MeilisearchTaskState.Processing,
            "succeeded" => MeilisearchTaskState.Succeeded,
            "failed" => MeilisearchTaskState.Failed,
            "canceled" => MeilisearchTaskState.Canceled,
            _ => throw new InvalidDataException("Meilisearch returned an unknown task state."),
        };
        return new MeilisearchTaskResult(taskId, state)
        { ErrorCode = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code) ? code.GetString() : null };
    }

    public async Task<MeilisearchTaskResult> WaitForSuccessAsync(
        MeilisearchTaskId taskId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        var maximumWait = timeout ?? TimeSpan.FromMinutes(2);
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (maximumWait <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await GetTaskAsync(taskId, cancellationToken);
            if (result.State == MeilisearchTaskState.Succeeded) return result;
            if (result.State is MeilisearchTaskState.Failed or MeilisearchTaskState.Canceled)
                throw new MeilisearchTaskFailedException(taskId, result.State, result.ErrorCode);
            if (Stopwatch.GetElapsedTime(started) >= maximumWait)
                throw new TimeoutException($"Meilisearch task {taskId.Value} did not finish before the wait timeout.");
            await Task.Delay(interval, cancellationToken);
        }
    }

    HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, new Uri(endpoint, relativePath));
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Do not include response bodies or request headers here; neither credentials nor echoed
            // backend configuration should accidentally end up in logs through exception messages.
            throw new HttpRequestException($"Meilisearch returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }

    static async Task<MeilisearchTaskId> ReadEnqueuedTaskIdAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var value = ReadTaskId(document.RootElement);
        if (value < 0) throw new InvalidDataException("Meilisearch returned a negative task ID.");
        return new MeilisearchTaskId(value);
    }

    static long ReadTaskId(JsonElement root)
    {
        if (root.TryGetProperty("taskUid", out var taskUid) && taskUid.TryGetInt64(out var taskValue)) return taskValue;
        if (root.TryGetProperty("uid", out var uid) && uid.TryGetInt64(out var uidValue)) return uidValue;
        throw new InvalidDataException("Meilisearch task response did not contain a task ID.");
    }
}
