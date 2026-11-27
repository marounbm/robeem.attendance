# Payroll workflow: from punches to a closed pay period

How attendance becomes pay-ready hours. It covers who does what, in which order, and what each screen is for. The screens are named as they appear in the menu.

The output is **hours only**. There is no money yet: rates on the employee and overtime multipliers come in a later stage. This guide is the operating view; the rules the code keeps are in `CLAUDE.md` (*Compensation report*, §6).

## The idea in one paragraph

Punches are calculated into days. A day is **payable** once it is approved and has no open exception. Overtime and under time are **not exceptions**: a day that has either stays approved and payable. At the end of each employee's own pay period, the payroll administrator opens the **compensation report**, decides each line (ignore, leave, overtime, penalise), reads the balance, and **closes the period** for that employee.

## Who does what

| Role | Does |
|---|---|
| Employee | Corrects their own days |
| Manager | Corrects or accepts for their direct reports, on their behalf; approves their corrections |
| HR | Rules, policies, holidays and leaves; corrects and approves for anyone |
| Payroll administrator | Pay cycles, the compensation report, decisions, closing and reopening a period |

## 1. One-time setup

1. **Start date** (*Settings → Start date*, HR): the date punching started. Nothing is expected before it.
2. **Rules** (*Settings → Rules*, HR): switch off what payroll should decide instead of raising as an exception.
   - Switch **A day worked short** off. A new tenant has it **on**, and while it is on a short day becomes an exception and is not payable, so it shows as *not ready* in the report instead of as under time.
   - Leave **Overtime**, **Late check-in** and **Early check-out** off (they are off by default).
   - Optionally switch on the automatic punch fixes to reduce exceptions.
3. **Policies** (*Settings → Policies*, HR): one default and one per schedule. In the **Pay rules** card set:

   | Setting | Meaning |
   |---|---|
   | Late arrival penalty % | 0 is none, 100 means the late minutes are simply unpaid, 200 twice that |
   | Early departure penalty % | The same, for minutes before the end of the shift |
   | Under time penalty % | Used only for under time that payroll chooses to penalise, and only the part beyond the late and early minutes |
   | Work on a day off is overtime | Work with no shift becomes an overtime line |
   | Work on a public holiday is overtime | All the work of a holiday becomes an overtime line |

   Keep **Overtime needs approval first** off, or every overtime day waits in *Approvals* before payroll can read it.
4. **Pay cycles** (*Payroll → Pay cycles*, payroll): set the **default** cycle, then give any employee a cycle of their own. With no cycle at all, the period is the calendar month. The kinds of cycle:

   | Cycle | Periods | Set with |
   |---|---|---|
   | Monthly | One a month, from a day of the month to the day before it in the next. Day 1 is the calendar month; 26 gives the 26th to the 25th | The day of the month (1 to 28) |
   | Twice a month | Two a month: the 1st to the 15th and the 16th to the end of the month; from start day 11 they are the 11th to the 25th and the 26th to the 10th | The first start day (1 to 13) |
   | Every two weeks | 14 days each, 26 a year | A first day of a period |
   | Weekly | 7 days each, 52 a year; the weekday of the first day is the day each period starts on | A first day of a period |
   | Every four weeks | 28 days each, 13 a year | A first day of a period |
5. **Holidays and leaves** (HR) are entered as usual. They excuse time, and saving one settles the affected days at once.

## 2. Every day

1. Punches arrive and days are calculated automatically.
2. Each day ends up one of three ways:
   - **Approved.** It may be shown as *Inferred* or *Under timed*. Overtime and under time do not stop it.
   - **An exception** (missing punch, punches out of order, a break or no-show finding): settle it on *Attendance → Exceptions*.
     - The employee corrects the day.
     - The **manager or HR can correct it on the employee's behalf**, with a reason. The correction applies at once when the sender holds every approval step, otherwise it waits only for the step nobody took.
     - Findings with no punch to supply (breaks, no-show) can be **accepted** with a reason.
   - **To review or approve**: decided in *Approvals*.
3. HR can use **Find missing days** and **Calculate again** (*Attendance*) when something old changed, such as a policy or a leave.

## 3. At the end of each pay period

Each employee has their own period, so this is done per employee, not once for the company.

1. **Open *Payroll → Compensation*.**
   - The default view shows, for each employee, the period of their **latest day with attendance**, so history imported long after it happened is found at once.
   - Choose "Each one's last finished period" to count from today instead, or "The period that contains…" and a date to look at any other one.
   - On an employee's page, **« Previous period** and **Next period »** move through that employee's own periods (for example from May back to April).
   - Each row shows days, worked, overtime, under time, balance, penalty minutes, lines to decide and **not-ready days**.
2. **Clear the not-ready days first.** Open the employee: the page lists the dates of days that have attendance data but are not payable (an unsettled exception, a day to review, an unapproved day). Get each settled by the employee, manager or HR. The period **cannot be closed** while any remain, and those days are not in the report.
3. **Decide the lines.** A line is one day. The date links to that day's page.

   | Line | Decisions | If left undecided |
   |---|---|---|
   | **Overtime** (weekday, day off or holiday) | *Overtime* counts it; *Ignore* leaves it out | Counts as overtime |
   | **Under time** | *Ignore* forgives it; *Penalise* adds penalty minutes; *Paid leave* or *Unpaid leave* adds leave | Stays, and offsets the overtime |

   - **Leave decisions create real leaves.** They add part-day leaves to the employee's leaves over the time missed at the start and end of the shift (from the shift start to the check-in, and from the check-out to the shift end). For paid leave you choose the type: Annual, Sick or Other. Unpaid leave is added as Unpaid. The day is then recalculated and the covered minutes leave the under time.
   - A leave is refused, and nothing is saved, if it would overlap another leave of the employee. Under time that cannot be placed in time (such as gaps in the day) cannot become leave.
   - **Shortcut:** "All the under time that has no decision" applies *Penalise* or *Ignore* to every undecided under time line at once. Leave is always decided line by line.
   - **Late and early minutes** are penalised automatically by the schedule's percents, with no decision needed. They accumulate over the period.
   - If a day is calculated again after you decided its line and the minutes change, the line shows **needs a new decision**.
4. **Read the result.**
   - **Balance** = overtime counted − under time remaining.
   - The page ends with **one overtime line**: the balance when it is positive, or the under time left over (a deficit) when it is negative.
   - **Penalties** accumulated are shown beside the balance and are not taken from it.
   - **Unpaid leave** minutes are listed separately.
   - Under time that is ignored, covered by leave or penalised no longer offsets overtime. Each minute is dealt with one way.
5. **Export** the overview as CSV (one line per employee) from the overview page, for the hand-over to pay calculation.
6. **Close the period** for the employee, or for everyone ready at once.
   - On the employee's page, **Close the pay period** closes that one employee.
   - On the overview, **Close all ready periods** closes the period shown for every employee of the list (all pages, and only those matching the search): the one whose period has ended, has days to pay for, and has no not-ready day. The others are left open, and the message names them with the reason (not ended, not ready). Employees with nothing to pay for are skipped, so no empty periods are created, and those already closed are counted.
   - Payroll then stops reading a closed employee's days in the period, and their decisions are locked.
   - Other employees are not affected, and the report stays readable.
   - A single period can be closed only once it has ended and no not-ready day remains.
7. **To change something after closing:** reopen the period (a reason is required and kept in the audit log), fix it, and close it again.

## 4. Worked example

Employee on a 08:00–17:00 shift with a 12:00–13:00 break, paid monthly from the 26th. Late penalty 100%, under time penalty 200%, late grace 10 minutes.

| Day | Punches | Line | Minutes |
|---|---|---|---|
| Mon 5th | 08:00 – 18:30 | Overtime, weekday | 90 |
| Mon 12th | 08:30 – 17:00 | Under time (30), of which 20 are late after the grace | 30 |

- The 12th has 20 penalty minutes automatically (20 late × 100%).
- **Decide:** the 5th *Overtime*; the 12th *Penalise*, which adds the 10 minutes that are not late, at 200%, so 20 more.
- **After the decisions:** overtime counted 90, under time remaining 0 (it was penalised), **balance +90**, one overtime line of 1:30, penalties accumulated 40 minutes.
- **Instead, decide the 12th *Paid leave*:** a leave 08:00–08:30 is added to the employee's leaves, the day is recalculated, and there is no under time left. Balance +90, penalties 0.
- **Or leave the 12th undecided:** under time 30 remains and offsets the overtime, so balance +60, one overtime line of 1:00, penalties 20.

## 5. Cautions

- **Settle exceptions before deciding lines.** A day that is still an exception is not in the report and blocks closing.
- **A leave decision cannot be undone from the report.** To undo one, delete the leave on the *Leaves* screen. The day is recalculated and the under time returns, so it can be decided again.
- **Do not recalculate a closed period.** The system skips closed periods on purpose.
- **A period closed for one employee does not close it for anyone else.** A *company-wide* pay period applies to everyone at once and still works as before, but it is no longer part of the normal workflow: its page is not in the menu, and is reached from the *Compensation* overview (**Company pay periods**) or by its address (`/PayPeriods`). Use it only to hold back everyone's days for some dates together. For the usual month-end, use **Close all ready periods**.
- **Hourly staff** have no shift, so no under time, no late or early minutes and no penalties. Their hours are paid as worked, and they appear in the report as regular hours only.

## Where things are

| Need | Screen |
|---|---|
| Start date, rules, policies, pay rules | *Settings* |
| Pay cycles (default and per employee) | *Payroll → Pay cycles* |
| Overtime, under time, penalties, decisions, closing | *Payroll → Compensation* |
| The approved days payroll may read, and a CSV of them | *Payroll → Payroll* |
| Periods that apply to every employee (rarely needed) | *Company pay periods*, a link on the *Compensation* overview |
| Settling a day | *Attendance → Exceptions* and *Approvals* |
| Leaves (including those a decision added) | *Leaves* |
