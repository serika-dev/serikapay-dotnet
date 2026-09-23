using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SerikaPay;

/// <summary>Verify and parse webhook deliveries.</summary>
public static class Webhook
{
    /// <summary>The Serika-Signature value for a payload. Useful in tests.</summary>
    public static string GenerateSignature(string payload, string secret)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>True when <paramref name="signature"/> matches the raw payload.</summary>
    public static bool VerifySignature(string payload, string? signature, string? secret)
    {
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(GenerateSignature(payload, secret)),
            Encoding.UTF8.GetBytes(signature.Trim()));
    }

    /// <summary>Verify the Serika-Signature header and parse the event. Pass the raw request body.</summary>
    /// <exception cref="SignatureVerificationException">Missing or mismatched signature.</exception>
    public static Event ConstructEvent(string payload, string? signature, string? secret)
    {
        if (string.IsNullOrEmpty(signature)) throw new SignatureVerificationException("Missing Serika-Signature header");
        if (!VerifySignature(payload, signature, secret))
            throw new SignatureVerificationException("Signature does not match the payload. Are you passing the raw body and the right secret?");
        try
        {
            return JsonSerializer.Deserialize<Event>(payload, SerikaPayClient.Json)
                ?? throw new SignatureVerificationException("Payload is empty");
        }
        catch (JsonException)
        {
            throw new SignatureVerificationException("Payload is not valid JSON");
        }
    }
}
