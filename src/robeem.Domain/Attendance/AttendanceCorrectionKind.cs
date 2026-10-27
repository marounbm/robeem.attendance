namespace robeem.DataModel
{
    /// <summary>What an <see cref="AttendanceCorrection"/> changes. Stored as its name.</summary>
    public enum AttendanceCorrectionKind
    {
        /// <summary>It adds a punch that is not there: the one a missing-punch exception says is absent, or any punch the employee forgot.</summary>
        SupplyPunch,

        /// <summary>It gives an existing punch another type, for a punch that is out of order: a check-out that was really a check-in.</summary>
        ChangePunchType,

        /// <summary>It leaves an existing punch out of the calculation, for a punch that should not count: a duplicate, or a tap by mistake.</summary>
        IgnorePunch,

        /// <summary>It takes back a punch an earlier, applied correction added (<see cref="AttendanceCorrection.CancelsCorrectionId"/>). The earlier correction is never edited.</summary>
        CancelSuppliedPunch,
    }
}
