namespace robeem.DataModel
{
    /// <summary>
    /// A way an attendance vendor can deliver transactions. <see cref="AttendanceVendor.Transports"/> stores a comma-separated list of these member names.
    /// </summary>
    public enum AttendanceTransport
    {
        /// <summary>A file (CSV, Excel) uploaded by a user.</summary>
        FileUpload,

        /// <summary>Files dropped on, and collected from, an SFTP server.</summary>
        Sftp,

        /// <summary>robeem polls the vendor's REST API.</summary>
        RestApi,

        /// <summary>The vendor pushes transactions to a robeem endpoint.</summary>
        Webhook,

        /// <summary>robeem reads the vendor's on-premises database.</summary>
        Database,

        /// <summary>robeem talks to the device directly through the vendor's SDK.</summary>
        DeviceSdk,
    }
}
