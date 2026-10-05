using robeem.Contracts.Security;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's Development "act as" employee list (<c>api/development/actors</c>).</summary>
public interface IActorsApiClient
{
    /// <summary>Lists the employees that can be acted as; <c>null</c> when the Api has no development stand-in user (a 404).</summary>
    Task<IReadOnlyList<ActorOption>?> ListAsync(CancellationToken cancellationToken);
}
