namespace robeem.Infrastructure.Attendance.Decryption;

/// <summary>What every decryptor shares: a ceiling on the plain output, and the one error it may raise.</summary>
internal static class DecryptionLimits
{
    /// <summary>The most a decrypted file may hold; a compressed or crafted input can't grow past it.</summary>
    public const long MaxPlainBytes = 100L * 1024 * 1024;

    /// <summary>The error for any file that the key does not open. It names no key material and carries no inner message.</summary>
    public static InvalidDataException CannotDecrypt(string what) =>
        new($"The file could not be decrypted as {what} with the configured key. Check the key, and that the file is encrypted that way.");

    public static async Task CopyBoundedAsync(Stream from, Stream to, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxPlainBytes)
            {
                throw new InvalidDataException($"The decrypted file is larger than {MaxPlainBytes / (1024 * 1024)} MB.");
            }

            await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
