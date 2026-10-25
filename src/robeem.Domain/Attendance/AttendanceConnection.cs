using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A configured endpoint of an <see cref="AttendanceIntegration"/> and how it is authenticated: where attendance comes from
    /// (a file upload, an SFTP folder, a REST API, a webhook, a database, a device SDK agent) and what proves it. An integration
    /// has one default connection; a device may name another of the integration's connections instead.
    /// </summary>
    /// <remarks>
    /// Only references to secrets are stored (<see cref="SecretRef"/>); <see cref="Settings"/> holds the non-secret parameters the
    /// transport declares (host, port, path), as JSON. The last test result is runtime state, written by the test operation alone.
    /// </remarks>
    [Serializable]
    [Table(nameof(AttendanceConnection), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceConnection : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int IntegrationId { get; set; }

        public AttendanceIntegration? Integration { get; set; }

        /// <summary>Gets or sets the connection's name. Unique per integration.</summary>
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether this is the integration's default connection: the one its devices use unless they name another. Exactly one per integration.</summary>
        public bool IsDefault { get; set; }

        public AttendanceTransport Transport { get; set; } = AttendanceTransport.FileUpload;

        public ConnectionAuthMethod AuthMethod { get; set; } = ConnectionAuthMethod.None;

        /// <summary>Gets or sets the non-secret parameters of the transport as a JSON object of text values. Only keys the transport declares; never a credential.</summary>
        public string Settings { get; set; } = "{}";

        /// <summary>
        /// Gets or sets a reference (key) to the secret that authenticates the connection. Never a raw secret.
        /// Resolved by <c>ISecretStore</c> under <c>Secrets:{tenantId}:{reference}</c> (CLAUDE.md §2.8).
        /// </summary>
        [MaxLength(255)]
        public string SecretRef { get; set; } = string.Empty;

        /// <summary>Gets or sets the cron expression of a pulling transport: when to pull. Null or empty: never.</summary>
        [MaxLength(100)]
        public string? PollingSchedule { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? LastTestedAt { get; set; }

        public bool? LastTestSucceeded { get; set; }

        /// <summary>Gets or sets the outcome of the last test, in words the code chose: never an exception message and never a response body.</summary>
        [MaxLength(500)]
        public string? LastTestMessage { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => Name;
        #endregion
    }
}
