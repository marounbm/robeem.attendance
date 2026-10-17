using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application;
using robeem.Application.Attendance;
using robeem.Infrastructure;

namespace robeem.Tests.Infrastructure.Attendance;

/// <summary>
/// The catalog services, wired as the Api wires them (<c>AddRobeemInfrastructure</c> then <c>AddRobeemApplication</c>):
/// each is registered, and so is every dependency of its implementation, so a missing registration fails a test
/// and not the first request.
/// </summary>
public sealed class CatalogCompositionTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddLogging()
        .AddRobeemInfrastructure(new ConfigurationBuilder().Build())
        .AddRobeemApplication()
        .BuildServiceProvider();

    public void Dispose() => _services.Dispose();

    [Theory]
    [InlineData(typeof(IAttendanceProviderService))]
    [InlineData(typeof(IAttendanceDeviceTypeService))]
    [InlineData(typeof(IAttendanceTransportService))]
    [InlineData(typeof(IAttendanceIntegrationService))]
    [InlineData(typeof(IAttendanceFileFormatService))]
    [InlineData(typeof(IIngestionService))]
    [InlineData(typeof(INormalizationService))]
    [InlineData(typeof(IAttendanceAdapterCatalog))]
    [InlineData(typeof(ICustomFormatFileReader))]
    [InlineData(typeof(ICustomFormatInterpreterFactory))]
    public void EveryCatalogService_IsRegistered(Type service) =>
        Assert.True(_services.GetRequiredService<IServiceProviderIsService>().IsService(service), $"{service.Name} is not registered.");

    [Theory]
    [InlineData(typeof(AttendanceProviderService))]
    [InlineData(typeof(AttendanceDeviceTypeService))]
    [InlineData(typeof(AttendanceTransportService))]
    [InlineData(typeof(AttendanceIntegrationService))]
    [InlineData(typeof(AttendanceFileFormatService))]
    [InlineData(typeof(IngestionService))]
    [InlineData(typeof(NormalizationService))]
    public void EveryDependencyOfTheirImplementations_IsRegistered(Type implementation)
    {
        var isService = _services.GetRequiredService<IServiceProviderIsService>();

        foreach (var parameter in implementation.GetConstructors().Single().GetParameters())
        {
            Assert.True(isService.IsService(parameter.ParameterType), $"{implementation.Name} needs {parameter.ParameterType.Name}, which is not registered.");
        }
    }
}
