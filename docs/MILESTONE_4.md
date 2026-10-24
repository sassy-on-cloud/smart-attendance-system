# Milestone 4: attendance management

Implemented 2026-10-24 (Asia/Katmandu). This milestone extends the approved session/enrollment design and retains the patched PKCS dependency. No frontend files, existing migrations, or persistent/development database records were changed. No migration was applied to the development database. New migration and deployment SQL are prepared for review only.

## Implemented behavior

- Staff can create, list, filter, inspect, reschedule, close, reopen and cancel sessions within their authorization scope. Cancellation preserves all records and is terminal; there are no hard-delete endpoints.
- Admin can manage all courses. Teacher marking, corrections and session management require a course assignment. Staff writes additionally require a positive authenticated account ID belonging to an existing account with the corresponding current role. Forged client actor fields, missing actor identity and mismatched account roles cannot authorize a write.
- Students retain read access only to their own attendance. They cannot mark, correct, manage sessions, or inspect staff audit reasons/history. Admin/assigned Teacher history reads are scoped to their accessible records.
- Marking requires a real verified session in Open state, an existing Student profile linked to a Student account, and a confirmed enrollment in that session's course. The course is derived from the session; clients cannot override it. Composite foreign keys continue enforcing matching student/course/session relationships.
- The `(StudentId, AttendanceSessionId)` unique index remains the durable duplicate guard. Duplicates return 409 and never overwrite a record.
- Manual corrections require an allowed status, a nonblank reason of at most 500 characters, and ExpectedVersion from the latest response. The Student, Course and Session identifiers cannot be changed through corrections.
- Corrections preserve the original Date/RecordedByUserId and append audit history. The latest status/method, modifying actor, timestamp and version are updated atomically. Stale/no-op corrections return 409. A manual review of a previous recognition-labelled record records Manual as the new method and retains the old label in history.
- All mutations expose only explicit DTOs. Method, actor, timestamps, version assignment, legacy metadata and nested EF entities are server-controlled. Client method/recognition-confidence/actor/student-identity claims are not accepted as recognition proof. No AI API/model or recognition implementation was added.

## Session lifecycle and time policy

| State | New marking | Corrections | Management |
| --- | --- | --- | --- |
| Open | Allowed for authorized staff and confirmed enrollment | Allowed with reason/version | Reschedule before any attendance; close or cancel |
| Closed | 409 | Allowed with reason/version | Reopen or cancel; times remain locked once attendance exists |
| Cancelled | 409 | 409 | Terminal; records/history remain readable to authorized users |
| Legacy placeholder | 400 | 400 | Cannot edit into a verified meeting; separate reviewed reconciliation is required |

Open means manual entry is enabled, rather than automatically proving the current time is inside the meeting window. Dates use the existing UTC storage convention. No college-specific lateness cutoff, automatic absence generation, percentage denominator or late-correction deadline was invented. Staff choose Present, Absent, Late or Excused explicitly. Such policies still require college agreement.

Changing session times after any attendance would reinterpret existing evidence, so it returns 409. CourseId is immutable after session creation. Multiple sessions per calendar day remain supported.

## API contracts

All body DTOs reject unknown JSON fields and nested entities. Existing marking requests remain valid.

| Method / route | Contract and response |
| --- | --- |
| `GET /api/attendancesessions` | Staff-scoped array; optional courseId/state filters; skip >= 0, take 1–100 (default 50), deterministic ordering |
| `GET /api/attendancesessions/{id}` | Safe session DTO, 200 or scoped 404 |
| `POST /api/attendancesessions` | `{ courseId, startsAt, endsAt }`; 201 with safe DTO and Location |
| `PUT /api/attendancesessions/{id}` | `{ startsAt, endsAt, state, expectedVersion, reason }`; successful update returns 200 with new version |
| `GET /api/attendancesessions/{id}/history` | Staff-scoped chronological session history DTOs; reasons and actors included |
| `POST /api/attendances` | `{ studentId, attendanceSessionId, status }`; 201 with safe DTO and Location |
| `PUT /api/attendances/{id}` | `{ status, expectedVersion, reason }`; successful correction returns 200 with new version |
| `GET /api/attendances/{id}/history` | Admin/assigned Teacher; chronological creation/correction history DTOs |
| Existing attendance GET routes | Same ownership rules and safe fields, with added actor/time/version metadata |

Example correction:

```json
{
  "status": "Late",
  "expectedVersion": 1,
  "reason": "Corrected after checking the class register."
}
```

Session DTOs add State, Version, CreatedByUserId, CreatedAtUtc, LastModifiedByUserId and LastModifiedAtUtc (camelCase in JSON). Attendance DTOs add Version, RecordedByUserId, LastModifiedByUserId and LastModifiedAtUtc. Raw User entities, hashes, credentials and signing keys never enter these DTOs. History DTOs include stable actor IDs, times, old/new values, reasons and versions; they do not include User entities or credentials.

HTTP behavior:

- 400: invalid body/ID/format/status/reason/version, unconfirmed/missing enrollment, invalid Student account link, legacy placeholder mutation, or overposting.
- 401: missing/invalid JWT.
- 403: role disallowed, invalid/current-role-mismatched staff identity, or unassigned Teacher write.
- 404: nonexistent student/session/attendance/course for an authorized operation, or inaccessible detail/history read. Missing students/sessions on marking now return 404 instead of the previous generic 400.
- 409: duplicate attendance, stale expected version, no-op update, marking a non-Open session, correction on a Cancelled session, editing a Cancelled session, or changing times after marks exist.

## Atomicity, concurrency and audit integrity

Marking, corrections and session edits start an explicit transaction and acquire the same session-row write gate before reading mutable state or writing records. A no-op database UPDATE takes the row lock without changing the public session version. The transaction holds it until commit/rollback. This serializes competing marks and coordinates marking with cancellation/rescheduling. Different sessions can proceed independently.

Once gated, marking rechecks eligibility and duplicates, then writes Attendance and its creation-history row in one transaction. The existing unique index still rejects duplicate writes made outside the service. Corrections and session edits require matching application-managed concurrency versions; EF concurrency tokens and unique history/version keys prevent lost updates. Failed operations, including failures after SaveChanges but before transaction commit, leave no partial attendance/history write.

AttendanceHistory stores creation/correction actor, timestamp, reason, old/new status and method, and resulting version. AttendanceSessionHistory stores creation/edit actor, reason, state/time changes and resulting version. No public route edits or deletes history. This is append-only at the API boundary, not a guarantee against a privileged DBA modifying tables directly. NO ACTION foreign keys retain referenced actors and prevent cascade deletion of history.

The correctness tests exercise real SQL Server locks and separate HTTP request scopes/connections; SQLite-only tests are not used as proof of concurrent SQL Server behavior. The shared SQL test collection runs sequentially against one disposable instance to avoid launching multiple 2 GiB SQL containers concurrently.

## Additive migration and preservation

New migration: `20261024065458_AttendanceManagementAudit`.

- Adds nullable actor/time links to existing Attendance and AttendanceSession rows.
- Adds positive integer Version with default 1 and a concurrency token on each entity.
- Adds session State, initially Open to preserve the previous manually enabled behavior. Existing legacy placeholders remain explicitly flagged and unmodifiable.
- Adds AttendanceHistory and AttendanceSessionHistory tables with required actor FKs, bounded reason/value fields, unique entity/version indexes and restrictive delete relationships.
- Does not delete or rewrite original attendance values, fabricate unknown historical actors, or invent history for existing rows. Historical actor fields remain null until a real subsequent modification records its own actor. A first correction on an old version-1 record creates version-2 history; it does not assert a known original version-1 actor.
- Existing migration files are unchanged. Down throws instead of discarding audit information; recovery needs an approved restoration/forward-correction plan.

Before deployment, review and approve the SQL, confirm the current migration baseline, take/verify a backup, rehearse on a disposable restoration of actual data, and coordinate backend deployment with the schema update. If Milestone 3 is not applied, its reviewed preservation migration is a prerequisite. The actual database's migration history was not queried here. Never point test fixtures at the development database.

Generate review artifacts offline using the existing design-time factory:

```bash
dotnet build SmartAttendanceSystem.sln --no-restore
dotnet ef migrations has-pending-model-changes --project SmartAttendance.API --no-build
dotnet ef migrations script 20261024061936_SessionAttendanceIntegrity \
  20261024065458_AttendanceManagementAudit --project SmartAttendance.API --no-build \
  --idempotent --output /tmp/smartattendance-milestone4.sql
```

The repository also contains the generated upgrade SQL under `docs/migrations/`. This session did not execute it against a persistent database. Obtain explicit approval before applying a reviewed migration to the project database.

## Changed areas

- Attendance/session controllers and services: management, permissions, DTO mapping, lifecycle, transactions, corrections and concurrency results.
- AttendanceAccess: verifies current staff account role and course assignment.
- Attendance/AttendanceSession models, new history models, EF configurations and AppDbContext: provenance, optimistic versions, audit relations and durable constraints.
- Management/history request/response DTOs and explicit projections: safe contracts and validation.
- Additive migration, designer and snapshot; generated SQL and this review document.
- Existing security/integrity tests: updated response metadata, missing-resource status expectations, migration count and shared SQL fixture setup.
- New management HTTP tests and SQL Server concurrency/atomicity tests.

## Verification

Run the full suite explicitly with SQL opt-in:

```bash
dotnet build SmartAttendanceSystem.sln --no-restore
SMARTATTENDANCE_RUN_SQLSERVER_TESTS=1 dotnet test \
  tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj \
  --no-build --no-restore
dotnet list SmartAttendanceSystem.sln package --vulnerable --include-transitive --no-restore
git diff --check
```

SQL tests use only the fixture-created disposable SQL Server container, a random loopback port/password, and random databases. They apply migrations only there and remove their own container afterward. Without opt-in, all 17 SQL tests are explicitly skipped; do not report a default test run as full SQL verification.

Final verification results:

- Build passed with 0 warnings and 0 errors.
- All 215 tests executed and passed: 0 failed, 0 skipped. This includes all 17 SQL Server tests (nine existing database tests and eight new HTTP concurrency/audit/migration tests).
- SQL tests verified 12 simultaneous HTTP submissions produce exactly one 201, eleven 409 responses, one attendance and one creation audit record; competing corrections and session edits have one winner; cancellation/rescheduling races respect a consistent serial order; an injected failure after SaveChanges rolls back attendance and history together.
- Additive-upgrade tests preserve previous records and leave unknown actors null; full migration installation and generated idempotent script reruns pass on disposable SQL Server.
- The EF model-drift check passed, and the offline idempotent Milestone 4 SQL script was generated.
- NuGet audit reports no vulnerable packages in either project using current sources; no package references were changed in this milestone.
- `git diff --check` passed. The disposable SQL Server container was removed; existing migration files and frontend files have no changes.

The original development database remains untouched and the new migration is unapplied there. No commit or push was performed.

Remaining limits: no live-data migration rehearsal, frontend/recognition end-to-end test, or production-scale load test. Teacher assignment/enrollment revocation and academic-offering/term policies remain separate work; direct SQL changes must preserve those semantics. College-specific attendance timing/reporting rules and approved recognition authentication remain pending. API history is protected by authorization and transaction boundaries, not by a tamper-proof external audit store.
