namespace robeem.Application.Attendance;

/// <summary>
/// Which attendance providers have code behind them: an <see cref="IAttendanceProvider"/> and an
/// <see cref="IAttendanceEventInterpreter"/> registered under the provider's key (CLAUDE.md §1.9). A provider
/// row in the database is usable when it has such an adapter, or a default file format to be read by.
/// </summary>
public interface IAttendanceAdapterCatalog
{
    /// <summary>Gets a value indicating whether both the adapter and the interpreter of <paramref name="providerKey"/> are registered.</summary>
    bool IsRegistered(string providerKey);
}
