namespace robeem.Application.Attendance;

/// <summary>The wording of normalization failures that another part of the code has to recognize.</summary>
public static class NormalizationFailures
{
    /// <summary>
    /// What a raw punch's error starts with when it failed for want of a time zone: its timestamp has no
    /// UTC offset, and neither its device nor its integration says which zone to read it in. Retrying
    /// such a punch is worthwhile once a zone is set; retrying any other failure is not.
    /// </summary>
    public const string NoTimeZonePrefix = "The timestamp has no UTC offset";
}
