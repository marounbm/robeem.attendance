using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace robeem.Application.Attendance;

public sealed class RawPayloadSanitizer : IRawPayloadSanitizer
{
    // The payload is stored, not rendered as HTML, so keep non-ASCII text (e.g. names) readable.
    private static readonly JsonSerializerOptions WriteOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string[] _denyTerms;

    public RawPayloadSanitizer(IOptions<RawPayloadSanitizerOptions> options)
    {
        _denyTerms = RawPayloadSanitizerOptions.DefaultDenyTerms
            .Concat(options.Value.AdditionalDenyTerms)
            .Select(Normalize)
            .Where(t => t.Length > 0)
            .Distinct()
            .ToArray();
    }

    public string Sanitize(string rawJson)
    {
        ArgumentNullException.ThrowIfNull(rawJson);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawJson);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new FormatException("The raw payload is not valid JSON, so it cannot be checked for biometric fields.", ex);
        }

        return root is not null && RemoveDenied(root) ? root.ToJsonString(WriteOptions) : rawJson;
    }

    private static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Removes denied properties under <paramref name="node"/>; returns whether anything was removed.</summary>
    private bool RemoveDenied(JsonNode node)
    {
        var removed = false;

        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (IsDenied(property.Key))
                    {
                        obj.Remove(property.Key);
                        removed = true;
                    }
                    else if (property.Value is not null)
                    {
                        removed |= RemoveDenied(property.Value);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        removed |= RemoveDenied(item);
                    }
                }

                break;
        }

        return removed;
    }

    private bool IsDenied(string propertyName)
    {
        var name = Normalize(propertyName);
        return _denyTerms.Any(name.Contains);
    }
}
