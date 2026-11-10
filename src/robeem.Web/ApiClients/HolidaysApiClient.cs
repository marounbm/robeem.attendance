using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using robeem.Contracts.HR.Holidays;

namespace robeem.Web.ApiClients;

public sealed class HolidaysApiClient : IHolidaysApiClient
{
    private const string Path = "api/holidays";

    private readonly HttpClient _http;

    public HolidaysApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<HolidayItem>> ListAsync(int year, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?> { ["year"] = year.ToString(CultureInfo.InvariantCulture) };
        using var response = await _http.GetAsync(QueryHelpers.AddQueryString(Path, query), cancellationToken);
        return await ApiResponses.ReadAsync<List<HolidayItem>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<int>> ListYearsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/years", cancellationToken);
        return await ApiResponses.ReadAsync<List<int>>(response, cancellationToken);
    }

    public async Task<HolidayItem?> GetAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(ItemPath(id), cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<HolidayItem>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(HolidayInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<HolidayInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, HolidayInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(ItemPath(id), input, cancellationToken);
        return await ApiResponses.SaveAsync<HolidayInput>(response, id, cancellationToken);
    }

    public async Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync(ItemPath(id), cancellationToken);
        return await ApiResponses.SaveAsync<HolidayInput>(response, id, cancellationToken);
    }

    private static string ItemPath(int id) => $"{Path}/{id.ToString(CultureInfo.InvariantCulture)}";
}
