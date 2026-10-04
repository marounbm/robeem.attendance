namespace robeem.Application.Attendance;

/// <summary>What a provider adapter needs to reach one tenant integration. Built from <c>AttendanceIntegration</c>; holds no secret itself.</summary>
public sealed record AttendanceIntegrationConfig(int IntegrationId, string ProviderKey, string ConnectionConfigRef)
{
    /// <summary>Gets the uploaded file to read, for a manual upload.</summary>
    public string? UploadFilePath { get; init; }
}
