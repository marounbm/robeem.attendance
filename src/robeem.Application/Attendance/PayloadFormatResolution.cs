using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Which payload format reads and interprets an integration's files.</summary>
public static class PayloadFormatResolution
{
    /// <summary>
    /// For an integration that receives manual uploads, its connector's default format; an integration has no format of its own, so
    /// every file of a connector is read the same way. Null when there is none: the connector's code adapter reads and interprets
    /// what it receives. The format read is the latest version of that name, unless a batch recorded an exact version.
    /// </summary>
    public static string? EffectiveFormatName(AttendanceTransport transport, string? connectorDefaultFormatName) =>
        transport == AttendanceTransport.FileUpload ? connectorDefaultFormatName : null;
}
