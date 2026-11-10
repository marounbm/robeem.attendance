using robeem.Application.Common;
using robeem.Contracts.HR.Holidays;

namespace robeem.Application.HR.Holidays;

public interface IHolidayService
{
    /// <summary>Lists the holidays of one calendar year, by date.</summary>
    Task<IReadOnlyList<HolidayItem>> ListAsync(int year, CancellationToken cancellationToken);

    /// <summary>Lists the years that have at least one holiday, latest first.</summary>
    Task<IReadOnlyList<int>> ListYearsAsync(CancellationToken cancellationToken);

    Task<HolidayItem?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Adds the holiday, or one row per day when <see cref="HolidayInput.LastDate"/> is given. A date that already has a holiday is refused.</summary>
    Task<SaveResult> CreateAsync(HolidayInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateAsync(int id, HolidayInput input, CancellationToken cancellationToken);

    Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
