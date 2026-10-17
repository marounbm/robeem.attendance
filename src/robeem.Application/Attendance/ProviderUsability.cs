using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>When an integration can be made with a provider.</summary>
public static class ProviderUsability
{
    /// <summary>
    /// A provider is usable when it is active and something can read what it delivers: a default file format, or
    /// a code adapter registered under its key.
    /// </summary>
    public static bool IsUsable(AttendanceProvider provider, IAttendanceAdapterCatalog adapters) =>
        provider.IsActive && (provider.DefaultFileFormatId is not null || adapters.IsRegistered(provider.ProviderKey));
}
