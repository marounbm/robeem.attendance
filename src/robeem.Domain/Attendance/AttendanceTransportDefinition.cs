using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// What the tenant calls one of the six ways attendance reaches robeem (<see cref="AttendanceTransport"/>),
    /// and what is true of it: whether robeem pulls on a schedule, and whether a file format applies. The
    /// behaviour of a transport is code, so a row exists only for a member of the enum and is never added
    /// from a screen; it can be relabeled or switched off.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceTransportDefinition), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceTransportDefinition : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the name of the <see cref="AttendanceTransport"/> member this row describes. Unique per tenant.</summary>
        [Required]
        [MaxLength(32)]
        public string TransportKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether robeem fetches the attendance itself, on a schedule (an integration with this transport has a polling schedule).</summary>
        public bool IsPull { get; set; }

        /// <summary>Gets or sets a value indicating whether the attendance arrives as a file read by a file format.</summary>
        public bool UsesFileFormat { get; set; }

        /// <summary>Gets or sets a value indicating whether a new integration can choose this transport. An integration that has it keeps it.</summary>
        public bool IsActive { get; set; } = true;

        #region not mapped
        [NotMapped]
        public override string Title => DisplayName;
        #endregion
    }
}
