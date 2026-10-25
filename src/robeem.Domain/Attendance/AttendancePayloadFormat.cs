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
    /// set to manual upload may use one instead of its connector's default layout.
    /// </summary>
    [Serializable]
    [Table(nameof(AttendancePayloadFormat), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendancePayloadFormat : Entity, ISeedData, ITenantScoped
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

        /// <summary>
        /// Gets or sets the version of the format, from 1. A change to how a file is read, made once punches have been read by
        /// the format, is a new row with the next version (<see cref="SupersedesId"/> names the one it replaces), so a punch
        /// that is read again is read the way it was first read. Only the latest version of a name is offered or edited.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>Gets or sets the older version this one replaces; null for the first version.</summary>
        public int? SupersedesId { get; set; }

        public AttendancePayloadFormat? Supersedes { get; set; }

        /// <summary>
        /// Gets or sets the connector this format is made for, which groups it on the connector's screen. Null: a shared
        /// format, such as Generic CSV, that no connector owns. Any integration may choose any active format.
        /// </summary>
        public int? ConnectorId { get; set; }

        public AttendanceConnectorDefinition? Connector { get; set; }

        public AttendancePayloadKind PayloadKind { get; set; } = AttendancePayloadKind.Auto;

        /// <summary>Gets or sets the field delimiter of a text file: one of <c>,</c> <c>;</c> <c>|</c> or a tab.</summary>
        [Required]
        [MaxLength(1)]
        public string Delimiter { get; set; } = ",";

        public bool HasHeaderRow { get; set; } = true;

        /// <summary>Gets or sets the 1-based sheet of an Excel workbook that holds the punches.</summary>
        public int SheetNumber { get; set; } = 1;

        /// <summary>
        /// Gets or sets, for a JSON or XML payload, the path to the repeating record: dot-separated names, with <c>[*]</c> over
        /// an array in JSON (<c>data.punches[*]</c>), or element names from the root in XML (<c>Punches/Punch</c>). Empty: a JSON
        /// document that is an array, or an XML document whose root's children are the records. The column settings below are
        /// then paths relative to a record (<c>employee.id</c>, <c>@code</c> for an XML attribute).
        /// </summary>
        [MaxLength(255)]
        public string RecordPath { get; set; } = string.Empty;

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
        /// Gets or sets the patterns the <see cref="DateColumn"/> may be written in, separated by <c>;</c> (e.g. <c>dd/MM/yyyy;yyyy-MM-dd</c>).
        /// With <see cref="TimeFormats"/> this replaces <see cref="TimestampFormats"/> for a date column and a time column: the
        /// text read is <c>date time</c>, tried against every date pattern followed by every time pattern. Empty keeps the old
        /// behaviour of <see cref="TimestampFormats"/> describing the joined text.
        /// </summary>
        [MaxLength(1000)]
        public string DateFormats { get; set; } = string.Empty;

        /// <summary>Gets or sets the patterns the <see cref="TimeColumn"/> may be written in, separated by <c>;</c> (e.g. <c>HH:mm;HH:mm:ss</c>).</summary>
        [MaxLength(1000)]
        public string TimeFormats { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the culture whose month and day names the patterns read (e.g. <c>fr-FR</c>, <c>ar-LB</c>). Empty is the
        /// invariant culture. Always read with the Gregorian calendar.
        /// </summary>
        [MaxLength(16)]
        public string TimestampCulture { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the headers of the only columns kept in the raw payload, separated by <c>|</c>. Empty keeps every
        /// column (the shared sanitizer still removes biometric-looking ones). Needs a header row.
        /// </summary>
        [MaxLength(1000)]
        public string PayloadColumns { get; set; } = string.Empty;

        public ICollection<AttendancePayloadEventCode> EventCodes { get; set; } = new List<AttendancePayloadEventCode>();

        #region not mapped
        [NotMapped]
        public override string Title => Name;
        #endregion
    }
}
