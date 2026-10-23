using System;

namespace robeem.DataModel
{
    /// <summary>
    /// What was found about a shift's break when its day was calculated. Every finding is recorded on the
    /// <see cref="AttendanceRecord"/> whatever the policy says; whether it also raises an
    /// <see cref="AttendanceException"/> is the policy's decision. Stored as a number.
    /// </summary>
    [Flags]
    public enum BreakFindings
    {
        None = 0,

        /// <summary>The breaks together lasted longer than the scheduled break plus the policy's allowance.</summary>
        LongBreak = 1,

        /// <summary>The first break started later than scheduled, beyond the policy's allowance.</summary>
        LateBreak = 2,

        /// <summary>The first break started earlier than scheduled, beyond the policy's allowance.</summary>
        EarlyBreak = 4,

        /// <summary>The shift has a scheduled break and the employee took none (a break shorter than the policy's minimum is not a break).</summary>
        NoBreak = 8,
    }
}
