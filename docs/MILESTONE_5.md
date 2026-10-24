# Milestone 5: attendance reports and statistics

Implemented 2026-10-24 (Asia/Katmandu). This is a read-only reporting layer over the existing session, enrollment, attendance and authorization models. It does not add a schema change, generate a migration, or modify the frontend. Milestone 3/4 migrations remain unapplied to the development database; no connection or migration operation targeted that database. All database verification uses private disposable databases.

## Calculation policy: closed-past-v1

The policy is returned by `GET /api/reports/policy`, described on every reporting Swagger operation, and identified by PolicyVersion/AsOfUtc on report results.

```text
PresentSessions = OnTimePresentSessions + LateSessions
TotalEligibleSessions = ClosedPastSessions - ExcusedSessions
Attendance Percentage = PresentSessions / TotalEligibleSessions * 100
```

A student-session opportunity exists for a confirmed Student/Course enrollment and a verified, Closed meeting whose end time is on or before the request's AsOfUtc. The enrollment's linked account must have Student role. The current schema has no enrollment start/end dates or academic offering terms, so a confirmed enrollment applies to all filtered meetings of that course; historical eligibility cannot be prorated from information that is not stored.

| Condition | Numerator | Denominator / reporting treatment |
| --- | --- | --- |
| Present, eligible meeting | +1 | +1 |
| Late, eligible meeting | +1 | +1; explicitly treated as attended, with LateSessions separately exposed |
| Absent, eligible meeting | +0 | +1 |
| Excused, eligible meeting | +0 | Excluded for that student only; other students in the same meeting are unaffected |
| No record, eligible meeting | +0 | +1, counted as Unmarked; never inferred as Present |
| Unverified status on an otherwise eligible verified meeting | +0 | +1, separately counted as Unverified |
| Open meeting, even if all recorded marks are Present | +0 | Excluded as incomplete; manual marking remains independent from reporting completion |
| Closed meeting whose end is still in the future | +0 | Excluded until its end time passes |
| Cancelled meeting | +0 | Excluded, including retained Present records |
| Missing start/end time | +0 | Excluded; valid verified rows normally cannot have missing times because of existing constraints |
| Legacy placeholder | +0 | Always excluded, even when its retained status says Present; never counted as a real class |
| Inferred historical enrollment | +0 | Excluded until explicitly confirmed through the existing Admin workflow |

Session completeness is defined by verified identity, Closed state and elapsed end time, **not by whether every student has been marked**. A partially marked Closed meeting still counts for its enrolled students; missing marks do not cause the denominator to shrink. No record is automatically changed to Absent.

If the denominator is zero, Percentage is null rather than zero or 100. Percentages are calculated with decimal arithmetic and rounded to two decimal places, midpoint away from zero. Sorting by percentage uses the unrounded ratio in SQL; zero-denominator values sort first ascending and last descending, with stable ID tie-breakers.

Current Attendance rows are the source of status/provenance. Correction-history entries are not extra classes or extra marks. A correction changes the report's current result without duplicating the numerator/denominator. Staff can still use Milestone 4's audit-history endpoints for provenance; reporting does not expose staff-only correction reasons to students.

Overall and course/teacher summaries use **summed student-session counts**, not an average of rounded percentages. For example, 2/5, 1/5 and 1/1 aggregate to 4/11 = 36.36%, not the average of 40%, 20% and 100%. In aggregated DTOs, ClosedPastSessions/TotalEligibleSessions are student-session opportunities; CompletedMeetings is a distinct class-meeting count.

This is an explicit v1 policy for review. Any college change to late credit, excused treatment, session completion or enrollment periods should update the policy version, documentation and tests together.

## Endpoints

Swagger UI remains available in Development at `/swagger`. All new endpoints are under the Attendance reports tag and document successful response schemas, 400/401/403/404 responses, role scope, date semantics, filtering and the formula.

| GET route | Result / authorization |
| --- | --- |
| `/api/reports/policy` | Machine-readable calculation policy; authenticated recognized role/account |
| `/api/reports/attendance` | Scoped current attendance rows with course/student/date/session/status/method filters |
| `/api/reports/students/{studentId}/history` | One student's attendance history; Student only self, Teacher only within assigned courses, Admin permitted administrative scope |
| `/api/reports/courses/{courseId}/records` | Course-wise records; Student receives only own records, not the class roster |
| `/api/reports/sessions/{attendanceSessionId}/records` | One actual meeting's recorded attendance, with the same row-level scope |
| `/api/reports/percentages` | Paged student/course enrollment statistics, including confirmed enrollments with zero eligible sessions |
| `/api/reports/overall` | Weighted statistics across all filtered authorized enrollments; no paging or record-status denominator filtering |
| `/api/reports/teachers/me/summary` | Teacher-only paged summaries of assigned courses |
| `/api/reports/teachers/{teacherUserId}/summary` | Teacher only self; Admin can inspect a Teacher account's assigned-course summaries |
| `/api/reports/admin/dashboard` | Admin-only global entity totals plus filtered meeting/record counts and weighted statistics |

Route StudentId is a Student profile key; TeacherUserId is an account key. They are not interchangeable. A route ID and corresponding query ID must agree if both are supplied.

All records endpoints return existing records only. They do not fabricate missing attendance rows. To identify missing attendance per student in a class meeting, query `/api/reports/percentages?attendanceSessionId=42`; eligible enrollments with no record have UnmarkedSessions = 1 and PresentSessions = 0. Never calculate a percentage from the records page's TotalCount: that count includes historical/incomplete records and omits missing records.

Record DTOs project approved fields only: attendance/student/course/session identifiers, student name/roll number, course labels, meeting times/state, status/method, recorded time, legacy flag and IsEligible. They contain no User entity, email, password hash, signing key, connection credential or correction reason. A method label is historical provenance, not proof that recognition ran. No AI model/API or recognition implementation was added.

## Filtering, dates and pagination

Common filters: positive studentId, courseId, attendanceSessionId; optional fromDate/toDate (`YYYY-MM-DD`).

- Calendar dates refer to **meeting start in UTC**, using inclusive fromDate and inclusive toDate implemented as a half-open range ending at the next midnight. They do not use Attendance.Date/recording time or the execution host's local timezone.
- Separate sessions on the same date remain separate opportunities. A meeting crossing midnight belongs to its start date.
- Legacy placeholders have unknown meeting dates. They are included and flagged in unfiltered records by default, can be removed with includeLegacy=false, and cannot match a date range. A date-filtered legacy count of zero does not prove that no historical rows exist.
- Raw RecordedAt on legacy rows retains the original Date semantics/timezone uncertainty. It is never used as a fallback class date.
- Invalid/reversed dates and `toDate=9999-12-31` return 400; the latter cannot be represented as an exclusive next-day bound.

Records additionally accept status, method, includeLegacy (default true), page (default 1), pageSize (1–100, default 25), sortBy (`sessionDate`, `studentId`, `courseId`, `status`, `recordedAt`) and direction (`asc`/`desc`, default desc). AttendanceId is the final stable tie-breaker.

Student/course percentages accept the common filters, page/pageSize, sortBy (`studentId`, `courseId`, `percentage`, default courseId) and direction (default asc). CourseId and StudentId provide stable tie-breakers. Teacher summaries accept courseId/percentage sorting. TotalCount is computed before paging; a page beyond the result returns an empty Items array without integer-offset overflow.

Status/method/includeLegacy affect **records only**. Statistical endpoints intentionally do not use those query values to narrow eligibility; adding `status=Present` to an overall request does not turn the result into 100%. Clients should send only the documented parameters. Percentage date/session/course/student filters are applied before aggregation and independently of list pagination.

Dashboard fields named GlobalUsers/GlobalStudents/GlobalTeachers/GlobalCourses are all-time entity counts. GlobalStudents counts Student profiles; GlobalTeachers counts Teacher-role accounts. Meeting/record counts use the common course/session/date filters. A studentId filter narrows record counts and attendance statistics, not global or meeting totals. Global totals are never returned to Teachers or Students.

## Authorization and privacy

Every report verifies the authenticated account exists and its current recognized role agrees with its JWT. Missing/invalid account identity and stale/mismatched roles return 403; absent/invalid JWTs return 401.

Role constraints are composed into EF queries before counts, aggregates, sorting and paging:

- Student scope uses Student.UserId from the authenticated account, not a client-supplied identity. A studentId filter cannot widen it.
- Teacher scope uses TeacherCourseAssignments. Student/course/session filters outside that scope return 404, and Teacher cannot request another Teacher's summary. An unassigned Teacher receives empty scoped reports rather than global data.
- Admin can read administrative reports across the existing application scope. No multi-tenant/department boundary is invented where the model has none.
- Inaccessible and nonexistent targeted resources both return 404. Unknown roles and disallowed dashboard/teacher-summary role requests return 403.

## Query implementation and efficiency

The scoped service captures AsOfUtc once per request for consistent eligibility time. This is an eligibility cutoff, not a transactional database snapshot; concurrent legitimate corrections can change data between requests or between a count and page query.

Statistics join confirmed enrollments to eligible sessions and **left join** current attendance on the unique student/session key, preserving missing marks. Grouped counts, overall sums, filtering, ordering and pagination execute in SQL. Empty enrollment/course results are retained through a second left join, with null-to-zero count handling. Only the bounded response page and decimal rounding are materialized in memory.

Read queries use AsNoTracking or scalar aggregates and explicit DTO projections. No Include-based EF graph is serialized, no password hash is selected for reporting, and no per-row database request is used. Existing enrollment, student/session, session/course/date and assignment keys/indexes support these queries; no migration or index change was needed.

A SQL Server command-interceptor test verifies that a paged percentage request uses bounded query count (at most four commands in the tested unfiltered case), contains server-side GROUP BY and FETCH NEXT, and does not select PasswordHash. SQL tests also execute the nested overall/teacher/dashboard aggregates against the actual provider; SQLite translation alone is not treated as sufficient evidence.

## Changes

- New AttendanceReportsController, report filter/result DTOs, AttendanceReportService and typed request scope.
- New Swagger operation documentation attribute/filter; Program.cs registers the report service and Swagger filter.
- Test-only clock injection and an empty SQLite initializer retaining the existing fail-closed provider/connection guard. Existing fixture seeding and all 215 prior tests remain intact.
- New shared reporting data, HTTP calculation/privacy/filter tests, Swagger contract test, and disposable SQL Server reporting tests.
- This document records policy and operating limits. No models, EF configurations, migrations, dependencies, existing application controllers or frontend files were changed.

## Verification commands

```bash
dotnet build SmartAttendanceSystem.sln --no-restore
SMARTATTENDANCE_RUN_SQLSERVER_TESTS=1 dotnet test \
  tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj \
  --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project SmartAttendance.API --no-build
dotnet list SmartAttendanceSystem.sln package --vulnerable --include-transitive --no-restore
git diff --check
```

SQL tests accept no development connection string. They use only the fixture's newly created private SQL Server container, random loopback port/password and random databases; all migrations applied in tests target that container. It is removed afterward. Without opt-in, the SQL tests are explicitly skipped and must not be reported as passing.

Final verification results:

- Build passed with 0 warnings and 0 errors.
- All 252 tests executed and passed: 0 failed, 0 skipped, including all 23 disposable SQL Server tests. The original 215 cases remain present and passing, with 37 added reporting/Swagger cases. The prior/new TRX comparison includes theory-case multiplicity rather than deduplicating display names.
- The six new SQL Server reporting cases cover real-provider aggregates, weighted totals, empty data, paging/sorting/date filters, query efficiency, exact end-time eligibility, corrections and row-level authorization. The remaining 17 SQL tests preserve all prior constraint/concurrency/audit coverage.
- Swagger contract verification passed for all ten reporting paths, successful DTO schemas, error responses, policy descriptions and query documentation. The initial Swagger contract failure was fixed by declaring explicit successful response schemas; it was not hidden or skipped.
- A regression test confirms additional JWT Admin claims cannot grant a current Student access to global dashboard totals; ordinary reports remain scoped to the account's current role.
- Model-drift check passed: no model change since the existing migration. No migration was generated or applied to the development database.
- NuGet audit reports no vulnerable packages in either project using current sources.
- Whitespace checks passed; the disposable SQL Server container was removed. Existing models/configurations/migrations, production dependencies, existing application controllers/services and frontend files have no changes.

SQL tests are deliberately opt-in. A default run without SMARTATTENDANCE_RUN_SQLSERVER_TESTS=1 skips all 23 SQL cases and does not reproduce the complete verification result above.

Not run: deployment or queries against the development database, actual-data migration rehearsal, frontend/recognition integration, or production-scale load/query-plan benchmarking. Milestone 3 and 4 still require the user's separate approval before any development-database migration. Stop after this reporting milestone; no commit or push.
