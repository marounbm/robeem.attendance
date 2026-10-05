namespace robeem.Application.Common;

/// <summary>
/// Outcome of a create, update or delete. <see cref="Errors"/> is keyed by the property names of the
/// input (or by <c>id</c> for a refusal of the record itself), so the Api can answer 400 with them.
/// </summary>
public sealed class SaveResult
{
    private SaveResult(int? id, bool notFound, IReadOnlyDictionary<string, string> errors)
    {
        Id = id;
        NotFound = notFound;
        Errors = errors;
    }

    public int? Id { get; }

    public bool NotFound { get; }

    public IReadOnlyDictionary<string, string> Errors { get; }

    public bool Succeeded => !NotFound && Errors.Count == 0;

    public static SaveResult Success(int id) => new(id, false, new Dictionary<string, string>());

    public static SaveResult Missing() => new(null, true, new Dictionary<string, string>());

    public static SaveResult Invalid(string field, string message) =>
        new(null, false, new Dictionary<string, string> { [field] = message });

    public static SaveResult Invalid(IReadOnlyDictionary<string, string> errors) => new(null, false, errors);
}
