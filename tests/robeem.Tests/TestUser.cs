using robeem.Application.Security;

namespace robeem.Tests;

/// <summary>A current user a test names.</summary>
public sealed class TestUser : ICurrentUser
{
    private readonly HashSet<string> _roles;

    public TestUser(int? employeeId, params string[] roles)
    {
        EmployeeId = employeeId;
        _roles = [.. roles];
    }

    public bool IsAuthenticated => true;

    public int? EmployeeId { get; }

    public string Actor => EmployeeId is { } id ? $"employee:{id}" : "user:test";

    public bool IsInRole(string role) => _roles.Contains(role);
}
