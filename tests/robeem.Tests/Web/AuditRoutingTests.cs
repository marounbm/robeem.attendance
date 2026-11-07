using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;

namespace robeem.Tests.Web;

/// <summary>
/// The audit screen's filters as a browser sends them: through the routing and model binding of the real Web controller, not by
/// calling its action with arguments. (The route values <c>controller</c> and <c>action</c> are easy to confuse with a filter called action.)
/// </summary>
public sealed class AuditRoutingTests
{
    [Fact]
    public async Task TheFiltersOfTheUrl_ReachTheApi_AndTheActionFilterIsNotTheRoutesAction()
    {
        var audit = new RecordingAudit();

        await Get(audit, "/Audit?auditAction=ReviewDecided&entityType=Thing&entityId=7&from=2026-11-01T00%3A00&to=2026-11-07T10%3A00&pageSize=50");

        Assert.Equal(("ReviewDecided", "Thing", 7, new DateTime(2026, 11, 1, 0, 0, 0), new DateTime(2026, 11, 7, 10, 0, 0), 50), (audit.Action, audit.EntityType, audit.EntityId, audit.From, audit.To, audit.PageSize));
    }

    [Fact]
    public async Task FilteringByDateAlone_SendsNoActionAtAll()
    {
        var audit = new RecordingAudit();

        // This is what the form sends when only the dates are filled: every other filter is there, and empty (and an old link may still carry action=).
        await Get(audit, "/Audit?action=&auditAction=&entityType=&entityId=&from=2026-11-01T00%3A00&to=2026-11-07T10%3A00&pageSize=20");

        Assert.Equal((null, null, null, new DateTime(2026, 11, 1, 0, 0, 0), new DateTime(2026, 11, 7, 10, 0, 0)), (audit.Action, audit.EntityType, audit.EntityId, audit.From, audit.To));
    }

    [Fact]
    public async Task WithNoFilterAtAll_NothingIsAskedFor()
    {
        var audit = new RecordingAudit();

        await Get(audit, "/Audit");

        Assert.Equal((null, null, null, null, null), (audit.Action, audit.EntityType, audit.EntityId, audit.From, audit.To));
    }

    private static async Task Get(RecordingAudit audit, string url)
    {
        using var host = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddControllersWithViews().AddApplicationPart(typeof(AuditController).Assembly);
                services.AddSingleton<IAuditApiClient>(audit);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}"));
            })
            .UseTestServer()
            .Build();
        await host.StartAsync();

        // The page is not rendered here (its layout needs the whole site), so the request may end in an error: what matters is what was asked of the Api.
        using var client = host.GetTestClient();
        try
        {
            using var response = await client.GetAsync(url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Assert.True(audit.Asked, $"The action never asked the Api for the log: {ex.Message}");
        }
    }

    private sealed class RecordingAudit : IAuditApiClient
    {
        public bool Asked { get; private set; }

        public string? Action { get; private set; }

        public string? EntityType { get; private set; }

        public int? EntityId { get; private set; }

        public DateTime? From { get; private set; }

        public DateTime? To { get; private set; }

        public int PageSize { get; private set; }

        public Task<PagedResult<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken)
        {
            Asked = true;
            (Action, EntityType, EntityId, From, To, PageSize) = (action, entityType, entityId, from, to, pageSize);
            return Task.FromResult(new PagedResult<AuditLogEntry> { Page = page, PageSize = pageSize });
        }
    }
}
