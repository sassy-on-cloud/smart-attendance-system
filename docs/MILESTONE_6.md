# Milestone 6: authentication and role workflows

Implemented on 2026-10-24. This document supplements the original audit roadmap; its historical findings and counts are not the current implementation status.

## Audit and resulting behavior

The existing API already used Identity PasswordHasher, signed JWTs, safe response DTOs, Admin-managed registration and attendance/report resource authorization. Those mechanisms remain. The audit found incomplete configuration validation, no login throttling or password rehash handling, email uniqueness enforced only by an application precheck, no safe first-Admin command, and overly broad Teacher access to the student directory.

Changes:

- Login retains `POST /api/users/login` and its `{ token, user }` response. Invalid credentials, missing accounts, malformed stored hashes and unsupported account roles receive the same generic 401 response. Model validation returns 400. Password verification against a dummy hash mitigates account-existence timing differences; it does not guarantee identical timing, particularly for legacy hashes. Login responses and throttling responses use no-store cache headers.
- New passwords require 12–128 characters and at least six distinct characters. Passphrases are supported without mandatory character categories. Existing passwords remain usable. Successful login upgrades legacy hashes through Identity's `SuccessRehashNeeded` mechanism; newly generated Identity V3 hashes use 210,000 iterations. Passwords and hashes never enter response DTOs.
- Registration remains Admin-only. Only Admin, Teacher and Student roles are accepted. Explicit mapping and closed request DTOs prevent client assignment of IDs, hashes or navigation graphs. An authenticated Student or Teacher cannot create an account or elevate their role.
- `GET /api/users/me` provides the current account's safe DTO. Administrative account operations and student creation require the current database account to retain its Admin role. Named Student, Teacher, Admin, staff and current-account policies centralize role rules. Existing attendance/report services retain their current-actor and assigned-course checks. Resource reads fail closed through scoped queries; some existing read contracts intentionally return empty results or 404 for inaccessible resources.
- Teachers can read student profiles only through enrollment in their assigned courses; an unassigned Teacher receives an empty directory and 404 for individual profiles. Historical enrollments remain available for roster lookup. Extra Admin claims do not elevate a current Teacher account. Reporting calculations, eligibility, legacy-placeholder treatment and `closed-past-v1` remain unchanged.
- Missing, expired, tampered, unsigned, wrong-issuer/audience, wrong-algorithm and future-not-before tokens fail authentication with 401. Authenticated accounts failing a role/current-account policy receive 403. Unknown roles are denied. JWT verification requires signature, issuer, audience, lifetime and JWT type, and allows only HS256. Default lifetime remains 120 minutes, configurable from 5–120; clock skew defaults to 30 seconds, configurable from 0–60. Detailed bearer failure messages are disabled. Signing keys and credentials are never response fields.
- Swagger describes anonymous login using an empty security requirement, documents 429, and retains bearer requirements on protected operations.

## Login throttling and deployment limits

`LoginSecurity` defaults to 20 login requests per remote IP per 60 seconds and five attempts per normalized account identity per 300 seconds. Excess attempts return 429 with `Retry-After`. Account attempts are reserved before verification, so parallel submissions cannot bypass the limit. Successful login clears that reservation window; failed and unknown accounts follow the same account limit. Throttle state is bounded to 5,000 account identities and fails closed when full. Account keys are HMAC-derived with a process-generated secret rather than storing raw email addresses.

These controls are per process and reset on restart. A deployment with multiple instances needs a shared limiter or gateway enforcement. Shared campus IP addresses may require measured adjustment of the IP allowance. Only the actual connection address is used; spoofed `X-Forwarded-For` headers are ignored. Configure known reverse proxies explicitly before deploying behind a proxy. Account throttling can temporarily block a victim's login when an attacker knows their email; it is a short window, not a permanent account lockout.

## Configuration and secrets

JWT and login settings are bound to validated options and checked at startup. Missing/placeholder/repetitive JWT keys, insufficient key size, missing issuer/audience and invalid limits fail startup without printing secret values. Key validation checks at least 32 UTF-8 bytes and basic placeholder/repetition patterns; operators must still generate a cryptographically random key.

The API now has a nonsecret `UserSecretsId`. The committed example contains blank connection/key values. Ignored local settings were not edited: the inspected local signing key is placeholder-like and must be replaced through secure configuration before normal startup. No actual credential was generated, rotated or copied by this milestone.

Development-only examples below are operator instructions, not commands executed during implementation. Run from the repository root. Pipe JSON to User Secrets to keep values out of command arguments and terminal output:

```bash
python3 -c 'import json,secrets; print(json.dumps({"Jwt:Key":secrets.token_urlsafe(64)}))' |
  dotnet user-secrets set --project SmartAttendance.API
python3 -c 'import getpass,json; print(json.dumps({"ConnectionStrings:DefaultConnection":getpass.getpass("SQL connection string: ")}))' |
  dotnet user-secrets set --project SmartAttendance.API
dotnet user-secrets set 'Jwt:Issuer' 'SmartAttendance.API' --project SmartAttendance.API
dotnet user-secrets set 'Jwt:Audience' 'SmartAttendance.Client' --project SmartAttendance.API
```

Issuer/audience must agree with the consuming deployment. User Secrets are unencrypted development storage, not a production vault. Production should inject `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` and `ConnectionStrings__DefaultConnection` through environment/deployment secret management. Do not dump environment values or run `user-secrets list` into shared logs. See [Microsoft's User Secrets guidance](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).

## Initial Admin provisioning

There is no public bootstrap route, automatic startup seed or default password. After explicitly approving the target database and schema, an operator can configure one-time `InitialAdmin:Name`, `InitialAdmin:Email` and `InitialAdmin:Password` secrets and run:

```bash
# This command writes an account to the configured database. Review the target first.
ASPNETCORE_ENVIRONMENT=Development dotnet run --project SmartAttendance.API \
  --no-launch-profile -- --provision-admin
```

Use a hidden prompt and JSON pipe as above for provisioning values, especially the password; never put a real password in shell history. The command starts no HTTP listener and applies no migrations. It uses a serializable transaction and a SQL Server transaction-owned application lock to serialize concurrent first-Admin attempts. Any existing Admin prevents provisioning; an email belonging to another account is never promoted or reset. Exit codes are 0 for creation, 2 for rejected/no-op input or existing account, and 1 for operational failure. Remove only the three `InitialAdmin` secrets immediately afterward; retain the application's JWT/connection settings. The command was tested only against disposable databases, not run against the development database.

## Email integrity migration and preservation

New migration: `20261024080451_AuthenticationEmailIntegrity`. Existing migrations remain intact. This additive migration is required before running the new API against an existing database. It has **not** been applied to the development database, which also still requires the approved Milestone 3 and 4 migrations.

The migration adds a nullable, maximum-254-character `Users.NormalizedEmail`, backfills trimmed uppercase identities, and creates a filtered unique index plus a nonblank-key check. SQL Server uses `Latin1_General_100_CI_AS` comparison. EF normalizes new/modified users using `Trim().ToUpperInvariant()`; registration prechecks and database uniqueness handle ordinary and concurrent duplicates. Duplicate email registration returns 409. Blank legacy emails retain null keys and cannot authenticate; their records are preserved. User IDs, raw emails, roles, password hashes and attendance relationships are unchanged.

Before adding the column, SQL preflights abort on duplicate case/space-normalized identities (51021) or oversized keys (51020). They never truncate, merge or delete accounts. Operators must review/reconcile such data explicitly and rerun after approval. Review unusual international or whitespace identities against the deployment's actual data before rollout: .NET invariant normalization and SQL collation are separate mechanisms. Any privileged direct-SQL writer must populate the normalized key correctly; the check constraint ensures presence, not exact equality with the raw email. Application writes always populate it.

Review [the generated incremental idempotent SQL](migrations/20261024080451_AuthenticationEmailIntegrity.sql). It assumes Milestone 4 has already been applied; it does not install Milestone 3/4. Before an approved rollout: back up, restore a separate review copy, inspect migration history and duplicate/oversized email identities, review all pending SQL in order, and test the complete migration chain against that copy. Only then request approval to apply pending migrations to development. The migration's Down path deliberately rejects automatic removal of the security constraint; recovery requires a reviewed plan.

Safe offline review commands (the design-time factory uses a nonconnecting dummy target):

```bash
dotnet ef migrations has-pending-model-changes --project SmartAttendance.API --no-build
dotnet ef migrations script 20261024065458_AttendanceManagementAudit \
  20261024080451_AuthenticationEmailIntegrity --project SmartAttendance.API --no-build \
  --idempotent --output /tmp/AuthenticationEmailIntegrity.sql
```

## Token lifecycle decision

Refresh tokens are unnecessary for the current interactive college workflow: the existing two-hour access token continues to work, and expiry requires login. Adding refresh credentials would introduce storage, rotation, replay handling and revocation responsibilities without an established persistent-session requirement. No refresh endpoint or client contract was added. JWT logout does not itself revoke a signed token. Sensitive account policies and attendance/report resource checks consult current database roles/relationships; this is not a general per-token revocation system.

## Verification

Reproducible commands:

```bash
dotnet build tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj --no-restore
SMARTATTENDANCE_RUN_SQLSERVER_TESTS=1 dotnet test \
  tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj \
  --no-build --no-restore --logger 'trx;LogFileName=milestone6.trx' \
  --results-directory /tmp/smartattendance-milestone6
dotnet list SmartAttendance.API/SmartAttendance.API.csproj package --vulnerable --include-transitive --no-restore
dotnet list tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj package --vulnerable --include-transitive --no-restore
git diff --check
```

SQL tests require Docker, the locally available `mcr.microsoft.com/mssql/server:2022-latest` image (the fixture uses `--pull=never`), and explicit opt-in. The existing fixture creates its own private SQL Server container, random credentials, disposable databases and cleanup; it never accepts the development connection string. New SQL coverage exercises concurrent registration, concurrent login throttling, first-Admin serialization and migration preservation/fail-closed preflights. HTTP/unit coverage exercises credentials, hashing, escalation, current roles, course scope, token validation, secrets/options validation, password rules, throttling and Swagger.

Actual final results:

| Check | Result |
| --- | --- |
| Build of API and test project | Passed, 0 warnings, 0 errors |
| Full opted-in suite | 305 executed, 305 passed, 0 failed, 0 skipped |
| Original Milestone 5 cases | All 252 retained and passed; verified by comparing TRX case/outcome multisets |
| New cases | 53 passed |
| Disposable SQL Server cases | 29 passed, including the original 23 and 6 new cases |
| Transitive NuGet audit, API and tests | No vulnerable packages reported by configured NuGet sources |
| EF pending-model check | No model changes since the new migration |
| Incremental idempotent migration SQL | Generated offline; not applied to development |
| Git whitespace check | Passed |
| Disposable container cleanup | No labeled test containers remained |

Final logs: `/tmp/smartattendance-m6-build.log`, `/tmp/smartattendance-m6-full.log`; results: `/tmp/smartattendance-milestone6/milestone6.trx`. Intermediate runs identified and corrected a concurrent shared-SQLite test fixture limitation and a case-sensitive Swagger route lookup. The results above describe the final full run. The TRX-overwrite notice is a test-runner artifact notice, not a compiler warning.

Changed areas: `Security/` contains options, policies, hashing/throttling support, provisioning and OpenAPI metadata; `AccountService` and `TokenService` implement login/registration/token handling; controllers adopt policies and scoped reads; the user model/context/configuration and one additive migration implement normalized email uniqueness. Configuration changes add User Secrets metadata and safe example settings. Five authentication test files and the existing test factory/migration-count assertion provide verification. Package versions, prior migration files, report query/DTO logic and frontend files remain unchanged.

Production deployment, real development-data migration, frontend end-to-end flows and distributed/load testing were not run. No development database records were changed; no migrations were applied to it. No commit or push was performed.
