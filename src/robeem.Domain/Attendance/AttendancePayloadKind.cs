namespace robeem.DataModel
{
    /// <summary>What kind of file an <see cref="AttendancePayloadFormat"/> describes.</summary>
    public enum AttendancePayloadKind
    {
        /// <summary>Text (CSV) or an Excel sheet, told by the file's content.</summary>
        Auto,

        /// <summary>A delimited text file.</summary>
        Csv,

        /// <summary>An Excel workbook (XLS or XLSX); the sheet is <see cref="AttendancePayloadFormat.SheetNumber"/>.</summary>
        Excel,

        /// <summary>A JSON document; <see cref="AttendancePayloadFormat.RecordPath"/> names the array of punches.</summary>
        Json,

        /// <summary>An XML document; <see cref="AttendancePayloadFormat.RecordPath"/> names the repeating punch element.</summary>
        Xml,
    }
}
