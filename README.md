# SerikaPay .NET library

[![CI](https://github.com/serika-dev/serikapay-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/serika-dev/serikapay-dotnet/actions/workflows/ci.yml)

The official [SerikaPay](https://wallet.serika.dev/developers) library for .NET 8+. No dependencies beyond the BCL.

## Install

```bash
dotnet add package SerikaPay
```

## Usage

Create a key under **Developers → API keys** in your wallet. Keep it on the server.

```csharp
using SerikaPay;

var serika = new SerikaPayClient(Environment.GetEnvironmentVariable("SERIKA_SECRET_KEY"));

var payment = await serika.Payments.CreateAsync(new PaymentCreateOptions
{
    Amount = 4.99m,
    Description = "Sticker pack",
    CustomerEmail = "fan@example.com",
    Metadata = new() { ["orderId"] = "1042" },
    IdempotencyKey = "order_1042", // optional — generated if null
});

Console.WriteLine(payment.Status); // completed
```

Amounts are `decimal`, so coins never pick up floating-point error.

### Payments

```csharp
var p = await serika.Payments.GetAsync("pay_m1x2y3");
var page = await serika.Payments.ListAsync(new ListOptions { Limit = 10 }); // page.Data, page.HasMore
await serika.Payments.RefundAsync("pay_m1x2y3");                               // full refund
await serika.Payments.RefundAsync("pay_m1x2y3", new PaymentRefundOptions { Amount = 2.00m });
```

### Balance and transactions

```csharp
var balance = await serika.Balance.GetAsync();          // EUR
var txs = await serika.Transactions.ListAsync(new TransactionListOptions { Limit = 20, Type = "spend" });
```

### Webhooks (ASP.NET Core minimal API)

```csharp
app.MapPost("/webhooks/serika", async (HttpRequest req) =>
{
    var json = await new StreamReader(req.Body).ReadToEndAsync();
    try
    {
        var evt = Webhook.ConstructEvent(json, req.Headers["Serika-Signature"], builder.Configuration["Serika:WebhookSecret"]);
        if (evt.Type == "payment.completed")
        {
            var paid = evt.GetPayment();
            // fulfil the order
        }
        return Results.Ok();
    }
    catch (SignatureVerificationException)
    {
        return Results.BadRequest();
    }
});
```

### Errors

```csharp
try
{
    await serika.Payments.CreateAsync(new PaymentCreateOptions { Amount = 4.99m });
}
catch (SerikaPayException e) when (e.Type == "insufficient_balance")
{
    // e.StatusCode == 400, e.Raw has the full error object
}
```

Network failures throw `SerikaPayException` with `Type == "connection_error"` and `StatusCode == 0`.

## Configuration

```csharp
var serika = new SerikaPayClient(key, new SerikaPayClientOptions
{
    BaseUrl = "https://wallet-api.serika.dev", // default
    Timeout = TimeSpan.FromSeconds(30),
    MaxRetries = 2,                             // network errors, 429 and 5xx
    HttpClient = httpClientFactory.CreateClient("serika"), // optional
});
```

`GET` requests and payment creation (which always carries an idempotency key) are retried with exponential backoff; refunds are not. The client is thread-safe — register it as a singleton.

## Development

```bash
dotnet test
```

## License

MIT
