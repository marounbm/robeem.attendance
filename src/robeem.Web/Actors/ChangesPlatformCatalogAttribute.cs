namespace robeem.Web.Actors;

/// <summary>
/// Marks an action that creates or changes the attendance catalog, which the prime tenant owns and every other tenant reads. Only a
/// platform administrator acting in the prime tenant may run it; <see cref="ActorAccessFilter"/> turns anyone else away before the call.
/// The Api refuses it for them as well. Reading the catalog and downloading example files stay open to the tenant's integration administrators.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ChangesPlatformCatalogAttribute : Attribute
{
}
