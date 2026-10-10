using System.Security.Cryptography;
using System.Text;

namespace robeem.Application.Attendance;

/// <summary>
/// The signature of a webhook request: HMAC-SHA256 of the raw body under the integration's secret,
/// as hex (an optional <c>sha256=</c> prefix is accepted). Compared in constant time.
/// </summary>
public static class WebhookSignature
{
    private const string Prefix = "sha256=";

    /// <summary>Computes the signature a sender must send: lower-case hex.</summary>
    public static string Compute(string secret, byte[] body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    /// <summary>Checks the signature header against the body. Always does the work of a check, so the time does not tell whether the secret was right.</summary>
    public static bool IsValid(string secret, byte[] body, string? header)
    {
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        var text = header?.Trim() ?? string.Empty;
        if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[Prefix.Length..];
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(text));
        }
        catch (FormatException)
        {
            // Not hex at all: it can't match, and says nothing about the secret.
            return false;
        }
    }
}
