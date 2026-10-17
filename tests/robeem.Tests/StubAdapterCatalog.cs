using robeem.Application.Attendance;

namespace robeem.Tests;

/// <summary>An adapter catalog that knows exactly the keys it is given.</summary>
public sealed class StubAdapterCatalog : IAttendanceAdapterCatalog
{
    private readonly HashSet<string> _keys;

    public StubAdapterCatalog(params string[] keys)
    {
        _keys = keys.ToHashSet(StringComparer.Ordinal);
    }

    public bool IsRegistered(string providerKey) => _keys.Contains(providerKey);
}
