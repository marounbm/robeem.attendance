using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/ingestion")]
public sealed class AttendanceIngestionController : ControllerBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IIngestionService _ingestion;

    public AttendanceIngestionController(IIngestionService ingestion)
    {
        _ingestion = ingestion;
    }

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

        // The adapter reads from a path, so the upload goes through a temporary file.
        var path = Path.GetTempFileName();
        try
        {
            await using (var stream = global::System.IO.File.Create(path))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            return await _ingestion.IngestManualUploadAsync(integrationId, path, cancellationToken) is { } result ? result : NotFound();
        }
        finally
        {
            global::System.IO.File.Delete(path);
        }
    }
}
