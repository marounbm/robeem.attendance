using Microsoft.AspNetCore.Mvc;
using robeem.Web.Actors;

namespace robeem.Web.ViewComponents;

/// <summary>The navbar's "Acting as: ... Change" label. Development only.</summary>
public sealed class CurrentActorViewComponent : ViewComponent
{
    private readonly ICurrentActor _actor;
    private readonly IWebHostEnvironment _environment;

    public CurrentActorViewComponent(ICurrentActor actor, IWebHostEnvironment environment)
    {
        _actor = actor;
        _environment = environment;
    }

    public IViewComponentResult Invoke() => _environment.IsDevelopment() ? View(_actor.Choice) : Content(string.Empty);
}
