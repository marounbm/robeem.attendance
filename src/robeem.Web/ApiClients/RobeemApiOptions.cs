namespace robeem.Web.ApiClients;

/// <summary>Bound from the <c>RobeemApi</c> configuration section.</summary>
public sealed class RobeemApiOptions
{
    public const string SectionName = "RobeemApi";

    /// <summary>Gets or sets the absolute base URL of robeem.Api, e.g. <c>http://localhost:5181/</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Gets the base URL with a trailing slash, so relative paths such as <c>api/employees</c> append to it.</summary>
    public Uri BaseUri => new(BaseUrl.TrimEnd('/') + "/");
}
