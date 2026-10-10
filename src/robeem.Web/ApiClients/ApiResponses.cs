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
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ApiAccessDeniedException(response.StatusCode, await ReadRefusalReasonAsync(response, cancellationToken));
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ApiNotFoundException();
        }

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
    /// Reads the answer to an action: a success body, or a 400 as the messages to show the user.
    /// Anything else (a refusal of access, a missing record, a server error) is thrown as usual.
    /// </summary>
    public static async Task<ApiCommandResult<T>> CommandAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            await ThrowIfTenantRejectedAsync(response, cancellationToken);
            var problem = await ReadJsonOrDefaultAsync<ValidationProblem>(response, cancellationToken);
            var messages = problem?.Errors?.Values.SelectMany(m => m).Where(m => !string.IsNullOrWhiteSpace(m)).ToList() ?? [];
            if (messages.Count == 0)
            {
                messages.Add(problem?.Title ?? "robeem.Api rejected the request.");
            }

            return ApiCommandResult<T>.Refused(messages);
        }

        return ApiCommandResult<T>.Ok(await ReadAsync<T>(response, cancellationToken));
    }

    /// <summary>
    /// Reads the answer to a create, update or delete: 201 with the new id, 204, a 400 mapped onto
    /// <typeparamref name="TInput"/> property names, or a 404. Anything else is thrown as usual.
    /// </summary>
    public static async Task<ApiSaveResult> SaveAsync<TInput>(HttpResponseMessage response, int? knownId, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            await ThrowIfTenantRejectedAsync(response, cancellationToken);
            return ApiSaveResult.Invalid(await ReadValidationErrorsAsync<TInput>(response, cancellationToken));
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ApiSaveResult.Missing();
        }

        await EnsureSuccessAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var created = await response.Content.ReadFromJsonAsync<robeem.Contracts.Common.CreatedResponse>(cancellationToken);
            return ApiSaveResult.Success(created?.Id);
        }

        return ApiSaveResult.Success(knownId);
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

    // The Api's problem title says why, e.g. "Your account is not linked to an employee"; a bare "Forbidden"/"Unauthorized" says nothing more than the status.
    private static async Task<string?> ReadRefusalReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var title = (await ReadJsonOrDefaultAsync<ValidationProblem>(response, cancellationToken))?.Title;
        return string.IsNullOrWhiteSpace(title) || title is "Forbidden" or "Unauthorized" ? null : title.Trim();
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
