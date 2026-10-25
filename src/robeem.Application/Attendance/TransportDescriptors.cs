using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// What each transport needs: the non-secret settings it declares and the authentication methods it allows. A transport's
/// behaviour is code, so this is code too; the connection form is drawn from it and <see cref="ConnectionSettingsValidator"/>
/// holds every connection to it.
/// </summary>
public static class TransportDescriptors
{
    private static readonly IReadOnlyList<TransportDescriptorItem> Items =
    [
        new()
        {
            Transport = nameof(AttendanceTransport.FileUpload),
            IsPull = false,
            AuthMethods = [nameof(ConnectionAuthMethod.None)],
        },
        new()
        {
            Transport = nameof(AttendanceTransport.Sftp),
            IsPull = true,
            Fields =
            [
                Field("host", "Host", "text", true, "The server's name or address."),
                Field("port", "Port", "number", false, "Empty: 22."),
                Field("remotePath", "Remote folder", "path", true, "The folder the files are collected from."),
                Field("filePattern", "File name pattern", "text", false, "For example *.csv. Empty: every file."),
                Field("archivePath", "Archive folder", "path", false, "Where a collected file is moved. Empty: it is left in place."),
                Field("hostKeyFingerprint", "Host key fingerprint", "text", true, "The server's SSH host key fingerprint (SHA256:...). The connection is refused when the server shows another."),
            ],
            AuthMethods = [nameof(ConnectionAuthMethod.SshKey), nameof(ConnectionAuthMethod.UsernamePassword)],
        },
        new()
        {
            Transport = nameof(AttendanceTransport.RestApi),
            IsPull = true,
            Fields =
            [
                Field("baseUrl", "Base URL", "url", false, "The API's address, for example https://biostar.example.test:8443. Empty: it is read from the secret, as before."),
                Field("transactionsPath", "Transactions path", "path", false, "Empty: the adapter's own."),
                Field("devicesPath", "Devices path", "path", false, "Empty: the adapter's own."),
            ],
            AuthMethods =
            [
                nameof(ConnectionAuthMethod.SessionLogin),
                nameof(ConnectionAuthMethod.ApiKey),
                nameof(ConnectionAuthMethod.UsernamePassword),
                nameof(ConnectionAuthMethod.BearerToken),
                nameof(ConnectionAuthMethod.OAuth2ClientCredentials),
            ],
        },
        new()
        {
            Transport = nameof(AttendanceTransport.Webhook),
            IsPull = false,
            AuthMethods = [nameof(ConnectionAuthMethod.HmacSignature), nameof(ConnectionAuthMethod.ApiKey), nameof(ConnectionAuthMethod.BearerToken)],
        },
        new()
        {
            Transport = nameof(AttendanceTransport.Database),
            IsPull = true,
            Fields =
            [
                Field("engine", "Database engine", "text", true, "For example SqlServer, PostgreSql or MySql."),
                Field("host", "Host", "text", true, "The database server's name or address."),
                Field("port", "Port", "number", false, "Empty: the engine's usual port."),
                Field("database", "Database", "text", true, "The database name."),
                Field("query", "Query", "text", true, "The read-only query that returns the punches."),
            ],
            AuthMethods = [nameof(ConnectionAuthMethod.UsernamePassword)],
        },
        new()
        {
            Transport = nameof(AttendanceTransport.DeviceSdk),
            IsPull = true,
            Fields = [Field("agentId", "Agent id", "text", true, "The on-premises agent that talks to the device.")],
            AuthMethods = [nameof(ConnectionAuthMethod.AgentToken)],
        },
    ];

    /// <summary>Gets a description of every transport.</summary>
    public static IReadOnlyList<TransportDescriptorItem> All => Items;

    public static TransportDescriptorItem? Find(AttendanceTransport transport) =>
        Items.FirstOrDefault(d => d.Transport == transport.ToString());

    private static TransportFieldItem Field(string key, string label, string kind, bool required, string help) =>
        new() { Key = key, Label = label, Kind = kind, Required = required, Help = help };
}
