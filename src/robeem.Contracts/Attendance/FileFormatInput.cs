using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>What an integration administrator sets when defining a custom file format.</summary>
public sealed class FileFormatInput
{
    [Required]
    [StringLength(255)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the kind of file: Auto, Csv or Excel.</summary>
    [Display(Name = "File kind")]
    public string FileKind { get; set; } = "Auto";

    /// <summary>Gets or sets the field delimiter of a text file: <c>,</c> <c>;</c> <c>|</c> or a tab.</summary>
    [Display(Name = "Delimiter")]
    public string Delimiter { get; set; } = ",";

    [Display(Name = "The file has a header row")]
    public bool HasHeaderRow { get; set; } = true;

    [Display(Name = "Excel sheet number")]
    public int SheetNumber { get; set; } = 1;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets the column of the vendor's employee id: its header, or its 1-based number when there is no header row.</summary>
    [Display(Name = "Employee id column")]
    public string? EmployeeColumn { get; set; }

    [Display(Name = "Timestamp column")]
    public string? TimestampColumn { get; set; }

    [Display(Name = "Date column")]
    public string? DateColumn { get; set; }

    [Display(Name = "Time column")]
    public string? TimeColumn { get; set; }

    [Display(Name = "Type column")]
    public string? TypeColumn { get; set; }

    [Display(Name = "Direction column")]
    public string? DirectionColumn { get; set; }

    [Display(Name = "Device column")]
    public string? DeviceColumn { get; set; }

    [Display(Name = "Transaction id column")]
    public string? TransactionIdColumn { get; set; }

    /// <summary>Gets or sets the .NET date-time patterns the timestamp is written in, one per line or separated by <c>;</c>.</summary>
    [Display(Name = "Timestamp patterns")]
    public string? TimestampFormats { get; set; }

    public List<FileFormatEventCodeInput> EventCodes { get; set; } = [];
}
