using System.Text;
using System.Text.Json;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// What the BioStar adapter reads from the integration's connection secret (CLAUDE.md §2.8): a JSON
/// object <c>{"baseUrl":"https://biostar.example.com","loginId":"...","password":"..."}</c>, and
/// optionally <c>"allowUntrustedCertificate":true</c> for a server with a self-signed certificate.
/// </summary>
public sealed record BiostarConnectionSettings(Uri BaseUrl, string LoginId, string Password, bool AllowUntrustedCertificate)
{
    /// <summary>Reads the secret. The error never repeats what the secret said.</summary>
    public static BiostarConnectionSettings Parse(string? secret)
    {
        const string Expected = "The BioStar connection secret is missing or not valid: it must be JSON with baseUrl (https), loginId and password.";
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(Expected);
        }

        try
        {
            using var document = JsonDocument.Parse(secret);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(Expected);
            }

            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            var baseUrl = Text("baseUrl");
            var loginId = Text("loginId");
            var password = Text("password");
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrEmpty(loginId)
                || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(Expected);
            }

            var untrusted = root.TryGetProperty("allowUntrustedCertificate", out var flag) && flag.ValueKind == JsonValueKind.True;
            return new BiostarConnectionSettings(uri, loginId, password, untrusted);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(Expected);
        }
    }

    // The password stays out of any text made from the record.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"BaseUrl = {BaseUrl}, LoginId = {LoginId}, AllowUntrustedCertificate = {AllowUntrustedCertificate}");
        return true;
    }
}
