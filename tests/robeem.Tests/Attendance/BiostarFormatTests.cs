using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;

namespace robeem.Tests.Attendance;

/// <summary>The BioStar 2 web client's export, read by the seeded "Suprema BioStar 2 export" file format.</summary>
public sealed class BiostarFormatTests : IDisposable
{
    // The shape of a BioStar 2 event log export, plus a card number and a column the allowlist does not know.
    private const string SampleCsv =
        "Date/Time,Event ID,User ID,User Name,User Group,Device ID,Device Name,T&A Event,Card ID,Extra\r\n" +
        "2026/04/01 08:33:25,9001,21001,Hiba Rahme,All Users/Marketing,541531088,Front door,Check In,0099887,keep-out\r\n" +
        "2026/04/01 17:35:02,9002,21001,Hiba Rahme,All Users/Marketing,541531088,Front door,Check Out,,\r\n" +
        "2026/04/01 12:00:00,9003,21001,Hiba Rahme,All Users/Marketing,541531088,Front door,,,\r\n" +
        "2026/04/01 12:30:00,9004,21001,Hiba Rahme,All Users/Marketing,541531088,Front door,Lunch,,\r\n";

    private readonly TestDatabase _database = new();
    private readonly string _csvPath = Path.GetTempFileName();

    public BiostarFormatTests()
    {
        File.WriteAllText(_csvPath, SampleCsv);
        _database.SeedAttendanceCatalog();
    }

    public void Dispose()
    {
        File.Delete(_csvPath);
        _database.Dispose();
    }

    [Fact]
    public async Task TheExport_IsReadWithTheVendorsFieldsMapped_AndNoCardNumberStored()
    {
        var transactions = await new CustomFormatFileReader().ReadAsync(Format(), _csvPath, CancellationToken.None);

        Assert.Equal(4, transactions.Count);
        var first = transactions[0];
        Assert.Equal("21001", first.ExternalEmployeeId);
        Assert.Equal("541531088", first.DeviceExternalId);
        Assert.Equal("9001", first.ExternalTransactionId);
        Assert.Equal("Check In", first.TransactionTypeRaw);
        Assert.Equal("2026/04/01 08:33:25", first.TransactionTimestampRaw);

        // Invariant 10: a column the format does not list never reaches the database, Card ID included.
        var payload = JsonDocument.Parse(first.RawPayload).RootElement;
        Assert.DoesNotContain("0099887", payload.GetRawText());
        Assert.DoesNotContain("keep-out", payload.GetRawText());
        Assert.DoesNotContain(payload.EnumerateObject(), p => p.Name is "Card ID" or "Extra");
    }

    [Theory]
    [InlineData("2026/04/01 08:33:25", 2026, 4, 1, 8, 33, 25)]
    [InlineData("2026/04/01 8:33", 2026, 4, 1, 8, 33, 0)]
    [InlineData("2026-04-01 08:33:25", 2026, 4, 1, 8, 33, 25)]
    [InlineData("2026-04-01T08:33:25", 2026, 4, 1, 8, 33, 25)]
    public void Interpreter_ReadsTheYearFirstFormats_AsDeviceWallClock(string text, int year, int month, int day, int hour, int minute, int second)
    {
        var parsed = new CustomFormatInterpreter(Format()).ParseTimestamp(text);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), parsed.Value.WallClock);
        Assert.Null(parsed.Value.Offset);
    }

    [Theory]
    [InlineData("01/04/2026 08:33")]
    [InlineData("")]
    public void Interpreter_DoesNotGuess_AtADayFirstOrMonthFirstDate(string text) =>
        Assert.Null(new CustomFormatInterpreter(Format()).ParseTimestamp(text));

    [Theory]
    [InlineData("Check In", NormalizedEventType.CheckIn)]
    [InlineData("check out", NormalizedEventType.CheckOut)]
    [InlineData("Break Start", NormalizedEventType.BreakOut)]
    [InlineData("Meal End", NormalizedEventType.BreakIn)]
    [InlineData("PUNCH_TYPE_MEAL_START", NormalizedEventType.BreakOut)]
    [InlineData("4", NormalizedEventType.BreakIn)]
    public void Interpreter_MapsTheT_AndA_KeyLabels(string label, NormalizedEventType expected) =>
        Assert.Equal(expected, new CustomFormatInterpreter(Format()).MapEventType(label, null));

    [Fact]
    public void Interpreter_ReturnsNull_ForALabelItDoesNotKnow() =>
        Assert.Null(new CustomFormatInterpreter(Format()).MapEventType("Lunch", null));

    private AttendanceFileFormat Format()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceFileFormats.Include(f => f.EventCodes).AsNoTracking().Single(f => f.Name == "Suprema BioStar 2 export");
    }
}
