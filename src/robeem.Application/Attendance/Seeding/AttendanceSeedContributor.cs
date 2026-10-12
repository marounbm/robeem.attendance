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
/// A provider is seeded active only when its adapter exists: <c>generic-csv</c>, <c>zkteco</c> and <c>suprema-biostar</c> today. The others
/// are listed so an integration can be planned, and are switched on when their adapter is added
/// (CLAUDE.md §1.9). <see cref="CsvProviderKey"/> must equal <c>GenericCsvProvider.ProviderKey</c>;
/// a test checks that, since Application cannot reference Infrastructure.
/// </remarks>
public sealed class AttendanceSeedContributor : ISeedContributor
{
    public const string CsvProviderKey = "generic-csv";

    // The names rows were seeded with earlier, by provider key. The transport is chosen on the
    // integration, so a provider name carries only the vendor and product.
    private static readonly Dictionary<string, string[]> LegacyDisplayNames = new(StringComparer.Ordinal)
    {
        ["generic-webhook"] = ["Generic webhook (adapter not available yet)"],
        ["zkteco"] = ["ZKTeco-style biometric devices (adapter not available yet)", "ZKTeco attendance export (CSV upload)", "ZKTeco attendance export (XLS / CSV upload)"],
        ["suprema-biostar"] = ["Suprema BioStar (adapter not available yet)", "Suprema BioStar 2 (REST API)"],
    };

    private static readonly SeedProvider[] Providers =
    [
        new(CsvProviderKey, "Generic CSV upload", AttendanceTransport.FileUpload, SupportsWebhooks: false, SupportsIncrementalSync: false, IsActive: true),
        new("generic-webhook", "Generic webhook", AttendanceTransport.Webhook, SupportsWebhooks: true, SupportsIncrementalSync: false, IsActive: false),
        new("zkteco", "ZKTeco", AttendanceTransport.FileUpload, SupportsWebhooks: false, SupportsIncrementalSync: false, IsActive: true, Manufacturer: "ZKTeco", WebsiteUrl: "https://www.zkteco.com"),
        new("suprema-biostar", "Suprema BioStar 2", AttendanceTransport.RestApi, SupportsWebhooks: false, SupportsIncrementalSync: true, IsActive: true),
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

        // Rows seeded before a field existed get it, but only into empty fields: what an administrator
        // typed is never replaced. A provider is also switched on once its adapter exists (its seed flag
        // is true): a row seeded while the adapter was missing would otherwise stay disabled for good,
        // and nothing in the application ever switches a provider off.
        var present = Providers.Where(p => existing.Contains(p.Key, StringComparer.Ordinal)).ToList();
        if (present.Count > 0)
        {
            var keys = present.Select(v => v.Key).ToList();
            foreach (var row in await db.AttendanceProviders.Where(p => keys.Contains(p.ProviderKey)).ToListAsync(cancellationToken))
            {
                var seeded = present.Single(v => v.Key == row.ProviderKey);

                // The name this row was seeded with before the suffix was dropped; a name an administrator typed is left alone.
                if (LegacyDisplayNames.TryGetValue(row.ProviderKey, out var legacyNames) && legacyNames.Contains(row.DisplayName))
                {
                    row.DisplayName = seeded.DisplayName;
                }

                if (row.Manufacturer.Length == 0)
                {
                    row.Manufacturer = seeded.Manufacturer;
                }

                if (row.WebsiteUrl.Length == 0)
                {
                    row.WebsiteUrl = seeded.WebsiteUrl;
                }

                if (row.SupportedTransports.Length == 0)
                {
                    row.SupportedTransports = seeded.Transport.ToString();
                }

                if (seeded.IsActive && !row.IsActive)
                {
                    row.IsActive = true;
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
