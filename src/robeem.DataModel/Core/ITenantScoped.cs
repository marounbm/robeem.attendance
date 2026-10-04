namespace robeem.DataModel
{
    /// <summary>
    /// Entity that exposes its owning tenant as a CLR property. Every tenant has its own database,
    /// so this column is defense in depth: the persistence layer stamps it on insert and filters
    /// every query on it. Entities that do not implement this interface still get a
    /// <c>TenantId</c> column, as an EF Core shadow property.
    /// </summary>
    public interface ITenantScoped
    {
        public const int TenantIdMaxLength = 64;

        string TenantId { get; set; }
    }
}
