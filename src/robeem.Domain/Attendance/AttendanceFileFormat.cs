using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A file layout an administrator defines: how the columns of an uploaded file map onto a punch,
    /// how its timestamps are written and what its type labels mean. An <see cref="AttendanceIntegration"/>
    /// set to manual upload may use one instead of its provider's own layout.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendanceFileFormat), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceFileFormat : Entity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the format's name. Unique per tenant.</summary>
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        public AttendanceFileKind FileKind { get; set; } = AttendanceFileKind.Auto;

        /// <summary>Gets or sets the field delimiter of a text file: one of <c>,</c> <c>;</c> <c>|</c> or a tab.</summary>
        [Required]
        [MaxLength(1)]
        public string Delimiter { get; set; } = ",";

        public bool HasHeaderRow { get; set; } = true;

        /// <summary>Gets or sets the 1-based sheet of an Excel workbook that holds the punches.</summary>
        public int SheetNumber { get; set; } = 1;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Gets or sets the column that holds the vendor's employee id: its header, or its 1-based number when the file has no header row.
        /// With a header row, several headers may be listed, separated by <c>|</c>: the first non-empty value among the ones the file has is used,
        /// and a file with none of them leaves the field empty. The same holds for every column below.
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string EmployeeColumn { get; set; } = string.Empty;

        /// <summary>Gets or sets the column with the whole timestamp. Empty when <see cref="DateColumn"/> and <see cref="TimeColumn"/> are used.</summary>
        [MaxLength(255)]
        public string TimestampColumn { get; set; } = string.Empty;

        [MaxLength(255)]
        public string DateColumn { get; set; } = string.Empty;

        [MaxLength(255)]
        public string TimeColumn { get; set; } = string.Empty;

        [MaxLength(255)]
        public string TypeColumn { get; set; } = string.Empty;

        [MaxLength(255)]
        public string DirectionColumn { get; set; } = string.Empty;

        [MaxLength(255)]
        public string DeviceColumn { get; set; } = string.Empty;

        [MaxLength(255)]
        public string TransactionIdColumn { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the .NET date-time patterns the timestamp may be written in, separated by <c>;</c>
        /// (e.g. <c>dd/MM/yyyy HH:mm;dd/MM/yyyy HH:mm:ss</c>). A trailing <c>Z</c> or <c>+hh:mm</c> offset is also accepted.
        /// </summary>
        [Required]
        [MaxLength(1000)]
        public string TimestampFormats { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the headers of the only columns kept in the raw payload, separated by <c>|</c>. Empty keeps every
        /// column (the shared sanitizer still removes biometric-looking ones). Needs a header row.
        /// </summary>
        [MaxLength(1000)]
        public string PayloadColumns { get; set; } = string.Empty;

        public ICollection<AttendanceFileFormatEventCode> EventCodes { get; set; } = new List<AttendanceFileFormatEventCode>();

        #region not mapped
        [NotMapped]
        public override string Title => Name;
        #endregion
    }
}
