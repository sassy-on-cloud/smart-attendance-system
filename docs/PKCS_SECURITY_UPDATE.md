# PKCS security update

Verified 2026-10-24 (Asia/Katmandu). Resolves GHSA-846f-f764-76mx / CVE-2026-84008 (High) and GHSA-2fmv-rv2c-59x7 / CVE-2026-70162 (Moderate).

## Selection and compatibility

Original graph in both projects:

```text
Microsoft.EntityFrameworkCore.SqlServer 10.0.11
  -> Microsoft.Data.SqlClient 6.1.6
     -> System.Security.Cryptography.Pkcs 9.0.11
```

The publisher's [high-severity advisory](https://github.com/advisories/GHSA-846f-f764-76mx) and [moderate-severity advisory](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) list 9.0.21 as patched on the existing 9.x line, with 10.0.13 patched on 10.x. NuGet metadata confirms the stable 9.0.21 package is available and compatible with this net10.0 application.

Upstream candidates were checked against the actual published NuGet manifests:

| Candidate | Declared dependency relevant to this application | Resolves both advisories alone? |
| --- | --- | --- |
| SqlClient 6.1.7, latest stable 6.1 patch | PKCS >= 9.0.11 in the applicable dependency group | No; normal minimum-version resolution still selects vulnerable PKCS |
| EF Core SqlServer 10.0.12 | SqlClient >= 6.1.6 | No |
| EF Core SqlServer 10.0.13 | SqlClient >= 6.1.7 | No; SqlClient still permits the vulnerable minimum |
| Direct PKCS 9.0.21 reference | Raises the resolved PKCS security floor | Yes |

Sources: [SqlClient 6.1.7 package](https://www.nuget.org/packages/Microsoft.Data.SqlClient/6.1.7), [EF Core 10.0.13 manifest](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.sqlserver/10.0.13/microsoft.entityframeworkcore.sqlserver.nuspec), [PKCS 9.0.21 package](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/9.0.21). Microsoft's [SqlClient dependency policy](https://learn.microsoft.com/en-us/sql/connect/ado-net/sqlclient-driver-support-lifecycle#dependency-vulnerability-policy) permits updating affected dependencies in the application when the supported driver line does not supply an appropriate fix.

Added one production PackageReference for PKCS 9.0.21, with a comment explaining why it must remain until upstream dependency floors advance. This is the smallest compatible patch in the already-resolved major version. No EF/SqlClient major upgrade or broad framework update is necessary to resolve these two advisories. No warning suppression, NuGet audit exclusion, or application/schema/API change was introduced.

Both restored assets graphs differ from their previous graphs in exactly one resolved library version: PKCS 9.0.11 -> 9.0.21. No other libraries were added, removed or changed. The API project reference propagates the fix to the integration-test project; a duplicate test-project pin is unnecessary.

The 9.x line remains supported on the verification date but has a short remaining support horizon. Microsoft's [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) lists .NET 9 support ending November 10, 2026. Plan a separately tested move to the supported 10.x package line before that date rather than retaining this minimal 9.x pin indefinitely. The application already targets .NET 10; this dependency patch does not change its target framework or update the installed host SDK/runtime. Host servicing remains a separate deployment responsibility.

## Verification performed

```bash
dotnet restore SmartAttendanceSystem.sln --force-evaluate --no-cache
dotnet build SmartAttendanceSystem.sln --no-restore
SMARTATTENDANCE_RUN_SQLSERVER_TESTS=1 dotnet test \
  tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj \
  --no-build --no-restore
dotnet list SmartAttendanceSystem.sln package --vulnerable --include-transitive --no-restore
dotnet nuget why SmartAttendanceSystem.sln System.Security.Cryptography.Pkcs
```

- Fresh dependency evaluation/restore succeeded.
- Build: 0 warnings, 0 errors.
- Tests: 177 passed, 0 failed, 0 skipped, including all nine SQL Server tests. These used only the fixture's private disposable SQL Server container and random test databases; the project/development database was not accessed or migrated.
- NuGet audit: no vulnerable packages reported in either API or integration tests using current NuGet.org sources.
- Resolved graph: PKCS 9.0.21 in both projects; 9.0.11 absent from both assets files.
- No frontend changes, development database changes, or commits/pushes. Older audit/milestone documents retain their dated historical findings; this document records the current remediation.

NuGet audit covers the resolved package graph and available advisories at verification time; it does not certify the host runtime, container image, or future package advisories. Keep the direct reference until a reviewed upstream upgrade makes its security floor redundant, then restore, inspect the graph and rerun the full suite/audit before removing it.
