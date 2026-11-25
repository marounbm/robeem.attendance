using System.Globalization;

namespace robeem.Web.Models;

/// <summary>
/// The Attendance list as a person had it (filters, sort, page size) and where a day sat in it, carried in the address of a day's page so that the page
/// can offer the next day of that list and a way back to it. The field names are the ones the day's page and its forms bind.
/// </summary>
public sealed class RecordListContext
{
    public string? Status { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public bool OnlyWithExceptions { get; init; }

    /// <summary>Gets the one employee the list was filtered to. Named apart from the day's own <c>employeeId</c>.</summary>
    public int? FilterEmployeeId { get; init; }

    public string? Sort { get; init; }

    public string? Dir { get; init; }

    public int PageSize { get; init; }

    /// <summary>Gets the 1-based place of the day in the whole list (across pages) when it was opened; 0 when unknown.</summary>
    public int Position { get; init; }

    /// <summary>Gets the route values that carry this list to a day's page, with the day at <paramref name="position"/>.</summary>
    public Dictionary<string, string?> ToDayValues(int position)
    {
        var values = Values("filterEmployeeId");
        values["position"] = position.ToString(CultureInfo.InvariantCulture);
        return values;
    }

    /// <summary>Gets the route values of the Attendance list on the page the day was on, filters and sort as they were.</summary>
    public Dictionary<string, string?> ToIndexValues()
    {
        var values = Values("employeeId");
        if (Position > 0 && PageSize > 0)
        {
            values["page"] = (((Position - 1) / PageSize) + 1).ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    private Dictionary<string, string?> Values(string employeeKey)
    {
        var values = new Dictionary<string, string?>();
        if (PageSize > 0)
        {
            values["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(Status) && Status != "Any")
        {
            values["status"] = Status;
        }

        if (From is { } from)
        {
            values["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (To is { } to)
        {
            values["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (OnlyWithExceptions)
        {
            values["onlyWithExceptions"] = "true";
        }

        if (FilterEmployeeId is { } employeeId)
        {
            values[employeeKey] = employeeId.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(Sort))
        {
            values["sort"] = Sort;
            values["dir"] = Dir;
        }

        return values;
    }
}
