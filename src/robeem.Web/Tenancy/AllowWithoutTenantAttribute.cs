namespace robeem.Web.Tenancy;

/// <summary>Marks a controller or action that works without a resolved tenant (it must not touch tenant data).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWithoutTenantAttribute : Attribute
{
}
