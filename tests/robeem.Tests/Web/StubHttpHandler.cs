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

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("http://api.test/") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = new HttpResponseMessage(_status) { RequestMessage = request };
        if (_json is not null)
        {
            response.Content = new StringContent(_json, Encoding.UTF8, _mediaType);
        }

        return Task.FromResult(response);
    }
}
