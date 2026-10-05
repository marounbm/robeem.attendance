namespace robeem.Web.ApiClients;

/// <summary>
/// Outcome of a create, update or delete. <see cref="Errors"/> is keyed by the input's property
/// names; a key with no property (such as the Api's <c>id</c> for a refused delete) is the empty
/// key, a model-level message.
/// </summary>
public sealed class ApiSaveResult
{
    private ApiSaveResult(int? id, bool notFound, IReadOnlyDictionary<string, string> errors)
    {
        Id = id;
        NotFound = notFound;
        Errors = errors;
    }

    public int? Id { get; }

    public bool NotFound { get; }

    public IReadOnlyDictionary<string, string> Errors { get; }

    public bool Succeeded => !NotFound && Errors.Count == 0;

    /// <summary>Gets every message, field-keyed or not, as one line for a banner.</summary>
    public string MessageText => string.Join(" ", Errors.Values);

    public static ApiSaveResult Success(int? id) => new(id, false, new Dictionary<string, string>());

    public static ApiSaveResult Missing() => new(null, true, new Dictionary<string, string>());

    public static ApiSaveResult Invalid(IReadOnlyDictionary<string, string> errors) => new(null, false, errors);
}
