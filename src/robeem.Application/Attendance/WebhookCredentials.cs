namespace robeem.Application.Attendance;

/// <summary>What a webhook request presented to prove itself: the headers it may carry. Which one counts is the connection's authentication method.</summary>
/// <param name="Signature">The signature header (HMAC of the body), or null.</param>
/// <param name="ApiKey">The API key header, or null.</param>
/// <param name="Authorization">The Authorization header (<c>Bearer ...</c>), or null.</param>
public sealed record WebhookCredentials(string? Signature = null, string? ApiKey = null, string? Authorization = null);
