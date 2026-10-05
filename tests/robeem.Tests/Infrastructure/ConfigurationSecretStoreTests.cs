using Microsoft.Extensions.Configuration;
using robeem.Application.Tenancy;
using robeem.Infrastructure.Security;
using robeem.Infrastructure.Tenancy;

namespace robeem.Tests.Infrastructure;

public sealed class ConfigurationSecretStoreTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Secrets:tenant-a:vendor-key"] = "secret-of-a",
            ["Secrets:tenant-b:vendor-key"] = "secret-of-b",
            ["Secrets:tenant-b:only-b"] = "b-only",
        })
        .Build();

    [Fact]
    public async Task AReference_ResolvesToTheCurrentTenantsSecret()
    {
        Assert.Equal("secret-of-a", await Store("tenant-a").GetSecretAsync("vendor-key", CancellationToken.None));
        Assert.Equal("secret-of-b", await Store("tenant-b").GetSecretAsync("vendor-key", CancellationToken.None));
    }

    [Fact]
    public async Task ATenant_NeverSeesAnotherTenantsSecret()
    {
        Assert.Null(await Store("tenant-a").GetSecretAsync("only-b", CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown-reference")]
    public async Task AnEmptyOrUnknownReference_GivesNoSecret(string? reference)
    {
        Assert.Null(await Store("tenant-a").GetSecretAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task WithNoTenant_NoSecretIsLookedUp()
    {
        await Assert.ThrowsAsync<TenantNotResolvedException>(() => Store(null).GetSecretAsync("vendor-key", CancellationToken.None));
    }

    private static ConfigurationSecretStore Store(string? tenantId) => new(Configuration, new FixedTenantContext(tenantId));
}
