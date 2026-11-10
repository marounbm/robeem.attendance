using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// The rules that decide how breaks are read and which findings become exceptions, for one attendance policy's scope: one time
    /// schedule, or the whole tenant when <see cref="TimeScheduleId"/> is null. A published set is never edited: a change is a new
    /// draft that is published as the next <see cref="Version"/>. With no published set, the rules are derived from the policy.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceRuleSet), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceRuleSet : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the schedule the rules are for; null for the tenant-wide set.</summary>
        public int? TimeScheduleId { get; set; }

        public TimeSchedule? TimeSchedule { get; set; }

        /// <summary>
        /// Gets or sets the number of this publication. It is taken from one sequence for the whole tenant, so a bigger number is always a later
        /// publication; 0 is "derived from the policy". A draft has none until it is published.
        /// </summary>
        public int Version { get; set; }

        public RuleSetStatus Status { get; set; } = RuleSetStatus.Draft;

        /// <summary>Gets or sets when the set was published, as local wall-clock time.</summary>
        public DateTime? PublishedAt { get; set; }

        /// <summary>Gets or sets who published it (an employee id); null when the system did, or while a draft.</summary>
        public int? PublishedById { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public ICollection<AttendanceRule> Rules { get; set; } = new List<AttendanceRule>();
    }
}
