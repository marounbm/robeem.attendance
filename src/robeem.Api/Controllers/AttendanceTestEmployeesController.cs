using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance.TestEmployees;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>
/// Loads test employees from a CSV file: the employees, their time schedule and team, and the mapping of their numbers to a device. Only an
/// integration administrator, and only where the host has turned it on (<c>Attendance:TestEmployees:Enabled</c>).
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/test-employees")]
public sealed class AttendanceTestEmployeesController : ControllerBase
{
    private const long MaxUploadBytes = 4 * 1024 * 1024;

    private readonly ITestEmployeeLoader _loader;
    private readonly ITestEmployeeRemover _remover;

    public AttendanceTestEmployeesController(ITestEmployeeLoader loader, ITestEmployeeRemover remover)
    {
        _loader = loader;
        _remover = remover;
    }

    /// <summary>Whether the host allows loading, how many employees the tenant has, and the columns the file may have.</summary>
    [HttpGet]
    public async Task<TestEmployeeSummary> Summary(CancellationToken cancellationToken) =>
        await _loader.GetSummaryAsync(cancellationToken);

    /// <summary>
    /// Loads the employees of a CSV file (multipart field <c>file</c>; <c>timeZone</c> is the IANA zone of a device the file names that the tenant does not
    /// have). 409 when the host doesn't allow it; 400 with the rows that are wrong, and nothing loaded, when any row is.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<ActionResult<TestEmployeeLoadResult>> Load(IFormFile file, [FromForm] string? timeZone, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "The file is empty.");
            return ValidationProblem(ModelState);
        }

        await using var stream = file.OpenReadStream();
        var outcome = await _loader.LoadAsync(stream, string.IsNullOrWhiteSpace(timeZone) ? TestEmployeeLoader.DefaultTimeZone : timeZone.Trim(), cancellationToken);
        switch (outcome.Status)
        {
            case TestEmployeeLoadStatus.Disabled:
                return Problem(statusCode: StatusCodes.Status409Conflict, detail: "Loading test employees is not turned on for this host (Attendance:TestEmployees:Enabled).");
            case TestEmployeeLoadStatus.Invalid:
                for (var i = 0; i < outcome.Errors.Count; i++)
                {
                    ModelState.AddModelError($"row{i}", outcome.Errors[i]);
                }

                return ValidationProblem(ModelState);
            default:
                return outcome.Result!;
        }
    }

    /// <summary>
    /// What deleting the employees of a CSV file (multipart field <c>file</c>; only its employee numbers are read) and everything of theirs would delete.
    /// Nothing is deleted. 409 when the host doesn't allow it; 400 when the file is wrong.
    /// </summary>
    [HttpPost("delete-preview")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<ActionResult<TestEmployeeDeleteResult>> DeletePreview(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "The file is empty.");
            return ValidationProblem(ModelState);
        }

        await using var stream = file.OpenReadStream();
        return Answer(await _remover.PreviewAsync(stream, cancellationToken));
    }

    /// <summary>
    /// Deletes the employees of a CSV file and everything of theirs, raw punches included (<c>confirmation</c> is the tenant id, typed). All or nothing; audited
    /// in an entry that is kept. 409 when the host doesn't allow it or the tenant id was not typed; 400 when the file is wrong or something still needs an employee.
    /// </summary>
    [HttpPost("delete")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<ActionResult<TestEmployeeDeleteResult>> Delete(IFormFile file, [FromForm] string? confirmation, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "The file is empty.");
            return ValidationProblem(ModelState);
        }

        await using var stream = file.OpenReadStream();
        return Answer(await _remover.DeleteAsync(stream, confirmation ?? string.Empty, cancellationToken));
    }

    private ActionResult<TestEmployeeDeleteResult> Answer(TestEmployeeDeleteOutcome outcome)
    {
        switch (outcome.Status)
        {
            case TestEmployeeDeleteStatus.Disabled:
                return Problem(statusCode: StatusCodes.Status409Conflict, detail: "Loading and deleting test employees is not turned on for this host (Attendance:TestEmployees:Enabled).");
            case TestEmployeeDeleteStatus.ConfirmationMismatch:
                return Problem(statusCode: StatusCodes.Status409Conflict, detail: "Type the tenant id exactly as shown to confirm.");
            case TestEmployeeDeleteStatus.Invalid:
            case TestEmployeeDeleteStatus.Blocked:
                for (var i = 0; i < outcome.Errors.Count; i++)
                {
                    ModelState.AddModelError($"row{i}", outcome.Errors[i]);
                }

                return ValidationProblem(ModelState);
            default:
                return outcome.Result!;
        }
    }
}
