using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Which file format reads and interprets an integration's files.</summary>
public static class FileFormatResolution
{
    /// <summary>
    /// The integration's own format, else, for an integration that receives manual uploads, its provider's default
    /// format. Null when there is none: the provider's code adapter reads and interprets what it receives.
    /// </summary>
    public static int? EffectiveFormatId(AttendanceTransport transport, int? integrationFormatId, int? providerDefaultFormatId) =>
        integrationFormatId ?? (transport == AttendanceTransport.FileUpload ? providerDefaultFormatId : null);
}
