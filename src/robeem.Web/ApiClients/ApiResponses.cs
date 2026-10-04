using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using robeem.Contracts.Tenancy;

namespace robeem.Web.ApiClients;

/// <summary>Response handling shared by the robeem.Api clients.</summary>
internal static class ApiResponses
{
    /// <summary>Reads a successful JSON body; throws on an error status or an empty body.</summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException($"robeem.Api returned an empty {typeof(T).Name} body.");
    }

    /// <summary>
    /// Throws <see cref="TenantSelectionRequiredException"/> when the API rejected the request's
    /// tenant, otherwise <see cref="HttpRequestException"/> for any non-success status.
    /// </summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await ThrowIfTenantRejectedAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task ThrowIfTenantRejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Forbidden))
        {
            return;
        }

        var code = await ReadProblemCodeAsync(response, cancellationToken);
        if (code is TenancyProtocol.TenantRequiredCode or TenancyProtocol.UnknownTenantCode)
        {
            throw new TenantSelectionRequiredException(code);
        }
    }

    /// <summary>
    /// Maps a 400 ValidationProblemDetails onto <typeparamref name="TInput"/> property names. Keys
    /// that name no property (e.g. a JSON path the API could not bind) map to <see cref="string.Empty"/>,
    /// a model-level error.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> ReadValidationErrorsAsync<TInput>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await ReadJsonOrDefaultAsync<ValidationProblem>(response, cancellationToken);
        var properties = typeof(TInput).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.Name, StringComparer.OrdinalIgnoreCase);

        var errors = new Dictionary<string, string>();
        foreach (var (key, messages) in problem?.Errors ?? new Dictionary<string, string[]>())
        {
            var name = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
            var field = properties.TryGetValue(name, out var property) ? property : string.Empty;
            var message = string.Join(" ", messages);
            errors[field] = errors.TryGetValue(field, out var existing) ? $"{existing} {message}" : message;
        }

        if (errors.Count == 0)
        {
            errors[string.Empty] = problem?.Title ?? "robeem.Api rejected the request.";
        }

        return errors;
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await ReadJsonOrDefaultAsync<JsonDocument>(response, cancellationToken);
        return document?.RootElement is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty(TenancyProtocol.ErrorCodeExtension, out var code)
            && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
    }

    /// <summary>Reads a JSON body without consuming it (HttpClient buffers it), or <c>default</c> when it isn't JSON.</summary>
    private static async Task<T?> ReadJsonOrDefaultAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase))
        {
            return default;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed class ValidationProblem
    {
        public string? Title { get; set; }

        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
