using Microsoft.Extensions.Options;
using robeem.Infrastructure.Tenancy;

namespace robeem.Tests.Infrastructure;

public sealed class TenantRegistryTests
{
    [Fact]
    public void Find_IsCaseInsensitive_AndReturnsNullForUnknown()
    {
        var registry = Create(new TenancyOptions { Tenants = { Tenant("acme") } });

        Assert.Equal("acme", registry.Find("ACME")?.Id);
        Assert.Null(registry.Find("other"));
        Assert.Null(registry.Find(null));
    }

    [Theory]
    [InlineData("Has Space")]
    [InlineData("-leading-dash")]
    [InlineData("")]
    public void InvalidTenantId_FailsAtStartup(string id)
    {
        Assert.Throws<InvalidOperationException>(() => Create(new TenancyOptions { Tenants = { Tenant(id) } }));
    }

    [Fact]
    public void DuplicateTenant_MissingConnectionString_OrUnknownDefault_FailAtStartup()
    {
        Assert.Throws<InvalidOperationException>(() => Create(new TenancyOptions { Tenants = { Tenant("a"), Tenant("a") } }));
        Assert.Throws<InvalidOperationException>(() => Create(new TenancyOptions { Tenants = { new TenantDefinition { Id = "a" } } }));
        Assert.Throws<InvalidOperationException>(() => Create(new TenancyOptions { Tenants = { Tenant("a") }, DefaultTenantId = "b" }));
        Assert.Throws<InvalidOperationException>(() => Create(new TenancyOptions()));
    }

    [Fact]
    public void TenantContext_CannotSwitchTenantWithinAScope()
    {
        var context = new TenantContext(Create(new TenancyOptions { Tenants = { Tenant("a"), Tenant("b") } }));
        context.SetTenant("a");

        context.SetTenant("A");
        Assert.Throws<InvalidOperationException>(() => context.SetTenant("b"));
        Assert.Throws<UnknownTenantException>(() => new TenantContext(Create(new TenancyOptions { Tenants = { Tenant("a") } })).SetTenant("zzz"));
    }

    private static TenantRegistry Create(TenancyOptions options) => new(Options.Create(options));

    private static TenantDefinition Tenant(string id) => new() { Id = id, Name = id, ConnectionString = $"Database={id}" };
}
