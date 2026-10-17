using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>What one label in a file's type or direction column means, in an <see cref="AttendanceFileFormat"/>.</summary>
    [Serializable]
    [Table(nameof(AttendanceFileFormatEventCode), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceFileFormatEventCode : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int FileFormatId { get; set; }

        public AttendanceFileFormat? FileFormat { get; set; }

        /// <summary>Gets or sets the label as written in the file. Matched ignoring case, spaces and punctuation.</summary>
        [Required]
        [MaxLength(100)]
        public string Code { get; set; } = string.Empty;

        public NormalizedEventType EventType { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => Code;
        #endregion
    }
}
