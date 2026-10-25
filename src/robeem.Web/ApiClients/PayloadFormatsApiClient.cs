using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class PayloadFormatsApiClient : IPayloadFormatsApiClient
{
    private const string Path = "api/attendance/payload-formats";

    private readonly HttpClient _http;

    public PayloadFormatsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<PayloadFormatListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<PayloadFormatListItem>>(response, cancellationToken);
    }

    public async Task<PayloadFormatInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}/input", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<PayloadFormatInput>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(PayloadFormatInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<PayloadFormatInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, PayloadFormatInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<PayloadFormatInput>(response, id, cancellationToken);
    }

    public async Task<ApiCommandResult<PayloadFormatPreview>> PreviewAsync(PayloadFormatInput input, Stream file, string fileName, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StreamContent(file), "file", fileName },
            { new StringContent(JsonSerializer.Serialize(input, JsonSerializerOptions.Web)), "format" },
        };
        using var response = await _http.PostAsync($"{Path}/preview", content, cancellationToken);
        return await ApiResponses.CommandAsync<PayloadFormatPreview>(response, cancellationToken);
    }
}
