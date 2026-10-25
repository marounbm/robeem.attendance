using System.Text.Json;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;

namespace robeem.Tests.Attendance;

/// <summary>
/// What a file format can say beyond one header per field: several headers to try, the first with a value winning;
/// the columns to keep in the payload; and every normalized event type for a label. These are what let the
/// ZKTeco and generic CSV layouts be data rather than code.
/// </summary>
public sealed class PayloadFormatAliasTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();
    private readonly TestDatabase _database = new();

    public void Dispose()
    {
        File.Delete(_path);
        _database.Dispose();
    }

    [Fact]
    public async Task SeveralHeaders_AreTried_InOrder_AndTheFirstWithAValueWins()
    {
        File.WriteAllText(_path, "ID Number,No.,Date/Time\r\n22002,,2026-04-01 08:00\r\n,21001,2026-04-01 09:00\r\n23003,24004,2026-04-01 10:00\r\n,,2026-04-01 11:00\r\n");

        var rows = await ReadAsync(Format(employee: "No.|ID Number"));

        Assert.Equal(["22002", "21001", "24004", null], rows.Select(r => r.ExternalEmployeeId));
    }

    [Fact]
    public async Task OfSeveralHeaders_TheFileMayHaveAnyNumber_EvenNone_ButASingleMissingHeaderIsAnError()
    {
        File.WriteAllText(_path, "Badge,Date/Time\r\nB-1,2026-04-01 08:00\r\n");

        var lenient = await ReadAsync(Format(employee: "Badge", type: "Type|Event Type|Punch Type", device: "Device ID|Terminal ID"));
        var strict = await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(Format(employee: "Badge", type: "Type")));

        var row = Assert.Single(lenient);
        Assert.Equal(("B-1", null, null), (row.ExternalEmployeeId, row.TransactionTypeRaw, row.DeviceExternalId));
        Assert.Contains("no column named 'Type'", strict.Message);
    }

    [Fact]
    public async Task OnlyTheColumnsToKeep_EnterThePayload_UnderTheNamesTheFormatGivesThem()
    {
        File.WriteAllText(_path, "Name,CARDNO,date/time,Secret,No.\r\nHiba,0099887,2026-04-01 08:00,keep-out,21001\r\n");

        var rows = await ReadAsync(Format(employee: "No.", payload: "Name|Date/Time|No."));

        var payload = JsonDocument.Parse(Assert.Single(rows).RawPayload).RootElement;
        Assert.Equal(["Date/Time", "Name", "No."], payload.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("0099887", payload.GetRawText());
        Assert.DoesNotContain("keep-out", payload.GetRawText());

        // The mapped fields are still read from the row, kept in the payload or not.
        Assert.Equal("21001", Assert.Single(rows).ExternalEmployeeId);
    }

    [Fact]
    public async Task WithNoColumnsToKeep_EveryColumnIsKept()
    {
        File.WriteAllText(_path, "No.,Date/Time,Extra\r\n21001,2026-04-01 08:00,x\r\n");

        var rows = await ReadAsync(Format(employee: "No."));

        Assert.Equal(["Date/Time", "Extra", "No."], JsonDocument.Parse(Assert.Single(rows).RawPayload).RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheLayout_ShowsTheAlternativeHeaders_AndTheColumnsKept()
    {
        var guide = PayloadFormatLayout.Describe(Format(employee: "No.|ID Number", payload: "Name|No."), "zkteco");

        Assert.Contains(guide.Fields, f => f.Target == "ExternalEmployeeId" && f.Source == "No. / ID Number");
        Assert.Contains("Only these columns are kept in the payload: Name, No.", guide.Summary);
        Assert.Contains(guide.NotStored, n => n.Contains("not listed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheColumnsToKeep_NeedAHeaderRow_AndNeverABiometricColumn()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new AttendancePayloadFormatService(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), new CustomFormatFileReader(), new CustomFormatInterpreterFactory());

        var noHeader = Input();
        noHeader.HasHeaderRow = false;
        noHeader.EmployeeColumn = "1";
        noHeader.TimestampColumn = "2";
        noHeader.PayloadColumns = "Name";
        var biometric = Input();
        biometric.PayloadColumns = "Name|Fingerprint Template";

        Assert.Contains("header row", (await service.CreateAsync(noHeader, CancellationToken.None)).Errors[nameof(PayloadFormatInput.PayloadColumns)]);
        Assert.Contains("biometric", (await service.CreateAsync(biometric, CancellationToken.None)).Errors[nameof(PayloadFormatInput.PayloadColumns)]);
    }

    [Fact]
    public async Task AFormatWithAlternativeHeadersAndColumnsToKeep_IsSavedAndReadBack_AndAnyEventTypeCanBeMapped()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new AttendancePayloadFormatService(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), new CustomFormatFileReader(), new CustomFormatInterpreterFactory());
        var input = Input();
        input.EmployeeColumn = " No. | ID Number ";
        input.PayloadColumns = " Name | No. ";
        input.EventCodes =
        [
            new PayloadFormatEventCodeInput { Code = "OT In", EventType = "Other" },
            new PayloadFormatEventCodeInput { Code = "Unclear punch", EventType = "Unknown" },
        ];

        var created = await service.CreateAsync(input, CancellationToken.None);

        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => $"{e.Key}: {e.Value}")));
        var back = (await service.GetInputAsync(created.Id!.Value, CancellationToken.None))!;
        Assert.Equal(("No.|ID Number", "Name|No."), (back.EmployeeColumn, back.PayloadColumns));
        Assert.Equal(["OT In", "Unclear punch"], back.EventCodes.Select(c => c.Code));
        Assert.Equal(["Other", "Unknown"], back.EventCodes.Select(c => c.EventType));
    }

    private static PayloadFormatInput Input() => new()
    {
        Name = "Alias format",
        EmployeeColumn = "No.",
        TimestampColumn = "Date/Time",
        TimestampFormats = "yyyy-MM-dd HH:mm",
    };

    private static AttendancePayloadFormat Format(string employee, string type = "", string device = "", string payload = "") => new()
    {
        Name = "Alias",
        EmployeeColumn = employee,
        TimestampColumn = "Date/Time",
        TypeColumn = type,
        DeviceColumn = device,
        PayloadColumns = payload,
        TimestampFormats = "yyyy-MM-dd HH:mm",
    };

    private async Task<IReadOnlyList<ExternalAttendanceTransaction>> ReadAsync(AttendancePayloadFormat format) =>
        await new CustomFormatFileReader().ReadAsync(format, _path, CancellationToken.None);
}
