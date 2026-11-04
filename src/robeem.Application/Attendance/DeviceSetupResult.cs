using robeem.Application.Common;

namespace robeem.Application.Attendance;

/// <summary>
/// The outcome of setting a device up from the vendor down. <see cref="Result"/> carries the new device's id, or the errors keyed by the
/// <c>DeviceSetupInput</c> fields (the device's own as <c>Device.{Field}</c>).
/// </summary>
/// <param name="Result">Whether it was done, with the device's id or the errors.</param>
/// <param name="IntegrationId">The integration the device was put in; 0 when nothing was done.</param>
/// <param name="IntegrationCreated">Whether the connector had no integration yet, so one was made.</param>
/// <param name="ConnectionCreated">Whether a connection was made for the device on an integration that already existed.</param>
public sealed record DeviceSetupResult(SaveResult Result, int IntegrationId = 0, bool IntegrationCreated = false, bool ConnectionCreated = false);
