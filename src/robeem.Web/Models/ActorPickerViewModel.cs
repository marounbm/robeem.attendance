using robeem.Contracts.Security;
using robeem.Web.Actors;

namespace robeem.Web.Models;

public sealed class ActorPickerViewModel
{
    /// <summary>Gets the role sets offered as one-click presets.</summary>
    public static IReadOnlyDictionary<string, string[]> Presets { get; } = new Dictionary<string, string[]>
    {
        ["Employee"] = ["Employee"],
        ["Manager"] = ["Employee", "Manager"],
        ["HR administrator"] = ["Employee", "HRAdministrator"],
        ["Payroll administrator"] = ["Employee", "PayrollAdministrator"],
        ["Integration administrator"] = ["IntegrationAdministrator"],
        ["Every role"] = [.. ActorProtocol.Roles],
    };

    public IReadOnlyList<ActorOption> Actors { get; init; } = Array.Empty<ActorOption>();

    public ActingAs? Current { get; init; }

    public string? ReturnUrl { get; init; }
}
