using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Bcpg.OpenPgp;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Decryption;

/// <summary>
/// Decrypts an OpenPGP message (binary or ASCII-armored) with a private key. The secret is the armored private key block, or
/// JSON <c>{"privateKey":"-----BEGIN PGP PRIVATE KEY BLOCK-----...","passphrase":"..."}</c> when the key has a passphrase. A
/// compressed message is expanded; a message with an integrity packet is verified. Signatures are not checked.
/// </summary>
public sealed class PgpPayloadDecryptor : IPayloadDecryptor
{
    public PayloadEncryptionKind Kind => PayloadEncryptionKind.Pgp;

    public async Task DecryptAsync(string inputPath, string outputPath, string secret, CancellationToken cancellationToken)
    {
        var (armoredKey, passphrase) = ParseSecret(secret);
        try
        {
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(armoredKey));
            var keys = new PgpSecretKeyRingBundle(PgpUtilities.GetDecoderStream(keyStream));

            await using var input = File.OpenRead(inputPath);
            var factory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(input));
            var first = factory.NextPgpObject();
            var encrypted = first as PgpEncryptedDataList ?? factory.NextPgpObject() as PgpEncryptedDataList
                ?? throw DecryptionLimits.CannotDecrypt("an OpenPGP message");

            PgpPrivateKey? privateKey = null;
            PgpPublicKeyEncryptedData? data = null;
            foreach (var candidate in encrypted.GetEncryptedDataObjects().OfType<PgpPublicKeyEncryptedData>())
            {
                if (keys.GetSecretKey(candidate.KeyId) is { } secretKey)
                {
                    privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());
                    data = candidate;
                    break;
                }
            }

            if (privateKey is null || data is null)
            {
                throw DecryptionLimits.CannotDecrypt("an OpenPGP message");
            }

            var plainFactory = new PgpObjectFactory(data.GetDataStream(privateKey));
            var message = plainFactory.NextPgpObject();
            if (message is PgpCompressedData compressed)
            {
                message = new PgpObjectFactory(compressed.GetDataStream()).NextPgpObject();
            }

            if (message is not PgpLiteralData literal)
            {
                throw DecryptionLimits.CannotDecrypt("an OpenPGP message");
            }

            await using (var output = File.Create(outputPath))
            {
                await DecryptionLimits.CopyBoundedAsync(literal.GetInputStream(), output, cancellationToken);
            }

            if (data.IsIntegrityProtected() && !data.Verify())
            {
                throw DecryptionLimits.CannotDecrypt("an OpenPGP message");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // BouncyCastle's messages can name key ids and packets; the caller gets the fixed one.
            throw DecryptionLimits.CannotDecrypt("an OpenPGP message");
        }
    }

    private static (string PrivateKey, string Passphrase) ParseSecret(string secret)
    {
        var trimmed = secret.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            return (secret, string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(secret);
            var root = document.RootElement;
            var key = root.GetProperty("privateKey").GetString();
            var passphrase = root.TryGetProperty("passphrase", out var element) ? element.GetString() : null;
            return key is { Length: > 0 } ? (key, passphrase ?? string.Empty) : throw new FormatException();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("The PGP key secret must be the armored private key, or JSON like {\"privateKey\":\"-----BEGIN PGP PRIVATE KEY BLOCK-----...\",\"passphrase\":\"...\"}.");
        }
    }
}
