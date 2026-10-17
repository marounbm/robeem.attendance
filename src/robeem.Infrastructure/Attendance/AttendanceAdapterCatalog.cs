using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Attendance;

namespace robeem.Infrastructure.Attendance;

/// <summary>Answers from the keyed registrations of <see cref="IAttendanceProvider"/> and <see cref="IAttendanceEventInterpreter"/>.</summary>
public sealed class AttendanceAdapterCatalog : IAttendanceAdapterCatalog
{
    private readonly IServiceProvider _services;

    public AttendanceAdapterCatalog(IServiceProvider services)
    {
        _services = services;
    }

    public bool IsRegistered(string providerKey) =>
        _services.GetService<IServiceProviderIsKeyedService>() is { } probe
        && probe.IsKeyedService(typeof(IAttendanceProvider), providerKey)
        && probe.IsKeyedService(typeof(IAttendanceEventInterpreter), providerKey);
}
