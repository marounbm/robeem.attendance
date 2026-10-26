-- Empties the attendance and payroll data of the robeem_demo database, so the same files can be imported again.
--
-- Run it against robeem_demo only, with the Api stopped (its normalization job would otherwise keep working while the rows go):
--   psql -h localhost -U postgres -d robeem_demo -f scripts/reset-demo-attendance.sql
--
-- DELETED   raw and normalized punches, the import batches and the record of uploaded files, the calculated days (records,
--           day summaries), their exceptions, corrections and review decisions, and the pay periods.
-- KEPT      employees, schedules, policies, providers, file formats, device types, integrations, devices, employee-id mappings,
--           the audit log and the job-run history.
--
-- It is one transaction: it either empties all of it or none. There is no undo, so take a backup first if you may want the data back
-- (pg_dump -h localhost -U postgres -Fc -f robeem_demo.backup robeem_demo).

BEGIN;

-- Refuse to run anywhere but the demo database.
DO $$
BEGIN
    IF current_database() <> 'robeem_demo' THEN
        RAISE EXCEPTION 'This script empties robeem_demo, but it is connected to %. Nothing was changed.', current_database();
    END IF;
END
$$;

SELECT 'before' AS "when",
       (SELECT count(*) FROM "Attendance"."RawAttendanceTransaction") AS raw_punches,
       (SELECT count(*) FROM "Attendance"."NormalizedAttendanceTransaction") AS normalized_punches,
       (SELECT count(*) FROM "Attendance"."AttendanceRecord") AS records,
       (SELECT count(*) FROM "Attendance"."AttendanceDaySummary") AS day_summaries,
       (SELECT count(*) FROM "Attendance"."AttendanceException") AS exceptions,
       (SELECT count(*) FROM "Attendance"."AttendanceCorrection") AS corrections,
       (SELECT count(*) FROM "Attendance"."AttendanceReviewDecision") AS review_decisions,
       (SELECT count(*) FROM "Attendance"."AttendanceProcessingBatch") AS batches,
       (SELECT count(*) FROM "Attendance"."AttendanceUploadedFile") AS uploaded_files,
       (SELECT count(*) FROM "Attendance"."PayPeriod") AS pay_periods;

-- One statement, so the foreign keys between these tables need no CASCADE. A table outside the list that referred to one of them would
-- make PostgreSQL refuse, and nothing would be deleted.
TRUNCATE TABLE
    "Attendance"."AttendanceReviewDecision",
    "Attendance"."AttendanceCorrection",
    "Attendance"."AttendanceException",
    "Attendance"."AttendanceRecord",
    "Attendance"."AttendanceDaySummary",
    "Attendance"."NormalizedAttendanceTransaction",
    "Attendance"."RawAttendanceTransaction",
    "Attendance"."AttendanceUploadedFile",
    "Attendance"."AttendanceProcessingBatch",
    "Attendance"."PayPeriod"
    RESTART IDENTITY;

SELECT 'after' AS "when",
       (SELECT count(*) FROM "Attendance"."RawAttendanceTransaction") AS raw_punches,
       (SELECT count(*) FROM "Attendance"."NormalizedAttendanceTransaction") AS normalized_punches,
       (SELECT count(*) FROM "Attendance"."AttendanceRecord") AS records,
       (SELECT count(*) FROM "Attendance"."AttendanceDaySummary") AS day_summaries,
       (SELECT count(*) FROM "Attendance"."AttendanceException") AS exceptions,
       (SELECT count(*) FROM "Attendance"."AttendanceCorrection") AS corrections,
       (SELECT count(*) FROM "Attendance"."AttendanceReviewDecision") AS review_decisions,
       (SELECT count(*) FROM "Attendance"."AttendanceProcessingBatch") AS batches,
       (SELECT count(*) FROM "Attendance"."AttendanceUploadedFile") AS uploaded_files,
       (SELECT count(*) FROM "Attendance"."PayPeriod") AS pay_periods;

-- Nothing is final until this line. To look first and decide, replace COMMIT with ROLLBACK.
COMMIT;
