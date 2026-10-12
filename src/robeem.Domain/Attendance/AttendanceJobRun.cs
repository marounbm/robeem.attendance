using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// One run of a background job for one tenant, with how it ended. A run is written as
    /// <see cref="JobRunStatus.Running"/> and settled when it ends, so a row left Running by a
    /// restart is known to have been interrupted. Quiet ticks of the frequent jobs are not stored.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceJobRun), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceJobRun : SystemEntity, ITenantScoped
    {
        public const int SummaryMaxLength = 2000;

        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the job's name, e.g. <c>attendance-normalization</c>.</summary>
        [Required]
        [MaxLength(100)]
        public string JobName { get; set; } = string.Empty;

        public JobTrigger Trigger { get; set; } = JobTrigger.Scheduled;

        public JobRunStatus Status { get; set; } = JobRunStatus.Running;

        /// <summary>Gets or sets the time the run was due: the cron occurrence of a cron job, including a missed one. Null for a tick of an interval job and for a manual run.</summary>
        public DateTime? ScheduledFor { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.Now;

        public DateTime? CompletedAt { get; set; }

        /// <summary>Gets or sets how much the run did (rows normalized, integrations synced, ...); what it counts depends on the job.</summary>
        public int WorkItems { get; set; }

        /// <summary>Gets or sets what the run reports, or why it failed; never a stack trace or a secret.</summary>
        [MaxLength(SummaryMaxLength)]
        public string? Summary { get; set; }

        /// <summary>Gets or sets who started a manual run.</summary>
        public int? RequestedById { get; set; }

        public Profile? RequestedBy { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => PartitionName;
        #endregion
    }
}
