using System.Globalization;
using System.Text;
using ExcelDataReader;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Reads an uploaded attendance file as rows of text, whether it is a CSV, an Excel workbook
/// (<c>.xlsx</c>, or a <c>.xls</c> in an OLE2 container) or a bare BIFF stream, which is how the
/// ZKTeco software writes its <c>.xls</c> export. The format is recognised from the first bytes, not
/// the name: the Api hands the adapter an extension-less temporary file.
/// </summary>
internal static class TabularFile
{
    private const string DateCellFormat = "dd-MMM-yy HH:mm:ss";

    // BIFF record ids (the BIFF2 ones, then their BIFF3+ successors) that carry a cell's value.
    private const ushort RecordEof = 0x000A;
    private const ushort RecordInteger = 0x0002;
    private const ushort RecordNumber = 0x0003;
    private const ushort RecordLabel = 0x0004;
    private const ushort RecordNumberBiff3 = 0x0203;
    private const ushort RecordLabelBiff3 = 0x0204;

    private enum FileFormat
    {
        Csv,
        Workbook,
        BareBiff,
    }

    /// <summary>Reads the first sheet (or the whole CSV); a row with no text in any cell is skipped.</summary>
    public static async Task<List<List<string>>> ReadRowsAsync(string path, CancellationToken cancellationToken)
    {
        switch (await DetectAsync(path, cancellationToken))
        {
            case FileFormat.Workbook:
                return ReadWorkbook(path);
            case FileFormat.BareBiff:
                return ReadBareBiff(await File.ReadAllBytesAsync(path, cancellationToken));
            default:
                return CsvDocument.Parse(await CsvDocument.ReadFileAsync(path, cancellationToken));
        }
    }

    /// <summary>
    /// Reads a file as a custom format describes it: a text file split by <paramref name="delimiter"/>, or sheet
    /// <paramref name="sheetNumber"/> of a workbook. <paramref name="kind"/> Auto tells them apart by content;
    /// naming one that the content contradicts is an error rather than garbage rows.
    /// </summary>
    public static async Task<List<List<string>>> ReadRowsAsync(string path, AttendanceFileKind kind, char delimiter, int sheetNumber, CancellationToken cancellationToken)
    {
        var detected = await DetectAsync(path, cancellationToken);
        var isText = detected == FileFormat.Csv;
        if (kind == AttendanceFileKind.Excel && isText)
        {
            throw new InvalidDataException("The file is not an Excel workbook, which this format expects.");
        }

        if (kind == AttendanceFileKind.Csv && !isText)
        {
            throw new InvalidDataException("The file is an Excel workbook, but this format expects a text (CSV) file.");
        }

        return detected switch
        {
            FileFormat.Workbook => ReadWorkbook(path, sheetNumber),
            FileFormat.BareBiff => ReadBareBiff(await File.ReadAllBytesAsync(path, cancellationToken)),
            _ => CsvDocument.Parse(await CsvDocument.ReadFileAsync(path, cancellationToken), delimiter),
        };
    }

    private static async Task<FileFormat> DetectAsync(string path, CancellationToken cancellationToken)
    {
        var head = new byte[8];
        int read;
        await using (var stream = File.OpenRead(path))
        {
            read = await stream.ReadAsync(head, cancellationToken);
        }

        if (read >= 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04)
        {
            return FileFormat.Workbook; // zip container: .xlsx
        }

        if (read >= 8 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0)
        {
            return FileFormat.Workbook; // OLE2 container: .xls
        }

        // A bare BIFF stream starts with a BOF record: id 0x0009 (BIFF2), 0x0209 (BIFF3), 0x0409 (BIFF4)
        // or 0x0809 (BIFF5/8), little-endian, followed by its length. No CSV starts with a control byte.
        if (read >= 4 && head[0] == 0x09 && (head[1] is 0x00 or 0x02 or 0x04 or 0x08) && head[3] == 0x00)
        {
            return FileFormat.BareBiff;
        }

        return FileFormat.Csv;
    }

    private static List<List<string>> ReadWorkbook(string path, int sheetNumber = 1)
    {
        // Legacy .xls files carry their text in a Windows code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        for (var skipped = 1; skipped < sheetNumber; skipped++)
        {
            if (!reader.NextResult())
            {
                throw new InvalidDataException($"The workbook has no sheet {sheetNumber}.");
            }
        }

        var rows = new List<List<string>>();
        while (reader.Read())
        {
            var row = new List<string>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row.Add(CellText(reader.GetValue(i)));
            }

            AddIfNotBlank(rows, row);
        }

        return rows;
    }

    /// <summary>
    /// Reads the cells of the first sheet of a bare BIFF2-4 stream: text and number records only, with
    /// no shared string table. That is all the ZKTeco software writes (every cell is text). The
    /// general-purpose readers choke on this layout.
    /// </summary>
    private static List<List<string>> ReadBareBiff(byte[] data)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(1252);

        var cells = new SortedDictionary<int, SortedDictionary<int, string>>();
        var columns = 0;
        var position = 0;
        var started = false;

        while (position + 4 <= data.Length)
        {
            var id = BitConverter.ToUInt16(data, position);
            var length = BitConverter.ToUInt16(data, position + 2);
            var offset = position + 4;
            position = offset + length;
            if (position > data.Length)
            {
                break;
            }

            // The first record is the BOF; a second BOF would start another sheet, which is not read.
            if ((id & 0xFF) == 0x09)
            {
                if (started)
                {
                    break;
                }

                started = true;
                continue;
            }

            if (id == RecordEof)
            {
                break;
            }

            var cell = id switch
            {
                RecordLabel when length >= 8 => (Text: Text(encoding, data, offset + 8, data[offset + 7], length - 8), Ok: true),
                RecordLabelBiff3 when length >= 8 => (Text: Text(encoding, data, offset + 8, BitConverter.ToUInt16(data, offset + 6), length - 8), Ok: true),
                RecordNumber when length >= 15 => (Text: CellText(BitConverter.ToDouble(data, offset + 7)), Ok: true),
                RecordNumberBiff3 when length >= 14 => (Text: CellText(BitConverter.ToDouble(data, offset + 6)), Ok: true),
                RecordInteger when length >= 9 => (Text: BitConverter.ToUInt16(data, offset + 7).ToString(CultureInfo.InvariantCulture), Ok: true),
                _ => (Text: string.Empty, Ok: false),
            };
            if (!cell.Ok)
            {
                continue;
            }

            var rowIndex = BitConverter.ToUInt16(data, offset);
            var columnIndex = BitConverter.ToUInt16(data, offset + 2);
            if (!cells.TryGetValue(rowIndex, out var row))
            {
                cells[rowIndex] = row = [];
            }

            row[columnIndex] = cell.Text;
            columns = Math.Max(columns, columnIndex + 1);
        }

        // Every row gets the same width, so the header and the data line up.
        var rows = new List<List<string>>();
        foreach (var row in cells.Values)
        {
            AddIfNotBlank(rows, Enumerable.Range(0, columns).Select(c => row.GetValueOrDefault(c, string.Empty)).ToList());
        }

        return rows;
    }

    private static string Text(Encoding encoding, byte[] data, int offset, int characters, int available) =>
        encoding.GetString(data, offset, Math.Min(characters, available));

    private static void AddIfNotBlank(List<List<string>> rows, List<string> row)
    {
        if (row.Any(cell => cell.Length > 0))
        {
            rows.Add(row);
        }
    }

    private static string CellText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        DateTime date => date.ToString(DateCellFormat, CultureInfo.InvariantCulture),
        double number when number == Math.Floor(number) && Math.Abs(number) < 1e15 => number.ToString("0", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable other => other.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
