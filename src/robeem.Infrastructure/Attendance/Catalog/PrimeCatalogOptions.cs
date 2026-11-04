using robeem.Contracts.Attendance;

namespace robeem.Infrastructure.Attendance.Catalog;

/// <summary>
/// Bound from the <c>Catalog</c> section. Every tenant but the prime one reads the attendance catalog by calling the prime tenant's
/// Api (<see cref="PrimeApiBaseUrl"/>); the same <see cref="ServiceKey"/> is what that Api demands of the call. Nothing is cached.
/// </summary>
public sealed class PrimeCatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>Gets or sets the absolute URL of the Api that serves the prime tenant (<c>https://...</c> outside Development). May be this very Api.</summary>
    public string? PrimeApiBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the shared secret of the catalog calls: sent by a tenant's Api, checked by the prime Api. At least
    /// <see cref="CatalogProtocol.MinimumServiceKeyLength"/> characters. Keep it in user-secrets or a secret store, never in a committed file.
    /// </summary>
    public string? ServiceKey { get; set; }

    /// <summary>Gets or sets how long one call may take before the catalog is reported unavailable.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Returns what is wrong with these options for reading the catalog remotely; empty when they are usable.</summary>
    public IReadOnlyList<string> Validate(bool requireHttps)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(PrimeApiBaseUrl, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"'{SectionName}:{nameof(PrimeApiBaseUrl)}' must be the absolute http(s) URL of the prime tenant's Api.");
        }
        else if (requireHttps && url.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"'{SectionName}:{nameof(PrimeApiBaseUrl)}' must be https outside Development: the service key travels in a header.");
        }

        if (ServiceKey is null || ServiceKey.Length < CatalogProtocol.MinimumServiceKeyLength)
        {
            errors.Add($"'{SectionName}:{nameof(ServiceKey)}' is required and must be at least {CatalogProtocol.MinimumServiceKeyLength} characters.");
        }

        if (TimeoutSeconds is < 1 or > 120)
        {
            errors.Add($"'{SectionName}:{nameof(TimeoutSeconds)}' must be between 1 and 120.");
        }

        return errors;
    }
}
