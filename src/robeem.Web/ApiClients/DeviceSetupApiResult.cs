using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>The Api's answer to setting a device up: whether it was done (with the errors by field if not), and where the device was put.</summary>
/// <param name="Result">Done, refused with field errors, or not found.</param>
/// <param name="Response">The device and its integration; null unless done.</param>
public sealed record DeviceSetupApiResult(ApiSaveResult Result, DeviceSetupResponse? Response);
