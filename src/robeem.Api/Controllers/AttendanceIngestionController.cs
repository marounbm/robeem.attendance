using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/attendance/ingestion")]
public sealed class AttendanceIngestionController : ControllerBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IIngestionService _ingestion;
    private readonly ICurrentUser _user;

    public AttendanceIngestionController(IIngestionService ingestion, ICurrentUser user)
    {
        _ingestion = ingestion;
        _user = user;
    }

    /// <summary>The upload history, newest first: each file's name, size, SHA-256, upload time and uploader. Optionally one integration's.</summary>
    [HttpGet("uploads")]
    public async Task<IReadOnlyList<UploadedFileItem>> Uploads(int? integrationId, int take = 50, CancellationToken cancellationToken = default) =>
        await _ingestion.ListUploadedFilesAsync(integrationId, take, cancellationToken);

    /// <summary>The integrations a file can be uploaded into, each with the file formats and layout of its vendor.</summary>
    [HttpGet("targets")]
    public async Task<IReadOnlyList<UploadTarget>> Targets(CancellationToken cancellationToken) =>
        await _ingestion.ListUploadTargetsAsync(cancellationToken);

    /// <summary>
    /// Ingests an uploaded attendance file (multipart field <c>file</c>) into the integration's raw
    /// transactions. A row-level problem does not fail the request; it shows in the returned counts.
    /// </summary>
    [HttpPost("{integrationId:int}/uploads")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<ActionResult<AttendanceIngestionResult>> Upload(int integrationId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "The file is empty.");
            return ValidationProblem(ModelState);
        }

        // Only an active integration of a file vendor takes an upload; anything else is not found, as it is not in the list.
        if (!(await _ingestion.ListUploadTargetsAsync(cancellationToken)).Any(t => t.IntegrationId == integrationId))
        {
            return NotFound();
        }

        // The adapter reads from a path, so the upload goes through a temporary file.
        var path = Path.GetTempFileName();
        try
        {
            await using (var stream = global::System.IO.File.Create(path))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            return await _ingestion.IngestUploadedFileAsync(integrationId, path, file.FileName, _user.EmployeeId, cancellationToken) is { } result ? result : NotFound();
        }
        finally
        {
            global::System.IO.File.Delete(path);
        }
    }
}
