using robeem.Contracts.HR.Holidays;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's public holiday endpoints (<c>api/holidays</c>).</summary>
public interface IHolidaysApiClient
{
    Task<IReadOnlyList<HolidayItem>> ListAsync(int year, CancellationToken cancellationToken);

    /// <summary>The years that have at least one holiday, latest first.</summary>
    Task<IReadOnlyList<int>> ListYearsAsync(CancellationToken cancellationToken);

    /// <summary>Gets a holiday, or <c>null</c> when it does not exist.</summary>
    Task<HolidayItem?> GetAsync(int id, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(HolidayInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, HolidayInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
