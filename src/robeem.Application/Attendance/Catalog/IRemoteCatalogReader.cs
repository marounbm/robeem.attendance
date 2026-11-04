namespace robeem.Application.Attendance.Catalog;

/// <summary>
/// An <see cref="ICatalogReader"/> that asks the prime tenant's Api for the catalog instead of opening its database. What every
/// tenant but the prime one reads through. Nothing is kept between calls: each read is a call, and a prime Api that cannot answer
/// is a <see cref="CatalogUnavailableException"/>.
/// </summary>
public interface IRemoteCatalogReader : ICatalogReader
{
}
