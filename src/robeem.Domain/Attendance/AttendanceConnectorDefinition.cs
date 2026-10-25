using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// One software or protocol a vendor offers (the vendor's desktop export, its REST API, its push protocol). A vendor has
    /// many connectors; an <see cref="AttendanceIntegration"/> is made with one. The connector says how data arrives
    /// (transports), what reads it (a code adapter, or a payload format) and whether it is encrypted. Named a connector
    /// rather than a profile because <c>Profile</c> is robeem's employee entity.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceConnectorDefinition), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceConnectorDefinition : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int VendorId { get; set; }

        public AttendanceVendor? Vendor { get; set; }

        /// <summary>Gets or sets the stable slug of the connector, e.g. <c>zkteco-export</c>. Unique per tenant; never changed once saved.</summary>
        [Required]
        [MaxLength(100)]
        public string ConnectorKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string DisplayName { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the key a code adapter and its event interpreter are registered under, when something in code reads
        /// this connector's data (a pull or a push). Null: the connector is read by payload formats alone, and needs no code.
        /// </summary>
        [MaxLength(100)]
        public string? AdapterKey { get; set; }

        /// <summary>Gets or sets a value indicating whether the source can be asked only for what changed after a cursor.</summary>
        public bool SupportsIncrementalSync { get; set; }

        /// <summary>
        /// Gets or sets the payload format an upload is read by when its integration names none. Null: the connector
        /// has no layout of its own, so an upload needs a format chosen on the integration.
        /// </summary>
        public int? DefaultPayloadFormatId { get; set; }

        public AttendancePayloadFormat? DefaultPayloadFormat { get; set; }

        public PayloadEncryptionKind EncryptionKind { get; set; } = PayloadEncryptionKind.None;

        public bool IsActive { get; set; } = true;

        /// <summary>Gets or sets the transports the connector supports, besides file upload, which any connector can be given.</summary>
        public ICollection<AttendanceConnectorTransport> Transports { get; set; } = new List<AttendanceConnectorTransport>();

        #region not mapped
        [NotMapped]
        public override string Title => DisplayName;
        #endregion
    }
}
