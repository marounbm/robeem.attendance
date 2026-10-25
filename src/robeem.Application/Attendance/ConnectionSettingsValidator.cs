using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Holds a connection to what its transport declares (<see cref="TransportDescriptors"/>): only declared settings, the required
/// ones present, an authentication method the transport allows, a secret named by reference and never typed in, and a schedule
/// only where robeem pulls. It also converts the stored settings text to and from a dictionary.
/// </summary>
public static class ConnectionSettingsValidator
{
    public const int MaxValueLength = 500;

    // A reference is a name, like "acme-clock-api" or "kv:acme/clock": not a connection string or a key.
    private static readonly Regex ReferencePattern = new(@"^[A-Za-z0-9][A-Za-z0-9._:/\-]*$", RegexOptions.CultureInvariant);

    private static readonly string[] SecretWords = ["password", "passwd", "secret", "token", "apikey", "privatekey", "passphrase", "credential"];

    /// <summary>The errors of an input, keyed by the input's property (a setting by <c>Settings[key]</c>); empty when it is valid.</summary>
    public static Dictionary<string, string> Validate(ConnectionInput input)
    {
        var errors = new Dictionary<string, string>();

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 255)
        {
            errors[nameof(ConnectionInput.Name)] = "A name is required (at most 255 characters).";
        }

        if (!Enum.TryParse<AttendanceTransport>(input.Transport, ignoreCase: true, out var transport) || !Enum.IsDefined(transport))
        {
            errors[nameof(ConnectionInput.Transport)] = $"Choose how attendance is received: {string.Join(", ", Enum.GetNames<AttendanceTransport>())}.";
            return errors;
        }

        var descriptor = TransportDescriptors.Find(transport)!;
        if (!Enum.TryParse<ConnectionAuthMethod>(input.AuthMethod, ignoreCase: true, out var method) || !Enum.IsDefined(method))
        {
            errors[nameof(ConnectionInput.AuthMethod)] = $"The authentication must be one of: {string.Join(", ", Enum.GetNames<ConnectionAuthMethod>())}.";
        }
        else if (!descriptor.AuthMethods.Contains(method.ToString()))
        {
            errors[nameof(ConnectionInput.AuthMethod)] = $"{transport} can't be authenticated by {method}. It allows: {string.Join(", ", descriptor.AuthMethods)}.";
        }

        ValidateSettings(input, descriptor, errors);

        // The secret may still be unnamed (the connection is saved first, the secret provisioned after; a test says when it is missing),
        // but a name that looks like a connection string or a key is refused whatever the method.
        var reference = (input.SecretRef ?? string.Empty).Trim();
        if (reference.Length > 0 && (reference.Length > 255 || !ReferencePattern.IsMatch(reference)))
        {
            errors[nameof(ConnectionInput.SecretRef)] = "Give the name the secret is kept under (letters, digits and . _ : / -), never the secret itself.";
        }

        // A schedule is only kept where robeem pulls, but one that can't be a schedule is refused wherever it is typed.
        var schedule = (input.PollingSchedule ?? string.Empty).Trim();
        if (schedule.Length > 0)
        {
            if (schedule.Length > 100)
            {
                errors[nameof(ConnectionInput.PollingSchedule)] = "The schedule can be at most 100 characters.";
            }
            else if (!CronSchedule.TryParse(schedule, out _))
            {
                errors[nameof(ConnectionInput.PollingSchedule)] = "Not a valid cron expression: five fields (minute hour day month weekday), or six with seconds first.";
            }
        }

        return errors;
    }

    /// <summary>The settings as they are stored: only non-empty, trimmed values, as JSON.</summary>
    public static string Serialize(IReadOnlyDictionary<string, string>? settings) =>
        JsonSerializer.Serialize(Clean(settings));

    /// <summary>The stored settings text as a dictionary; an unreadable or empty text is no settings.</summary>
    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Gets a value indicating whether robeem pulls over this transport, so a schedule means something.</summary>
    public static bool IsPull(AttendanceTransport transport) => TransportDescriptors.Find(transport)?.IsPull ?? false;

    public static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string>? settings) =>
        (settings ?? new Dictionary<string, string>())
            .Select(pair => (Key: (pair.Key ?? string.Empty).Trim(), Value: (pair.Value ?? string.Empty).Trim()))
            .Where(pair => pair.Key.Length > 0 && pair.Value.Length > 0)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

    private static void ValidateSettings(ConnectionInput input, TransportDescriptorItem descriptor, Dictionary<string, string> errors)
    {
        var settings = Clean(input.Settings);
        var declared = descriptor.Fields.ToDictionary(f => f.Key, StringComparer.Ordinal);

        foreach (var (key, value) in settings)
        {
            var field = $"{nameof(ConnectionInput.Settings)}[{key}]";
            if (!declared.TryGetValue(key, out var declaration))
            {
                var looksSecret = SecretWords.Any(word => key.Contains(word, StringComparison.OrdinalIgnoreCase));
                errors[field] = looksSecret
                    ? $"'{key}' looks like a credential. A credential is never stored here: keep it in a secret and name that secret in the secret reference."
                    : $"'{key}' is not a setting of {descriptor.Transport}. It declares: {(declared.Count == 0 ? "no settings" : string.Join(", ", declared.Keys))}.";
            }
            else if (value.Length > MaxValueLength)
            {
                errors[field] = $"{declaration.Label} can be at most {MaxValueLength} characters.";
            }
            else if (declaration.Kind == "number" && !(int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 65535))
            {
                errors[field] = $"{declaration.Label} must be a number from 1 to 65535.";
            }
            else if (declaration.Kind == "url" && !IsPlainWebAddress(value))
            {
                errors[field] = $"{declaration.Label} must be an http or https address with no user name or password in it.";
            }
        }

        foreach (var required in descriptor.Fields.Where(f => f.Required && !settings.ContainsKey(f.Key)))
        {
            errors[$"{nameof(ConnectionInput.Settings)}[{required.Key}]"] = $"{required.Label} is required for {descriptor.Transport}.";
        }
    }

    private static bool IsPlainWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.UserInfo.Length == 0;
}
