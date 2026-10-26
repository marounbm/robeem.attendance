using System.Text.Json;
using System.Xml.Linq;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;

namespace robeem.Tests.Attendance;

/// <summary>
/// The example files of a payload format: made from the format as it stands, in CSV, JSON or XML, and each one is read back by the
/// format itself, so an example is never a file the format could not read.
/// </summary>
public sealed class PayloadFormatSampleTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task AJsonExample_NestsTheValuesByTheirPath_UnderTheRecordPath_AndIsReadBackByTheFormat()
    {
        var input = JsonStyle();

        var sample = PayloadFormatSampleBuilder.Build(input, "json");

        using var document = JsonDocument.Parse(sample.Content);
        var first = document.RootElement.GetProperty("data").GetProperty("punches")[0];
        Assert.Equal("E-1001", first.GetProperty("emp").GetProperty("id").GetString());
        Assert.Equal("2026-03-14 08:02", first.GetProperty("at").GetString());
        Assert.Equal(("sample-branch-json.json", "application/json"), (sample.FileName, sample.ContentType));

        var punches = await ReadBack(input, sample);
        Assert.Equal(4, punches.Count);

        // A working day in the format's own labels: in, out for a break, back from it, out.
        Assert.Equal(["IN", "BRK", "BACK", "OUT"], punches.Select(p => p.TransactionTypeRaw));
        Assert.All(punches, p => Assert.Equal(("E-1001", "DEV-1"), (p.ExternalEmployeeId, p.DeviceExternalId)));
        Assert.Equal(new DateTime(2026, 3, 14, 8, 2, 0), punches[0].Timestamp!.Value.WallClock);
    }

    [Fact]
    public async Task AJsonExampleWithNoRecordPath_IsATopLevelArray()
    {
        var input = JsonStyle();
        input.RecordPath = string.Empty;

        var sample = PayloadFormatSampleBuilder.Build(input, "json");

        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(sample.Content).RootElement.ValueKind);
        Assert.Equal(4, (await ReadBack(input, sample)).Count);
    }

    [Fact]
    public async Task AnXmlExample_UsesAttributesAndElements_UnderTheRecordPath_AndIsReadBackByTheFormat()
    {
        var input = XmlStyle();

        var sample = PayloadFormatSampleBuilder.Build(input, "xml");

        var root = XDocument.Parse(sample.Content).Root!;
        Assert.Equal("Punches", root.Name.LocalName);
        var punch = root.Elements("Punch").First();
        Assert.Equal(("E-1001", "IN"), (punch.Attribute("badge")!.Value, punch.Attribute("code")!.Value));
        Assert.Equal("2026-03-14 08:02", punch.Element("Time")!.Value);
        Assert.StartsWith("<?xml", sample.Content);

        var punches = await ReadBack(input, sample);
        Assert.Equal(4, punches.Count);
        Assert.Equal(("E-1001", "IN", "DEV-1"), (punches[0].ExternalEmployeeId, punches[0].TransactionTypeRaw, punches[0].DeviceExternalId));
    }

    [Fact]
    public async Task AnXmlExampleWithNoRecordPath_HasTheRecordsAsChildrenOfTheRoot()
    {
        var input = XmlStyle();
        input.RecordPath = string.Empty;

        var sample = PayloadFormatSampleBuilder.Build(input, "xml");

        Assert.Equal(4, XDocument.Parse(sample.Content).Root!.Elements().Count());
        Assert.Equal(4, (await ReadBack(input, sample)).Count);
    }

    [Fact]
    public async Task ACsvExample_HasTheFormatsHeaders_Delimiter_AndPatterns_AndIsReadBack()
    {
        var input = CsvStyle();

        var sample = PayloadFormatSampleBuilder.Build(input, "csv");

        var lines = sample.Content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Badge;Date;Time;Direction;Terminal", lines[0]);
        Assert.Equal("E-1001;14/03/2026;08:02;In;DEV-1", lines[1]);
        Assert.Equal("text/csv", sample.ContentType);

        var punches = await ReadBack(input, sample);
        Assert.Equal(["In", "Out"], punches.Select(p => p.DirectionRaw));
        Assert.Equal(new DateTime(2026, 3, 14, 17, 5, 0), punches[1].Timestamp!.Value.WallClock);
    }

    [Fact]
    public async Task ACsvExampleWithoutAHeaderRow_PutsEachValueInItsNumberedColumn()
    {
        var input = new PayloadFormatInput
        {
            Name = "Numbered",
            HasHeaderRow = false,
            EmployeeColumn = "2",
            TimestampColumn = "4",
            TimestampFormats = "yyyy-MM-dd HH:mm",
            TypeColumn = "5",
            EventCodes = [new PayloadFormatEventCodeInput { Code = "I", EventType = "CheckIn" }, new PayloadFormatEventCodeInput { Code = "O", EventType = "CheckOut" }],
        };

        var sample = PayloadFormatSampleBuilder.Build(input, "csv");

        Assert.Equal(",E-1001,,2026-03-14 08:02,I", sample.Content.Split(Environment.NewLine)[0]);
        Assert.Equal(2, (await ReadBack(input, sample)).Count);
    }

    [Fact]
    public void ADelimiterOrQuoteInAValue_IsQuoted()
    {
        var input = CsvStyle();
        input.Delimiter = ",";
        input.EmployeeColumn = "Badge";
        input.TimestampColumn = "Time";
        input.DateColumn = string.Empty;
        input.TimeColumn = string.Empty;
        input.TimestampFormats = "dd/MM/yyyy, HH:mm";
        input.DateFormats = string.Empty;
        input.TimeFormats = string.Empty;

        var sample = PayloadFormatSampleBuilder.Build(input, "csv");

        Assert.Contains("\"14/03/2026, 08:02\"", sample.Content);
    }

    [Fact]
    public void TheMonthNamesFollowTheFormatsLanguage()
    {
        var input = CsvStyle();
        input.DateFormats = "dd MMMM yyyy";
        input.TimestampCulture = "fr-FR";

        Assert.Contains("14 mars 2026", PayloadFormatSampleBuilder.Build(input, "csv").Content);
    }

    [Fact]
    public void AFormatNotFilledInYet_StillGetsAnExample_WithDefaultNames()
    {
        var json = PayloadFormatSampleBuilder.Build(new PayloadFormatInput(), "json");
        var xml = PayloadFormatSampleBuilder.Build(new PayloadFormatInput { Name = "  Mine!  " }, "xml");
        var csv = PayloadFormatSampleBuilder.Build(new PayloadFormatInput(), "csv");

        using var document = JsonDocument.Parse(json.Content);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal("E-1001", document.RootElement[0].GetProperty("Employee ID").GetString());
        Assert.Equal("sample-mine.xml", xml.FileName);
        Assert.StartsWith("Employee ID,Timestamp", csv.Content);
        Assert.Equal("sample-payload-format.json", json.FileName);
    }

    [Fact]
    public void ANameThatIsNotAnXmlName_IsMadeOneAndTheKindIsChecked()
    {
        var input = XmlStyle();
        input.EmployeeColumn = "Employee ID";
        input.RecordPath = string.Empty;

        Assert.Contains("<EmployeeID>E-1001</EmployeeID>", PayloadFormatSampleBuilder.Build(input, "xml").Content);
        Assert.True(PayloadFormatSampleBuilder.IsKind(" JSON "));
        Assert.False(PayloadFormatSampleBuilder.IsKind("pdf"));
        Assert.Throws<ArgumentException>(() => PayloadFormatSampleBuilder.Build(input, "pdf"));
    }

    private static PayloadFormatInput JsonStyle() => new()
    {
        Name = "Branch JSON",
        PayloadKind = "Json",
        RecordPath = "data.punches[*]",
        EmployeeColumn = "emp.id",
        TimestampColumn = "at",
        TimestampFormats = "yyyy-MM-dd HH:mm",
        TypeColumn = "kind",
        DeviceColumn = "dev",
        EventCodes = [new PayloadFormatEventCodeInput { Code = "IN", EventType = "CheckIn" }, new PayloadFormatEventCodeInput { Code = "OUT", EventType = "CheckOut" }, new PayloadFormatEventCodeInput { Code = "BRK", EventType = "BreakOut" }, new PayloadFormatEventCodeInput { Code = "BACK", EventType = "BreakIn" }],
    };

    private static PayloadFormatInput XmlStyle() => new()
    {
        Name = "Branch XML",
        PayloadKind = "Xml",
        RecordPath = "Punches/Punch",
        EmployeeColumn = "@badge",
        TimestampColumn = "Time",
        TimestampFormats = "yyyy-MM-dd HH:mm",
        TypeColumn = "@code",
        DeviceColumn = "Device",
        EventCodes = [new PayloadFormatEventCodeInput { Code = "IN", EventType = "CheckIn" }, new PayloadFormatEventCodeInput { Code = "OUT", EventType = "CheckOut" }, new PayloadFormatEventCodeInput { Code = "BRK", EventType = "BreakOut" }, new PayloadFormatEventCodeInput { Code = "BACK", EventType = "BreakIn" }],
    };

    private static PayloadFormatInput CsvStyle() => new()
    {
        Name = "Branch CSV",
        Delimiter = ";",
        EmployeeColumn = "Badge",
        DateColumn = "Date",
        TimeColumn = "Time",
        DateFormats = "dd/MM/yyyy",
        TimeFormats = "HH:mm",
        DirectionColumn = "Direction",
        DeviceColumn = "Terminal",
        EventCodes = [new PayloadFormatEventCodeInput { Code = "In", EventType = "CheckIn" }, new PayloadFormatEventCodeInput { Code = "Out", EventType = "CheckOut" }],
    };

    /// <summary>Writes the example to a file and reads it with the format itself, as an upload would be.</summary>
    private async Task<IReadOnlyList<ExternalAttendanceTransaction>> ReadBack(PayloadFormatInput input, PayloadFormatSample sample)
    {
        await File.WriteAllTextAsync(_path, sample.Content);
        var format = new AttendancePayloadFormat
        {
            Name = input.Name,
            PayloadKind = Enum.Parse<AttendancePayloadKind>(input.PayloadKind, ignoreCase: true),
            Delimiter = input.Delimiter,
            HasHeaderRow = input.HasHeaderRow,
            RecordPath = input.RecordPath ?? string.Empty,
            EmployeeColumn = input.EmployeeColumn ?? string.Empty,
            TimestampColumn = input.TimestampColumn ?? string.Empty,
            DateColumn = input.DateColumn ?? string.Empty,
            TimeColumn = input.TimeColumn ?? string.Empty,
            TypeColumn = input.TypeColumn ?? string.Empty,
            DirectionColumn = input.DirectionColumn ?? string.Empty,
            DeviceColumn = input.DeviceColumn ?? string.Empty,
            TimestampFormats = input.TimestampFormats ?? string.Empty,
            DateFormats = input.DateFormats ?? string.Empty,
            TimeFormats = input.TimeFormats ?? string.Empty,
            TimestampCulture = input.TimestampCulture ?? string.Empty,
        };
        foreach (var code in input.EventCodes)
        {
            format.EventCodes.Add(new AttendancePayloadEventCode { Code = code.Code ?? string.Empty, EventType = Enum.Parse<NormalizedEventType>(code.EventType) });
        }

        return await new CustomFormatFileReader().ReadAsync(format, _path, CancellationToken.None);
    }
}
