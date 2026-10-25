using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Which payload format reads and interprets an integration's files.</summary>
public static class PayloadFormatResolution
{
    /// <summary>
    /// The integration's own format, else, for an integration that receives manual uploads, its connector's default
    /// format. Null when there is none: the connector's code adapter reads and interprets what it receives.
    /// </summary>
    public static int? EffectiveFormatId(AttendanceTransport transport, int? integrationFormatId, int? connectorDefaultFormatId) =>
        integrationFormatId ?? (transport == AttendanceTransport.FileUpload ? connectorDefaultFormatId : null);
}
