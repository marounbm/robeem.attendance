namespace robeem.DataModel
{
    /// <summary>What kind of file an <see cref="AttendanceFileFormat"/> describes.</summary>
    public enum AttendanceFileKind
    {
        /// <summary>Text (CSV) or an Excel sheet, told by the file's content.</summary>
        Auto,

        /// <summary>A delimited text file.</summary>
        Csv,

        /// <summary>An Excel workbook (XLS or XLSX); the sheet is <see cref="AttendanceFileFormat.SheetNumber"/>.</summary>
        Excel,
    }
}
