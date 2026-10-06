using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

/// <summary>
/// Gives a tenant database the provider catalog and the tenant-wide default attendance policy
/// (study §20.1). Adds only what is missing, matching providers by key and the policy by being the
/// tenant default, so edits made by an administrator are never overwritten.
/// </summary>
/// <remarks>
/// A provider is seeded active only when its adapter exists: <c>generic-csv</c> today. The others
/// are listed so an integration can be planned, and are switched on when their adapter is added
/// (CLAUDE.md §1.9). <see cref="CsvProviderKey"/> must equal <c>GenericCsvProvider.ProviderKey</c>;
/// a test checks that, since Application cannot reference Infrastructure.
/// </remarks>
public sealed class AttendanceSeedContributor : ISeedContributor
{
    public const string CsvProviderKey = "generic-csv";

    private static readonly SeedProvider[] Providers =
    [
        new(CsvProviderKey, "Generic CSV upload", AttendanceTransport.FileUpload, SupportsWebhooks: false, SupportsIncrementalSync: false, IsActive: true),
        new("generic-webhook", "Generic webhook (adapter not available yet)", AttendanceTransport.Webhook, SupportsWebhooks: true, SupportsIncrementalSync: false, IsActive: false),
        new("zkteco", "ZKTeco-style biometric devices (adapter not available yet)", AttendanceTransport.DeviceSdk, SupportsWebhooks: false, SupportsIncrementalSync: true, IsActive: false, Manufacturer: "ZKTeco", WebsiteUrl: "https://www.zkteco.com"),
        new("suprema-biostar", "Suprema BioStar (adapter not available yet)", AttendanceTransport.RestApi, SupportsWebhooks: false, SupportsIncrementalSync: true, IsActive: false),
    ];

    /// <summary>Gets the run order; after the HR seed.</summary>
    public int Order => 200;

    public async Task SeedAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.AttendanceProviders.Select(p => p.ProviderKey).ToListAsync(cancellationToken);
        foreach (var provider in Providers.Where(p => !existing.Contains(p.Key, StringComparer.Ordinal)))
        {
            db.AttendanceProviders.Add(new AttendanceProvider
            {
                ProviderKey = provider.Key,
                DisplayName = provider.DisplayName,
                SupportedTransports = provider.Transport.ToString(),
                SupportsWebhooks = provider.SupportsWebhooks,
                SupportsIncrementalSync = provider.SupportsIncrementalSync,
                IsActive = provider.IsActive,
                Manufacturer = provider.Manufacturer,
                WebsiteUrl = provider.WebsiteUrl,
            });
        }

        // Rows seeded before the vendor details existed get them, but only into empty fields: what an
        // administrator typed is never replaced.
        var vendors = Providers.Where(p => p.Manufacturer.Length > 0 && existing.Contains(p.Key, StringComparer.Ordinal)).ToList();
        if (vendors.Count > 0)
        {
            var keys = vendors.Select(v => v.Key).ToList();
            foreach (var row in await db.AttendanceProviders.Where(p => keys.Contains(p.ProviderKey)).ToListAsync(cancellationToken))
            {
                var vendor = vendors.Single(v => v.Key == row.ProviderKey);
                if (row.Manufacturer.Length == 0)
                {
                    row.Manufacturer = vendor.Manufacturer;
                }

                if (row.WebsiteUrl.Length == 0)
                {
                    row.WebsiteUrl = vendor.WebsiteUrl;
                }
            }
        }

        // The MVP defaults of study §20.1: no inference of unlabeled punches, no overtime
        // pre-approval, the manager approves corrections. An administrator changes them in place.
        if (!await db.AttendancePolicies.AnyAsync(p => p.TimeScheduleId == null, cancellationToken))
        {
            db.AttendancePolicies.Add(new AttendancePolicy
            {
                Title = "Default attendance policy",
                UnlabeledPunchInterpretationMode = UnlabeledPunchInterpretationMode.Off,
                RequiresOvertimePreApproval = false,
                CorrectionApprovalWorkflow = CorrectionApprovalWorkflow.Manager,
            });
        }
    }

    private sealed record SeedProvider(string Key, string DisplayName, AttendanceTransport Transport, bool SupportsWebhooks, bool SupportsIncrementalSync, bool IsActive, string Manufacturer = "", string WebsiteUrl = "");
}
