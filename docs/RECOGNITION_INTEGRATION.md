# Aryan's local recognition integration contract — Milestone 7

Version: `recognition-contract-v1`. Status: **contract and simulated tests only; real recognition is not implemented or approved**.

The college prohibits AI APIs and AI models in this project. No recognition library, pretrained model, external AI service, camera integration, image processing or biometric storage has been added. Aryan and the college must confirm a permitted approach before an actual adapter is developed or connected. A matching library being local does not make an AI model permissible.

## Implemented boundary and activation gate

`IRecognitionComponent` is the backend-only local integration boundary. Its `EnrollAsync` and `RecognizeAsync` methods receive server-generated commands and cancellation tokens and return explicit result messages. HTTP controllers never bind these command/result types from clients. `RecognitionWorkflow` validates results and current database relationships. The interface deliberately specifies no HTTP destination, executable command or recognition algorithm.

The application always registers `UnavailableRecognitionComponent`, including in Development. Authorized requests for eligible resources return 503 `ServiceUnavailable`. There is no configuration switch or public mock-result endpoint. `MockRecognitionComponent` exists only in the test project and scripts synthetic outcomes; it reads no images and stores no biometric data. Test hosts explicitly replace the unavailable adapter.

**Every workflow is a preview.** A valid mocked enrollment returns `SimulatedEnrollment` with `isSimulation: true` and `enrollmentStored: false`. A valid mocked match returns `SimulatedMatch` with `isSimulation: true` and `attendanceRecorded: false`. No preview writes attendance, enrollment, audit history or biometric state. A component success claiming `isSimulation: false` is blocked with 503 `IntegrationNotApproved`; replacing the adapter alone cannot enable attendance marking. Existing manual attendance APIs remain the operational fallback. Mocks must never be presented as actual recognition during the college demonstration.

## Browser-to-backend API

HTTPS and the existing bearer JWT are required. JSON property names are camelCase. Both request bodies accept **only** `captureId`, a nonempty UUID. Unknown properties return 400, including `studentId`, `outcome`, `confidence`, `method`, `isSimulation`, `actorUserId`, or a component-result object. Bodies are limited to 2,048 bytes. The response is marked no-store.

`captureId` is an opaque reference, not evidence of identity, freshness, liveness, authorization or successful recognition. There is no upload or capture-issuance endpoint in this milestone. A future approved acquisition flow must issue short-lived, actor-bound handles backed by trusted acquisition state; a random UUID submitted by a browser must not resolve successfully. Never substitute a URL, file path, image base64 or student ID for this handle.

### Enrollment

`POST /api/recognition/students/{studentId}/enrollment`

Access: current **Admin only**. `studentId` is an existing Student profile ID, not the user's account ID. The target must still have a Student-role account. The Admin selects an enrollment target but that selection alone does not establish whose face is being enrolled. A future workflow needs supervised identity verification and recorded consent/authority before capture association.

Request:

```json
{"captureId":"a181475f-760c-45d5-a2f6-8182e721907d"}
```

Example **test simulation** response, HTTP 200:

```json
{
  "requestId":"1db8a60e-dfb1-4136-b091-45fcb9773238",
  "outcome":"SimulatedEnrollment",
  "studentId":17,
  "isSimulation":true,
  "enrollmentStored":false
}
```

Enrollment does not mean course enrollment: it proposes an identity-to-capture association in Aryan's component. Course enrollments remain managed by the existing academic APIs. Neither enrollment type is created by this preview. `AlreadyEnrolled` is an explicit component outcome mapped to 409; no silent template overwrite is permitted.

### Session recognition

`POST /api/recognition/sessions/{sessionId}/recognize`

Access: current Admin, or current Teacher assigned to that session's course. Students cannot call this supervised workflow. There is no Student self-service recognition route. Authorization is checked before the component is called and again after it returns.

The session must be a real, nonlegacy meeting, `Open`, with `StartsAtUtc <= backend UTC now < EndsAtUtc`. Closed, Cancelled, undated legacy placeholders, future meetings and ended meetings return 409. Course and session version come from the backend, not the browser. This active-time rule is specific to the recognition preview; existing manual marking rules are preserved.

The body has the same single `captureId` field. Example **test simulation** response, HTTP 200:

```json
{
  "requestId":"752eb62e-58c7-4e80-8810-b3fbce68613d",
  "attendanceSessionId":42,
  "outcome":"SimulatedMatch",
  "studentId":17,
  "confidence":0.91,
  "isSimulation":true,
  "attendanceRecorded":false
}
```

The matched StudentId comes exclusively from the server-invoked component result, then is checked against the current Student account and confirmed enrollment in the session's course. Inferred legacy enrollment is insufficient. An ineligible result returns 422 `IneligibleMatch` with no student identity or confidence exposed. Existing attendance for that student/session returns 409 `DuplicateAttendance`; neither Present nor Absent records are overwritten.

`confidence` is optional (`null` allowed), finite and within [0,1]. It is component-specific information, not a calibrated probability or standalone proof. A threshold, score meaning and calibration must be agreed with Aryan and the college before real operation. Nonmatch results must contain no identity or confidence, and public failures disclose neither candidate lists nor scores. Backend decisions must never rely on browser confidence values.

## Backend-to-component messages

The C# immutable records in `Recognition/IRecognitionComponent.cs` are authoritative. If an approved local transport uses JSON, serialize with `JsonSerializerDefaults.Web` (camelCase); outcome names are explicit strings. No transport has been installed. All IDs are positive database keys; UUIDs are backend-created correlation/capture references; timestamps are UTC ISO 8601 values.

Enrollment command:

```json
{
  "contractVersion":"recognition-contract-v1",
  "requestId":"1db8a60e-dfb1-4136-b091-45fcb9773238",
  "captureId":"a181475f-760c-45d5-a2f6-8182e721907d",
  "actorUserId":11,
  "studentId":17,
  "issuedAtUtc":"2026-10-24T10:00:00+00:00"
}
```

Enrollment result echoes the five binding fields (`contractVersion`, `requestId`, `captureId`, `actorUserId`, `studentId`) and adds `completedAtUtc`, `outcome` and `isSimulation`. It does not return templates, passwords or image data. Successful mock outcome is `Enrolled`. `Matched` is invalid for enrollment.

Recognition command:

```json
{
  "contractVersion":"recognition-contract-v1",
  "requestId":"752eb62e-58c7-4e80-8810-b3fbce68613d",
  "captureId":"a181475f-760c-45d5-a2f6-8182e721907d",
  "actorUserId":22,
  "attendanceSessionId":42,
  "sessionVersion":3,
  "courseId":8,
  "issuedAtUtc":"2026-10-24T10:00:00+00:00"
}
```

Recognition result:

```json
{
  "contractVersion":"recognition-contract-v1",
  "requestId":"752eb62e-58c7-4e80-8810-b3fbce68613d",
  "captureId":"a181475f-760c-45d5-a2f6-8182e721907d",
  "actorUserId":22,
  "attendanceSessionId":42,
  "sessionVersion":3,
  "completedAtUtc":"2026-10-24T10:00:01+00:00",
  "outcome":"Matched",
  "studentId":17,
  "confidence":0.91,
  "isSimulation":true
}
```

No claimed student identity is sent in the recognition command. A result must echo all binding fields exactly. The backend requires `issuedAtUtc <= completedAtUtc <= current UTC time` and request age at most 30 seconds; component calls have a five-second deadline. The component must honor cancellation. Protocol mismatch, stale/future results, wrong request/capture/actor/session/version, invalid scores, missing matched identity and identity-bearing nonmatches fail closed as 502 `InvalidComponentResponse`. A result replayed for another request is rejected. The session's current version, course, active state/window and authorization are rechecked after the call. Synchronous in-process records and checks are not cryptographic service authentication; a future transport must establish that trust separately.

Allowed component outcomes:

| Outcome | Meaning / public handling |
| --- | --- |
| `Enrolled` | Enrollment preview only; mock becomes 200 SimulatedEnrollment, real claim blocked |
| `Matched` | Single accepted match; eligible mock becomes 200 SimulatedMatch, real claim blocked |
| `AlreadyEnrolled` | Enrollment 409; invalid for recognition |
| `NoFace` | No face detected, 422 |
| `UnknownFace` | No enrolled identity matched, 422 |
| `MultipleFaces` | More than one face in the capture, 422 |
| `AmbiguousMatch` | More than one plausible identity or insufficient separation, 422 |
| `InvalidCapture` | Invalid/expired/unowned/unresolvable capture handle, 400 |
| `Unavailable` | Local service unavailable, 503 |

Missing auth returns 401; disallowed/currently invalid roles return 403. Unassigned Teachers receive 403 before invoking the component. Authorized requests for nonexistent student/session return 404. Invalid IDs/body return 400; invalid current Student target returns 400. Changes while awaiting a result return 403 for lost access, 409 `SessionChanged`/`StudentChanged`, or 502 for stale results. Timeout returns 504 `ServiceTimeout`; exceptions return sanitized 503 `ServiceUnavailable`. No response contains exception messages, local paths, credentials or service internals. Framework authentication/model errors use existing ASP.NET error formats; workflow errors use the endpoint's response DTO with an explicit `outcome` and `requestId`.

## Trusted verification and future attendance writes

The existing manual route continues to reject client-supplied recognition method/claims. There is no browser callback that can submit a successful component result and no endpoint that exchanges a match DTO for an attendance write.

Before a real implementation may record attendance, a separately reviewed integration must:

1. Obtain the college's permitted approach, supervised enrollment rules, consent/authority, anti-spoof/liveness requirements, confidence semantics and acceptance criteria. No AI API/model is permitted by this milestone.
2. Define trusted local acquisition, ownership and freshness: secure capture source, single-use short-lived capture handles, actor/request/session bindings and atomic consumption. Browser IDs or arbitrary uploaded photographs cannot constitute trusted verification. Mock previews intentionally do not consume captures; repeated previews are not an implemented one-use receipt system.
3. Choose and authenticate the local transport (for example OS-restricted IPC or mutually authenticated loopback service). Loopback address alone is insufficient. Restrict allowed peers, messages, sizes, timeouts and filesystem access; avoid arbitrary URLs/paths, shell execution and browser-accessible service ports. Manage any transport credentials outside source. No network/IPC adapter or callback listener exists yet.
4. Implement strict parsing, version negotiation, result validation and component identity checks. Correlation UUIDs prevent response mixups; they do not authenticate a sender. Enforce capture replay protection across processes/restarts, with defined failure/retry semantics.
5. Add a server-only verified attendance command which consumes verified evidence and repeats authorization, actual-session, student and confirmed-enrollment validation inside the existing session transaction gate. Map only an accepted trusted match to Present and server-owned FaceRecognition method/actor/time. Enforce the existing unique `(AttendanceSessionId, StudentId)` constraint and handle concurrent manual/recognition submissions as 409. Never silently convert or correct an existing attendance record.
6. Preserve attendance and correction audit history; persist an appropriately minimized verification reference and component/version provenance only after a reviewed schema/retention design. Current schema has no evidence store and no migration is proposed in this milestone. Add real-adapter replay/concurrency/failure tests before activation. A preview duplicate precheck is advisory and cannot establish atomicity for future writes; the existing manual concurrency tests remain in place.

The current endpoint deliberately cannot write even if a component returns a real-looking result. Activation requires reviewed code and tests, not a client flag. Until then, an authorized staff member may use the explicit Manual workflow; service failure must never manufacture Present/Absent records or relabel manual attendance as recognition.

## Privacy, retention and failure handling

Current retention: zero images, templates, biometric embeddings or new recognition evidence are collected or persisted. Requests contain only opaque capture handles and existing database IDs; the unavailable adapter does no external work. Tests use synthetic IDs and deterministic results. No request-body/result/image logging is added. Avoid reverse-proxy/APM body logging for these routes, including capture handles and scores; attendance/student IDs remain sensitive even without images.

Before actual collection, the college must approve purpose, access, consent/authority, a concrete retention period, deletion and withdrawal workflow, audit access, encryption/key ownership, secure backups and breach handling. Prefer transient capture memory and prompt destruction after processing; do not reuse captures for training, unrelated analytics or third-party uploads. Enrollment templates require controlled deletion/re-enrollment and a minimal audit trail without template contents. Minimize component metadata: commands expose IDs/session bindings, not names, emails, passwords or JWT signing keys. Do not add biometric fields to ordinary student responses.

Unknown/multiple/ambiguous faces cause no writes; offer supervised retry or the separate Manual flow. Unavailability/timeout must leave attendance untouched. Do not automatically retry enrollment or attendance writes: an external enrollment may have completed before a timeout. A real adapter will need idempotent request handling, single-use capture state and enrollment-status reconciliation before retry. Suppress component exception details, bound work, honor cancellation and never log raw captures or confidence-bearing identity lists. The five-second deadline stops awaiting uncooperative code; a real adapter must also cancel underlying I/O/work to bound resources.

## Files and verification

- API: `Controllers/RecognitionController.cs`, `DTOs/RecognitionDtos.cs`, `Recognition/IRecognitionComponent.cs`, `Recognition/RecognitionWorkflow.cs`, `Recognition/RecognitionOperationFilter.cs`, and DI/OpenAPI registration in `Program.cs`.
- Tests: `MockRecognitionComponent.cs`, `RecognitionContractTests.cs`, `SqlServerRecognitionTests.cs`; the existing test factory accepts an explicit test-only interface replacement.
- Existing application models, migrations, dependencies, manual attendance services, reporting queries/DTOs and `closed-past-v1` are unchanged. No frontend changes or Git mutations.

Run from the repository root:

```bash
dotnet build tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj --no-restore
SMARTATTENDANCE_RUN_SQLSERVER_TESTS=1 dotnet test \
  tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj \
  --no-build --no-restore --logger 'trx;LogFileName=milestone7.trx' \
  --results-directory /tmp/smartattendance-milestone7
dotnet list SmartAttendance.API/SmartAttendance.API.csproj package --vulnerable --include-transitive --no-restore
dotnet list tests/SmartAttendance.IntegrationTests/SmartAttendance.IntegrationTests.csproj package --vulnerable --include-transitive --no-restore
dotnet ef migrations has-pending-model-changes --project SmartAttendance.API --no-build
git diff --check
```

SQL tests require Docker and the cached `mcr.microsoft.com/mssql/server:2022-latest` image; the existing fixture uses its own private loopback container, generated credentials and isolated disposable databases. It refuses external connection configuration. No migrations were applied to the development database; its outstanding approved migrations remain pending.

Actual final verification:

| Check | Result |
| --- | --- |
| API and test project build | Passed; 0 warnings, 0 errors |
| Full opted-in suite | 386 executed, 386 passed, 0 failed, 0 skipped |
| Milestone 6 preservation | All 305 previous test cases remain passing, verified by TRX case/outcome multiset comparison |
| Added coverage | 81 cases: authorization, request validation/overposting, explicit outcomes, response binding/replay/freshness, session/role/assignment changes, confirmed enrollment, duplicates, optional/invalid confidence, cancellation deadline, exception privacy, default disabled adapter, JSON round trips and Swagger |
| Disposable SQL Server coverage | 30 passed, including the previous 29 and one new recognition-contract relationship/duplicate test |
| API and test transitive package audits | No vulnerable packages reported by configured NuGet sources |
| EF model drift | No pending changes; no migration added |
| Whitespace check and container cleanup | Passed; no labeled disposable test containers remained |

Final logs: `/tmp/smartattendance-m7-build.log`, `/tmp/smartattendance-m7-full.log`. Final test report: `/tmp/smartattendance-milestone7/milestone7-final.trx` (the command above can use that filename for a separate final report). Real recognition accuracy, trusted capture, liveness, real enrollment/deletion, local transport authentication, durable replay protection, recognition attendance writes and frontend integration are not implemented or verified. No development database changes, frontend edits, commits or pushes were performed. Stop after this milestone for review.
