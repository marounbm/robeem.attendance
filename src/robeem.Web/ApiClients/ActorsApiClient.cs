using System.Net;
using robeem.Contracts.Security;

namespace robeem.Web.ApiClients;

public sealed class ActorsApiClient : IActorsApiClient
{
    private const string ActorsPath = "api/development/actors";

    private readonly HttpClient _http;

    public ActorsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ActorOption>?> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(ActorsPath, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ApiResponses.ReadAsync<List<ActorOption>>(response, cancellationToken);
    }
}
