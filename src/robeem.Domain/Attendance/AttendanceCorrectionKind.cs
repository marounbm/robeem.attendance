namespace robeem.DataModel
{
    /// <summary>What an <see cref="AttendanceCorrection"/> changes. Stored as its name.</summary>
    public enum AttendanceCorrectionKind
    {
        /// <summary>It supplies the punch a missing-punch exception says is absent.</summary>
        SupplyPunch,

        /// <summary>It gives an existing punch another type, for a punch that is out of order: a check-out that was really a check-in.</summary>
        ChangePunchType,
    }
}
