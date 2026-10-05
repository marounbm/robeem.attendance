using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>The pay periods of the current tenant: the dates payroll pays for and whether each is closed.</summary>
public interface IPayPeriodsApiClient
{
    /// <summary>Lists the periods, latest first.</summary>
    Task<IReadOnlyList<PayPeriodItem>> ListAsync(CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(PayPeriodInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> CloseAsync(int id, CancellationToken cancellationToken);

    /// <summary>Opens a closed period again; the Api refuses without a reason.</summary>
    Task<ApiSaveResult> ReopenAsync(int id, string reason, CancellationToken cancellationToken);
}
