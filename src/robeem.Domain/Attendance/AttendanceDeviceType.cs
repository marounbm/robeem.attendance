namespace robeem.DataModel
{
    /// <summary>
    /// Kind of device that captures punches. Stored as its name.
    /// </summary>
    public enum AttendanceDeviceType
    {
        Biometric,
        Facial,
        RFID,
        Mobile,
        Cloud,
        OnPrem,
    }
}
