using System.Text.Json.Nodes;

namespace SerikaPay;

/// <summary>Thrown for every non-2xx response, and for network failures (Type "connection_error").</summary>
public class SerikaPayException : Exception
{
    /// <summary>Error type, e.g. insufficient_balance.</summary>
    public string Type { get; }
    /// <summary>HTTP status, or 0 for network failures.</summary>
    public int StatusCode { get; }
    /// <summary>The raw error object from the response.</summary>
    public JsonObject? Raw { get; }

    public SerikaPayException(string type, string message, int statusCode, JsonObject? raw, Exception? inner = null)
        : base(message, inner)
    {
        Type = type;
        StatusCode = statusCode;
        Raw = raw;
    }
}

/// <summary>The Serika-Signature header is missing or doesn't match the payload.</summary>
public class SignatureVerificationException : Exception
{
    public SignatureVerificationException(string message) : base(message) { }
}
