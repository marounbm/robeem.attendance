using robeem.Application.Security;

namespace robeem.Api.Security;

/// <summary>The authenticated user of the current request, from the claims of their token.</summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    public const string AnonymousActor = "anonymous";

    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public int? EmployeeId =>
        IsAuthenticated && int.TryParse(_accessor.HttpContext!.User.FindFirst(SecurityClaims.EmployeeId)?.Value, out var id) ? id : null;

    public string Actor => EmployeeId is { } id ? $"employee:{id}" : IsAuthenticated ? "user:" + (_accessor.HttpContext!.User.FindFirst("sub")?.Value ?? "unknown") : AnonymousActor;

    public bool IsInRole(string role) => _accessor.HttpContext?.User.IsInRole(role) == true;
}
