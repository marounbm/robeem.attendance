using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Which kinds of encrypted payload the server can decrypt, and the decryptor of each.</summary>
public interface IPayloadDecryptorCatalog
{
    /// <summary>Gets a value indicating whether a payload of <paramref name="kind"/> can be read: always for None, else when a decryptor is registered.</summary>
    bool CanRead(PayloadEncryptionKind kind);

    /// <summary>Gets the decryptor of <paramref name="kind"/>, or null for None or a kind with no decryptor.</summary>
    IPayloadDecryptor? Find(PayloadEncryptionKind kind);
}
