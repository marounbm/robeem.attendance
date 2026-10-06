using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class RecordDayViewModel
{
    public required RecordDayDetail Day { get; init; }

    /// <summary>Gets a value indicating whether the acting user may run the day through the engine again (HR).</summary>
    public bool CanReprocess { get; init; }
}
