namespace robeem.Application.Attendance;

public sealed class RawPayloadSanitizerOptions
{
    public const string SectionName = "Attendance:RawPayloadSanitizer";

    /// <summary>Gets the terms that are always denied. Configuration can add terms, never remove these.</summary>
    public static IReadOnlyList<string> DefaultDenyTerms { get; } = ["template", "biometric", "fingerprint", "faceprint"];

    /// <summary>Gets or sets terms denied in addition to <see cref="DefaultDenyTerms"/>.</summary>
    public List<string> AdditionalDenyTerms { get; set; } = [];
}
