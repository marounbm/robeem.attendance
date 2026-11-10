using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;

namespace robeem.Application.HR.Seeding;

/// <summary>
/// Gives a new tenant the Lebanese public holidays of 2026, so the calendar is not empty. Runs only while the tenant has no
/// holiday at all, so a holiday an administrator removes is never added back. Later years are added on the Holidays screen.
/// </summary>
/// <remarks>
/// The dates are the expected ones, not read from the official decree: the Islamic holidays follow the moon and are marked
/// provisional ("to be confirmed") until HR clears the flag. Verify the list against the Banque du Liban circular.
/// </remarks>
public sealed class HolidaySeedContributor : ISeedContributor
{
    private const int Year = 2026;

    public int Order => 110;

    /// <summary>Gets the 2026 list: one entry per day.</summary>
    public static IReadOnlyList<(DateTime Date, string Name, bool Provisional)> Holidays2026()
    {
        var list = new List<(DateTime, string, bool)>();

        void Add(string name, bool provisional, params (int Month, int Day)[] days)
        {
            foreach (var (month, day) in days)
            {
                list.Add((new DateTime(Year, month, day), name, provisional));
            }
        }

        Add("New Year's Day", false, (1, 1));
        Add("Armenian Christmas", false, (1, 6));
        Add("Saint Maroun's Day", false, (2, 9));
        Add("Eid al-Fitr", true, (3, 20), (3, 21), (3, 22));
        Add("Annunciation", false, (3, 25));
        Add("Good Friday (Western)", false, (4, 3));
        Add("Easter Monday", true, (4, 6));
        Add("Good Friday (Orthodox)", false, (4, 10));
        Add("Labour Day", false, (5, 1));
        Add("Martyrs' Day", false, (5, 6));
        Add("Resistance and Liberation Day", false, (5, 25));
        Add("Eid al-Adha", true, (5, 27), (5, 28));
        Add("Islamic New Year", true, (6, 16));
        Add("Ashura", true, (6, 25));
        Add("Assumption", false, (8, 15));
        Add("Prophet's Birthday (Mawlid)", true, (8, 25));
        Add("Independence Day", false, (11, 22));
        Add("Christmas Day", false, (12, 25));

        return list.OrderBy(h => h.Item1).ToList();
    }

    public async Task SeedAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        if (await db.PublicHolidays.AnyAsync(cancellationToken))
        {
            return;
        }

        db.PublicHolidays.AddRange(Holidays2026().Select(h => new PublicHoliday { Date = h.Date, Title = h.Name, IsProvisional = h.Provisional }));
    }
}
