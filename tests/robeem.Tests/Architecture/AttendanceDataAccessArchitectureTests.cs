using System.Reflection;
using NetArchTest.Rules;
using robeem.Application.Attendance;
using robeem.Application.Persistence;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;
using robeem.Web.Tenancy;

namespace robeem.Tests.Architecture;

/// <summary>
/// Raw and normalized attendance are internal to the Attendance feature (CLAUDE.md §1.8, §8):
/// everything else reads attendance only through <see cref="IPayrollAttendanceExtract"/>. These
/// tests are the first use of NetArchTest in this solution (new tooling, not a reuse).
/// </summary>
public sealed class AttendanceDataAccessArchitectureTests
{
    private static readonly string[] Internal =
    [
        typeof(RawAttendanceTransaction).FullName!,
        typeof(NormalizedAttendanceTransaction).FullName!,
    ];

    private static readonly Assembly Contracts = typeof(robeem.Contracts.Common.CreatedResponse).Assembly;
    private static readonly Assembly Web = typeof(TenantHeaderHandler).Assembly;
    private static readonly Assembly Application = typeof(IPayrollAttendanceExtract).Assembly;

    [Fact]
    public void WebAndContracts_DoNotUseRawOrNormalizedAttendance()
    {
        AssertNoDependency(Types.InAssembly(Web));
        AssertNoDependency(Types.InAssembly(Contracts));
    }

    [Fact]
    public void WebAndContracts_DoNotReferenceTheInnerLayersAtAll()
    {
        var inner = new[] { "robeem.Domain", "robeem.Application", "robeem.Infrastructure" };

        foreach (var assembly in new[] { Web, Contracts })
        {
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name);
            Assert.Empty(references.Intersect(inner));
        }
    }

    [Fact]
    public void InApplication_OnlyTheAttendanceFeatureAndThePersistenceAbstractionUseRawOrNormalizedAttendance()
    {
        // IRobeemDbContext exposes the DbSets, so Persistence is allowed; HR and the rest are not.
        Assert.Equal("robeem.Application.Persistence", typeof(IRobeemDbContext).Namespace);

        var outsiders = Types.InAssembly(Application)
            .That().DoNotResideInNamespace("robeem.Application.Attendance")
            .And().DoNotResideInNamespace("robeem.Application.Persistence");

        AssertNoDependency(outsiders);
    }

    [Fact]
    public void ThePayrollExtract_DoesNotUseRawOrNormalizedAttendance()
    {
        var extract = Types.InAssembly(Application)
            .That().ResideInNamespace("robeem.Application.Attendance")
            .And().HaveNameMatching("Payroll");

        AssertNoDependency(extract);

        // The state machine of an async method is a nested type of its own: it must be covered too.
        var nested = Types.InAssembly(Application)
            .That().ResideInNamespace("robeem.Application.Attendance")
            .And().HaveNameMatching("PayrollAttendanceExtract");
        Assert.NotEmpty(nested.GetTypes());
    }

    [Fact]
    public void IngestionAndSync_NeverCallNormalization()
    {
        // CLAUDE.md §1.3: normalization is asynchronous, run by its own job, never inline in the request that received the data.
        var receivers = Types.InAssembly(Application)
            .That().HaveNameMatching("IngestionService|IntegrationSyncService|RawBatchWriter");

        Assert.NotEmpty(receivers.GetTypes());
        var result = receivers.ShouldNot()
            .HaveDependencyOnAny(typeof(INormalizationService).FullName!, typeof(NormalizationService).FullName!)
            .GetResult();

        Assert.True(result.IsSuccessful, "Normalization is called inline by: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void TheInfrastructureAssembly_IsAllowedToMapThem()
    {
        // Sanity check that the rules can see a dependency at all: the DbContext does have DbSets of them.
        var result = Types.InAssembly(typeof(RobeemDbContext).Assembly)
            .That().HaveName(nameof(RobeemDbContext))
            .Should().HaveDependencyOnAny(Internal)
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    private static void AssertNoDependency(Types types) => AssertNoDependency(types.That().AreClasses().Or().AreNotClasses());

    private static void AssertNoDependency(PredicateList types)
    {
        var result = types.ShouldNot().HaveDependencyOnAny(Internal).GetResult();

        Assert.True(result.IsSuccessful, "Disallowed use of raw/normalized attendance in: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
