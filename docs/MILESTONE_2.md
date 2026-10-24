# Milestone 2: API request and response security

Implemented 2026-10-24. The initial audit remains in `DEVELOPMENT_ROADMAP.md`; this milestone addresses DTO boundaries, validation, student attendance privacy, and automated security tests. No SQL Server connection, migration, schema change, persistent database mutation or frontend change was made.

## Changed files

- `SmartAttendance.API/Controllers/{Users,Students,Courses,Attendances}Controller.cs`: explicit DTO projections and mappings, validation of route IDs, cancellation support, and existing role restrictions. Attendance reads additionally enforce student ownership and reject unknown roles.
- `SmartAttendance.API/DTOs/CreateStudentDto.cs`, `CreateCourseDto.cs`, `CreateAttendanceDto.cs`: request allowlists with validation and rejection of unknown JSON members.
- `SmartAttendance.API/DTOs/{Attendance,Course,Login}ResponseDto.cs`: explicit response contracts.
- `SmartAttendance.API/DTOs/ResponseProjections.cs`: shared SQL expression projections for safe fields.
- `SmartAttendance.API/DTOs/{LoginDto,RegisterUserDto}.cs`: required fields, email/length validation, role allowlist and unknown-field rejection.
- Existing, pre-uncommitted `StudentResponseDto.cs` is retained and used; its existing safe field selection is preserved. Student create now returns populated Name/Email, matching GET.
- `SmartAttendance.API/Models/User.cs`: serialization-only `[JsonIgnore]` on PasswordHash as a secondary safeguard; EF mapping and schema are unchanged.
- `SmartAttendance.API/Program.cs`: generic exception responses in every environment; public partial entry point for HTTP tests.
- `SmartAttendanceSystem.sln`: groups API and test project for root build/test discovery.
- `tests/SmartAttendance.IntegrationTests/`: test project, isolated factory, HTTP/serialization tests. Test-only dependencies were added; production package references were not changed.
- This document records contracts and verification.

## Request contracts and compatibility

All JSON request DTOs reject unknown properties with HTTP 400. Callers must stop sending EF entities, nested navigation properties, primary keys, password hashes or other fields outside the contracts below. Explicit mapping creates fresh entities from allowed values only.

| POST route | Accepted fields | Validation / server behavior |
| --- | --- | --- |
| `/api/users` | name, email, password, role | Admin-only. Nonblank name (max 100); valid email (max 254); password 8–128 characters; role exactly Admin, Teacher or Student (omitted role defaults to Student). PasswordHash and userId are rejected. Password is hashed, never echoed. |
| `/api/users/login` | email, password | Anonymous login preserved. Valid nonblank email (max 254) and nonblank password (max 128); arbitrary role/userId/hash fields are rejected. Existing generic 401 text is preserved for wrong/missing accounts. |
| `/api/students` | userId, rollNumber, department, semester | Admin-only. Positive existing UserId belonging to a Student account; existing profile returns 409. Roll number max 50, department nonblank/max 100, semester positive. No college-specific semester maximum is invented. Nested User and studentId/role/hash fields are rejected. |
| `/api/courses` | courseName, courseCode | Admin/Teacher. Nonblank course name max 200; code max 30. courseId and navigation fields are rejected. |
| `/api/attendances` | studentId, courseId, date, status | Admin/Teacher. Positive existing student/course IDs; required ISO-8601 timestamp parsed as DateTimeOffset, converted to UTC for storage; minimum/default date rejected. Status exactly Present, Absent or Late. Method is server-set to Manual, and supplying method/attendanceId/student/course/recordedBy is rejected. |

Roll numbers and course codes must start with an ASCII letter/digit and contain only ASCII letters/digits, period, underscore, slash or hyphen. Names/departments are trimmed on storage; codes must already match the format. Passwords are not trimmed. Emails are trimmed but existing database collation/case behavior is preserved. Field size limits are API validation only; no column definitions changed.

All four detail routes require positive integer IDs; zero, negative, noninteger and overflow IDs return 400 for authorized callers. Unknown positive IDs return 404. MVC validation/JSON errors return ValidationProblemDetails. Existing controlled domain errors remain simple messages; unexpected failures return generic 500 ProblemDetails without exception/configuration details, including in Development.

These limits and the initial status vocabulary should be reviewed with the college before broader attendance rules are implemented. Clients should send timestamps with `Z` or an explicit offset to avoid ambiguity. There is no session/late-window/future-date policy in this milestone. The route records manual attendance only; it does not claim recognition provenance or introduce an AI dependency. Existing records and their Method values are unchanged.

## Response contracts

JSON uses the existing camelCase convention. Every successful resource response uses only these fields:

| Resource | Fields |
| --- | --- |
| User | userId, name, email, role |
| Student | studentId, userId, rollNumber, name, email, department, semester |
| Course | courseId, courseName, courseCode |
| Attendance | attendanceId, studentId, courseId, date, status, method |
| Login | token, user (safe User DTO above) |

List endpoints return arrays of the corresponding DTO. Create endpoints return 201 with a Location and the same DTO shape/values as the subsequent GET. Login retains its intentional access-token response; a JWT access token is distinct from the private JWT signing key, which is never returned.

Attendance responses no longer contain nested Student/Course graphs. Authorized clients needing names/course labels must resolve IDs through their permitted lookup endpoints; Student access to `/api/students` remains forbidden as before. No response DTO contains User entities, hashes, passwords, connection credentials or signing keys. User list/detail queries project safe columns instead of loading hashes. Login alone loads the stored hash for authentication.

## Authorization

- Existing Admin-only user management and student creation are preserved.
- Existing Admin/Teacher student reads, Admin/Teacher course/attendance creation, and Admin/Teacher/Student course reads are preserved.
- Students can list only attendance attached to their authenticated account's Student profiles. Filtering occurs in the database query via `Student.UserId` and the validated token's NameIdentifier, not via request parameters or a coincidentally matching StudentId.
- Foreign attendance detail and nonexistent attendance both return 404. Missing, malformed, nonpositive or unlinked Student identities cannot see attendance. Unknown roles are forbidden. Query parameters cannot widen scope.
- Admin/Teacher attendance access remains global because no teacher/course assignment model exists. Course-level teacher authorization is a separate milestone requiring a reviewed model; no schema was invented here.

## Test isolation and commands

The xUnit suite uses WebApplicationFactory, the real MVC pipeline, real JWT bearer validation, and EF Core's relational SQLite provider over a private `:memory:` connection. It replaces local configuration with generated/synthetic test settings, removes the SQL Server DbContext registration, verifies provider/connection identity before resetting its disposable database, and uses synthetic users. No test connects to the project's SQL Server or calls existing SQL Server migrations. SQLite setup creates/resets only the ephemeral test schema.

The suite covers all read contracts; login/create responses; password-hash serialization defense; nested graph and server-field rejection with unchanged row counts; required/format/length/ID validation; student profile account checks; foreign-key existence checks; anonymous/Admin/Teacher/Student/unknown-role authorization across every list/detail/create route; real login tokens; invalid signature/issuer/audience/expired/malformed JWTs; both students' ownership boundaries; invalid/unlinked identities; staff access; and generic Development error responses under a synthetic secret-bearing exception.

Run from the repository root:

```bash
dotnet restore SmartAttendanceSystem.sln
dotnet build SmartAttendanceSystem.sln --no-restore
dotnet test tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj \
  --no-build --no-restore --logger 'console;verbosity=minimal'
dotnet list SmartAttendanceSystem.sln package --include-transitive --vulnerable --no-restore
git diff --check
```

Final verification:

- Restore succeeded. Production package references remained unchanged.
- Solution build passed with 0 errors and 4 warnings: the same NU1902/NU1903 PKCS warnings inherited by API and test project.
- All 143 tests passed; 0 failed and 0 skipped. The initial test-guard connection check and one test's plain-text login-error expectation were corrected before the final passing run.
- Dependency audit completed and still reports only the existing PKCS 9.0.11 high/moderate findings in the resolved API/test graphs.
- `git diff --check` passed; new DTO/test/report files were separately checked for trailing whitespace.
- Git diff confirms no changes to AppDbContext, migrations, production project dependencies or tracked frontend files. The pre-existing frontend `.gitkeep` remains untouched.

## Remaining issues and tests not run

- No SQL Server provider, migration, collation, deployment or shared-database tests were run. SQLite HTTP tests establish API behavior but do not prove SQL Server-specific behavior. Frontend/recognition end-to-end tests were not run; those implementations are absent here.
- Duplicate email/profile checks are still application pre-checks; race-safe uniqueness needs reviewed database constraints. Duplicate attendance/session/enrollment rules remain open. No applied migration or existing record was changed.
- Teacher course assignment, pagination, audit history, corrections, token revocation, rate limiting, first-admin provisioning, secret-strength startup validation, production CORS/certificate configuration and broader password policy remain roadmap work.
- The existing PKCS 9.0.11 high/moderate advisories remain unresolved; see the audit's linked advisories. Production package upgrades and host runtime remediation are outside this request/response milestone.
- No AI model/API or recognition implementation was added. No commit or push was performed.
