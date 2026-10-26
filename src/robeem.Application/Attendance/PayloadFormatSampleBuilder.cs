using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Makes an example file from a payload format as it stands in the form, saved or not, in the shape of CSV, JSON or XML. It uses
/// the format's own column names (or paths), timestamp patterns, culture, delimiter, record path and type labels, so the example
/// shows what the reader expects, and a file written like it is read back by the format. Nothing is stored and no real data is used.
/// </summary>
public static partial class PayloadFormatSampleBuilder
{
    public const string Csv = "csv";
    public const string Json = "json";
    public const string Xml = "xml";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Gets the kinds of example that can be made.</summary>
    public static IReadOnlyList<string> Kinds { get; } = [Csv, Json, Xml];

    /// <summary>Gets a value indicating whether <paramref name="kind"/> is one of <see cref="Kinds"/>.</summary>
    public static bool IsKind(string? kind) => Kinds.Contains((kind ?? string.Empty).Trim().ToLowerInvariant());

    /// <summary>Builds the example. Throws <see cref="ArgumentException"/> for a kind that is not one of <see cref="Kinds"/>.</summary>
    public static PayloadFormatSample Build(PayloadFormatInput input, string kind)
    {
        var chosen = (kind ?? string.Empty).Trim().ToLowerInvariant();
        var records = Records(input);
        var slug = Slug(input.Name);
        return chosen switch
        {
            Csv => new PayloadFormatSample { Kind = Csv, FileName = $"sample-{slug}.csv", ContentType = "text/csv", Content = ToCsv(input, records) },
            Json => new PayloadFormatSample { Kind = Json, FileName = $"sample-{slug}.json", ContentType = "application/json", Content = ToJson(input, records) },
            Xml => new PayloadFormatSample { Kind = Xml, FileName = $"sample-{slug}.xml", ContentType = "application/xml", Content = ToXml(input, records) },
            _ => throw new ArgumentException($"The kind must be one of: {string.Join(", ", Kinds)}.", nameof(kind)),
        };
    }

    /// <summary>The fields of a punch in the order a file lists them, each named as the format names its column, and the values of each record.</summary>
    private static List<List<(string Name, string Value)>> Records(PayloadFormatInput input)
    {
        var culture = TimestampCultures.Resolve(input.TimestampCulture);
        var moments = new[] { new DateTime(2026, 3, 14, 8, 2, 0), new DateTime(2026, 3, 14, 12, 0, 0), new DateTime(2026, 3, 14, 12, 31, 0), new DateTime(2026, 3, 14, 17, 5, 0) };

        // The labels the format knows, one per kind of event, in the order of a working day; two plain ones when it lists none.
        var codes = input.EventCodes.Where(c => !string.IsNullOrWhiteSpace(c.Code)).ToList();
        string? LabelOf(string type) => codes.FirstOrDefault(c => string.Equals(c.EventType, type, StringComparison.OrdinalIgnoreCase))?.Code?.Trim();
        var labelled = new[] { "CheckIn", "BreakOut", "BreakIn", "CheckOut" }
            .Select((type, index) => (Label: LabelOf(type), Moment: moments[index]))
            .Where(x => x.Label is not null)
            .Select(x => (Label: x.Label!, x.Moment))
            .ToList();
        if (labelled.Count == 0)
        {
            labelled = [("IN", moments[0]), ("OUT", moments[3])];
        }

        var timestampName = First(input.TimestampColumn);
        var dateName = First(input.DateColumn);
        var timeName = First(input.TimeColumn);
        var split = timestampName.Length == 0 && (dateName.Length > 0 || timeName.Length > 0);
        var joinedPattern = FirstPattern(input.TimestampFormats, "yyyy-MM-dd HH:mm");
        var datePattern = FirstPattern(input.DateFormats, "yyyy-MM-dd");
        var timePattern = FirstPattern(input.TimeFormats, "HH:mm");

        var result = new List<List<(string, string)>>();
        for (var i = 0; i < labelled.Count; i++)
        {
            var (label, moment) = labelled[i];
            var fields = new List<(string, string)> { (Or(First(input.EmployeeColumn), "Employee ID"), "E-1001") };
            if (split)
            {
                fields.Add((Or(dateName, "Date"), moment.ToString(datePattern, culture)));
                fields.Add((Or(timeName, "Time"), moment.ToString(timePattern, culture)));
            }
            else
            {
                fields.Add((Or(timestampName, "Timestamp"), moment.ToString(joinedPattern, culture)));
            }

            // The label goes where the format reads it: the type column, else the direction column; a format with neither has unlabeled punches.
            if (First(input.TypeColumn) is { Length: > 0 } typeName)
            {
                fields.Add((typeName, label));
            }
            else if (First(input.DirectionColumn) is { Length: > 0 } directionName)
            {
                fields.Add((directionName, label));
            }

            if (First(input.DeviceColumn) is { Length: > 0 } deviceName)
            {
                fields.Add((deviceName, "DEV-1"));
            }

            if (First(input.TransactionIdColumn) is { Length: > 0 } transactionName)
            {
                fields.Add((transactionName, $"T-{i + 1:0000}"));
            }

            result.Add(fields);
        }

        return result;
    }

    private static string ToCsv(PayloadFormatInput input, List<List<(string Name, string Value)>> records)
    {
        var delimiter = input.Delimiter is { Length: 1 } ? input.Delimiter[0] : ',';
        var builder = new StringBuilder();
        var lines = new List<string[]>();

        if (input.HasHeaderRow)
        {
            lines.Add(records[0].Select(f => f.Name).ToArray());
            lines.AddRange(records.Select(r => r.Select(f => f.Value).ToArray()));
        }
        else
        {
            // Without a header row the columns are numbered: put each value in the column the format names, and fill the gaps.
            var numbers = records[0].Select((f, index) => int.TryParse(f.Name, out var n) && n >= 1 ? n : index + 1).ToList();
            var width = Math.Max(numbers.Max(), records[0].Count);
            foreach (var record in records)
            {
                var cells = new string[width];
                Array.Fill(cells, string.Empty);
                for (var i = 0; i < record.Count; i++)
                {
                    cells[numbers[i] - 1] = record[i].Value;
                }

                lines.Add(cells);
            }
        }

        foreach (var line in lines)
        {
            builder.AppendLine(string.Join(delimiter, line.Select(cell => Quote(cell, delimiter))));
        }

        return builder.ToString();
    }

    private static string Quote(string cell, char delimiter) =>
        cell.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : cell;

    private static string ToJson(PayloadFormatInput input, List<List<(string Name, string Value)>> records)
    {
        var array = new JsonArray();
        foreach (var record in records)
        {
            var obj = new JsonObject();
            foreach (var (name, value) in record)
            {
                SetPath(obj, name, value);
            }

            array.Add(obj);
        }

        // The record path says where the array lives: data.punches[*] is { "data": { "punches": [ ... ] } }. Empty: a top-level array.
        JsonNode result = array;
        foreach (var segment in (input.RecordPath ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            var name = segment.EndsWith("[*]", StringComparison.Ordinal) ? segment[..^3] : segment;
            if (name.Length > 0)
            {
                result = new JsonObject { [name] = result };
            }
        }

        return result.ToJsonString(JsonOptions);
    }

    /// <summary>Puts a value at a dotted path, with <c>name[n]</c> for an array: <c>emp.id</c> is an object in an object, <c>tags[1]</c> an array.</summary>
    private static void SetPath(JsonObject root, string path, string value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = root;
        for (var i = 0; i < parts.Length; i++)
        {
            var last = i == parts.Length - 1;
            var match = IndexedSegment().Match(parts[i]);
            if (!match.Success)
            {
                if (last)
                {
                    current[parts[i]] = value;
                }
                else
                {
                    current = current[parts[i]] as JsonObject ?? (JsonObject)(current[parts[i]] = new JsonObject())!;
                }

                continue;
            }

            var name = match.Groups["name"].Value;
            var index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
            var array = current[name] as JsonArray ?? (JsonArray)(current[name] = new JsonArray())!;
            while (array.Count <= index)
            {
                array.Add(null);
            }

            if (last)
            {
                array[index] = value;
            }
            else
            {
                current = array[index] as JsonObject ?? (JsonObject)(array[index] = new JsonObject())!;
            }
        }
    }

    private static string ToXml(PayloadFormatInput input, List<List<(string Name, string Value)>> records)
    {
        // The record path is element names from the root. Empty: the root's children are the records. A single name is the root, which is the one record.
        var segments = (input.RecordPath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ElementName).ToList();
        var chain = segments.Count == 0 ? ["Punches", "Punch"] : segments;

        var recordElements = new List<XElement>();
        foreach (var record in chain.Count == 1 ? records.Take(1) : records)
        {
            var element = new XElement(chain[^1]);
            foreach (var (name, value) in record)
            {
                if (name.StartsWith('@'))
                {
                    element.SetAttributeValue(ElementName(name[1..]), value);
                    continue;
                }

                var parent = element;
                var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => ElementName(IndexedSegment().Match(p) is { Success: true } m ? m.Groups["name"].Value : p)).ToList();
                foreach (var part in parts[..^1])
                {
                    parent = parent.Element(part) ?? AddChild(parent, part);
                }

                parent.Add(new XElement(parts[^1], value));
            }

            recordElements.Add(element);
        }

        XElement root;
        if (chain.Count == 1)
        {
            root = recordElements[0];
        }
        else
        {
            // Wrap the records in the elements of the path above them.
            root = new XElement(chain[^2], recordElements);
            for (var i = chain.Count - 3; i >= 0; i--)
            {
                root = new XElement(chain[i], root);
            }
        }

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + root.ToString();
    }

    private static XElement AddChild(XElement parent, string name)
    {
        var child = new XElement(name);
        parent.Add(child);
        return child;
    }

    /// <summary>The first header a column setting lists (it may list several, separated by <c>|</c>).</summary>
    private static string First(string? column) => PayloadFormatLayout.Aliases(column ?? string.Empty).FirstOrDefault() ?? string.Empty;

    private static string FirstPattern(string? patterns, string fallback) => PayloadFormatLayout.Patterns(patterns ?? string.Empty).FirstOrDefault() ?? fallback;

    private static string Or(string value, string fallback) => value.Length == 0 ? fallback : value;

    /// <summary>A name that is a valid XML element name: punctuation and spaces dropped, a letter put in front of a leading digit.</summary>
    private static string ElementName(string name)
    {
        var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
        return cleaned.Length == 0 ? "Field" : char.IsLetter(cleaned[0]) || cleaned[0] == '_' ? cleaned : "F" + cleaned;
    }

    private static string Slug(string? name)
    {
        var slug = new string((name ?? string.Empty).Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? "payload-format" : slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }

    [GeneratedRegex(@"^(?<name>[^\[\]]+)\[(?<index>\d+)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex IndexedSegment();
}
