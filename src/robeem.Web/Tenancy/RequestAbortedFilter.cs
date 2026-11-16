using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace robeem.Web.Tenancy;

/// <summary>
/// When the browser goes away while a screen is waiting for robeem.Api (the page was refreshed, another link was followed, or the host is
/// stopping), the request's token is cancelled and the call to the Api ends with a <see cref="OperationCanceledException"/>. Nobody is left to
/// answer, so this ends the request quietly instead of letting it surface as an unhandled error. A cancellation that is not the browser's, such as the
/// <see cref="HttpClient"/> timing out, is left alone: that one is a real failure and still shows as one.
/// </summary>
public sealed class RequestAbortedFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not OperationCanceledException || !context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        context.Result = new EmptyResult();
        context.ExceptionHandled = true;
    }
}
