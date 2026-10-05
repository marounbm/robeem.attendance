using System.Net;
using System.Text;

namespace robeem.Tests.Web;

/// <summary>Answers every request with a fixed response and records the requests it saw.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string? _json;
    private readonly string _mediaType;

    public StubHttpHandler(HttpStatusCode status, string? json = null, string mediaType = "application/json")
    {
        _status = status;
        _json = json;
        _mediaType = mediaType;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>Gets the body of each request, read when it arrived (the request's own content is disposed after the call).</summary>
    public List<string?> Bodies { get; } = new();

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("http://api.test/") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        var response = new HttpResponseMessage(_status) { RequestMessage = request };
        if (_json is not null)
        {
            response.Content = new StringContent(_json, Encoding.UTF8, _mediaType);
        }

        return response;
    }
}
