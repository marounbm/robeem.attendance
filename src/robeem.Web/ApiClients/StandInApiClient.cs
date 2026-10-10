using System.Net;
using robeem.Contracts.Security;

namespace robeem.Web.ApiClients;

public sealed class StandInApiClient : IStandInApiClient
{
    private const string Path = "api/development/actors/stand-in";

    private readonly HttpClient _http;

    public StandInApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<StandInActor?> GetAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await ApiResponses.ReadAsync<StandInActor>(response, cancellationToken);
    }
}
