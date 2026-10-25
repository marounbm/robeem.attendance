using ICSharpCode.SharpZipLib.Zip;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Decryption;

/// <summary>
/// Opens a password-protected zip archive (ZipCrypto or AES) and writes out the one file it holds. The secret is the
/// password. An archive with more than one file, or a file larger than the decryption limit, is refused. A password-protected
/// Office workbook is not a zip archive and is not read by this decryptor.
/// </summary>
public sealed class PasswordZipPayloadDecryptor : IPayloadDecryptor
{
    public PayloadEncryptionKind Kind => PayloadEncryptionKind.PasswordProtected;

    public async Task DecryptAsync(string inputPath, string outputPath, string secret, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = new ZipFile(inputPath) { Password = secret };
            var files = archive.Cast<ZipEntry>().Where(e => e.IsFile).ToList();
            if (files.Count != 1)
            {
                throw new InvalidDataException(files.Count == 0 ? "The archive holds no file." : "The archive holds more than one file; it must hold exactly one.");
            }

            var entry = files[0];
            if (entry.Size > DecryptionLimits.MaxPlainBytes)
            {
                throw new InvalidDataException($"The decrypted file is larger than {DecryptionLimits.MaxPlainBytes / (1024 * 1024)} MB.");
            }

            await using var input = archive.GetInputStream(entry);
            await using var output = File.Create(outputPath);
            await DecryptionLimits.CopyBoundedAsync(input, output, cancellationToken);
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
            // A wrong password surfaces as a zip, CRC or format error whose text is of no use to the caller.
            throw DecryptionLimits.CannotDecrypt("a password-protected zip archive");
        }
    }
}
