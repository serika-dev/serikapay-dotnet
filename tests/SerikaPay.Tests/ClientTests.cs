using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SerikaPay;
using Xunit;

public class ClientTests
{
    const string PaymentJson = """{"id":"pay_m1x2y3","object":"payment","amount":4.99,"currency":"EUR","status":"completed","description":"Sticker pack","customerEmail":null,"customerId":null,"metadata":{"orderId":"1042"},"createdAt":"2026-09-23T12:00:00.000Z","completedAt":"2026-09-23T12:00:00.120Z","expiresAt":null}""";

    sealed class Stub : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Req, string? Body)> Seen = new();
        public readonly Queue<(int Status, string Body)> Replies = new();
        public bool Fail;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Fail) throw new HttpRequestException("connection refused");
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Seen.Add((request, body));
            var (status, json) = Replies.Count > 0 ? Replies.Dequeue() : (200, PaymentJson);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    static (SerikaPayClient, Stub) Make()
    {
        var stub = new Stub();
        var client = new SerikaPayClient("sk_test_abc12345", new SerikaPayClientOptions { HttpClient = new HttpClient(stub), BaseUrl = "http://mock" });
        return (client, stub);
    }

    [Fact]
    public async Task CreatePayment()
    {
        var (c, s) = Make();
        var p = await c.Payments.CreateAsync(new PaymentCreateOptions
        {
            Amount = 4.99m,
            Description = "Sticker pack",
            Metadata = new() { ["orderId"] = "1042" },
            IdempotencyKey = "order_1042",
        });
        Assert.Equal("pay_m1x2y3", p.Id);
        Assert.Equal(4.99m, p.Amount);
        Assert.Equal("1042", p.Metadata["orderId"].GetString());

        var (req, body) = s.Seen[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("http://mock/v1/payments", req.RequestUri!.ToString());
        Assert.Equal("Bearer sk_test_abc12345", req.Headers.Authorization!.ToString());
        Assert.Equal("order_1042", req.Headers.GetValues("X-Idempotency-Key").Single());
        Assert.StartsWith("serikapay-dotnet/", req.Headers.UserAgent.ToString());
        Assert.Equal("""{"amount":4.99,"description":"Sticker pack","metadata":{"orderId":"1042"}}""", body);
    }

    [Fact]
    public async Task CreateGeneratesIdempotencyKey()
    {
        var (c, s) = Make();
        await c.Payments.CreateAsync(new PaymentCreateOptions { Amount = 1 });
        Assert.True(s.Seen[0].Req.Headers.GetValues("X-Idempotency-Key").Single().Length >= 16);
    }

    [Fact]
    public async Task Routes()
    {
        var (c, s) = Make();
        await c.Payments.GetAsync("pay_m1x2y3");
        s.Replies.Enqueue((200, $$"""{"object":"list","data":[{{PaymentJson}}],"hasMore":false}"""));
        var page = await c.Payments.ListAsync(new ListOptions { Limit = 3 });
        await c.Payments.RefundAsync("pay_m1x2y3", new PaymentRefundOptions { Amount = 2.00m });
        Assert.Single(page.Data);
        Assert.Equal(new[] { "GET /v1/payments/pay_m1x2y3", "GET /v1/payments?limit=3", "POST /v1/payments/pay_m1x2y3/refund" },
            s.Seen.Select(x => $"{x.Req.Method} {x.Req.RequestUri!.PathAndQuery}"));
        Assert.Equal("""{"amount":2.00}""", s.Seen[2].Body);
    }

    [Fact]
    public async Task BalanceAndTransactions()
    {
        var (c, s) = Make();
        s.Replies.Enqueue((200, """{"object":"balance","available":10,"reserved":0,"total":10,"currency":"EUR"}"""));
        s.Replies.Enqueue((200, """{"object":"list","data":[]}"""));
        Assert.Equal(10m, (await c.Balance.GetAsync()).Available);
        await c.Transactions.ListAsync(new TransactionListOptions { Limit = 20, Type = "spend" });
        Assert.Equal("/v1/balance?currency=EUR", s.Seen[0].Req.RequestUri!.PathAndQuery);
        Assert.Equal("/v1/transactions?limit=20&type=spend", s.Seen[1].Req.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task ApiError()
    {
        var (c, s) = Make();
        s.Replies.Enqueue((400, """{"error":{"type":"insufficient_balance","message":"Insufficient","available":3}}"""));
        var e = await Assert.ThrowsAsync<SerikaPayException>(() => c.Payments.CreateAsync(new PaymentCreateOptions { Amount = 4.99m }));
        Assert.Equal("insufficient_balance", e.Type);
        Assert.Equal(400, e.StatusCode);
        Assert.Equal(3, e.Raw!["available"]!.GetValue<int>());
    }

    [Fact]
    public async Task Retries()
    {
        var (c, s) = Make();
        s.Replies.Enqueue((503, """{"error":{"type":"api_error","message":"down"}}"""));
        Assert.Equal("pay_m1x2y3", (await c.Payments.GetAsync("pay_m1x2y3")).Id);
        Assert.Equal(2, s.Seen.Count);

        var (c2, s2) = Make();
        s2.Replies.Enqueue((500, """{"error":{"type":"api_error","message":"boom"}}"""));
        await Assert.ThrowsAsync<SerikaPayException>(() => c2.Payments.RefundAsync("pay_m1x2y3"));
        Assert.Single(s2.Seen);
    }

    [Fact]
    public async Task ConnectionError()
    {
        var stub = new Stub { Fail = true };
        var c = new SerikaPayClient("sk_test_abc12345", new SerikaPayClientOptions { HttpClient = new HttpClient(stub), MaxRetries = 0 });
        var e = await Assert.ThrowsAsync<SerikaPayException>(() => c.Balance.GetAsync());
        Assert.Equal("connection_error", e.Type);
    }

    const string WhPayload = """{"id":"evt_1","object":"event","type":"payment.completed","createdAt":"2026-09-23T12:00:00.000Z","data":{"id":"pay_m1x2y3","amount":4.99}}""";
    const string WhSecret = "whsec_test_secret";
    const string WhSig = "sha256=87f1961717e2bf9fc53d344cf0bcd0b8a22a8f99c40ac890ed7ab48554f47415";

    [Fact]
    public void Webhooks()
    {
        Assert.Equal(WhSig, Webhook.GenerateSignature(WhPayload, WhSecret));
        var e = Webhook.ConstructEvent(WhPayload, WhSig, WhSecret);
        Assert.Equal("payment.completed", e.Type);
        Assert.Equal("pay_m1x2y3", e.GetPayment()!.Id);
        Assert.Throws<SignatureVerificationException>(() => Webhook.ConstructEvent(WhPayload.Replace("4.99", "499"), WhSig, WhSecret));
        Assert.Throws<SignatureVerificationException>(() => Webhook.ConstructEvent(WhPayload, WhSig, "nope"));
        Assert.Throws<SignatureVerificationException>(() => Webhook.ConstructEvent(WhPayload, null, WhSecret));
    }
}
