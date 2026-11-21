using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// The related exceptions of one employee's shift on one date, kept together as a case a person settles in one step. The exceptions
    /// stay as they are (each keeps its own status and resolution, and the engine still works on them); the group is the unit the Exceptions
    /// screen lists and the workflow acts on: it waits for a correction, is accepted, or is closed as a whole.
    /// </summary>
    /// <remarks>
    /// A group is a case, not a permanent bucket: an employee, date and shift have at most one group that is not closed. A closed group stays
    /// as history, and a problem found after it closed opens a new group. The group's status follows its exceptions, except that accepting
    /// the group is its own decision (who, when and why are kept here) and closes every exception in it.
    /// </remarks>
    [Serializable]
    [Table(nameof(AttendanceExceptionGroup), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceExceptionGroup : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        /// <summary>Gets or sets the date of the shift the group belongs to; for an overnight shift, the date it starts on.</summary>
        public DateTime AttendanceDate { get; set; }

        /// <summary>Gets or sets the scheduled shift; null for work outside any scheduled shift.</summary>
        public int? TimeScheduleShiftId { get; set; }

        public TimeScheduleShift? TimeScheduleShift { get; set; }

        /// <summary>Gets or sets where the group stands: Open while any exception in it is open, else PendingCorrection while one waits for a correction, else Closed.</summary>
        public AttendanceExceptionStatus Status { get; set; } = AttendanceExceptionStatus.Open;

        /// <summary>Gets or sets when the first exception of the group was raised, as local wall-clock time.</summary>
        public DateTime DetectedAt { get; set; } = DateTime.Now;

        /// <summary>Gets or sets when the group was closed; null while it is not.</summary>
        public DateTime? ResolvedAt { get; set; }

        /// <summary>Gets or sets how the group was closed; null while it is not.</summary>
        public AttendanceExceptionResolutionMethod? ResolutionMethod { get; set; }

        /// <summary>Gets or sets who accepted the group (an employee id); null unless it was closed as <see cref="AttendanceExceptionResolutionMethod.Accepted"/>.</summary>
        public int? ResolvedById { get; set; }

        /// <summary>Gets or sets the reason given when the group was accepted; null otherwise.</summary>
        [MaxLength(1000)]
        public string? ResolutionNote { get; set; }

        #region not mapped
        [NotMapped]
        public bool IsActive => Status != AttendanceExceptionStatus.Closed;

        [NotMapped]
        public override string Title => $"Exceptions — {AttendanceDate:yyyy-MM-dd}";
        #endregion
    }
}
