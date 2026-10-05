using System.Globalization;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

public sealed class CorrectionsApiClient : ICorrectionsApiClient
{
    private const string Path = "api/attendance/corrections";

    private readonly HttpClient _http;

    public CorrectionsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ApiCommandResult<CreatedResponse>> ProposeAsync(ProposeCorrectionRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, request, cancellationToken);
        return await ApiResponses.CommandAsync<CreatedResponse>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover awaiting, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/pending?awaiting={awaiting}", cancellationToken);
        return await ApiResponses.ReadAsync<List<CorrectionListItem>>(response, cancellationToken);
    }

    public async Task<ApiCommandResult<CorrectionDecisionResponse>> DecideAsync(int correctionId, DecideCorrectionRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Path}/{correctionId.ToString(CultureInfo.InvariantCulture)}/decision", request, cancellationToken);
        return await ApiResponses.CommandAsync<CorrectionDecisionResponse>(response, cancellationToken);
    }
}
