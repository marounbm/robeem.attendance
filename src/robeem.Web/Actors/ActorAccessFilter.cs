using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using robeem.Web.Navigation;
using robeem.Web.Tenancy;

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
    private readonly ICurrentTenantInfo? _tenant;

    public ActorAccessFilter(ICurrentActor actor, ICurrentTenantInfo? tenant = null)
    {
        _actor = actor;
        _tenant = tenant;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        await _actor.ResolveAsync(context.HttpContext.RequestAborted);
        if (_tenant is not null)
        {
            await _tenant.ResolveAsync(context.HttpContext.RequestAborted);
        }

        if (Refuse(context) is { } refusal)
        {
            context.Result = refusal;
            return;
        }

        await next();
    }

    private ViewResult? Refuse(ActionExecutingContext context)
    {
        // The platform catalog belongs to the prime tenant. In any other tenant its management screens are not there, and what changes it
        // is refused (the Api refuses it too). A tenant that is not known to be another one is not refused: the Api decides.
        var isPrime = _tenant?.IsPrime;
        var controller = context.RouteData.Values["controller"] as string;
        var menuItem = NavigationMenu.Find(controller);
        var changesCatalog = context.ActionDescriptor.EndpointMetadata.OfType<ChangesPlatformCatalogAttribute>().Any();
        if (isPrime == false && (menuItem is { PrimeTenantOnly: true } || changesCatalog))
        {
            return AccessDeniedPage.Create(context.ModelState, "The attendance catalog belongs to the prime tenant, and can only be changed there. Here it is read-only.");
        }

        if (changesCatalog && !_actor.IsInRole(NavigationMenu.Platform))
        {
            return AccessDeniedPage.Create(context.ModelState, "Only a platform administrator can change the attendance catalog.");
        }

        if (_actor.Effective is not { } actor)
        {
            return null;
        }

        // The screen as the menu lists it: the same rule that hides it from the menu and the start page.
        var access = menuItem is not null ? NavigationMenu.AccessTo(_actor, menuItem, isPrime) : NavAccess.Allowed;
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
