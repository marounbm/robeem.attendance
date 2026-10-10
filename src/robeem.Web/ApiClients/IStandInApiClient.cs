using robeem.Contracts.Security;

namespace robeem.Web.ApiClients;

public interface IStandInApiClient
{
    /// <summary>Gets the Api's Development stand-in user, or null when the Api has none (a 404: it is not in Development).</summary>
    Task<StandInActor?> GetAsync(CancellationToken cancellationToken);
}
