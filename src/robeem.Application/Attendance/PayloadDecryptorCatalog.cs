using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class PayloadDecryptorCatalog : IPayloadDecryptorCatalog
{
    private readonly Dictionary<PayloadEncryptionKind, IPayloadDecryptor> _decryptors;

    public PayloadDecryptorCatalog(IEnumerable<IPayloadDecryptor> decryptors) =>
        _decryptors = decryptors.ToDictionary(d => d.Kind);

    public bool CanRead(PayloadEncryptionKind kind) => kind == PayloadEncryptionKind.None || _decryptors.ContainsKey(kind);

    public IPayloadDecryptor? Find(PayloadEncryptionKind kind) => _decryptors.GetValueOrDefault(kind);
}
