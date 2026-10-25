namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>The rows of an uploaded file as text, and for a workbook the cells that hold a real date or time.</summary>
/// <param name="Rows">Every row with something in it, each cell as text.</param>
/// <param name="Dates">The same shape as <paramref name="Rows"/>: the value of a date or time cell, null for any other cell; null for a text file.</param>
internal sealed record TabularData(List<List<string>> Rows, List<List<DateTime?>>? Dates)
{
    /// <summary>Gets the date or time value of a cell, or null when the cell is not a date or time cell (or the file has none).</summary>
    public DateTime? DateAt(int row, int column) =>
        Dates is not null && row < Dates.Count && column < Dates[row].Count ? Dates[row][column] : null;
}
