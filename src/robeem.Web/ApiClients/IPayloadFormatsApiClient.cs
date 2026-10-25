using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's custom file formats (<c>api/attendance/payload-formats</c>).</summary>
public interface IPayloadFormatsApiClient
{
    Task<IReadOnlyList<PayloadFormatListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets a format as editable input, or null when it does not exist.</summary>
    Task<PayloadFormatInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(PayloadFormatInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, PayloadFormatInput input, CancellationToken cancellationToken);

    /// <summary>Reads a sample file by an unsaved format and returns its first rows as understood. Nothing is stored.</summary>
    Task<ApiCommandResult<PayloadFormatPreview>> PreviewAsync(PayloadFormatInput input, Stream file, string fileName, CancellationToken cancellationToken);
}
