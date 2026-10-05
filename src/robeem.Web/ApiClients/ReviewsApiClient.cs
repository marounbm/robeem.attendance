using System.Globalization;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class ReviewsApiClient : IReviewsApiClient
{
    private const string Path = "api/attendance/reviews";

    private readonly HttpClient _http;

    public ReviewsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ReviewListItem>> ListPendingAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/pending", cancellationToken);
        return await ApiResponses.ReadAsync<List<ReviewListItem>>(response, cancellationToken);
    }

    public async Task<ApiCommandResult<ReviewDecisionResponse>> DecideAsync(int recordId, DecideReviewRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Path}/{recordId.ToString(CultureInfo.InvariantCulture)}/decision", request, cancellationToken);
        return await ApiResponses.CommandAsync<ReviewDecisionResponse>(response, cancellationToken);
    }
}
