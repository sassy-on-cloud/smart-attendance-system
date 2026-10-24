# Smart Attendance System: instructions for future Codex sessions

## Scope and ownership

College project using ASP.NET Core Web API, .NET 10, C#, EF Core, SQL Server, JWT role authorization and Swagger/OpenAPI. The user owns backend, database, security, attendance rules, integration and testing. Samrat owns frontend; Aryan owns recognition.

Read `docs/DEVELOPMENT_ROADMAP.md` for the 2026-10-24 audit, evidence, pending tasks and verification limits. The roadmap is a backlog, not authorization to implement all tasks.

The current audit milestone permits only this file and `docs/DEVELOPMENT_ROADMAP.md`. Stop after reporting inspection results. Do not modify application code, dependencies, migrations, database records or frontend files unless a later user request authorizes that work. Do not commit, push, stage, reset, clean, stash, or discard user work; the user handles Git operations.

## Architecture

- `SmartAttendance.API/SmartAttendance.API.csproj`: single `net10.0` web project, nullable reference types and implicit usings enabled. No solution or test project at audit time.
- `Program.cs`: dependency injection, SQL Server provider, JWT validation, Swagger, HTTPS and authorization middleware. Swagger is Development-only.
- `Controllers/`: Users (login and Admin account creation/read), Students (Admin create, Admin/Teacher read), Courses (role-protected create/read), Attendances (authenticated reads and Admin/Teacher create). These roles describe existing behavior, not a complete resource-authorization policy.
- `Models/`: User, Student, Course, Attendance EF entities.
- `DTOs/`: LoginDto, RegisterUserDto, UserResponseDto, StudentResponseDto. The student response DTO/controller changes predate the audit and were uncommitted.
- `Data/AppDbContext.cs`: four DbSets; convention-based relationships. All database operations currently live in controllers.
- `Services/TokenService.cs`: HMAC JWT generation, two-hour expiry.
- `Migrations/`: InitialCreate and AddAttendanceMethod, designers and snapshot. No database state has been verified.
- `SmartAttendance.Frontend/`: untracked `.gitkeep` only at audit time. Recognition code is absent.

Intended direction when implementation is authorized: controllers handle HTTP/authentication boundaries, DTOs define allowed inputs/outputs, an attendance service enforces business rules, and EF/database constraints enforce durable invariants. Keep recognition behind an authenticated adapter; neither frontend nor recognition may write directly to SQL Server.

## Safety and data handling

- Do not add or invoke application AI APIs or AI models, including local recognition models, unless the college explicitly permits them. Codex development assistance is allowed. The existing `FaceRecognition` string is not permission or an implemented recognition component. Use synthetic deterministic stubs/manual marking for integration until a permitted design is agreed.
- Begin with `git status --short --branch` and inspect existing changes. Preserve user edits and untracked files. Do not overwrite Samrat's or Aryan's work.
- Use safe inspection commands first. Do not start the API against unknown configuration or run database-writing tests against the user's database.
- Do not apply migrations, reset/drop databases, seed accounts, alter shared data or delete attendance history without explicit scope. Generate/review migration SQL and use an isolated disposable database for tests. Do not edit previously applied migrations to rewrite history.
- Treat names, emails, attendance and biometric data as sensitive. Never log/copy passwords, hashes, JWTs, signing keys, connection credentials, images or templates. Document secret findings by path and metadata only.
- Local `appsettings.json` and Development configuration are ignored and may contain secrets. Keep only placeholders in committed examples. Do not assume ignored files are secure or purge/rotate secrets without an authorized plan.
- Production secrets should come from environment/deployment secret stores; development user-secrets require deliberate setup (no UserSecretsId existed at audit). Use least-privilege SQL credentials and proper certificate verification outside local development.
- Avoid installing tools, restoring/changing dependency graphs or invoking unrelated external services during audit-only work. Vulnerability metadata lookups are allowed; never upload source or secret configuration to scanners.

## Coding standards for authorized implementation

- Follow existing namespaces and C# conventions: PascalCase types/public members, camelCase locals/parameters, `_camelCase` private fields. Keep nullable types truthful, avoid null-forgiving operators for required configuration, and validate settings at startup.
- Use async EF calls and propagate cancellation where useful. Project response DTOs in queries; use `AsNoTracking` for reads. Add bounded paging and deterministic ordering to lists.
- Never bind writable EF navigation graphs from request JSON. Use dedicated validated request DTOs and map explicitly. Never return PasswordHash or User entities; serialize only approved response fields.
- Validate required/non-whitespace strings, lengths, email format/normalization, allowed roles/status/methods, IDs and agreed semester ranges. Database constraints must reinforce business keys; handle conflicts without leaking SQL exceptions.
- Enforce authentication, role and resource ownership on the server. Students access their own permitted records; teacher actions require assignment to the relevant course/session once that model exists. Reject unknown roles. Frontend visibility is not authorization.
- Use Identity password hashing; never implement custom password crypto or a default Admin password. Keep user creation Admin-only and design first-admin provisioning separately.
- Put attendance rules in a service, with server-owned timestamps/author/provenance. Agree session granularity and college timezone before implementing uniqueness, late/absence rules, corrections or reports. Use UTC instants with explicit local-date semantics and an injectable clock.
- Protect duplicate attendance under concurrent requests using a database business key and transactional handling. Do not rely solely on a pre-check. Preserve attendance history and correction provenance.
- Use consistent HTTP status codes and structured safe errors. Keep public Swagger examples aligned with actual authorization and DTOs. Configure CORS for exact agreed frontend origins when needed.
- Keep changes scoped; do not introduce a new architecture/framework/package without a concrete need. Coordinate API contract changes with Samrat and Aryan through documentation; do not send messages on the user's behalf without authorization.

## Inspection, build and test commands

Run from repository root. Prefer `rg`/`rg --files` for searches; exclude build outputs, IDE files and secret configuration from broad content output.

```bash
git status --short --branch
git diff --stat
dotnet --info
dotnet tool list --local
dotnet tool list --global
dotnet list SmartAttendance.API/SmartAttendance.API.csproj package \
  --include-transitive --vulnerable --no-restore
```

Build using the existing restored assets and temporary outputs during inspection:

```bash
audit_dir=$(mktemp -d /tmp/smartattendance-audit-build.XXXXXX)
dotnet build SmartAttendance.API/SmartAttendance.API.csproj --no-restore \
  -p:IntermediateOutputPath="$audit_dir/obj/" -p:OutputPath="$audit_dir/bin/"
git diff --check
git status --short --branch
```

Do not drop `--no-restore` silently if cached assets are missing. Explain the limitation, or perform a restore only when the active implementation scope allows it. `dotnet nuget why` does not accept `--no-restore` on the audited SDK; inspect existing `obj/project.assets.json` for dependency paths during audits.

At audit time there are no automated test projects. Do not report zero discovered tests or running `dotnet test` on the API as a passing suite. When authorized, add unit and HTTP/SQL Server integration test projects; then run explicit project paths, for example:

```bash
dotnet test tests/SmartAttendance.UnitTests/SmartAttendance.UnitTests.csproj --no-restore
dotnet test tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj --no-restore
```

These are future paths, not currently runnable commands. Use synthetic accounts and a disposable SQL Server database; tests must refuse the normal project/shared connection. Test role/ownership boundaries, nested graph rejection, password-hash privacy, JWT failures, validation, unique/FK constraints, concurrent duplicate attendance, session/timezone rules and integration contracts. EF InMemory alone cannot verify SQL Server behavior.

Baseline build passed with NU1902/NU1903 warnings for transitive `System.Security.Cryptography.Pkcs` 9.0.11. A successful audit command exit is not proof that vulnerabilities are absent. Recheck current advisories and resolved dependencies after authorized updates. Report what changed, what was actually verified, and what remains unverified; do not claim database or end-to-end success from compilation alone.
