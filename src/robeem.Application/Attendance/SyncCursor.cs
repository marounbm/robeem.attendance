namespace robeem.Application.Attendance;

/// <summary>An incremental-sync high-water mark. Its shape is defined by the provider.</summary>
public sealed record SyncCursor(string Value);
