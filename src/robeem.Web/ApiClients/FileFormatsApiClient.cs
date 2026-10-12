using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class FileFormatsApiClient : IFileFormatsApiClient
{
    private const string Path = "api/attendance/file-formats";

    private readonly HttpClient _http;

    public FileFormatsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<FileFormatListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<FileFormatListItem>>(response, cancellationToken);
    }

    public async Task<FileFormatInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}/input", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<FileFormatInput>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(FileFormatInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<FileFormatInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, FileFormatInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<FileFormatInput>(response, id, cancellationToken);
    }

    public async Task<ApiCommandResult<FileFormatPreview>> PreviewAsync(FileFormatInput input, Stream file, string fileName, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StreamContent(file), "file", fileName },
            { new StringContent(JsonSerializer.Serialize(input, JsonSerializerOptions.Web)), "format" },
        };
        using var response = await _http.PostAsync($"{Path}/preview", content, cancellationToken);
        return await ApiResponses.CommandAsync<FileFormatPreview>(response, cancellationToken);
    }
}
