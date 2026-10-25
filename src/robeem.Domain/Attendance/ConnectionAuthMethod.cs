namespace robeem.DataModel
{
    /// <summary>How a connection proves itself to, or is trusted by, the other end. The credential is never stored with the connection: it names a secret by reference.</summary>
    public enum ConnectionAuthMethod
    {
        /// <summary>Nothing to prove: people upload files.</summary>
        None,

        /// <summary>A user name and password (the secret is JSON with both, or the password alone).</summary>
        UsernamePassword,

        /// <summary>An SSH private key (SFTP).</summary>
        SshKey,

        /// <summary>A static API key sent with each call.</summary>
        ApiKey,

        /// <summary>A bearer token sent with each call.</summary>
        BearerToken,

        /// <summary>OAuth 2 client credentials: the secret holds the client id and secret.</summary>
        OAuth2ClientCredentials,

        /// <summary>A login call that returns a session (BioStar).</summary>
        SessionLogin,

        /// <summary>An HMAC signature of the request body, checked with a shared secret (webhooks).</summary>
        HmacSignature,

        /// <summary>A token that identifies an on-premises agent (device SDK).</summary>
        AgentToken,
    }
}
