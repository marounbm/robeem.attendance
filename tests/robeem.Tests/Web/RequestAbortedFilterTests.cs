using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using robeem.Web.Tenancy;

namespace robeem.Tests.Web;

public sealed class RequestAbortedFilterTests
{
    [Fact]
    public void ACallToTheApiThatEndsBecauseTheBrowserWentAway_IsHandledQuietly()
    {
        using var aborted = new CancellationTokenSource();
        var context = ContextFor(new TaskCanceledException("The operation was canceled."), aborted.Token);
        aborted.Cancel();

        new RequestAbortedFilter().OnException(context);

        Assert.True(context.ExceptionHandled);
        Assert.IsType<EmptyResult>(context.Result);
    }

    [Fact]
    public void ACancellationThatIsNotTheBrowsers_IsLeftAlone_SoATimeoutStillShowsAsAFailure()
    {
        var context = ContextFor(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."), CancellationToken.None);

        new RequestAbortedFilter().OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    [Fact]
    public void AnyOtherErrorIsLeftAlone_EvenWhenTheBrowserHasGone()
    {
        using var aborted = new CancellationTokenSource();
        var context = ContextFor(new InvalidOperationException("Something broke."), aborted.Token);
        aborted.Cancel();

        new RequestAbortedFilter().OnException(context);

        Assert.False(context.ExceptionHandled);
    }

    private static ExceptionContext ContextFor(Exception exception, CancellationToken requestAborted)
    {
        var http = new DefaultHttpContext { RequestAborted = requestAborted };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ExceptionContext(action, []) { Exception = exception };
    }
}
