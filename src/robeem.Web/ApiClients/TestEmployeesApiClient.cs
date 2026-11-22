using System.Net;
using System.Text.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class TestEmployeesApiClient : ITestEmployeesApiClient
{
    private const string Path = "api/attendance/test-employees";

    private readonly HttpClient _http;

    public TestEmployeesApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<TestEmployeeSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<TestEmployeeSummary>(response, cancellationToken);
    }

    public async Task<ApiCommandResult<TestEmployeeLoadResult>> LoadAsync(Stream file, string fileName, string timeZone, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StreamContent(file), "file", fileName },
            { new StringContent(timeZone), "timeZone" },
        };
        using var response = await _http.PostAsync(Path, content, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // The Api says why in the problem's detail: the host has not turned it on.
            return ApiCommandResult<TestEmployeeLoadResult>.Refused([await ReadDetailAsync(response, cancellationToken) ?? "Test employees can't be loaded now."]);
        }

        // A 400 carries a message for every row that is wrong.
        return await ApiResponses.CommandAsync<TestEmployeeLoadResult>(response, cancellationToken);
    }

    public async Task<ApiCommandResult<TestEmployeeDeleteResult>> PreviewDeleteAsync(Stream file, string fileName, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent { { new StreamContent(file), "file", fileName } };
        using var response = await _http.PostAsync($"{Path}/delete-preview", content, cancellationToken);
        return await CommandAsync(response, cancellationToken);
    }

    public async Task<ApiCommandResult<TestEmployeeDeleteResult>> DeleteAsync(Stream file, string fileName, string confirmation, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StreamContent(file), "file", fileName },
            { new StringContent(confirmation ?? string.Empty), "confirmation" },
        };
        using var response = await _http.PostAsync($"{Path}/delete", content, cancellationToken);
        return await CommandAsync(response, cancellationToken);
    }

    private static async Task<ApiCommandResult<TestEmployeeDeleteResult>> CommandAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // The Api says why in the problem's detail: not turned on, or the tenant id was not typed.
            return ApiCommandResult<TestEmployeeDeleteResult>.Refused([await ReadDetailAsync(response, cancellationToken) ?? "The employees can't be deleted now."]);
        }

        // A 400 carries a message for everything that is wrong.
        return await ApiResponses.CommandAsync<TestEmployeeDeleteResult>(response, cancellationToken);
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
