using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Reads a JSON or XML attendance file as a table: every record the format's record path selects becomes one row, and
/// every value inside it (a property, an attribute, a child element, to any depth) becomes a column named by its path
/// relative to the record (<c>employee.id</c>, <c>@code</c>, <c>tags[1]</c>). The first row is the header, so the rest of
/// the reading (column mapping, timestamp patterns, event codes, the payload allowlist) is the one a CSV file uses.
/// </summary>
/// <remarks>
/// The reader is hardened because the file comes from outside: a size limit, a nesting limit, a limit on columns and
/// records, and for XML no DTD, no entity expansion and no external resolver. A file that breaks a limit is an error, never a
/// partial read.
/// </remarks>
internal static class StructuredFile
{
    public const long MaxBytes = 50L * 1024 * 1024;
    public const int MaxDepth = 64;
    public const int MaxColumns = 200;
    public const int MaxRecords = 1_000_000;

    public static async Task<TabularData> ReadJsonAsync(string path, string recordPath, CancellationToken cancellationToken)
    {
        CheckSize(path);
        JsonDocument document;
        try
        {
            await using var stream = File.OpenRead(path);
            document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = MaxDepth, CommentHandling = JsonCommentHandling.Skip }, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The file is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var records = SelectJson(document.RootElement, recordPath).ToList();
            return Table(records.Select(FlattenJson));
        }
    }

    public static async Task<TabularData> ReadXmlAsync(string path, string recordPath, CancellationToken cancellationToken)
    {
        CheckSize(path);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxBytes,
            MaxCharactersFromEntities = 0,
            Async = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        XDocument document;
        try
        {
            await using var stream = File.OpenRead(path);
            using var reader = XmlReader.Create(stream, settings);
            document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"The file is not valid XML: {ex.Message}", ex);
        }

        var records = SelectXml(document.Root ?? throw new InvalidDataException("The XML file has no root element."), recordPath).ToList();
        return Table(records.Select(FlattenXml));
    }

    private static void CheckSize(string path)
    {
        if (new FileInfo(path).Length > MaxBytes)
        {
            throw new InvalidDataException($"The file is larger than {MaxBytes / (1024 * 1024)} MB.");
        }
    }

    private static IEnumerable<JsonElement> SelectJson(JsonElement root, string recordPath)
    {
        IEnumerable<JsonElement> current = [root];
        foreach (var segment in recordPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var expand = segment.EndsWith("[*]", StringComparison.Ordinal);
            var name = expand ? segment[..^3] : segment;
            var next = new List<JsonElement>();
            foreach (var element in current)
            {
                var child = element;
                if (name.Length > 0 && !(element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out child)))
                {
                    continue;
                }

                if (expand && child.ValueKind == JsonValueKind.Array)
                {
                    next.AddRange(child.EnumerateArray());
                }
                else if (!expand)
                {
                    next.Add(child);
                }
            }

            current = next;
        }

        // A path that ends at an array names its elements, so "data.punches" and "data.punches[*]" read the same.
        var records = new List<JsonElement>();
        foreach (var element in current)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                records.AddRange(element.EnumerateArray());
            }
            else
            {
                records.Add(element);
            }
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException(recordPath.Length == 0
                ? "The JSON file holds no records."
                : $"The record path '{recordPath}' selects nothing in the file.");
        }

        return records;
    }

    private static IEnumerable<XElement> SelectXml(XElement root, string recordPath)
    {
        var segments = recordPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return root.Elements().ToList();
        }

        if (segments[0] != root.Name.LocalName)
        {
            throw new InvalidDataException($"The record path '{recordPath}' starts at '{segments[0]}', but the file's root element is '{root.Name.LocalName}'.");
        }

        IEnumerable<XElement> current = [root];
        foreach (var segment in segments.Skip(1))
        {
            current = current.SelectMany(e => e.Elements().Where(c => c.Name.LocalName == segment)).ToList();
        }

        var records = current.ToList();
        return records.Count > 0 ? records : throw new InvalidDataException($"The record path '{recordPath}' selects nothing in the file.");
    }

    private static List<KeyValuePair<string, string>> FlattenJson(JsonElement record)
    {
        var cells = new List<KeyValuePair<string, string>>();
        Walk(record, string.Empty);
        return cells;

        void Walk(JsonElement element, string name)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        Walk(property.Value, name.Length == 0 ? property.Name : $"{name}.{property.Name}");
                    }

                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, $"{name}[{index++}]");
                    }

                    break;
                default:
                    cells.Add(new(name.Length == 0 ? "value" : name, JsonText(element)));
                    break;
            }
        }
    }

    private static string JsonText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => string.Empty,
    };

    private static List<KeyValuePair<string, string>> FlattenXml(XElement record)
    {
        var cells = new List<KeyValuePair<string, string>>();
        Walk(record, string.Empty);
        return cells;

        void Walk(XElement element, string prefix)
        {
            foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                cells.Add(new(prefix + "@" + attribute.Name.LocalName, attribute.Value));
            }

            var children = element.Elements().ToList();
            var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
            if (children.Count == 0)
            {
                // A leaf is its own value; at the record itself it is just "value".
                if (prefix.Length == 0 && !element.HasAttributes)
                {
                    cells.Add(new("value", text));
                }
                else if (prefix.Length > 0)
                {
                    cells.Add(new(prefix.TrimEnd('.'), text));
                }
                else if (text.Length > 0)
                {
                    cells.Add(new("#text", text));
                }

                return;
            }

            if (text.Length > 0)
            {
                cells.Add(new(prefix + "#text", text));
            }

            foreach (var group in children.GroupBy(c => c.Name.LocalName))
            {
                var siblings = group.ToList();
                for (var i = 0; i < siblings.Count; i++)
                {
                    var name = siblings.Count > 1 ? $"{group.Key}[{i}]" : group.Key;
                    Walk(siblings[i], prefix + name + ".");
                }
            }
        }
    }

    /// <summary>The union of the records' column names, in the order first met, as a header row and one row per record.</summary>
    private static TabularData Table(IEnumerable<List<KeyValuePair<string, string>>> records)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var all = new List<List<KeyValuePair<string, string>>>();
        foreach (var record in records)
        {
            if (all.Count >= MaxRecords)
            {
                throw new InvalidDataException($"The file has more than {MaxRecords} records.");
            }

            foreach (var (name, _) in record)
            {
                if (!columns.ContainsKey(name))
                {
                    columns[name] = columns.Count;
                    if (columns.Count > MaxColumns)
                    {
                        throw new InvalidDataException($"The records have more than {MaxColumns} distinct fields.");
                    }
                }
            }

            all.Add(record);
        }

        var rows = new List<List<string>> { columns.Keys.ToList() };
        foreach (var record in all)
        {
            var row = Enumerable.Repeat(string.Empty, columns.Count).ToList();
            foreach (var (name, value) in record)
            {
                row[columns[name]] = value;
            }

            rows.Add(row);
        }

        return new TabularData(rows, null);
    }
}
