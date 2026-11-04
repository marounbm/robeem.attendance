namespace robeem.Contracts.Attendance;

/// <summary>
/// How a tenant's Api reads the attendance catalog from the prime tenant's Api: read-only <c>GET</c>s under <see cref="Route"/>,
/// proved by a shared service key (there is no signed-in user in a background job).
/// </summary>
public static class CatalogProtocol
{
    /// <summary>The route the prime Api serves the catalog under.</summary>
    public const string Route = "api/catalog";

    /// <summary>The request header that carries the service key.</summary>
    public const string ServiceKeyHeader = "X-Catalog-Key";

    /// <summary>The shortest service key accepted.</summary>
    public const int MinimumServiceKeyLength = 32;
}
