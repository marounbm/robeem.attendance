using System.Text;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// The CSV reading that the file-based adapters share: RFC 4180 parsing, unique header names and
/// the header matching rule (ignore case, spaces and punctuation).
/// </summary>
internal static class CsvDocument
{
    /// <summary>Reads a whole file, honouring a byte order mark and defaulting to UTF-8.</summary>
    public static async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>Header text reduced to lower-case letters and digits, so <c>Date/Time</c> and <c>date_time</c> both give <c>datetime</c>.</summary>
    public static string NormalizeHeader(string header) =>
        new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Trims the headers and renames a blank or repeated one to <c>column{n}</c>, so every cell has a distinct key.</summary>
    public static List<string> UniqueHeaders(IReadOnlyList<string> header)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(header.Count);
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim();
            if (name.Length == 0 || !seen.Add(name))
            {
                name = $"column{i + 1}";
                seen.Add(name);
            }

            result.Add(name);
        }

        return result;
    }

    /// <summary>RFC 4180: comma-separated (or by <paramref name="delimiter"/>), double-quoted fields may hold the delimiter, quotes ("") and line breaks. Blank lines are skipped.</summary>
    public static List<List<string>> Parse(string content, char delimiter = ',')
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowHasContent = false;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            if (rowHasContent || row.Count > 1 || row[0].Length > 0)
            {
                rows.Add(row);
            }

            row = [];
            rowHasContent = false;
        }

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < content.Length && content[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == delimiter)
            {
                EndField();
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    rowHasContent = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0 || rowHasContent)
        {
            EndRow();
        }

        return rows;
    }
}
