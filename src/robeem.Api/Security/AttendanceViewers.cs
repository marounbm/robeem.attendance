using robeem.Application.Attendance;
using robeem.Application.Security;

namespace robeem.Api.Security;

/// <summary>Builds the <see cref="ExceptionViewer"/> that scopes what a user may look at.</summary>
public static class AttendanceViewers
{
    /// <summary>
    /// Who is looking, or null when there is nobody to scope to. An Employee's or Manager's scope
    /// depends on who they are, so without an <c>employee_id</c> they get null. HR sees everyone and
    /// needs no employee of their own: an HR user without one is a viewer with employee id 0, which no
    /// employee has, and with only the HR scope. That is also what the Development stand-in user (every
    /// role, no employee) is until someone is chosen in "act as".
    /// </summary>
    public static ExceptionViewer? For(ICurrentUser user)
    {
        var isHr = user.IsInRole(AttendanceRoles.HRAdministrator);
        if (user.EmployeeId is { } employeeId)
        {
            return new ExceptionViewer(employeeId, user.IsInRole(AttendanceRoles.Employee), user.IsInRole(AttendanceRoles.Manager), isHr);
        }

        return isHr ? new ExceptionViewer(0, false, false, true) : null;
    }
}
