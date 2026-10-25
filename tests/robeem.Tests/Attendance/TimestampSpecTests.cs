using System.Globalization;
using System.IO.Compression;
using System.Text;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// How a custom file format reads a timestamp: one column, or a date column and a time column with patterns of their own,
/// in a chosen language, from text cells or from the real date and time cells of a workbook.
/// </summary>
public sealed class TimestampSpecTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly string _path = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_path);
        _database.Dispose();
    }

    [Fact]
    public void ADateColumnAndATimeColumn_AreReadByEveryDatePatternWithEveryTimePattern()
    {
        var interpreter = new CustomFormatInterpreter(SeparateFormat(dates: "dd/MM/yyyy;yyyy-MM-dd", times: "HH:mm;h:mm tt"));

        Assert.Equal(new DateTime(2026, 10, 4, 9, 5, 0), interpreter.ParseTimestamp("04/10/2026 09:05")!.Value.WallClock);
        Assert.Equal(new DateTime(2026, 10, 4, 17, 30, 0), interpreter.ParseTimestamp("2026-10-04 5:30 PM")!.Value.WallClock);
        Assert.Null(interpreter.ParseTimestamp("2026/10/04 09:05"));
        Assert.Null(interpreter.ParseTimestamp("04/10/2026 09:05:30"));
    }

    [Fact]
    public void AFormatWithoutDateAndTimePatterns_StillReadsTheJoinedTextByTheTimestampPatterns()
    {
        var format = SeparateFormat(dates: string.Empty, times: string.Empty);
        format.TimestampFormats = "dd/MM/yyyy HH:mm";

        Assert.Equal(["dd/MM/yyyy HH:mm"], PayloadFormatLayout.EffectivePatterns(format));
        Assert.Equal(new DateTime(2026, 10, 4, 9, 5, 0), new CustomFormatInterpreter(format).ParseTimestamp("04/10/2026 09:05")!.Value.WallClock);
    }

    [Fact]
    public void AnOffsetAfterTheTime_IsStillAccepted()
    {
        var parsed = new CustomFormatInterpreter(SeparateFormat(dates: "yyyy-MM-dd", times: "HH:mm")).ParseTimestamp("2026-10-04 09:05+03:00")!;

        Assert.Equal((new DateTime(2026, 10, 4, 9, 5, 0), TimeSpan.FromHours(3)), (parsed.Value.WallClock, parsed.Value.Offset));
    }

    [Fact]
    public void MonthNames_AreReadInTheFormatsLanguage()
    {
        var french = SeparateFormat(dates: "dd MMMM yyyy", times: "HH:mm");
        french.TimestampCulture = "fr-FR";

        Assert.Equal(new DateTime(2026, 3, 14, 15, 9, 0), new CustomFormatInterpreter(french).ParseTimestamp("14 mars 2026 15:09")!.Value.WallClock);
        Assert.Null(new CustomFormatInterpreter(SeparateFormat(dates: "dd MMMM yyyy", times: "HH:mm")).ParseTimestamp("14 mars 2026 15:09"));
    }

    [Fact]
    public void ALanguageWhoseDefaultCalendarIsNotGregorian_StillReadsGregorianDates()
    {
        // ar-SA defaults to the Hijri calendar, in which "2026" would be read as a Hijri year.
        var format = SeparateFormat(dates: "dd/MM/yyyy", times: "HH:mm");
        format.TimestampCulture = "ar-SA";

        Assert.Equal(new DateTime(2026, 3, 14, 15, 9, 0), new CustomFormatInterpreter(format).ParseTimestamp("14/03/2026 15:09")!.Value.WallClock);
    }

    [Fact]
    public void WhenTwoPatternsReadTheSameText_ExplainShowsBoth_AndTheFirstListedWins()
    {
        var interpreter = new CustomFormatInterpreter(SeparateFormat(dates: "dd/MM/yyyy;MM/dd/yyyy", times: "HH:mm"));

        var matches = interpreter.Explain("03/04/2026 09:00");

        Assert.Equal(["dd/MM/yyyy HH:mm", "MM/dd/yyyy HH:mm"], matches.Select(m => m.Pattern));
        Assert.Equal(["2026-04-03 09:00:00", "2026-03-04 09:00:00"], matches.Select(m => m.Reading));
        Assert.Equal(new DateTime(2026, 4, 3, 9, 0, 0), interpreter.ParseTimestamp("03/04/2026 09:00")!.Value.WallClock);
        Assert.Single(interpreter.Explain("25/04/2026 09:00"));
    }

    [Fact]
    public async Task ATextFile_WithMixedDateAndTimeWritings_IsReadByTheirPatterns()
    {
        File.WriteAllText(_path, "Badge,Date,Time,Type\r\nE-1,04/10/2026,09:05,IN\r\nE-1,2026-10-04,5:30 PM,OUT\r\nE-2,not a date,09:00,IN\r\n");

        var rows = await new CustomFormatFileReader().ReadAsync(SeparateFormat(dates: "dd/MM/yyyy;yyyy-MM-dd", times: "HH:mm;h:mm tt"), _path, CancellationToken.None);

        Assert.Equal(["04/10/2026 09:05", "2026-10-04 5:30 PM", "not a date 09:00"], rows.Select(r => r.TransactionTimestampRaw));
        Assert.Equal([new DateTime(2026, 10, 4, 9, 5, 0), new DateTime(2026, 10, 4, 17, 30, 0), null], rows.Select(r => r.Timestamp?.WallClock));
    }

    [Fact]
    public async Task AWorkbooksRealDateAndTimeCells_AreReadAsValues_NotThroughTheirDisplayedText()
    {
        WriteWorkbook(
            _path,
            [
                [Text("Badge"), Text("Date"), Text("Time"), Text("Type")],
                [Text("E-1"), Date(new DateTime(2026, 10, 4), style: 1), Time(new TimeSpan(9, 5, 0), style: 2), Text("IN")],
                [Text("E-1"), Date(new DateTime(2026, 10, 4), style: 1), Time(new TimeSpan(17, 30, 0), style: 2), Text("OUT")],
                [Text("E-2"), Text("05/10/2026"), Text("08:15"), Text("IN")],
            ]);

        var rows = await new CustomFormatFileReader().ReadAsync(SeparateFormat(dates: "dd/MM/yyyy", times: "HH:mm"), _path, CancellationToken.None);

        Assert.Equal(["04/10/2026 09:05", "04/10/2026 17:30", "05/10/2026 08:15"], rows.Select(r => r.TransactionTimestampRaw));
        Assert.Equal(
            [new DateTime(2026, 10, 4, 9, 5, 0), new DateTime(2026, 10, 4, 17, 30, 0), new DateTime(2026, 10, 5, 8, 15, 0)],
            rows.Select(r => r.Timestamp!.Value.WallClock));
    }

    [Fact]
    public async Task AWorkbooksDateTimeCell_InOneTimestampColumn_KeepsTheCanonicalText()
    {
        WriteWorkbook(
            _path,
            [
                [Text("Badge"), Text("Date/Time"), Text("Type")],
                [Text("E-1"), Date(new DateTime(2026, 4, 1, 8, 33, 25), style: 3), Text("C/In")],
            ]);
        var format = SeparateFormat(dates: string.Empty, times: string.Empty);
        format.TimestampColumn = "Date/Time";
        format.DateColumn = string.Empty;
        format.TimeColumn = string.Empty;
        format.TimestampFormats = "dd-MMM-yy HH:mm:ss";

        var row = Assert.Single(await new CustomFormatFileReader().ReadAsync(format, _path, CancellationToken.None));

        Assert.Equal("01-Apr-26 08:33:25", row.TransactionTimestampRaw);
        Assert.Equal(new DateTime(2026, 4, 1, 8, 33, 25), row.Timestamp!.Value.WallClock);
    }

    [Fact]
    public async Task ASeparateFormat_IsSaved_AndReadBackWithItsPatternsAndLanguage()
    {
        var input = SeparateInput();
        input.TimestampFormats = "dd/MM/yyyy HH:mm";
        input.TimestampCulture = "fr-FR";

        var created = await CreateAsync(input);
        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Values));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var stored = db.AttendancePayloadFormats.Single();
        Assert.Equal(("dd/MM/yyyy;yyyy-MM-dd", "HH:mm", string.Empty, "fr-FR"), (stored.DateFormats, stored.TimeFormats, stored.TimestampFormats, stored.TimestampCulture));

        var back = (await FormatService(db).GetInputAsync(created.Id!.Value, CancellationToken.None))!;
        Assert.Equal(["dd/MM/yyyy", "yyyy-MM-dd"], back.DateFormats!.Split(Environment.NewLine));
        Assert.Equal(("HH:mm", "fr-FR"), (back.TimeFormats, back.TimestampCulture));
    }

    [Fact]
    public async Task OneTimestampColumn_DropsAnyDateAndTimePatterns()
    {
        var input = SeparateInput();
        input.TimestampColumn = "Stamp";
        input.DateColumn = null;
        input.TimeColumn = null;
        input.TimestampFormats = "dd/MM/yyyy HH:mm";

        var created = await CreateAsync(input);

        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Values));
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var stored = db.AttendancePayloadFormats.Single();
        Assert.Equal((string.Empty, string.Empty, "dd/MM/yyyy HH:mm"), (stored.DateFormats, stored.TimeFormats, stored.TimestampFormats));
    }

    [Theory]
    [InlineData("dd/MM/yyyy HH:mm", "HH:mm", nameof(PayloadFormatInput.DateFormats))]
    [InlineData("HH:mm", "HH:mm", nameof(PayloadFormatInput.DateFormats))]
    [InlineData("dd/MM/yyyy", "dd/MM/yyyy", nameof(PayloadFormatInput.TimeFormats))]
    [InlineData("dd/MM/yyyy", "HH", nameof(PayloadFormatInput.TimeFormats))]
    [InlineData("dd/MM/yyyy", "", nameof(PayloadFormatInput.TimeFormats))]
    [InlineData("", "HH:mm", nameof(PayloadFormatInput.DateFormats))]
    [InlineData("d", "HH:mm", nameof(PayloadFormatInput.DateFormats))]
    public async Task UnusableDateOrTimePatterns_AreRefused_OnTheFieldThatIsWrong(string dates, string times, string field)
    {
        var input = SeparateInput();
        input.DateFormats = dates;
        input.TimeFormats = times;

        var result = await CreateAsync(input);

        Assert.False(result.Succeeded);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public async Task MoreThanTenPatternsInAList_AreRefused()
    {
        var input = SeparateInput();
        input.DateFormats = string.Join(";", Enumerable.Range(1, 11).Select(n => "dd/MM/yyyy 'n" + n + "'"));

        var result = await CreateAsync(input);

        Assert.Contains("At most 10", result.Errors[nameof(PayloadFormatInput.DateFormats)]);
    }

    [Fact]
    public async Task AnUnknownLanguage_IsRefused_AndTheInvariantOneIsNot()
    {
        var unknown = SeparateInput();
        unknown.TimestampCulture = "xx-NOPE";

        Assert.Contains(nameof(PayloadFormatInput.TimestampCulture), (await CreateAsync(unknown)).Errors.Keys);
        Assert.True((await CreateAsync(SeparateInput())).Succeeded);
    }

    [Fact]
    public async Task AMonthNamePattern_NeedsTheLanguageItIsWrittenIn()
    {
        var input = SeparateInput();
        input.DateFormats = "dd MMMM yyyy";
        input.TimestampCulture = "fr-FR";

        Assert.True((await CreateAsync(input)).Succeeded);
    }

    [Fact]
    public async Task APreview_WarnsOfPatternsThatDifferOnlyInDayAndMonthOrder_AndShowsEveryPatternThatReadsARow()
    {
        File.WriteAllText(_path, "Badge,Date,Time,Type\r\nE-1,03/04/2026,09:00,IN\r\nE-1,25/04/2026,09:00,IN\r\n");
        var input = SeparateInput();
        input.DateFormats = "dd/MM/yyyy;MM/dd/yyyy";
        input.TimeFormats = "HH:mm";

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var preview = await FormatService(db).PreviewAsync(input, _path, CancellationToken.None);

        Assert.Equal(string.Empty, preview.Problem);
        var warning = Assert.Single(preview.Warnings);
        Assert.Contains("'dd/MM/yyyy' and 'MM/dd/yyyy'", warning);
        Assert.Equal(["dd/MM/yyyy HH:mm → 2026-04-03 09:00:00", "MM/dd/yyyy HH:mm → 2026-03-04 09:00:00"], preview.Rows[0].PatternMatches);
        Assert.Equal("2026-04-03 09:00:00", preview.Rows[0].ParsedTimestamp);
        Assert.Single(preview.Rows[1].PatternMatches);
    }

    [Fact]
    public void OnlyNumericDayAndMonthSwaps_AreWarnedAbout()
    {
        Assert.Empty(AttendancePayloadFormatService.AmbiguityWarnings(["dd/MM/yyyy", "yyyy-MM-dd"]));
        Assert.Empty(AttendancePayloadFormatService.AmbiguityWarnings(["dd MMM yyyy", "MMM dd yyyy"]));
        Assert.Single(AttendancePayloadFormatService.AmbiguityWarnings(["MM-dd-yyyy", "dd-MM-yyyy"]));
    }

    [Fact]
    public void ThePatternCatalog_OffersPatternsThatAreValid_AndWrittenAsTheirExample()
    {
        Assert.All(TimestampPatternCatalog.Dates, o => Assert.True(DateTime.TryParseExact(o.Example, o.Pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out _), o.Pattern));
        Assert.All(TimestampPatternCatalog.Times, o => Assert.True(DateTime.TryParseExact(o.Example, o.Pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out _), o.Pattern));
        Assert.All(TimestampPatternCatalog.Combined, o => Assert.True(DateTime.TryParseExact(o.Example, o.Pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out _), o.Pattern));
        Assert.All(TimestampPatternCatalog.Cultures, c => Assert.True(TimestampCultures.IsValid(c.Tag), c.Tag));
    }

    private static AttendancePayloadFormat SeparateFormat(string dates, string times) => new()
    {
        Name = "Test",
        HasHeaderRow = true,
        EmployeeColumn = "Badge",
        DateColumn = "Date",
        TimeColumn = "Time",
        TypeColumn = "Type",
        DateFormats = dates,
        TimeFormats = times,
        EventCodes =
        [
            new AttendancePayloadEventCode { Code = "IN", EventType = NormalizedEventType.CheckIn },
            new AttendancePayloadEventCode { Code = "OUT", EventType = NormalizedEventType.CheckOut },
        ],
    };

    private static PayloadFormatInput SeparateInput() => new()
    {
        Name = "Separate",
        EmployeeColumn = "Badge",
        DateColumn = "Date",
        TimeColumn = "Time",
        TypeColumn = "Type",
        DateFormats = "dd/MM/yyyy" + Environment.NewLine + "yyyy-MM-dd",
        TimeFormats = "HH:mm",
        EventCodes = [new PayloadFormatEventCodeInput { Code = "IN", EventType = "CheckIn" }],
    };

    private static Cell Text(string value) => new(value, null, 0);

    private static Cell Date(DateTime value, int style) => new(null, value.ToOADate(), style);

    private static Cell Time(TimeSpan value, int style) => new(null, value.TotalDays, style);

    /// <summary>
    /// Writes a minimal workbook by hand: text cells, and number cells whose style (a built-in date or time number format) makes a
    /// reader see a date or time. Style 1 is a date, 2 a time of day, 3 a date and time.
    /// </summary>
    private static void WriteWorkbook(string path, Cell[][] rows)
    {
        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Length; r++)
        {
            sheet.Append(CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = rows[r][c];
                var reference = $"{(char)('A' + c)}{r + 1}";
                if (cell.Text is not null)
                {
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{cell.Text}</t></is></c>");
                }
                else
                {
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{cell.Style}\"><v>{cell.Number!.Value.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        AddEntry(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
        AddEntry(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        AddEntry(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        AddEntry(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
        AddEntry(zip, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"20\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"22\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
        AddEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private async Task<robeem.Application.Common.SaveResult> CreateAsync(PayloadFormatInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await FormatService(db).CreateAsync(input, CancellationToken.None);
    }

    private AttendancePayloadFormatService FormatService(RobeemDbContext db) =>
        new(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), new CustomFormatFileReader(), new CustomFormatInterpreterFactory());

    private sealed record Cell(string? Text, double? Number, int Style);
}
