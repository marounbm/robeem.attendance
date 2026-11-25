namespace robeem.Web.Models;

/// <summary>
/// The choice of each pair of punches the form shows as one choice: two check-ins at the start, two check-outs at the end, a check-in then a check-out at the end,
/// and repeated check-ins and check-outs anywhere in the day.
/// </summary>
public readonly record struct PunchPairReading(PairKeeps Morning, PairKeeps Evening, PairKeeps InThenOut, PairKeeps RepeatedCheckIn = PairKeeps.Nothing, PairKeeps RepeatedCheckOut = PairKeeps.Nothing);
