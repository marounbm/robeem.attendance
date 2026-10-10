using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace robeem.Web.Actors;

/// <summary>The "Not allowed" page, shared by the filters that turn a refusal into it.</summary>
public static class AccessDeniedPage
{
    public const string ReasonKey = "Reason";

    /// <summary>Builds the 403 page; <paramref name="reason"/> replaces the default "your roles do not allow it" text.</summary>
    public static ViewResult Create(ModelStateDictionary modelState, string? reason = null)
    {
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState);
        if (reason is not null)
        {
            viewData[ReasonKey] = reason;
        }

        return new ViewResult { ViewName = "AccessDenied", StatusCode = StatusCodes.Status403Forbidden, ViewData = viewData };
    }
}
