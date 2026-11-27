namespace robeem.DataModel
{
    /// <summary>What a payroll administrator decides about one line of the compensation report. Stored as its name.</summary>
    public enum CompensationDecisionKind
    {
        /// <summary>The line has no effect on the pay.</summary>
        Ignore,

        /// <summary>Overtime that is counted.</summary>
        Overtime,

        /// <summary>Under time covered by paid leave: a leave is added to the employee's leaves.</summary>
        PaidLeave,

        /// <summary>Under time covered by unpaid leave: a leave is added to the employee's leaves.</summary>
        UnpaidLeave,

        /// <summary>Under time that is penalised, at the schedule's under time percent.</summary>
        Penalise,
    }
}
