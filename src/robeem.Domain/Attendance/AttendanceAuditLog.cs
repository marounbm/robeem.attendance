using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// One line of the attendance audit trail: who did what to which record, or looked at which
    /// data. Insert-only: a row is never changed or deleted.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceAuditLog), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceAuditLog : SystemEntity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets who acted: <c>employee:{id}</c> (a <c>Profile</c>), or <c>system</c> for background work.</summary>
        [Required]
        [MaxLength(100)]
        public string Actor { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the type of entity the action concerns; several, comma-separated, for a read
        /// across tables. (Named for its role: <c>EntityType</c> is a member of <c>Entity</c>.)
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string TargetType { get; set; } = string.Empty;

        /// <summary>Gets or sets the id of the entity the action concerns; null for a read.</summary>
        public int? TargetId { get; set; }

        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        [MaxLength(2000)]
        public string? Details { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => PartitionName;
        #endregion
    }
}
