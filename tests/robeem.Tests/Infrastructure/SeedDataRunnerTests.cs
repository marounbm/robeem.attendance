using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.HR.Seeding;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Seeding;

namespace robeem.Tests.Infrastructure;

public sealed class SeedDataRunnerTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task HrSeed_CreatesStandardWeekOnce()
    {
        for (var run = 0; run < 2; run++)
        {
            using var db = _database.CreateContext(TestDatabase.TenantA);
            await new SeedDataRunner(db, new ISeedContributor[] { new HrSeedContributor() }, NullLogger<SeedDataRunner>.Instance)
                .RunAsync(CancellationToken.None);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var schedule = Assert.Single(check.TimeSchedules);
        Assert.Equal("Standard Week", schedule.Title);
        Assert.True(Assert.Single(check.TimeScheduleTeams).AllInclusive);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, check.TimeScheduleLines.Select(l => l.CycleDay).OrderBy(d => d).ToArray());
    }

    [Fact]
    public async Task Contributor_SeedingANonSeedDataEntity_IsRejected()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var runner = new SeedDataRunner(db, new ISeedContributor[] { new NonSeedableSeeder() }, NullLogger<SeedDataRunner>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(CancellationToken.None));
        Assert.Empty(db.NormalizedAttendanceTransactions);
    }

    private sealed class NonSeedableSeeder : ISeedContributor
    {
        public int Order => 0;

        public Task SeedAsync(IRobeemDbContext db, CancellationToken cancellationToken)
        {
            // NormalizedAttendanceTransaction does NOT implement ISeedData, so this should be rejected
            db.NormalizedAttendanceTransactions.Add(new NormalizedAttendanceTransaction
            {
                EmployeeId = 1,
                AttendanceDate = DateTime.Today,
                EventTimestampUtc = DateTimeOffset.UtcNow,
            });
            return Task.CompletedTask;
        }
    }
}
