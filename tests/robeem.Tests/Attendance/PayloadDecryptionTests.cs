using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Decryption;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>A connector whose files arrive encrypted: the decryptors, and the upload that reads such a file through them.</summary>
public sealed class PayloadDecryptionTests : IDisposable
{
    private const string Csv =
        "transaction_id,employee_id,timestamp,type,device_id,fingerprint_template\r\n" +
        "T1,E-1,2026-10-04 09:00:00,IN,DEV-1,AAEC\r\n" +
        "T2,E-1,2026-10-04 17:05,OUT,DEV-1,\r\n";

    private readonly TestDatabase _database = new();
    private readonly string _encrypted = Path.GetTempFileName();
    private readonly string _plain = Path.GetTempFileName();

    public PayloadDecryptionTests()
    {
        _database.SeedAttendanceCatalog();
    }

    public void Dispose()
    {
        File.Delete(_encrypted);
        File.Delete(_plain);
        _database.Dispose();
    }

    [Theory]
    [InlineData("cbc", true)]
    [InlineData("cbc", false)]
    [InlineData("gcm", true)]
    [InlineData("gcm", false)]
    public async Task AnAesFile_IsDecrypted_WithTheIvInTheSecretOrAtTheStartOfTheFile(string mode, bool ivInSecret)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(mode == "gcm" ? 12 : 16);
        var body = Aes256(mode, key, iv, Encoding.UTF8.GetBytes(Csv));
        File.WriteAllBytes(_encrypted, ivInSecret ? body : [.. iv, .. body]);
        var secret = ivInSecret
            ? $$"""{"key":"{{Convert.ToBase64String(key)}}","iv":"{{Convert.ToBase64String(iv)}}","mode":"{{mode}}"}"""
            : $$"""{"key":"{{Convert.ToBase64String(key)}}","mode":"{{mode}}"}""";

        await new AesPayloadDecryptor().DecryptAsync(_encrypted, _plain, secret, CancellationToken.None);

        Assert.Equal(Csv, File.ReadAllText(_plain));
    }

    [Fact]
    public async Task AesWithTheWrongKey_OrAMalformedSecret_FailsWithoutNamingTheKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(12);
        File.WriteAllBytes(_encrypted, [.. iv, .. Aes256("gcm", key, iv, Encoding.UTF8.GetBytes(Csv))]);
        var wrong = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var bad = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new AesPayloadDecryptor().DecryptAsync(_encrypted, _plain, $$"""{"key":"{{wrong}}","mode":"gcm"}""", CancellationToken.None));
        var malformed = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new AesPayloadDecryptor().DecryptAsync(_encrypted, _plain, "super-secret-password", CancellationToken.None));

        Assert.DoesNotContain(wrong, bad.Message);
        Assert.Contains("could not be decrypted", bad.Message);
        Assert.DoesNotContain("super-secret-password", malformed.Message);
        Assert.Contains("must be JSON", malformed.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APgpMessage_IsDecrypted_WithThePrivateKey_AndAPassphraseWhenTheKeyHasOne(bool withPassphrase)
    {
        var passphrase = withPassphrase ? "correct horse" : string.Empty;
        var (publicKey, armoredPrivate) = NewPgpKey(passphrase);
        File.WriteAllBytes(_encrypted, PgpEncrypt(publicKey, Encoding.UTF8.GetBytes(Csv)));
        var secret = withPassphrase ? $$"""{"privateKey":{{JsonSerializer.Serialize(armoredPrivate)}},"passphrase":"{{passphrase}}"}""" : armoredPrivate;

        await new PgpPayloadDecryptor().DecryptAsync(_encrypted, _plain, secret, CancellationToken.None);

        Assert.Equal(Csv, File.ReadAllText(_plain));
    }

    [Fact]
    public async Task APgpMessage_IsRefused_ForAnotherKeyOrAWrongPassphrase_WithAFixedMessage()
    {
        var (publicKey, _) = NewPgpKey("pass phrase");
        var (_, otherPrivate) = NewPgpKey(string.Empty);
        var (_, samePrivate) = NewPgpKey("pass phrase");
        File.WriteAllBytes(_encrypted, PgpEncrypt(publicKey, Encoding.UTF8.GetBytes(Csv)));

        var otherKey = await Assert.ThrowsAsync<InvalidDataException>(() => new PgpPayloadDecryptor().DecryptAsync(_encrypted, _plain, otherPrivate, CancellationToken.None));
        var notPgp = await Assert.ThrowsAsync<InvalidDataException>(() =>
        {
            File.WriteAllText(_encrypted, "plain text, not a message");
            return new PgpPayloadDecryptor().DecryptAsync(_encrypted, _plain, samePrivate, CancellationToken.None);
        });

        Assert.Contains("could not be decrypted", otherKey.Message);
        Assert.Contains("could not be decrypted", notPgp.Message);
    }

    [Fact]
    public async Task APasswordProtectedZip_IsOpened_WithThePassword_AndRefusedWithAnother()
    {
        WriteZip(_encrypted, "attendance.csv", Csv, password: "open-sesame");

        await new PasswordZipPayloadDecryptor().DecryptAsync(_encrypted, _plain, "open-sesame", CancellationToken.None);
        Assert.Equal(Csv, File.ReadAllText(_plain));

        var wrong = await Assert.ThrowsAsync<InvalidDataException>(() => new PasswordZipPayloadDecryptor().DecryptAsync(_encrypted, _plain + ".x", "not-it", CancellationToken.None));
        Assert.DoesNotContain("not-it", wrong.Message);
        File.Delete(_plain + ".x");
    }

    [Fact]
    public async Task AZipWithMoreThanOneFile_IsRefused()
    {
        using (var stream = File.Create(_encrypted))
        using (var zip = new ZipOutputStream(stream) { Password = "pw" })
        {
            foreach (var name in new[] { "a.csv", "b.csv" })
            {
                zip.PutNextEntry(new ZipEntry(name));
                zip.Write(Encoding.UTF8.GetBytes(Csv));
                zip.CloseEntry();
            }
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PasswordZipPayloadDecryptor().DecryptAsync(_encrypted, _plain, "pw", CancellationToken.None));

        Assert.Contains("more than one file", error.Message);
    }

    [Fact]
    public async Task AnEncryptedUpload_IsDecryptedWithTheIntegrationsKey_ReadByItsFormat_AndNoTemporaryFileIsLeft()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(16);
        File.WriteAllBytes(_encrypted, [.. iv, .. Aes256("cbc", key, iv, Encoding.UTF8.GetBytes(Csv))]);
        var secrets = new FakeSecretStore();
        secrets.Set("file-key", $$"""{"key":"{{Convert.ToBase64String(key)}}"}""");
        var integrationId = AddIntegration(PayloadEncryptionKind.Aes, "file-key");
        var before = Directory.GetFiles(Path.GetTempPath(), "robeem-*.tmp").Length;

        AttendanceIngestionResult? result;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            result = await CreateIngestion(db, secrets).IngestManualUploadAsync(integrationId, _encrypted, CancellationToken.None);
        }

        Assert.Equal((2, 0), (result!.RecordsIngested, result.RecordsFailed));
        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "robeem-*.tmp").Length);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        var payloads = check.RawAttendanceTransactions.Select(r => r.RawPayload).ToList();
        Assert.Equal(2, payloads.Count);

        // Invariant 10 holds for a decrypted file as for a plain one.
        Assert.All(payloads, p => Assert.DoesNotContain("fingerprint", p, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnEncryptedUpload_WithAWrongOrMissingKey_FailsTheBatchWithTheReason_AndStoresNothing()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(16);
        File.WriteAllBytes(_encrypted, [.. iv, .. Aes256("cbc", key, iv, Encoding.UTF8.GetBytes(Csv))]);
        var wrong = new FakeSecretStore();
        wrong.Set("file-key", $$"""{"key":"{{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}}"}""");
        var integrationId = AddIntegration(PayloadEncryptionKind.Aes, "file-key");

        AttendanceIngestionResult? wrongKey;
        AttendanceIngestionResult? missing;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            wrongKey = await CreateIngestion(db, wrong).IngestManualUploadAsync(integrationId, _encrypted, CancellationToken.None);
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            missing = await CreateIngestion(db, new FakeSecretStore()).IngestManualUploadAsync(integrationId, _encrypted, CancellationToken.None);
        }

        Assert.Equal(nameof(AttendanceBatchStatus.Failed), wrongKey!.Status);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.RawAttendanceTransactions);
        var errors = check.AttendanceProcessingBatches.Select(b => b.ErrorSummary).ToList();
        Assert.Contains(errors, e => e!.Contains("could not be decrypted"));
        Assert.Contains(errors, e => e!.Contains("'file-key' is not configured"));
        Assert.Equal(nameof(AttendanceBatchStatus.Failed), missing!.Status);
    }

    [Fact]
    public async Task AnEncryptedConnector_CanBeChosenOnlyWhereADecryptorIsRegistered_AndItsIntegrationNeedsAKeyReference()
    {
        var input = new ConnectorInput
        {
            VendorId = VendorId("generic-csv"),
            ConnectorKey = "encrypted-feed",
            DisplayName = "Encrypted feed",
            EncryptionKind = "Pgp",
            DefaultPayloadFormatId = FormatId("Generic CSV"),
        };

        Assert.Contains(nameof(ConnectorInput.EncryptionKind), (await Connectors(null).CreateAsync(input, CancellationToken.None)).Errors.Keys);
        var created = await Connectors(Catalog()).CreateAsync(input, CancellationToken.None);
        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Values));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new AttendanceIntegrationService(db, null!, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), new ServiceCollection().BuildServiceProvider(), new StubAdapterCatalog(), Catalog());
        var withoutKey = await service.CreateAsync(new IntegrationInput { ConnectorId = created.Id!.Value, DisplayName = "Feed", Transport = "FileUpload", Status = "Active" }, CancellationToken.None);
        var withKey = await service.CreateAsync(new IntegrationInput { ConnectorId = created.Id.Value, DisplayName = "Feed", Transport = "FileUpload", Status = "Active", PayloadKeyRef = "feed-key" }, CancellationToken.None);
        var notAKey = await service.CreateAsync(new IntegrationInput { ConnectorId = created.Id.Value, DisplayName = "Feed 2", Transport = "FileUpload", Status = "Active", PayloadKeyRef = "-----BEGIN PGP PRIVATE KEY BLOCK-----" }, CancellationToken.None);
        var plainWithKey = await service.CreateAsync(new IntegrationInput { ConnectorId = ConnectorId("generic-csv"), DisplayName = "Plain", Transport = "FileUpload", Status = "Active", PayloadKeyRef = "feed-key" }, CancellationToken.None);

        Assert.Contains(nameof(IntegrationInput.PayloadKeyRef), withoutKey.Errors.Keys);
        Assert.True(withKey.Succeeded, string.Join(" ", withKey.Errors.Values));
        Assert.Contains(nameof(IntegrationInput.PayloadKeyRef), notAKey.Errors.Keys);
        Assert.Contains(nameof(IntegrationInput.PayloadKeyRef), plainWithKey.Errors.Keys);
        Assert.Equal("feed-key", (await service.GetInputAsync(withKey.Id!.Value, CancellationToken.None))!.PayloadKeyRef);
    }

    [Fact]
    public void AConnectorWhoseEncryptionHasNoDecryptor_IsNotUsable()
    {
        var connector = new AttendanceConnectorDefinition { ConnectorKey = "enc", IsActive = true, EncryptionKind = PayloadEncryptionKind.Pgp, DefaultPayloadFormatId = 1 };
        var empty = new PayloadDecryptorCatalog([]);

        Assert.False(ConnectorUsability.IsUsable(connector, true, new StubAdapterCatalog()));
        Assert.False(ConnectorUsability.IsUsable(connector, true, new StubAdapterCatalog(), empty));
        Assert.True(ConnectorUsability.IsUsable(connector, true, new StubAdapterCatalog(), Catalog()));
    }

    private static IPayloadDecryptorCatalog Catalog() =>
        new PayloadDecryptorCatalog([new AesPayloadDecryptor(), new PgpPayloadDecryptor(), new PasswordZipPayloadDecryptor()]);

    private static byte[] Aes256(string mode, byte[] key, byte[] ivOrNonce, byte[] plain)
    {
        if (mode == "cbc")
        {
            using var aes = Aes.Create();
            aes.Key = key;
            return aes.EncryptCbc(plain, ivOrNonce, PaddingMode.PKCS7);
        }

        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(ivOrNonce, plain, cipher, tag);
        return [.. cipher, .. tag];
    }

    private static void WriteZip(string path, string entryName, string content, string password)
    {
        using var stream = File.Create(path);
        using var zip = new ZipOutputStream(stream) { Password = password };
        zip.PutNextEntry(new ZipEntry(entryName));
        zip.Write(Encoding.UTF8.GetBytes(content));
        zip.CloseEntry();
    }

    private static (PgpPublicKey Public, string ArmoredPrivate) NewPgpKey(string passphrase)
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new RsaKeyGenerationParameters(BigInteger.ValueOf(0x10001), new SecureRandom(), 2048, 12));
        var pair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, generator.GenerateKeyPair(), DateTime.UtcNow);
        var ringGenerator = new PgpKeyRingGenerator(PgpSignature.PositiveCertification, pair, "tester <tester@example.test>", SymmetricKeyAlgorithmTag.Aes256, passphrase.ToCharArray(), true, null, null, new SecureRandom());
        var secretRing = ringGenerator.GenerateSecretKeyRing();

        using var buffer = new MemoryStream();
        using (var armored = new ArmoredOutputStream(buffer))
        {
            secretRing.Encode(armored);
        }

        return (ringGenerator.GeneratePublicKeyRing().GetPublicKey(), Encoding.ASCII.GetString(buffer.ToArray()));
    }

    private static byte[] PgpEncrypt(PgpPublicKey publicKey, byte[] plain)
    {
        using var output = new MemoryStream();
        var generator = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Aes256, true, new SecureRandom());
        generator.AddMethod(publicKey);
        using (var encrypted = generator.Open(output, new byte[1 << 16]))
        {
            var literal = new PgpLiteralDataGenerator();
            using var message = literal.Open(encrypted, PgpLiteralData.Binary, "attendance.csv", plain.Length, DateTime.UtcNow);
            message.Write(plain, 0, plain.Length);
        }

        return output.ToArray();
    }

    private IngestionService CreateIngestion(RobeemDbContext db, ISecretStore secrets)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>()
            .AddSingleton(Catalog())
            .AddSingleton(secrets)
            .BuildServiceProvider();
        return new IngestionService(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), services);
    }

    private int AddIntegration(PayloadEncryptionKind kind, string keyRef)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var connector = AttendanceTestData.Connector("encrypted-csv", "Encrypted CSV");
        connector.EncryptionKind = kind;
        connector.DefaultPayloadFormat = db.AttendancePayloadFormats.Single(f => f.Name == "Generic CSV");
        var integration = new AttendanceIntegration { Connector = connector, DisplayName = "Encrypted uploads", PayloadKeyRef = keyRef };
        db.AttendanceDevices.Add(new AttendanceDevice { Integration = integration, ExternalDeviceId = "DEV-1", Name = "Front door", TimeZoneId = "UTC" });
        db.SaveChanges();
        return integration.Id;
    }

    private AttendanceConnectorService Connectors(IPayloadDecryptorCatalog? decryptors)
    {
        var db = _database.CreateContext(TestDatabase.TenantA);
        return new AttendanceConnectorService(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), new StubAdapterCatalog(), decryptors);
    }

    private int VendorId(string key)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceVendors.Single(v => v.VendorKey == key).Id;
    }

    private int ConnectorId(string key)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceConnectorDefinitions.Single(c => c.ConnectorKey == key).Id;
    }

    private int FormatId(string name)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.AttendancePayloadFormats.Single(f => f.Name == name).Id;
    }
}
