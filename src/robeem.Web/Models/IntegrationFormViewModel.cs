using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class IntegrationFormViewModel
{
    /// <summary>Gets or sets the integration id; null while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    public IntegrationInput Input { get; set; } = new();

    [BindNever]
    public IReadOnlyList<ProviderOption> Providers { get; set; } = Array.Empty<ProviderOption>();
}
