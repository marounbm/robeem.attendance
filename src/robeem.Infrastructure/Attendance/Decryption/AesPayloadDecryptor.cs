using System.Security.Cryptography;
using System.Text.Json;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Decryption;

/// <summary>
/// Decrypts AES-CBC (PKCS#7) or AES-GCM bytes. The secret is a JSON object:
/// <c>{"key":"&lt;base64, 16, 24 or 32 bytes&gt;","iv":"&lt;base64, optional&gt;","mode":"cbc"|"gcm"}</c>. Without an
/// <c>iv</c> the file starts with it (16 bytes for CBC, a 12-byte nonce for GCM); GCM's 16-byte tag is the file's last 16 bytes.
/// The mode defaults to CBC. Vendor variants are therefore configuration, not code.
/// </summary>
public sealed class AesPayloadDecryptor : IPayloadDecryptor
{
    private const int CbcIvBytes = 16;
    private const int GcmNonceBytes = 12;
    private const int GcmTagBytes = 16;

    public PayloadEncryptionKind Kind => PayloadEncryptionKind.Aes;

    public async Task DecryptAsync(string inputPath, string outputPath, string secret, CancellationToken cancellationToken)
    {
        var (key, iv, gcm) = ParseSecret(secret);
        if (new FileInfo(inputPath).Length > DecryptionLimits.MaxPlainBytes)
        {
            throw new InvalidDataException($"The file is larger than {DecryptionLimits.MaxPlainBytes / (1024 * 1024)} MB.");
        }

        var data = await File.ReadAllBytesAsync(inputPath, cancellationToken);
        try
        {
            var plain = gcm ? DecryptGcm(key, iv, data) : DecryptCbc(key, iv, data);
            await File.WriteAllBytesAsync(outputPath, plain, cancellationToken);
        }
        catch (CryptographicException)
        {
            throw DecryptionLimits.CannotDecrypt("AES");
        }
        catch (ArgumentException)
        {
            throw DecryptionLimits.CannotDecrypt("AES");
        }
    }

    private static byte[] DecryptCbc(byte[] key, byte[]? iv, byte[] data)
    {
        var ivBytes = iv ?? (data.Length >= CbcIvBytes ? data[..CbcIvBytes] : throw DecryptionLimits.CannotDecrypt("AES"));
        var cipher = iv is null ? data[CbcIvBytes..] : data;
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(cipher, ivBytes, PaddingMode.PKCS7);
    }

    private static byte[] DecryptGcm(byte[] key, byte[]? iv, byte[] data)
    {
        var nonce = iv ?? (data.Length >= GcmNonceBytes ? data[..GcmNonceBytes] : throw DecryptionLimits.CannotDecrypt("AES"));
        var body = iv is null ? data[GcmNonceBytes..] : data;
        if (body.Length < GcmTagBytes)
        {
            throw DecryptionLimits.CannotDecrypt("AES");
        }

        var cipher = body[..^GcmTagBytes];
        var tag = body[^GcmTagBytes..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, GcmTagBytes);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static (byte[] Key, byte[]? Iv, bool Gcm) ParseSecret(string secret)
    {
        try
        {
            using var document = JsonDocument.Parse(secret);
            var root = document.RootElement;
            var key = Convert.FromBase64String(root.GetProperty("key").GetString() ?? string.Empty);
            if (key.Length is not (16 or 24 or 32))
            {
                throw new FormatException();
            }

            var iv = root.TryGetProperty("iv", out var ivElement) && ivElement.GetString() is { Length: > 0 } ivText ? Convert.FromBase64String(ivText) : null;
            var mode = root.TryGetProperty("mode", out var modeElement) ? modeElement.GetString() : null;
            if (mode is not (null or "" or "cbc" or "CBC" or "gcm" or "GCM"))
            {
                throw new FormatException();
            }

            var gcm = string.Equals(mode, "gcm", StringComparison.OrdinalIgnoreCase);
            if (iv is not null && iv.Length != (gcm ? GcmNonceBytes : CbcIvBytes))
            {
                throw new FormatException();
            }

            return (key, iv, gcm);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            // The secret itself must not reach the message.
            throw new InvalidDataException("The AES key secret must be JSON like {\"key\":\"<base64 of 16, 24 or 32 bytes>\",\"iv\":\"<base64, optional>\",\"mode\":\"cbc\" or \"gcm\"}.");
        }
    }
}
