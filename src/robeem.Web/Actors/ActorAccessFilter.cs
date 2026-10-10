using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using robeem.Web.Navigation;

namespace robeem.Web.Actors;

/// <summary>
/// Shows the "Not allowed" page, without calling robeem.Api, when who the request will be treated
/// as (<see cref="ICurrentActor.Effective"/>: the Development "act as" choice, else the Api's own
/// stand-in user) cannot use the screen: they hold none of the roles the menu lists for the
/// controller, or the action is <see cref="RequiresEmployeeAttribute"/> and they have no employee.
/// The Api would refuse these calls with a 403 anyway; this just spares the round trip and says
/// why. When who they are is not known (an unreachable Api, or production), the Api decides.
/// </summary>
public sealed class ActorAccessFilter : IAsyncActionFilter
{
    private readonly ICurrentActor _actor;

    public ActorAccessFilter(ICurrentActor actor)
    {
        _actor = actor;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        await _actor.ResolveAsync(context.HttpContext.RequestAborted);
        if (Refuse(context) is { } refusal)
        {
            context.Result = refusal;
            return;
        }

        await next();
    }

    private ViewResult? Refuse(ActionExecutingContext context)
    {
        if (_actor.Effective is not { } actor)
        {
            return null;
        }

        // The screen as the menu lists it: the same rule that hides it from the menu and the start page.
        var controller = context.RouteData.Values["controller"] as string;
        var access = NavigationMenu.Find(controller) is { } item ? NavigationMenu.AccessTo(_actor, item) : NavAccess.Allowed;
        if (access == NavAccess.MissingRole)
        {
            return AccessDeniedPage.Create(context.ModelState);
        }

        // An action the Api answers only for a user linked to an employee, whether or not it is a menu screen (a decision, a proposal).
        var requirement = context.ActionDescriptor.EndpointMetadata.OfType<RequiresEmployeeAttribute>().LastOrDefault();
        if (access == NavAccess.NeedsEmployee
            || (requirement is not null && actor.EmployeeId is null && !(requirement.HrExempt && _actor.IsInRole(NavigationMenu.Hr))))
        {
            var who = _actor.HasChoice ? "the person you are acting as" : "the Api's development user";
            return AccessDeniedPage.Create(
                context.ModelState,
                $"This screen shows what an employee's record may see, and {who} is not linked to an employee. Act as an employee instead.");
        }

        return null;
    }
}
