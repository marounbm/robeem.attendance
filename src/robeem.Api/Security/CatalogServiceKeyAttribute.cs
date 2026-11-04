using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using robeem.Contracts.Attendance;
using robeem.Infrastructure.Attendance.Catalog;

namespace robeem.Api.Security;

/// <summary>
/// Lets a call through only when it presents the catalog service key (<see cref="CatalogProtocol.ServiceKeyHeader"/>). The caller is
/// another tenant's Api, often a background job, so there is no signed-in user. The key is compared in constant time, and a missing,
/// wrong or unconfigured key is the same 401 with no hint of which, so a caller learns nothing about the key.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CatalogServiceKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configured = context.HttpContext.RequestServices.GetRequiredService<IOptions<PrimeCatalogOptions>>().Value.ServiceKey;
        var presented = context.HttpContext.Request.Headers[CatalogProtocol.ServiceKeyHeader].ToString();

        if (configured is null || configured.Length < CatalogProtocol.MinimumServiceKeyLength || !Matches(configured, presented))
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "The catalog request was not accepted." })
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            };
        }
    }

    // Hashing first gives both sides the same length, which FixedTimeEquals needs, without revealing the key's length.
    private static bool Matches(string configured, string presented) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)), SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
}
