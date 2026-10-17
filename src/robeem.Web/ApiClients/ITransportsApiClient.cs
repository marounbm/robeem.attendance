using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's transport definitions (<c>api/attendance/transports</c>).</summary>
public interface ITransportsApiClient
{
    Task<IReadOnlyList<TransportItem>> ListAsync(CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, TransportInput input, CancellationToken cancellationToken);
}
