using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SerikaPay;

/// <summary>A charge against your wallet.</summary>
public sealed record Payment
{
    public string Id { get; init; } = "";
    public string Object { get; init; } = "payment";
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EUR";
    /// <summary>pending, completed, failed, refunded or expired.</summary>
    public string Status { get; init; } = "";
    public string? Description { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerId { get; init; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Balance of one ledger currency.</summary>
public sealed record Balance
{
    public string Object { get; init; } = "balance";
    public decimal Available { get; init; }
    public decimal Reserved { get; init; }
    public decimal Total { get; init; }
    public string Currency { get; init; } = "EUR";
}

/// <summary>One ledger entry.</summary>
public sealed record Transaction
{
    public string Id { get; init; } = "";
    /// <summary>purchase, spend, refund or adjustment.</summary>
    public string Type { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal BalanceAfter { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

public sealed record PaymentList
{
    public List<Payment> Data { get; init; } = new();
    public bool HasMore { get; init; }
}

public sealed record TransactionList
{
    public List<Transaction> Data { get; init; } = new();
}

/// <summary>Fields for <see cref="PaymentService.CreateAsync"/>.</summary>
public sealed class PaymentCreateOptions
{
    /// <summary>Amount in coins, at least 0.01.</summary>
    public decimal Amount { get; set; }
    /// <summary>EUR (default), USD or JPY ledger.</summary>
    public string? Currency { get; set; }
    public string? Description { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerId { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    /// <summary>Makes the request safe to retry. Generated when null.</summary>
    [JsonIgnore]
    public string? IdempotencyKey { get; set; }
}

public sealed class PaymentRefundOptions
{
    /// <summary>Partial amount. Null refunds the whole payment.</summary>
    public decimal? Amount { get; set; }
}

public class ListOptions
{
    /// <summary>1–100, default 10.</summary>
    public int? Limit { get; set; }
}

public sealed class TransactionListOptions : ListOptions
{
    public string? Type { get; set; }
    public string? Currency { get; set; }
}

/// <summary>A webhook delivery.</summary>
public sealed record Event
{
    public string Id { get; init; } = "";
    public string Object { get; init; } = "event";
    /// <summary>e.g. payment.completed</summary>
    public string Type { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public JsonObject Data { get; init; } = new();

    /// <summary>Decode <see cref="Data"/> as a payment, for payment.* events.</summary>
    public Payment? GetPayment() => Data.Deserialize<Payment>(SerikaPayClient.Json);
}
