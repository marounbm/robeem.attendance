namespace robeem.Web.Actors;

/// <summary>
/// Marks a screen or action that robeem.Api answers only for a user linked to an employee (its
/// <c>employee_id</c> claim): whose days, exceptions or reports are shown depends on who they are.
/// <see cref="ActorAccessFilter"/> turns an acting choice without an employee away before the call.
/// An attribute on an action wins over one on its controller.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresEmployeeAttribute : Attribute
{
    /// <summary>Gets or sets a value indicating whether HR may open it with no employee of their own (they see everyone's, so need none).</summary>
    public bool HrExempt { get; set; }
}
