using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SerikaPay;

/// <summary>Options for <see cref="SerikaPayClient"/>.</summary>
public sealed class SerikaPayClientOptions
{
    public string BaseUrl { get; set; } = SerikaPayClient.DefaultBaseUrl;
    /// <summary>Per-attempt timeout. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Retries for network errors, 429 and 5xx. Default 2.</summary>
    public int MaxRetries { get; set; } = 2;
    /// <summary>Bring your own HttpClient (e.g. from IHttpClientFactory). Its timeout is left alone.</summary>
    public HttpClient? HttpClient { get; set; }
}

/// <summary>SerikaPay API client. Thread-safe; register it as a singleton.</summary>
public sealed class SerikaPayClient
{
    public const string Version = "1.0.0";
    public const string DefaultBaseUrl = "https://wallet-api.serika.dev";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly int _maxRetries;
    private readonly HttpClient _http;

    public PaymentService Payments { get; }
    public BalanceService Balance { get; }
    public TransactionService Transactions { get; }

    public SerikaPayClient(string? apiKey, SerikaPayClientOptions? options = null)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("SerikaPay: an API key is required (sk_test_... or sk_live_...)", nameof(apiKey));
        options ??= new SerikaPayClientOptions();
        _apiKey = apiKey;
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _maxRetries = options.MaxRetries;
        _http = options.HttpClient ?? new HttpClient { Timeout = options.Timeout };
        Payments = new PaymentService(this);
        Balance = new BalanceService(this);
        Transactions = new TransactionService(this);
    }

    internal async Task<T> RequestAsync<T>(HttpMethod method, string path, IDictionary<string, string?>? query, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var url = new StringBuilder(_baseUrl).Append(path);
        var q = query?.Where(kv => kv.Value is not null).Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}").ToList();
        if (q is { Count: > 0 }) url.Append('?').Append(string.Join('&', q));
        var payload = body is null ? null : JsonSerializer.Serialize(body, body.GetType(), Json);
        var retryable = method == HttpMethod.Get || idempotencyKey is not null;

        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, url.ToString());
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.UserAgent.ParseAdd($"serikapay-dotnet/{Version}");
            if (idempotencyKey is not null) req.Headers.Add("X-Idempotency-Key", idempotencyKey);
            if (payload is not null) req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
            {
                if (retryable && attempt < _maxRetries)
                {
                    await Backoff(attempt, ct).ConfigureAwait(false);
                    continue;
                }
                throw new SerikaPayException("connection_error", $"Could not reach SerikaPay: {e.Message}", 0, null, e);
            }

            using (res)
            {
                var status = (int)res.StatusCode;
                if ((res.StatusCode == HttpStatusCode.TooManyRequests || status >= 500) && retryable && attempt < _maxRetries)
                {
                    await Backoff(attempt, ct).ConfigureAwait(false);
                    continue;
                }
                var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    JsonObject? err = null;
                    try { err = JsonNode.Parse(text)?["error"] as JsonObject; } catch (JsonException) { }
                    throw new SerikaPayException(
                        err?["type"]?.GetValue<string>() ?? "api_error",
                        err?["message"]?.GetValue<string>() ?? $"HTTP {status}",
                        status,
                        err);
                }
                return JsonSerializer.Deserialize<T>(text, Json)!;
            }
        }
    }

    private static Task Backoff(int attempt, CancellationToken ct)
    {
        var baseMs = Math.Min(500 * (1 << attempt), 5000);
        return Task.Delay(baseMs / 2 + Random.Shared.Next(baseMs / 2 + 1), ct);
    }
}

/// <summary>Create, read and refund payments.</summary>
public sealed class PaymentService
{
    private readonly SerikaPayClient _c;
    internal PaymentService(SerikaPayClient c) => _c = c;

    /// <summary>Charge your wallet. Returns the completed payment.</summary>
    public Task<Payment> CreateAsync(PaymentCreateOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _c.RequestAsync<Payment>(HttpMethod.Post, "/v1/payments", null, options, options.IdempotencyKey ?? Guid.NewGuid().ToString(), ct);
    }

    public Task<Payment> GetAsync(string id, CancellationToken ct = default) =>
        _c.RequestAsync<Payment>(HttpMethod.Get, $"/v1/payments/{Uri.EscapeDataString(id)}", null, null, null, ct);

    public Task<PaymentList> ListAsync(ListOptions? options = null, CancellationToken ct = default) =>
        _c.RequestAsync<PaymentList>(HttpMethod.Get, "/v1/payments", new Dictionary<string, string?> { ["limit"] = options?.Limit?.ToString() }, null, null, ct);

    /// <summary>Refund all of a completed payment, or <see cref="PaymentRefundOptions.Amount"/> of it.</summary>
    public Task<Payment> RefundAsync(string id, PaymentRefundOptions? options = null, CancellationToken ct = default) =>
        _c.RequestAsync<Payment>(HttpMethod.Post, $"/v1/payments/{Uri.EscapeDataString(id)}/refund", null, options ?? new PaymentRefundOptions(), null, ct);
}

/// <summary>Read your wallet balance.</summary>
public sealed class BalanceService
{
    private readonly SerikaPayClient _c;
    internal BalanceService(SerikaPayClient c) => _c = c;

    /// <summary>The balance of one ledger currency (EUR by default).</summary>
    public Task<Balance> GetAsync(string currency = "EUR", CancellationToken ct = default) =>
        _c.RequestAsync<Balance>(HttpMethod.Get, "/v1/balance", new Dictionary<string, string?> { ["currency"] = currency }, null, null, ct);
}

/// <summary>List ledger entries.</summary>
public sealed class TransactionService
{
    private readonly SerikaPayClient _c;
    internal TransactionService(SerikaPayClient c) => _c = c;

    public Task<TransactionList> ListAsync(TransactionListOptions? options = null, CancellationToken ct = default) =>
        _c.RequestAsync<TransactionList>(HttpMethod.Get, "/v1/transactions", new Dictionary<string, string?>
        {
            ["limit"] = options?.Limit?.ToString(),
            ["type"] = options?.Type,
            ["currency"] = options?.Currency,
        }, null, null, ct);
}
