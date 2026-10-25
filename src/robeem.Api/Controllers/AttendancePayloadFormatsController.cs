using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// The custom file formats an integration can read its manual uploads by: columns, timestamp patterns and
/// type labels an administrator defines. A format is made inactive, never deleted (integrations point at it).
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/payload-formats")]
public sealed class AttendancePayloadFormatsController : ControllerBase
{
    private const long MaxSampleBytes = 10 * 1024 * 1024;

    private readonly IAttendancePayloadFormatService _formats;

    public AttendancePayloadFormatsController(IAttendancePayloadFormatService formats)
    {
        _formats = formats;
    }

    [HttpGet]
    public async Task<IReadOnlyList<PayloadFormatListItem>> List(CancellationToken cancellationToken) =>
        await _formats.ListAsync(cancellationToken);

    /// <summary>The active formats, to choose from on an integration.</summary>
    [HttpGet("options")]
    public async Task<IReadOnlyList<PayloadFormatOption>> Options(CancellationToken cancellationToken) =>
        await _formats.ListOptionsAsync(cancellationToken);

    [HttpGet("{id:int}/input")]
    public async Task<ActionResult<PayloadFormatInput>> GetInput(int id, CancellationToken cancellationToken) =>
        await _formats.GetInputAsync(id, cancellationToken) is { } input ? input : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(PayloadFormatInput input, CancellationToken cancellationToken)
    {
        var result = await _formats.CreateAsync(input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PayloadFormatInput input, CancellationToken cancellationToken)
    {
        var result = await _formats.UpdateAsync(id, input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>
    /// Reads a sample file (multipart field <c>file</c>) by the format in the <c>format</c> JSON field, which need not be saved,
    /// and returns the first rows as they would be understood. Nothing is stored.
    /// </summary>
    [HttpPost("preview")]
    [RequestSizeLimit(MaxSampleBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxSampleBytes)]
    public async Task<ActionResult<PayloadFormatPreview>> Preview(IFormFile file, [FromForm] string format, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "The file is empty.");
            return ValidationProblem(ModelState);
        }

        PayloadFormatInput? input;
        try
        {
            input = JsonSerializer.Deserialize<PayloadFormatInput>(format, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            input = null;
        }

        if (input is null)
        {
            ModelState.AddModelError(nameof(format), "The format is not valid JSON.");
            return ValidationProblem(ModelState);
        }

        var path = Path.GetTempFileName();
        try
        {
            await using (var stream = global::System.IO.File.Create(path))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            return await _formats.PreviewAsync(input, path, cancellationToken);
        }
        finally
        {
            global::System.IO.File.Delete(path);
        }
    }

    private ActionResult Failure(SaveResult result)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }
}
