using System.Text;

namespace robeem.Application.Attendance.TestEmployees;

/// <summary>A small CSV reader for the test employees file: a header row, quoted fields (a doubled quote is one quote), commas or line breaks inside quotes.</summary>
internal static class TestEmployeeCsv
{
    /// <summary>Reads the records of <paramref name="text"/>. Blank lines are skipped. Every record is returned as read, however many fields it has.</summary>
    public static List<string[]> Parse(string text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var any = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    any = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    any = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    field.Append(c);
                    any = true;
                    break;
            }
        }

        EndRecord();
        return records;

        void EndRecord()
        {
            if (any || fields.Count > 0)
            {
                fields.Add(field.ToString());
                records.Add([.. fields.Select(f => f.Trim())]);
            }

            fields.Clear();
            field.Clear();
            any = false;
        }
    }

    /// <summary>A column name as it is matched: lower case, letters and digits only, so <c>Employee Number</c>, <c>employee_number</c> and <c>EmployeeNumber</c> are one.</summary>
    public static string Key(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
