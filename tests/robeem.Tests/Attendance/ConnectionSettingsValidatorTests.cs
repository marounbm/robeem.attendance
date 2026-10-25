using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>A connection is held to what its transport declares: its settings, its authentication, and a secret named, never typed.</summary>
public sealed class ConnectionSettingsValidatorTests
{
    [Fact]
    public void EveryTransportHasADescriptor_WithAnAllowedAuthentication_AndNoSettingThatIsACredential()
    {
        foreach (var transport in Enum.GetValues<AttendanceTransport>())
        {
            var descriptor = TransportDescriptors.Find(transport);

            Assert.NotNull(descriptor);
            Assert.NotEmpty(descriptor.AuthMethods);
            Assert.All(descriptor.AuthMethods, method => Assert.True(Enum.TryParse<ConnectionAuthMethod>(method, out _), method));
            Assert.All(descriptor.Fields, field => Assert.DoesNotContain(
                ["password", "secret", "token", "apikey", "privatekey"],
                word => field.Key.Contains(word, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(ConnectionSettingsValidator.IsPull(transport), descriptor.IsPull);
        }
    }

    [Fact]
    public void ASftpConnection_NeedsItsHostAndFolderAndTheHostKey_AndAcceptsKeyOrPasswordAuthentication()
    {
        var input = Sftp();
        input.Settings.Remove("hostKeyFingerprint");
        input.Settings.Remove("host");

        var errors = ConnectionSettingsValidator.Validate(input);

        Assert.Contains("Settings[hostKeyFingerprint]", errors.Keys);
        Assert.Contains("Settings[host]", errors.Keys);
        Assert.Empty(ConnectionSettingsValidator.Validate(Sftp()));
        Assert.Empty(ConnectionSettingsValidator.Validate(WithAuth(Sftp(), "UsernamePassword")));
        Assert.Contains(nameof(ConnectionInput.AuthMethod), ConnectionSettingsValidator.Validate(WithAuth(Sftp(), "ApiKey")).Keys);
    }

    [Fact]
    public void ASettingTheTransportDoesNotDeclare_IsRefused_AndOneThatLooksLikeACredentialIsToldToGoInTheSecret()
    {
        var input = Sftp();
        input.Settings["colour"] = "blue";
        input.Settings["password"] = "hunter2";
        input.Settings["apiKey"] = "abc";

        var errors = ConnectionSettingsValidator.Validate(input);

        Assert.Contains("is not a setting of Sftp", errors["Settings[colour]"]);
        Assert.Contains("keep it in a secret", errors["Settings[password]"]);
        Assert.Contains("keep it in a secret", errors["Settings[apiKey]"]);

        // The refusal never repeats the value that was typed.
        Assert.DoesNotContain(errors.Values, message => message.Contains("hunter2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("twenty-two")]
    public void APortThatIsNotAPort_IsRefused(string port)
    {
        var input = Sftp();
        input.Settings["port"] = port;

        Assert.Contains("Settings[port]", ConnectionSettingsValidator.Validate(input).Keys);
    }

    [Theory]
    [InlineData("ftp://biostar.example.test", false)]
    [InlineData("https://user:pass@biostar.example.test", false)]
    [InlineData("not a url", false)]
    [InlineData("https://biostar.example.test:8443", true)]
    [InlineData("http://10.0.0.5", true)]
    public void ABaseUrl_MustBePlainHttpOrHttps_WithNoCredentialsInIt(string url, bool valid)
    {
        var input = new ConnectionInput { Name = "API", Transport = "RestApi", AuthMethod = "SessionLogin", SecretRef = "biostar", Settings = { ["baseUrl"] = url } };

        Assert.Equal(valid, !ConnectionSettingsValidator.Validate(input).ContainsKey("Settings[baseUrl]"));
    }

    [Theory]
    [InlineData("Host=db;Password=secret")]
    [InlineData("has a space")]
    [InlineData("-leading-dash")]
    public void ASecretThatIsNotAName_IsRefused(string reference)
    {
        var input = Sftp();
        input.SecretRef = reference;

        Assert.Contains(nameof(ConnectionInput.SecretRef), ConnectionSettingsValidator.Validate(input).Keys);
    }

    [Fact]
    public void AScheduleMustBeCron_AndOnlyATransportThatPullsKeepsOne()
    {
        var bad = Sftp();
        bad.PollingSchedule = "every five minutes";
        var upload = new ConnectionInput { Name = "Upload", Transport = "FileUpload", AuthMethod = "None", PollingSchedule = "0 * * * *" };

        Assert.Contains(nameof(ConnectionInput.PollingSchedule), ConnectionSettingsValidator.Validate(bad).Keys);
        Assert.Empty(ConnectionSettingsValidator.Validate(upload));
    }

    [Fact]
    public void AFileUploadAndAWebhook_NeedNoSettings_AndAWebhookIsAuthenticatedBySignatureKeyOrToken()
    {
        Assert.Empty(ConnectionSettingsValidator.Validate(new ConnectionInput { Name = "Upload", Transport = "FileUpload", AuthMethod = "None" }));
        foreach (var method in new[] { "HmacSignature", "ApiKey", "BearerToken" })
        {
            Assert.Empty(ConnectionSettingsValidator.Validate(new ConnectionInput { Name = "Push", Transport = "Webhook", AuthMethod = method, SecretRef = "push-secret" }));
        }

        Assert.Contains(nameof(ConnectionInput.AuthMethod), ConnectionSettingsValidator.Validate(new ConnectionInput { Name = "Push", Transport = "Webhook", AuthMethod = "None" }).Keys);
        Assert.Contains(nameof(ConnectionInput.AuthMethod), ConnectionSettingsValidator.Validate(new ConnectionInput { Name = "Upload", Transport = "FileUpload", AuthMethod = "ApiKey" }).Keys);
    }

    [Fact]
    public void TheSettingsAreStoredAsTrimmedJson_WithoutEmptyValues_AndReadBack()
    {
        var json = ConnectionSettingsValidator.Serialize(new Dictionary<string, string> { [" host "] = " sftp.example.test ", ["port"] = " ", ["remotePath"] = "/in" });

        var back = ConnectionSettingsValidator.Parse(json);

        Assert.Equal(new Dictionary<string, string> { ["host"] = "sftp.example.test", ["remotePath"] = "/in" }, back);
        Assert.Empty(ConnectionSettingsValidator.Parse("not json"));
        Assert.Empty(ConnectionSettingsValidator.Parse(null));
    }

    private static ConnectionInput WithAuth(ConnectionInput input, string method)
    {
        input.AuthMethod = method;
        return input;
    }

    private static ConnectionInput Sftp() => new()
    {
        Name = "Branch drop",
        Transport = "Sftp",
        AuthMethod = "SshKey",
        SecretRef = "branch-sftp-key",
        Settings =
        {
            ["host"] = "sftp.example.test",
            ["remotePath"] = "/attendance/in",
            ["hostKeyFingerprint"] = "SHA256:abc123",
        },
    };
}
