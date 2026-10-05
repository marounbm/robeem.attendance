namespace robeem.Web.Actors;

/// <summary>Who the developer chose to act as: an employee (or none) and the roles to hold.</summary>
public sealed record ActingAs(int? EmployeeId, string Name, IReadOnlyList<string> Roles);
