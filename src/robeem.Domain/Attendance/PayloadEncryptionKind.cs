namespace robeem.DataModel
{
    /// <summary>How the payload a connector delivers is encrypted, if it is. The key itself is never stored with the connector: a connection names it by reference.</summary>
    public enum PayloadEncryptionKind
    {
        /// <summary>The payload is plain.</summary>
        None,

        /// <summary>An OpenPGP message.</summary>
        Pgp,

        /// <summary>AES-encrypted bytes; the key and IV come from the connection's key reference.</summary>
        Aes,

        /// <summary>A password-protected archive or workbook.</summary>
        PasswordProtected,
    }
}
