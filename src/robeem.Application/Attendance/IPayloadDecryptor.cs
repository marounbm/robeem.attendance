using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Decrypts a file a connector delivers encrypted, before the payload format reads it. One implementation per
/// <see cref="PayloadEncryptionKind"/> other than None. The key material is a secret resolved for the one call; an
/// implementation never logs it, never puts it in an exception message and keeps no copy.
/// </summary>
public interface IPayloadDecryptor
{
    /// <summary>Gets the kind of encryption this decryptor reads.</summary>
    PayloadEncryptionKind Kind { get; }

    /// <summary>
    /// Writes the decrypted bytes of <paramref name="inputPath"/> to <paramref name="outputPath"/>. Throws
    /// <see cref="InvalidDataException"/>, with a message that names no key material, when the file can't be decrypted by
    /// <paramref name="secret"/>.
    /// </summary>
    Task DecryptAsync(string inputPath, string outputPath, string secret, CancellationToken cancellationToken);
}
