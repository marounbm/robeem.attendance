namespace robeem.Application.Attendance.Catalog;

/// <summary>The prime tenant's Api could not give the attendance catalog: it is down, slow, refused the call or sent something unreadable. There is no cache to fall back on.</summary>
public sealed class CatalogUnavailableException : Exception
{
    public CatalogUnavailableException(string message)
        : base(message)
    {
    }

    public CatalogUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
