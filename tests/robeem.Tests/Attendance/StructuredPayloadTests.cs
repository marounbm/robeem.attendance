using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>JSON and XML payloads: each record the record path selects is read as a row of fields named by their path.</summary>
public sealed class StructuredPayloadTests : IDisposable
{
    private const string PunchesJson = """
        {
          "status": "ok",
          "data": {
            "punches": [
              { "emp": { "id": "E-1" }, "at": "2026-10-04 09:00", "kind": "IN", "dev": "DEV-1", "face_template": "QUJD", "tags": ["a", "b"] },
              { "emp": { "id": "E-2" }, "at": "2026-10-04 17:30", "kind": "OUT", "dev": "DEV-1" }
            ]
          }
        }
        """;

    private const string PunchesXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Punches>
          <Punch badge="E-1" code="IN"><Time>2026-10-04 09:00</Time><Device>DEV-1</Device></Punch>
          <Punch badge="E-2" code="OUT"><Time>2026-10-04 17:30</Time><Device>DEV-1</Device></Punch>
        </Punches>
        """;

    private readonly TestDatabase _database = new();
    private readonly string _path = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_path);
        _database.Dispose();
    }

    [Fact]
    public async Task AJsonRecordPath_SelectsTheArray_AndNestedValuesAreColumnsNamedByTheirPath()
    {
        File.WriteAllText(_path, PunchesJson);

        var transactions = await new CustomFormatFileReader().ReadAsync(JsonFormat("data.punches[*]"), _path, CancellationToken.None);

        Assert.Equal(2, transactions.Count);
        Assert.Equal(("E-1", "IN", "DEV-1", "2026-10-04 09:00"), (transactions[0].ExternalEmployeeId, transactions[0].TransactionTypeRaw, transactions[0].DeviceExternalId, transactions[0].TransactionTimestampRaw));
        Assert.Equal(new DateTime(2026, 10, 4, 9, 0, 0), transactions[0].Timestamp!.Value.WallClock);
        Assert.Equal("E-2", transactions[1].ExternalEmployeeId);

        // The whole record is the payload, nested values under their path; the sanitizer removes the template when it is stored.
        Assert.Contains("\"emp.id\":\"E-1\"", transactions[0].RawPayload);
        Assert.Contains("\"tags[1]\":\"b\"", transactions[0].RawPayload);
    }

    [Fact]
    public async Task APathThatEndsAtAnArray_ReadsItsElements_AndAnEmptyPathReadsATopLevelArray()
    {
        File.WriteAllText(_path, PunchesJson);
        Assert.Equal(2, (await new CustomFormatFileReader().ReadAsync(JsonFormat("data.punches"), _path, CancellationToken.None)).Count);

        File.WriteAllText(_path, """[ { "emp": { "id": "E-9" }, "at": "2026-10-04 08:00", "kind": "IN" } ]""");
        var format = JsonFormat(string.Empty);
        format.DeviceColumn = string.Empty;
        var top = await new CustomFormatFileReader().ReadAsync(format, _path, CancellationToken.None);

        Assert.Equal("E-9", Assert.Single(top).ExternalEmployeeId);
    }

    [Fact]
    public async Task ARecordWithoutAField_LeavesItEmpty_AndJsonValuesKeepTheirText()
    {
        File.WriteAllText(_path, """[ { "emp": { "id": 42 }, "at": "2026-10-04 08:00", "kind": null }, { "emp": { "id": 43 }, "at": "2026-10-04 08:01", "kind": "IN", "dev": "DEV-1" } ]""");

        var rows = await new CustomFormatFileReader().ReadAsync(JsonFormat(string.Empty), _path, CancellationToken.None);

        // The columns are the union over the records: the first has no device, the second does.
        Assert.Equal((("42", null, null), ("43", "IN", "DEV-1")), ((rows[0].ExternalEmployeeId, rows[0].TransactionTypeRaw, rows[0].DeviceExternalId), (rows[1].ExternalEmployeeId, rows[1].TransactionTypeRaw, rows[1].DeviceExternalId)));
    }

    [Fact]
    public async Task AJsonFileThatIsNotJson_OrWhoseRecordPathSelectsNothing_IsRefusedWithTheReason()
    {
        File.WriteAllText(_path, "not json at all");
        var broken = await Assert.ThrowsAsync<InvalidDataException>(() => new CustomFormatFileReader().ReadAsync(JsonFormat("data"), _path, CancellationToken.None));
        Assert.Contains("not valid JSON", broken.Message);

        File.WriteAllText(_path, PunchesJson);
        var missing = await Assert.ThrowsAsync<InvalidDataException>(() => new CustomFormatFileReader().ReadAsync(JsonFormat("data.nothing[*]"), _path, CancellationToken.None));
        Assert.Contains("selects nothing", missing.Message);
    }

    [Fact]
    public async Task JsonNestedDeeperThanTheLimit_IsRefused()
    {
        File.WriteAllText(_path, new string('[', 200) + new string(']', 200));

        await Assert.ThrowsAsync<InvalidDataException>(() => new CustomFormatFileReader().ReadAsync(JsonFormat(string.Empty), _path, CancellationToken.None));
    }

    [Fact]
    public async Task AFileLargerThanTheLimit_IsRefusedBeforeItIsRead()
    {
        using (var stream = new FileStream(_path, FileMode.Create))
        {
            stream.SetLength((50L * 1024 * 1024) + 1);
        }

        var json = await Assert.ThrowsAsync<InvalidDataException>(() => new CustomFormatFileReader().ReadAsync(JsonFormat(string.Empty), _path, CancellationToken.None));
        var xml = await Assert.ThrowsAsync<InvalidDataException>(() => new CustomFormatFileReader().ReadAsync(XmlFormat(string.Empty), _path, CancellationToken.None));

        Assert.Contains("larger than", json.Message);
        Assert.Contains("larger than", xml.Message);
    }

    [Fact]
    public async Task AnXmlRecordPath_SelectsTheElements_AttributesAreAtColumns_AndChildElementsAreColumns()
    {
        File.WriteAllText(_path, PunchesXml);

        var transactions = await new CustomFormatFileReader().ReadAsync(XmlFormat("Punches/Punch"), _path, CancellationToken.None);

        Assert.Equal(2, transactions.Count);
        Assert.Equal(("E-1", "IN", "DEV-1"), (transactions[0].ExternalEmployeeId, transactions[0].TransactionTypeRaw, transactions[0].DeviceExternalId));
        Assert.Equal(new DateTime(2026, 10, 4, 17, 30, 0), transactions[1].Timestamp!.Value.WallClock);
        Assert.Contains("\"@badge\":\"E-1\"", transactions[0].RawPayload);
    }

    [Fact]
    public async Task AnEmptyXmlRecordPath_ReadsTheChildrenOfTheRoot_AndARootThatDoesNotMatchIsRefused()
    {
        File.WriteAllText(_path, PunchesXml);
        Assert.Equal(2, (await new CustomFormatFileReader().ReadAsync(XmlFormat(string.Empty), _path, CancellationToken.None)).Count);

        var wrongRoot = await Assert.ThrowsAsync<InvalidDataException>(() => new CustomFormatFileReader().ReadAsync(XmlFormat("Log/Punch"), _path, CancellationToken.None));
        Assert.Contains("root element is 'Punches'", wrongRoot.Message);
    }

    [Fact]
    public async Task RepeatedXmlSiblings_AreIndexed()
    {
        File.WriteAllText(_path, "<Log><Punch><Id>E-1</Id><Id>E-2</Id><Time>2026-10-04 09:00</Time></Punch></Log>");
        var format = XmlFormat("Log/Punch");
        format.EmployeeColumn = "Id[1]";
        format.TypeColumn = string.Empty;
        format.DeviceColumn = string.Empty;

        var row = Assert.Single(await new CustomFormatFileReader().ReadAsync(format, _path, CancellationToken.None));

        Assert.Equal("E-2", row.ExternalEmployeeId);
    }

    [Fact]
    public async Task AnXmlDocumentWithADtd_IsRefused_SoNoEntityIsEverExpanded()
    {
        const string hostile = """
            <?xml version="1.0"?>
            <!DOCTYPE Punches [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <Punches><Punch badge="&xxe;" code="IN"><Time>2026-10-04 09:00</Time></Punch></Punches>
            """;
        File.WriteAllText(_path, hostile);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new CustomFormatFileReader().ReadAsync(XmlFormat("Punches/Punch"), _path, CancellationToken.None));

        Assert.Contains("not valid XML", error.Message);
    }

    [Fact]
    public async Task APayloadColumnsAllowlist_KeepsOnlyTheNamedFields_OfAJsonRecord()
    {
        File.WriteAllText(_path, PunchesJson);
        var format = JsonFormat("data.punches[*]");
        format.PayloadColumns = "emp.id|kind";

        var row = (await new CustomFormatFileReader().ReadAsync(format, _path, CancellationToken.None))[0];

        Assert.DoesNotContain("face_template", row.RawPayload);
        Assert.DoesNotContain("tags", row.RawPayload);
        Assert.Contains("emp.id", row.RawPayload);
    }

    [Fact]
    public async Task TheFormatForm_AcceptsJsonAndXml_ForcesHeadersOn_AndKeepsTheRecordPathForThemOnly()
    {
        var json = Input("Json", "data.punches[*]");
        json.HasHeaderRow = false;
        json.EmployeeColumn = "emp.id";

        var created = await CreateAsync(json);
        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Values));
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var stored = db.AttendancePayloadFormats.Single(f => f.Id == created.Id);
            Assert.Equal((AttendancePayloadKind.Json, true, "data.punches[*]"), (stored.PayloadKind, stored.HasHeaderRow, stored.RecordPath));
        }

        var csv = Input("Csv", "data.punches[*]");
        var rejected = await CreateAsync(csv);
        Assert.Contains(nameof(PayloadFormatInput.RecordPath), rejected.Errors.Keys);
        Assert.Contains(nameof(PayloadFormatInput.RecordPath), (await CreateAsync(Input("Xml", "Punches//Punch"))).Errors.Keys);
        Assert.Equal([".json"], PayloadFormatLayout.FileExtensions(AttendancePayloadKind.Json));
        Assert.Equal([".xml"], PayloadFormatLayout.FileExtensions(AttendancePayloadKind.Xml));
    }

    private static AttendancePayloadFormat JsonFormat(string recordPath) => new()
    {
        Name = "Json",
        PayloadKind = AttendancePayloadKind.Json,
        RecordPath = recordPath,
        EmployeeColumn = "emp.id",
        TimestampColumn = "at",
        TimestampFormats = "yyyy-MM-dd HH:mm",
        TypeColumn = "kind",
        DeviceColumn = "dev",
    };

    private static AttendancePayloadFormat XmlFormat(string recordPath) => new()
    {
        Name = "Xml",
        PayloadKind = AttendancePayloadKind.Xml,
        RecordPath = recordPath,
        EmployeeColumn = "@badge",
        TimestampColumn = "Time",
        TimestampFormats = "yyyy-MM-dd HH:mm",
        TypeColumn = "@code",
        DeviceColumn = "Device",
    };

    private static PayloadFormatInput Input(string kind, string recordPath) => new()
    {
        Name = "Structured " + kind + recordPath.Length,
        PayloadKind = kind,
        RecordPath = recordPath,
        EmployeeColumn = "emp.id",
        TimestampColumn = "at",
        TimestampFormats = "yyyy-MM-dd HH:mm",
        EventCodes = [new PayloadFormatEventCodeInput { Code = "IN", EventType = "CheckIn" }],
    };

    private async Task<robeem.Application.Common.SaveResult> CreateAsync(PayloadFormatInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await new AttendancePayloadFormatService(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), new CustomFormatFileReader(), new CustomFormatInterpreterFactory())
            .CreateAsync(input, CancellationToken.None);
    }
}
