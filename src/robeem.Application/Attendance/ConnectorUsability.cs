using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>When an integration can be made with a connector.</summary>
public static class ConnectorUsability
{
    /// <summary>
    /// A connector is usable when it and its vendor are active and something can read what it delivers: a default
    /// payload format, or a code adapter registered under its adapter key. A connector whose payload is encrypted also needs
    /// a decryptor for its kind; without a catalog none is known, so only plain payloads are readable.
    /// </summary>
    public static bool IsUsable(AttendanceConnectorDefinition connector, bool vendorIsActive, IAttendanceAdapterCatalog adapters, IPayloadDecryptorCatalog? decryptors = null) =>
        connector.IsActive
        && vendorIsActive
        && (connector.EncryptionKind == PayloadEncryptionKind.None || (decryptors?.CanRead(connector.EncryptionKind) ?? false))
        && (connector.DefaultPayloadFormatId is not null || (connector.AdapterKey is { Length: > 0 } adapterKey && adapters.IsRegistered(adapterKey)));
}
