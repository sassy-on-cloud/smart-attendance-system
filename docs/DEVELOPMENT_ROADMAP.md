# Smart Attendance System development roadmap

Audit date: 2026-10-24 (Asia/Katmandu). Scope: repository inspection and documentation only. Tasks below are future work, not changes authorized or performed by this audit.

## Audit baseline and limits

- Workspace: `~/SmartAttendanceSystem`; branch: `feature/backend-completion`; HEAD: `4ca6537`.
- Pre-existing work: modified `SmartAttendance.API/Controllers/StudentsController.cs`, untracked `SmartAttendance.API/DTOs/StudentResponseDto.cs`, and untracked `SmartAttendance.Frontend/` containing only `.gitkeep`. Preserve these changes. Findings describe the working tree unless explicitly referring to HEAD.
- Inspected all four controllers, four models, four DTOs, the token service, `Program.cs`, `AppDbContext`, both migrations and their designers, the model snapshot, project dependencies, local/example configuration, launch profiles, HTTP sample, Git ignore/status/history paths, and cached dependency graph.
- One ASP.NET Core API project targeting `net10.0`; no solution, automated test project, CI workflow, README, recognition implementation, or frontend implementation found. No inherited `AGENTS.md` was found in the workspace or its ancestor directories.
- Installed SDK: 10.0.112; installed ASP.NET Core/.NET runtime: 10.0.12; global `dotnet-ef`: 10.0.11. No repository SDK pin, package lock, or local tool manifest.
- No API process was started and no database connection, SQL query, migration application, seed operation, or HTTP mutation was performed. Actual database state, applied migration history, production configuration, and runtime exploitability remain unverified.
- Secret values were not copied into this document. Checking configuration paths in reachable Git history found example configuration paths, not tracked live `appsettings.json`; this is not an exhaustive historical secret scan.

## Existing architecture and implemented features

`Program.cs` registers MVC, JWT bearer authentication, authorization, SQL Server `AppDbContext`, Identity `PasswordHasher<User>`, `TokenService`, and Swashbuckle. HTTPS redirection precedes authentication/authorization. Swagger UI/document generation is enabled only in Development and includes a bearer scheme.

Controllers call EF Core directly. The only service is token creation; there is no attendance/domain service or recognition adapter. `AppDbContext` exposes Users, Students, Courses, Attendances and relies entirely on EF conventions.

| Route | Existing behavior | Existing access |
| --- | --- | --- |
| `POST /api/users/login` | Email lookup, hashed-password verification, returns `{ token, user }` | Anonymous |
| `GET /api/users`, `GET /api/users/{id}` | DTOs exclude password hash | Admin |
| `POST /api/users` | Email pre-check, hashes password, returns DTO and Location | Admin |
| `GET /api/students`, `GET /api/students/{id}` | Working tree projects safe DTOs with name/email; no tracking | Admin, Teacher |
| `POST /api/students` | Checks user existence, discards nested User, returns DTO | Admin |
| `GET /api/courses`, `GET /api/courses/{id}` | Returns course entities | Admin, Teacher, Student |
| `POST /api/courses` | Inserts course entity | Admin, Teacher |
| `GET /api/attendances`, `GET /api/attendances/{id}` | Returns entities including Student and Course | Any authenticated user |
| `POST /api/attendances` | Checks supplied student/course IDs exist, inserts entity graph | Admin, Teacher |

JWTs include user ID, name, email, and role; use HMAC-SHA256 and a two-hour expiry. Validation enables issuer, audience, signature and lifetime checks. Login failures use the same message for missing user and incorrect password. User creation is protected; there is no public registration route.

Implemented database structure: identity primary keys; required FKs Student→User and Attendance→Student/Course; ordinary FK indexes; cascade deletes. `InitialCreate` creates all four tables. `AddAttendanceMethod` adds the required Method column. Designers/snapshot appear structurally consistent with these models by static inspection; no automated model-drift or live-schema check was performed.

Incomplete capabilities: enrollment and teacher assignments, class sessions, attendance rule enforcement, correction/history workflows, reports/percentages, self-service student attendance, pagination/filtering, secure first-admin provisioning, account lifecycle, integration contracts, and automated tests. Update/delete routes do not exist; agree whether these are needed rather than assuming full CRUD is a college requirement.

## Findings and evidence

Severity denotes potential project impact. P0 blocks exposure/integration with real data; P1 is core correctness/security; P2 is completion/maintainability. Static risks are identified explicitly and were not exploited.

### Security and data exposure

| ID | Priority | Evidence | Finding and consequence |
| --- | --- | --- | --- |
| SEC-01 | P0 | `Controllers/AttendancesController.cs:12-46` | Both attendance reads require authentication only and apply no ownership filter. A Student, or any token with an unrecognized role, can request every student's attendance and included student details. Teachers are also unrestricted across courses. Confirm intended scope with college; default to own records for students and assigned courses for teachers. |
| SEC-02 | P0 | `Controllers/AttendancesController.cs:52-72`; `Models/Attendance.cs`; `Models/Student.cs`; `Models/User.cs` | POST binds EF entities with writable Student/Course/User navigation graphs. FK existence pre-checks do not sanitize those graphs. EF Add traverses supplied entities; relationship fixup may replace checked IDs with newly inserted related rows. A Teacher may thereby insert a nested User with arbitrary Role/PasswordHash, bypassing Admin-only user creation. High-impact static risk requiring an isolated regression test; no exploit executed. Client-controlled primary keys can also cause identity-insert errors. |
| SEC-03 | P0 | `Program.cs:25-31`; local configuration (ignored) | Local JWT key is placeholder-like rather than a suitable production secret. HMAC byte length alone does not establish entropy. Anyone who knows a deployed signing key can forge roles. Example key is only 27 UTF-8 bytes, below the HS256 32-byte minimum, and is not runnable as-is. There is no startup validation for key/issuer/audience/connection settings. |
| SEC-04 | P0 | `SmartAttendance.API.csproj`; resolved assets; NuGet audit | Resolved transitive PKCS package has high/moderate advisories; details below. Do not treat successful compilation as a security pass. |
| SEC-05 | P1 | `Models/User.cs:11`; attendance entity responses; `git diff` for Students controller | PasswordHash is a public serializable property without a defensive exclusion. HEAD student reads include User and would expose hashes; the existing uncommitted DTO projection fixes that path in the working tree. Current attendance GET includes Student/Course, not Student.User, so a hash leak there is not established. Nested entities tracked by attendance POST can expose supplied nested User/hash in its response. Use explicit DTOs throughout and add recursive response checks. |
| SEC-06 | P1 | `UsersController.cs:113-148`; `Program.cs` | No login rate limiting/lockout or password policy. Unknown-user login skips hash verification, creating a potential timing distinction despite identical errors. Rehash-needed result is accepted without upgrading the stored hash. |
| SEC-07 | P1 | `TokenService.cs:20-44`; account models | No disabled-account state, token revocation/security stamp, logout invalidation, or explicit clock-skew policy. A role change would not invalidate an existing two-hour token. Refresh tokens are not automatically required; agree a session lifecycle and document expiration behavior. JWT payload contains readable personal data and must not be treated as encrypted. |
| SEC-08 | P1 | `UsersController.cs:83-88`; `StudentsController.cs:76-88` | Admin-supplied roles are unrestricted strings. Student creation permits links to Admin/Teacher users and multiple profiles for one account. This is an integrity gap, not anonymous self-assignment of Admin through `/api/users`. |
| SEC-09 | P1 | `Program.cs`; all controllers | No centralized production exception mapping, ProblemDetails setup, safe domain-error handling, or audit trail. Expected conflicts/SQL errors can become 500 responses. Development exception output must never be exposed to real users. No SQL-injection primitive was found in the inspected LINQ code. |

### Database relationships and attendance rules

| ID | Priority | Evidence | Finding and consequence |
| --- | --- | --- | --- |
| DB-01 | P1 | `AppDbContext.cs`; `InitialCreate.cs:94-107`; snapshot | `Students.UserId` is nonunique: actual relationship is many student profiles per user. If one profile/account is required, enforce a unique FK after existing-data review. Nullable navigation properties do not make the nonnullable FK relationships optional. |
| DB-02 | P1 | Models; initial migration | No unique email, roll number, course code, or attendance business key. Application email pre-check races; duplicate login identities are possible. Email is not trimmed/normalized and comparison depends on SQL collation. Decide roll-number uniqueness scope before indexing it. |
| DB-03 | P1 | Snapshot relationship blocks | Deleting a User cascades to Students then Attendances; deleting Course also deletes attendance history. There are no DELETE endpoints today, but direct/future deletion would erase history. Choose restrictive deletion or archival with a documented retention policy. |
| DB-04 | P1 | All models and context | No Enrollment, TeacherCourse assignment, or ClassSession relation. FKs only prove that student/course exist; they cannot prove course eligibility, teacher authority, or a valid scheduled class. |
| ATT-01 | P1 | `AttendancesController.cs:52-72` | Duplicate attendance is accepted, including concurrent submissions/retries. A database constraint and transactional handling are required; an AnyAsync pre-check alone is insufficient. Prefer `(StudentId, ClassSessionId)` if multiple sessions/day are possible; do not assume one record per course/day. |
| ATT-02 | P1 | `Attendance.cs:15-19` | Date/time, Status and Method come directly from the caller with no allowed sets, window checks, or server provenance. Missing Date can become `DateTime.MinValue`; arbitrary past/future marks and invented values are possible. `datetime2` does not retain a time-zone offset. Agree college timezone, store UTC instants and explicit session/local-date semantics. |
| ATT-03 | P1 | `Attendance.cs:19`; `AddAttendanceMethod.cs:13-18` | Model defaults Method to `FaceRecognition` even for manual submissions although no recognition exists. Migration backfills existing rows with empty Method instead. Both are provenance problems; define explicit Manual/other permitted methods and a truthful legacy value/backfill plan. |
| ATT-04 | P1 | Controllers and model inventory | No session open/closed state, late cutoff, absence calculation, excused/cancelled-session rules, correction reason/history, recorded-by identity, enrollment period, or idempotency/event ID. Reports cannot yet derive a reliable denominator or reconcile manual/recognition submissions. These business rules require college decisions. |
| DB-05 | P2 | All migration string definitions | Strings are unbounded `nvarchar(max)` with no length/check constraints; required columns still allow empty strings. Add bounded lengths before building business-key indexes. No concurrency token for future corrections. Index design for scoped student/course/session/date queries is absent. |

### Validation, API behavior and configuration

- **VAL-01 (P1):** DTOs/models lack explicit required/non-whitespace, email-format, string-length, positive-ID, semester-range, password-policy and enum/allowlist validation. `[ApiController]` supplies binding/model-state behavior but does not enforce these business rules; nonnullable strings initialized to empty are insufficient. Agree actual semester bounds and role/status/method vocabulary.
- **API-01 (P1):** No CORS policy. A frontend on a separate origin cannot read protected responses without a configured allowed-origin policy and Authorization preflight support. Same-origin deployment may avoid CORS; confirm Samrat's actual setup. Never use wildcard origins with credentials.
- **API-02 (P1):** `StudentsController.cs:93-100` omits Name/Email on successful create although GET populates them; DTO defaults produce empty values. Align the response schema and values. Students currently cannot read their own student profile through these routes.
- **API-03 (P2):** Lists are unbounded and generally tracked; no stable paging/order, filters, cancellation tokens, or summary/report endpoints. Users list loads password hashes into memory before mapping; project safe fields directly. Routes use unconstrained `{id}`, with no explicit positive-ID contract.
- **API-04 (P2):** `SmartAttendance.API.http` requests removed `/weatherforecast/`, so it is not an attendance smoke test. Swagger's global bearer requirement also describes login as requiring authorization even though login is anonymous. No checked examples, documented error schema, response annotations, or API contract tests.
- **CFG-01 (P1):** Ignored local configuration contains a SQL-authentication connection with the built-in administrator login and `TrustServerCertificate=True`. Restrict DB privileges; validate certificates outside local development. No claim is made that production uses this configuration. Ignoring a file does not encrypt or rotate credentials.
- **CFG-02 (P1):** Live `appsettings.json` and Development settings are ignored; only example configuration is tracked. New clones have no complete documented setup. No `UserSecretsId` or provisioning instructions. Use environment variables (`ConnectionStrings__DefaultConnection`, `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`) or configured development user-secrets; production secrets belong in the deployment secret store. Validate settings on startup without logging values.
- **CFG-03 (P2):** `AllowedHosts=*`; no explicit production HSTS, reverse-proxy/forwarded-header plan, health/readiness checks or deployment guide. HTTPS redirection is present but HTTP launch profile alone does not guarantee a usable HTTPS target. Local profiles advertise HTTP 5189 and HTTPS 7037. Agree deployment topology before configuring these features.
- **CFG-04 (P1):** Creating users needs an Admin token, but no safe first-admin provisioning path exists. Fresh-database setup is blocked without a separately reviewed one-time provisioning procedure. Do not add anonymous Admin registration or embed a default password.
- **CFG-05 (P2):** Duplicate `appsettings.json` entry in `.gitignore` is harmless cleanup. SDK/tool versions and restore graph are not pinned. `Microsoft.AspNetCore.OpenApi` is referenced but native `AddOpenApi`/`MapOpenApi` is unused; review redundancy with Swashbuckle in a later dependency milestone.

## Dependency vulnerability results

`dotnet list SmartAttendance.API/SmartAttendance.API.csproj package --include-transitive --vulnerable --no-restore` completed successfully using NuGet.org and the existing resolved graph. It reported no direct vulnerable packages and the following transitive package:

| Package/version | Severity | Advisory | Verified scope |
| --- | --- | --- | --- |
| `System.Security.Cryptography.Pkcs` 9.0.11 | High | [GHSA-846f-f764-76mx / CVE-2026-84008](https://github.com/advisories/GHSA-846f-f764-76mx) | CMS signature processing denial of service; advisory includes Windows/Linux/macOS |
| Same package | Moderate | [GHSA-2fmv-rv2c-59x7 / CVE-2026-70162](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) | CMS envelope decoding denial of service on Windows |

Both publisher advisories list 9.0.21 as a patched version on the package's 9.x line (and 10.0.13 on 10.x). Dependency path from existing assets: `Microsoft.EntityFrameworkCore.SqlServer 10.0.11 → Microsoft.Data.SqlClient 6.1.6 → System.Security.Cryptography.Pkcs 9.0.11`. No endpoint directly invokes CMS processing; reachability has not been established. Package presence is confirmed, exploitation is not. Review a supported parent-package update or deliberate patched transitive override, rebuild and rescan in a separately authorized milestone. Review installed/deployed runtimes too; package audit does not inventory the whole host.

Direct references: JwtBearer 10.0.11; AspNetCore.OpenApi 10.0.10; EF Core SqlServer/Tools 10.0.11; Swashbuckle.AspNetCore 10.2.3. Differing patches alone do not prove a vulnerability. Audit results are dated and rely on cached resolution; re-evaluate after an authorized fresh restore. No versions or dependency files were changed.

## Integration requirements

### Samrat: frontend

Only a `.gitkeep` is present, so frontend behavior cannot be audited. Agree base URL, HTTPS certificates, same/separate origin deployment, exact allowed origins, role-specific screens, DTO fields, paging/filter parameters, ISO-8601/timezone semantics, and a consistent error shape. Existing login returns `{ token, user }`; protected calls use `Authorization: Bearer <token>`. Document 400/401/403/404/409/429 behavior, expiry handling, and safe token storage appropriate to the deployment (avoid persistent browser token storage by default). Frontend role checks only control presentation; backend authorization remains mandatory.

Acceptance: documented OpenAPI examples cover login, student/course lookup and manual marking; Samrat can complete these flows against a disposable backend; preflight from the approved origin works; disallowed origins and cross-student access fail; responses never contain PasswordHash. Decide whether `/me` endpoints are needed. Do not share production/admin tokens as fixtures.

### Aryan: recognition

No recognition code, AI SDK/model dependency, model asset, or external AI call was found in the inspected repository. The string `FaceRecognition` is only a model default, not an implementation or permission to add AI. Application AI APIs and AI models are prohibited until the college explicitly permits them; Codex development assistance is allowed. This restriction includes local recognition models. Manual attendance must remain the usable baseline.

Before implementation, agree a permitted approach and versioned contract: stable student identity mapping, session/course ID, event ID for replay/idempotency, observation timestamp versus server receipt timestamp, authenticated source identity, authorization scope, timeout/retry behavior and manual review fallback. Confidence/threshold fields apply only if a college-approved approach requires them. Define unknown/ambiguous identity, replayed event, closed session, wrong enrollment, and duplicate/manual collision responses. The backend must validate and persist attendance; recognition must not write directly to SQL or assert arbitrary roles/methods.

Acceptance: a synthetic deterministic adapter can exercise the integration contract without an AI model/API; unauthorized sources and forged/replayed events are rejected; no biometric images/templates enter logs or attendance DTOs. Any later biometric storage needs explicit college permission, collection/retention/deletion decisions and access controls before implementation.

## Prioritized implementation backlog

Order matters: agree rules/contracts first, protect entity boundaries and secrets before exposure, then enforce data invariants and integrate. All tasks are pending.

| Task | Priority / owner | Acceptance criteria | Verification |
| --- | --- | --- | --- |
| R-01: agree authorization and attendance specification | P0; backend + college, Samrat, Aryan | Written role/resource matrix, session granularity, timezone, allowed statuses/methods, late/absence rules, retention, and AI restriction confirmed; unresolved choices identified explicitly | Review specification and route examples; no implementation dependent on an unresolved rule |
| R-02: close entity graph binding and response exposure | P0; backend | Request/response DTOs for every write/read; no client IDs/navigation graphs persisted; Teacher cannot create nested User/Course/Student; working student DTO protection preserved | Regression tests post malicious nested graphs and verify no extra rows; recursive JSON hash checks; create/get student field parity |
| R-03: scope every protected route | P0; backend | Student sees own permitted records only; Teacher sees/marks assigned sessions only; Admin access explicit; unknown roles denied; no existence leak outside policy | HTTP role matrix across list/detail/create, including other students and unassigned teachers |
| R-04: repair configuration and vulnerable dependency graph | P0; backend | Validated random signing key and complete settings; least-privilege DB credentials; production certificate validation; patched dependency resolution and reviewed host runtime; documented secret rotation when exposure is suspected | Build, transitive audit with no known advisories, missing/weak-config startup tests, configuration review; never print secrets |
| R-05: create meaningful test infrastructure | P1; backend | Unit tests plus WebApplicationFactory HTTP tests and disposable SQL Server integration tests; isolated secrets/data; reproducible commands; baseline regressions for R-02/R-03 | Explicit test project commands below; test discovery/counts verified; no connection to project/shared DB |
| R-06: validation and authentication hardening | P1; backend | Trim/normalize identities; bounded fields and allowed roles; password policy; login throttling, rehash handling and session lifecycle; safe initial Admin provisioning; structured errors | Boundary/invalid-input tests, duplicate race tests, invalid/expired/tampered/wrong-issuer/audience JWTs, 401 vs 403, throttling tests |
| R-07: model relationships and constraints | P1; backend | Reviewed enrollment/assignment/session design; unique normalized email and agreed business keys; one student/account if required; restrictive history deletion; valid value constraints; reviewed forward migration and legacy-data plan | Disposable SQL Server migration tests and model-drift check; FK/unique/cascade expectations verified; migration SQL reviewed before application |
| R-08: implement attendance domain service | P1; backend | Server-controlled author/provenance and clock; enrollment/assignment/window checks; unique session/student attendance under concurrency; defined retries/conflicts, corrections/history, truthful legacy Method handling | Unit rule matrix; real-SQL concurrent mark/replay tests; timezone boundaries; manual/recognition collision tests |
| R-09: frontend contract and smoke flows | P1; backend + Samrat | Explicit CORS if needed; safe token/expiry handling; aligned DTOs/errors and examples; login→lookup→manual attendance flow succeeds | OpenAPI/HTTP integration checks and manual demo using synthetic accounts in disposable environment |
| R-10: recognition boundary, gated by college permission | P1; backend + Aryan | Approved non-AI approach or recorded explicit permission before AI use; authenticated adapter; no direct DB access; replay protection and manual fallback | Synthetic adapter tests first; permitted component end-to-end tests later; no prohibited API/model invocation |
| R-11: completion and operations | P2; backend | Scoped pagination/filtering; agreed reports and denominator; correction concurrency; accurate Swagger/HTTP samples; CI checks, reproducible setup and deployment guide | Paging/report boundary tests, OpenAPI checks, fresh-clone setup in disposable environment, CI build/test/audit |

## Missing automated tests

There are zero discovered automated test projects/files; no test suite was run. A successful `dotnet test` against this API alone would not establish test coverage. Add tests for observable rules and attack boundaries, including:

- Login success/failure; empty/malformed input; hash storage/rehash; throttling; first-admin safety; role restrictions and JWT validation failures.
- All routes for anonymous/Admin/Teacher/Student/unknown role; student ownership and teacher assignment; response privacy; nested graph attacks; student creation consistency.
- SQL Server FK, business-key uniqueness and concurrent duplicate writes; migrations from empty/prior schema; legacy Method values; deletion restrictions. EF InMemory cannot prove SQL Server constraint/collation behavior.
- Enrollment, session time windows, UTC/local-date boundaries, missing dates, allowed statuses/methods, closed/cancelled classes, corrections/audit and percentage denominators.
- Frontend CORS/preflight, API error schemas/OpenAPI, pagination, recognition adapter authentication/retry/replay/unknown identity and fallback.

## Inspection results and verification commands

Run from repository root. Read-only Git/source inspection used `git status --short --branch`, `git ls-files`, `git diff`, path-only `git log`, `rg --files`, and numbered source reads. Configuration inspection printed metadata only. Never dump local config, environment variables, JWTs or secret stores to logs.

| Check | Actual result |
| --- | --- |
| `dotnet --info` | SDK 10.0.112, runtime 10.0.12; Linux; no SDK pin |
| `dotnet tool list --local` / `--global` | No local tools; global EF 10.0.11 |
| Build command below | Passed: 0 errors, 2 warnings (NU1902/NU1903 for PKCS); output directed to `/tmp` |
| Vulnerability command below | Completed; 1 transitive package with 2 advisories; exit success does not mean no vulnerabilities |
| Dependency path | Read existing `obj/project.assets.json`; no restore performed |
| `dotnet nuget why ... --no-restore` | Unsupported flag; command rejected without performing restore. Dependency path was instead obtained by parsing existing assets |
| Automated tests | Not run: no test project exists |
| Database/API integration | Not run: no database access or server startup in this milestone |
| `git diff --check` | Exit 2: pre-existing StudentsController diff has CRLF/trailing-whitespace reports; preserved without formatting changes. New documentation separately checked for trailing whitespace |

Repeat the safe build/audit with existing assets (build requires a previously restored graph; do not implicitly restore during an inspection-only milestone):

```bash
git status --short --branch
dotnet --info
audit_dir=$(mktemp -d /tmp/smartattendance-audit-build.XXXXXX)
dotnet build SmartAttendance.API/SmartAttendance.API.csproj --no-restore \
  -p:IntermediateOutputPath="$audit_dir/obj/" -p:OutputPath="$audit_dir/bin/"
dotnet list SmartAttendance.API/SmartAttendance.API.csproj package \
  --include-transitive --vulnerable --no-restore
git diff --check
git status --short --branch
```

Future verification commands, only after test projects/tooling are added and that milestone is authorized (the following paths are proposed, not existing):

```bash
dotnet test tests/SmartAttendance.UnitTests/SmartAttendance.UnitTests.csproj --no-restore
dotnet test tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj --no-restore
dotnet ef migrations has-pending-model-changes --project SmartAttendance.API --no-build
dotnet ef migrations script --idempotent --project SmartAttendance.API --no-build \
  --output /tmp/smartattendance-migrations.sql
```

Tests must fail closed unless configured for a disposable SQL Server database. Review generated SQL before applying any future migration. Do not run `database update`, schema reset, seed, or drop commands against the current project database as part of verification. A future fresh restore/build belongs to the implementation milestone and must be followed by a new dependency audit. No application code, dependencies, migrations, database records or frontend files were modified by this audit; no commit or push was performed.
