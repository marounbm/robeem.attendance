using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

/// <summary>
/// The catalog every tenant starts with. Plain data: the seeder adds these rows when they are missing and an
/// administrator then owns them (CLAUDE.md §2.3).
/// </summary>
internal static class AttendanceSeedData
{
    public const string GenericCsvFormatName = "Generic CSV";

    public const string ZktecoFormatName = "ZKTeco attendance export";

    public const string BiostarFormatName = "Suprema BioStar 2 export";

    /// <summary>The kinds of device an administrator can give a device.</summary>
    public static readonly string[] DeviceTypes = ["Biometric", "Facial", "RFID", "Mobile", "Cloud", "OnPrem"];

    public static readonly SeedTransport[] Transports =
    [
        new(AttendanceTransport.FileUpload, "Manual file upload", IsPull: false, UsesPayloadFormat: true),
        new(AttendanceTransport.Sftp, "SFTP drop", IsPull: true, UsesPayloadFormat: false),
        new(AttendanceTransport.RestApi, "REST API", IsPull: true, UsesPayloadFormat: false),
        new(AttendanceTransport.Webhook, "Webhook", IsPull: false, UsesPayloadFormat: false),
        new(AttendanceTransport.Database, "Database", IsPull: true, UsesPayloadFormat: false),
        new(AttendanceTransport.DeviceSdk, "Device SDK / agent", IsPull: true, UsesPayloadFormat: false),
    ];

    /// <summary>
    /// The vendors, each with one connector of the same key. A file-based connector is read by <see cref="SeedVendor.DefaultFormat"/>,
    /// an API or webhook one by the code adapter registered under its adapter key.
    /// </summary>
    public static readonly SeedVendor[] Vendors =
    [
        new("generic-csv", "Generic CSV upload", [], SupportsIncrementalSync: false, IsActive: true, DefaultFormat: GenericCsvFormatName),
        new("generic-webhook", "Generic webhook", [AttendanceTransport.Webhook], SupportsIncrementalSync: false, IsActive: false, AdapterKey: "generic-webhook"),
        new("zkteco", "ZKTeco", [], SupportsIncrementalSync: false, IsActive: true, DefaultFormat: ZktecoFormatName, Manufacturer: "ZKTeco", WebsiteUrl: "https://www.zkteco.com"),
        new("suprema-biostar", "Suprema BioStar 2", [AttendanceTransport.RestApi], SupportsIncrementalSync: true, IsActive: true, AdapterKey: "suprema-biostar"),
    ];

    /// <summary>
    /// The layouts of the file-based vendors: generic CSV (columns matched by several names, ISO 8601 timestamps),
    /// and the export of the ZKTeco clock's own software (day-first times, only the listed columns stored, CardNo never).
    /// </summary>
    public static readonly SeedFormat[] Formats =
    [
        new(
            GenericCsvFormatName,
            "A CSV file, or an Excel sheet, with a header row, uploaded by hand. Columns are matched by name, ignoring case, spaces and punctuation. Timestamps are ISO 8601 (yyyy-MM-dd HH:mm[:ss], T also accepted), optionally ending in Z or an offset.",
            EmployeeColumn: "Employee ID|Emp ID|Badge ID|User ID",
            TimestampColumn: "Timestamp|DateTime|Punch Time",
            TypeColumn: "Type|Event Type|Punch Type",
            DirectionColumn: "Direction|In Out",
            DeviceColumn: "Device ID|Terminal ID",
            TransactionIdColumn: "Transaction ID|Record ID",
            PayloadColumns: string.Empty,
            TimestampFormats:
            [
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
                "yyyy-MM-dd HH:mm:ss.FFFFFFF",
                "yyyy-MM-dd'T'HH:mm",
                "yyyy-MM-dd HH:mm",
            ],
            EventCodes:
            [
                ("In", NormalizedEventType.CheckIn),
                ("Check In", NormalizedEventType.CheckIn),
                ("Clock In", NormalizedEventType.CheckIn),
                ("Out", NormalizedEventType.CheckOut),
                ("Check Out", NormalizedEventType.CheckOut),
                ("Clock Out", NormalizedEventType.CheckOut),
                ("Break Out", NormalizedEventType.BreakOut),
                ("Break Start", NormalizedEventType.BreakOut),
                ("Break In", NormalizedEventType.BreakIn),
                ("Break End", NormalizedEventType.BreakIn),
            ]),

        new(
            ZktecoFormatName,
            "The attendance export of the ZKTeco clock's own software (Fingerprint Attendance System), uploaded as exported (XLS; XLSX or CSV also work). One row per punch; times are wall-clock at the device (no offset), day first. Break Out, Break In, OT In and OT Out are also read when the clock writes them; any other Status fails the row.",
            EmployeeColumn: "No.|ID Number",
            TimestampColumn: "Date/Time",
            TypeColumn: "Status",
            DirectionColumn: string.Empty,
            DeviceColumn: "Location ID",
            TransactionIdColumn: string.Empty,
            PayloadColumns: "Department|Name|No.|Date/Time|Status|Location ID|ID Number|VerifyCode",
            TimestampFormats:
            [
                "d-MMM-yy H:mm:ss",
                "d-MMM-yy H:mm",
                "d-MMM-yyyy H:mm:ss",
                "d-MMM-yyyy H:mm",
                "d/M/yyyy H:mm:ss",
                "d/M/yyyy H:mm",
                "d/M/yy H:mm:ss",
                "d/M/yy H:mm",
                "yyyy-MM-dd H:mm:ss",
                "yyyy-MM-dd H:mm",
            ],
            EventCodes:
            [
                ("C/In", NormalizedEventType.CheckIn),
                ("C/Out", NormalizedEventType.CheckOut),
                ("Break Out", NormalizedEventType.BreakOut),
                ("Break In", NormalizedEventType.BreakIn),
                ("OT In", NormalizedEventType.Other),
                ("OT Out", NormalizedEventType.Other),
            ]),

        new(
            BiostarFormatName,
            "An event log or Time & Attendance punch export from the BioStar 2 web client (CSV or Excel), uploaded by hand. Times are wall-clock at the device, year first (yyyy/MM/dd or yyyy-MM-dd) or ISO 8601 with an optional Z or offset; an export written day-first or month-first needs a copy of this format with those patterns, since the two cannot be told apart. The punch type is the T&A key label (Check In, Check Out, Break Start, Break End, Meal Start, Meal End); a row with no T&A key is an unlabeled punch and any other label fails the row. Only the listed columns are stored: card numbers and anything biometric never are.",
            EmployeeColumn: "User ID|UserID",
            TimestampColumn: "Date/Time|DateTime|Event Date|Date",
            TypeColumn: "T&A Event|T&A Key|TNA Key|Punch Type",
            DirectionColumn: string.Empty,
            DeviceColumn: "Device ID",
            TransactionIdColumn: "Event ID",
            PayloadColumns: "Date/Time|DateTime|Event Date|Date|User ID|UserID|User Name|Name|User Group|Department|Device ID|Device Name|Device|Event ID|T&A Event|T&A Key|TNA Key|Punch Type|Door",
            TimestampFormats:
            [
                "yyyy/MM/dd H:mm:ss",
                "yyyy/MM/dd H:mm",
                "yyyy-MM-dd H:mm:ss",
                "yyyy-MM-dd H:mm",
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
                "yyyy-MM-dd'T'HH:mm:ss",
            ],
            EventCodes:
            [
                ("Check In", NormalizedEventType.CheckIn),
                ("In", NormalizedEventType.CheckIn),
                ("PUNCH_TYPE_CHECK_IN", NormalizedEventType.CheckIn),
                ("1", NormalizedEventType.CheckIn),
                ("Check Out", NormalizedEventType.CheckOut),
                ("Out", NormalizedEventType.CheckOut),
                ("PUNCH_TYPE_CHECK_OUT", NormalizedEventType.CheckOut),
                ("2", NormalizedEventType.CheckOut),
                ("Break Start", NormalizedEventType.BreakOut),
                ("Meal Start", NormalizedEventType.BreakOut),
                ("PUNCH_TYPE_BREAK_START", NormalizedEventType.BreakOut),
                ("PUNCH_TYPE_MEAL_START", NormalizedEventType.BreakOut),
                ("3", NormalizedEventType.BreakOut),
                ("5", NormalizedEventType.BreakOut),
                ("Break End", NormalizedEventType.BreakIn),
                ("Meal End", NormalizedEventType.BreakIn),
                ("PUNCH_TYPE_BREAK_END", NormalizedEventType.BreakIn),
                ("PUNCH_TYPE_MEAL_END", NormalizedEventType.BreakIn),
                ("4", NormalizedEventType.BreakIn),
                ("6", NormalizedEventType.BreakIn),
            ]),
    ];
}
