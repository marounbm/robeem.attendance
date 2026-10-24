using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>What the employee fills in on the Correct page: one choice for each punch of the day, the punches they add, and why.</summary>
public sealed class DayCorrectionForm
{
    /// <summary>The choice that leaves a punch as it is.</summary>
    public const string Keep = "";

    public DateTime Date { get; set; }

    public string? Justification { get; set; }

    /// <summary>Gets or sets the choice for each punch of the day that can be edited.</summary>
    public List<PunchChoice> Punches { get; set; } = [];

    /// <summary>Gets or sets the punches the employee adds. Rows left empty are ignored.</summary>
    public List<AddedPunch> Added { get; set; } = [];

    /// <summary>Gets the form for a day, with every punch left as it is.</summary>
    public static DayCorrectionForm For(DayCorrectionView day) => new()
    {
        Date = day.Date,
        Punches = day.Punches.Where(p => p is { Editable: true, TransactionId: not null }).Select(p => new PunchChoice { TransactionId = p.TransactionId!.Value }).ToList(),
    };

    /// <summary>Gets what was typed, on the punches of the day as they are now, so the page can be shown again with the same choices.</summary>
    public DayCorrectionForm WithPunchesOf(DayCorrectionView day)
    {
        var chosen = Punches.ToDictionary(p => p.TransactionId, p => p.Action);
        return new DayCorrectionForm
        {
            Date = day.Date,
            Justification = Justification,
            Punches = For(day).Punches.Select(p => new PunchChoice { TransactionId = p.TransactionId, Action = chosen.GetValueOrDefault(p.TransactionId) }).ToList(),
            Added = Added,
        };
    }

    /// <summary>
    /// Gets the edits to send. A punch's choice is Ignore or the type to give it. The day of an added punch is the day being corrected, or the day after
    /// when the employee says it was after midnight; the Api refuses any other.
    /// </summary>
    public IReadOnlyList<DayCorrectionEdit> ToEdits()
    {
        var edits = new List<DayCorrectionEdit>();
        foreach (var punch in Punches.Where(p => !string.IsNullOrEmpty(p.Action)))
        {
            edits.Add(punch.Action == DayCorrectionRequest.Ignore
                ? new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = punch.TransactionId }
                : new DayCorrectionEdit { Action = DayCorrectionRequest.Change, TransactionId = punch.TransactionId, EventType = punch.Action });
        }

        foreach (var added in Added.Where(a => a.Time is not null || !string.IsNullOrEmpty(a.EventType)))
        {
            edits.Add(new DayCorrectionEdit
            {
                Action = DayCorrectionRequest.Add,
                EventType = added.EventType,
                At = added.Time is { } time ? Date.Date.AddDays(added.AfterMidnight ? 1 : 0).Add(time.ToTimeSpan()) : null,
            });
        }

        return edits;
    }
}
