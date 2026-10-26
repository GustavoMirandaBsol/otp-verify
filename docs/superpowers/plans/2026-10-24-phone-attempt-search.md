# Phone Verification Attempt Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an authenticated search that returns the latest 3, 5, or 10 WhatsApp Verify attempts for an E.164 phone number and shows a detailed diagnosis for each selected attempt.

**Architecture:** Extend the existing Twilio REST wrapper with a filtered Verification Attempts query and map Twilio JSON into application-owned contracts. Expose the data through one Minimal API endpoint, audit only a masked phone, and extend the existing lookup panel without changing SID lookup or resend behavior.

**Tech Stack:** ASP.NET Core 8 Minimal API, `HttpClient`, `System.Text.Json`, SQLite, React 18, TypeScript 5.6, Vite 6, Tailwind CSS 3.

**Spec:** `docs/superpowers/specs/2026-10-24-phone-attempt-search-design.md`

## Global Constraints

- Search only the configured Twilio Verify Service and channel `whatsapp`.
- Accept only E.164 phone numbers and limits `3`, `5`, or `10`; use `3` as the UI and endpoint default.
- Return attempts newest first and never more than the requested limit.
- Never expose an OTP code or persist/return the unmasked phone in search results or audit.
- Reuse the existing Twilio environment variables; add no frontend secret or new runtime dependency.
- Clearly state that Twilio Verification Attempts covers at most the last 30 days and is Public Beta.
- Preserve the existing Message SID lookup, Verification SID lookup, resend controls, authentication, CORS, and rate limiting.
- The current workspace is not a Git repository, so task checkpoints replace commit steps unless repository metadata becomes available.

## Review Focus

- Malformed or non-E.164 phone: return HTTP 400 and do not call Twilio (Task 2 test).
- Missing, unsupported, or manipulated limit: default a missing value to 3; reject values outside `3|5|10` with HTTP 400 (Task 2 test).
- Empty Twilio result: return HTTP 200 with an empty list and render the 30-day guidance, without leaving stale selection state (Tasks 2 and 4 tests).
- Missing optional Twilio properties or an invalid date: map safely to null/unknown and keep the request usable (Task 2 test).
- Sensitive phone handling: return and audit only the masked form even when Twilio echoes the full destination (Tasks 2 and 3 tests).

---

### Task 1: Attempt contracts and diagnosis rules

**Files:**
- Modify: `backend/OtpSupport.Api/Models/Contracts.cs`
- Modify: `backend/OtpSupport.Api/Services/DiagnosisService.cs`
- Create: `tests/OtpSupport.Api.Tests/OtpSupport.Api.Tests.csproj`
- Create: `tests/OtpSupport.Api.Tests/Program.cs`

**Interfaces:**
- Produces: `TwilioVerificationAttempt(string Sid, string VerificationSid, string ConversionStatus, string Channel, string? DateCreated, string? DateUpdated, string MessageStatus, string AttemptStatus, string? ErrorCode, string To)`.
- Produces: `VerificationAttemptSearchItem(TwilioVerificationAttempt Attempt, Diagnosis Diagnosis)`.
- Produces: `DiagnosisService.ForAttempt(TwilioVerificationAttempt attempt) -> Diagnosis`.

- [x] **Step 1: Create the dependency-free test harness and failing diagnosis tests**

Create a `net8.0` console project with a `ProjectReference` to `backend/OtpSupport.Api/OtpSupport.Api.csproj`. In `Program.cs`, add named assertions covering: converted attempt; delivered but unconverted attempt; queued/pending attempt; and failed/undelivered attempt with an error code. Assert category, severity, and that delivery is not described as verification approval.

- [x] **Step 2: Run the harness and verify it fails because the attempt contract and diagnosis method do not exist**

Run: `dotnet run --project tests/OtpSupport.Api.Tests/OtpSupport.Api.Tests.csproj`

Expected: compilation failure naming `TwilioVerificationAttempt` and/or `ForAttempt`.

- [x] **Step 3: Add the attempt records and `DiagnosisService.ForAttempt`**

Use conversion status first when it is `converted`; otherwise classify `failed|undelivered` as error, `delivered|read` as delivery-confirmed without claiming approval, and all other message states as pending/warning. Normalize comparisons with `ToLowerInvariant()` and tolerate blank statuses.

- [x] **Step 4: Run the harness and verify all diagnosis assertions pass**

Run: `dotnet run --project tests/OtpSupport.Api.Tests/OtpSupport.Api.Tests.csproj`

Expected: exit code 0 with a PASS line for diagnosis tests.

- [x] **Step 5: Record the Task 1 checkpoint**

List the four changed/created files and the passing harness command. If Git metadata is later available, commit them as `feat: add verification attempt diagnosis`.

### Task 2: Twilio Verification Attempts query and mapping

**Files:**
- Modify: `backend/OtpSupport.Api/Services/TwilioClient.cs`
- Modify: `tests/OtpSupport.Api.Tests/Program.cs`

**Interfaces:**
- Consumes: `TwilioVerificationAttempt` from Task 1.
- Produces: `TwilioClient.VerificationAttempts(string phone, int? limit, CancellationToken ct) -> Task<IReadOnlyList<TwilioVerificationAttempt>>`; null normalizes to `3`.

- [x] **Step 1: Add failing client tests with a fake `HttpMessageHandler`**

Capture the outgoing URI and return deterministic Twilio JSON. Assert that the request targets `/v2/Attempts` and includes destination, configured Verify Service, channel `whatsapp`, and `PageSize` equal to the normalized requested limit; assert output is newest first, truncated to `3|5|10`, and contains only a masked `To`. Add cases for an empty `attempts` array and missing optional `channel_data` fields.

- [x] **Step 2: Add failing validation tests**

Assert that `VerificationAttempts` throws `ArgumentException` before sending HTTP for a non-E.164 phone and for limits `0`, `4`, and `11`. Assert the three allowed limits reach the handler and a null limit sends `PageSize=3`.

- [x] **Step 3: Run the harness and verify the new tests fail because `VerificationAttempts` is absent**

Run: `dotnet run --project tests/OtpSupport.Api.Tests/OtpSupport.Api.Tests.csproj`

Expected: compilation failure naming `VerificationAttempts`.

- [x] **Step 4: Implement `VerificationAttempts` and JSON mapping**

Build the query with `Uri.EscapeDataString`; send it through the existing authenticated `Send` path; read the `attempts` array; map nested `channel_data.to`, `status`, `message_status`, and `error_code`; mask `to` before constructing the contract; sort parsable `date_created` values descending with invalid/missing dates last; and take the requested limit. Return an empty array when `attempts` is absent or empty.

- [x] **Step 5: Run the harness and verify all client and diagnosis tests pass**

Run: `dotnet run --project tests/OtpSupport.Api.Tests/OtpSupport.Api.Tests.csproj`

Expected: exit code 0 with PASS lines for validation, mapping, ordering, masking, empty results, and diagnosis.

- [x] **Step 6: Record the Task 2 checkpoint**

Record the request URI asserted by the fake handler and the passing harness output. If Git metadata is later available, commit as `feat: query Twilio verification attempts`.

### Task 3: Protected endpoint and masked audit

**Files:**
- Modify: `backend/OtpSupport.Api/Program.cs`
- Modify: `backend/OtpSupport.Api/Data/Store.cs`
- Modify: `tests/OtpSupport.Api.Tests/Program.cs`

**Interfaces:**
- Consumes: `TwilioClient.VerificationAttempts(phone, limit, ct)` and `DiagnosisService.ForAttempt(attempt)`.
- Produces: `GET /api/verifications/attempts?phone={E.164}&limit={3|5|10}` returning `IReadOnlyList<VerificationAttemptSearchItem>`.
- Produces: `Store.AuditAttemptLookup(string phone, int requestedLimit, int resultCount)` which masks internally and records action `verification.attempts.lookup`.

- [x] **Step 1: Add a failing audit privacy test**

Create a temporary SQLite path through `ConfigurationBuilder`, initialize `Store`, call `AuditAttemptLookup` with a full phone, and assert the newest history row contains the fixed action, requested/result counts in detail, a masked `PhoneMasked`, and no occurrence of the full phone in any persisted string field.

- [x] **Step 2: Run the harness and verify it fails because `AuditAttemptLookup` is absent**

Run: `dotnet run --project tests/OtpSupport.Api.Tests/OtpSupport.Api.Tests.csproj`

Expected: compilation failure naming `AuditAttemptLookup`.

- [x] **Step 3: Implement `Store.AuditAttemptLookup`**

Delegate to the existing `Audit` method with an empty case ID, outcome `ok`, `TwilioClient.MaskPhone(phone)`, and detail containing only numeric limit/result counts.

- [x] **Step 4: Add the protected endpoint before the `/{sid}` verification route**

Declare the literal `/verifications/attempts` route before `/verifications/{sid}`. Pass the nullable `limit` to the client so it owns defaulting and validation, project every attempt to `VerificationAttemptSearchItem`, write the masked audit with the normalized limit, and use the existing `Handle` function for validation and Twilio failures.

- [x] **Step 5: Run backend tests and build**

Run: `dotnet run --project tests/OtpSupport.Api.Tests/OtpSupport.Api.Tests.csproj`

Run: `dotnet build backend/OtpSupport.Api/OtpSupport.Api.csproj --no-restore`

Expected: harness and build exit 0; NU1900 may remain a warning when the private feed vulnerability index is unavailable.

- [x] **Step 6: Record the Task 3 checkpoint**

Record the endpoint signature, audit privacy assertion, and build result. If Git metadata is later available, commit as `feat: expose phone attempt search endpoint`.

### Task 4: Phone search, result list, and detailed diagnosis UI

**Files:**
- Create: `frontend/src/attempts.ts`
- Modify: `frontend/src/App.tsx`
- Create: `frontend/tests/attempts.test.mjs`

**Interfaces:**
- Consumes: `GET /api/verifications/attempts` returning `{ attempt, diagnosis }[]`.
- Produces: `ATTEMPT_LIMITS = [3, 5, 10]`, `isE164(value: string) -> boolean`, and TypeScript types matching the backend response.

- [x] **Step 1: Add failing frontend helper tests**

Use Vite's SSR loader, following the existing no-new-dependency test style, to load `src/attempts.ts`. Assert the exact limit options, valid examples such as `+59170000000`, and rejection of missing `+`, spaces, letters, and too-short/too-long numbers.

- [x] **Step 2: Run the test and verify it fails because `src/attempts.ts` does not exist**

Run from `frontend`: `node tests/attempts.test.mjs`

Expected: module-not-found failure.

- [x] **Step 3: Implement the typed helper module**

Export the three allowed limits as a readonly tuple, E.164 validation using the same `^\+[1-9]\d{7,14}$` rule as the backend, and response types for attempt plus diagnosis.

- [x] **Step 4: Run the helper test and verify it passes**

Run from `frontend`: `node tests/attempts.test.mjs`

Expected: exit code 0 with a PASS line.

- [x] **Step 5: Extend `App.tsx` lookup state and request behavior**

Add lookup type `phone`, separate phone and limit state with default 3, call the new endpoint, clear stale list/selection whenever type or phone changes, and keep SID lookup behavior untouched.

- [x] **Step 6: Render the phone controls and result states**

For phone lookup, render the E.164 field and 3/5/10 combo. Disable search for invalid input. Render an explicit loading/error path through the existing runner, an empty message mentioning 30 days, and a newest-first clickable result list showing date, delivery, conversion, and both SIDs.

- [x] **Step 7: Render selected-attempt details**

Show all safe attempt fields and the backend diagnosis explanation/recommendation. Use the existing status tags, never render the original search phone, and label delivery separately from conversion/approval.

- [x] **Step 8: Run frontend tests and production build**

Run from `frontend`: `node tests/vite-config.test.mjs`

Run from `frontend`: `node tests/attempts.test.mjs`

Run from `frontend`: `npm.cmd run build`

Expected: both test scripts and TypeScript/Vite build exit 0.

- [x] **Step 9: Record the Task 4 checkpoint**

Record the test/build output and the three visible result states: empty, list, selected detail. If Git metadata is later available, commit as `feat: search OTP attempts by phone`.

### Task 5: Documentation and full regression verification

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: the completed endpoint and UI from Tasks 1–4.
- Produces: operator documentation for phone search, its 3/5/10 limits, the 30-day/Public Beta constraint, and masked-data behavior.

- [x] **Step 1: Update feature and endpoint documentation**

Replace the statement that phone history is unavailable with the precise Verification Attempts limitation. Document `GET /api/verifications/attempts`, allowed limits, WhatsApp-only behavior, and that detailed results distinguish delivery from conversion.

- [x] **Step 2: Run the complete automated verification set**

Run from repository root:

`powershell -NoProfile -ExecutionPolicy Bypass -File tests/local-environment.Tests.ps1`

`dotnet run --project tests/OtpSupport.Api.Tests/OtpSupport.Api.Tests.csproj`

`dotnet build backend/OtpSupport.Api/OtpSupport.Api.csproj --no-restore`

`node frontend/tests/vite-config.test.mjs`

`node frontend/tests/attempts.test.mjs`

`npm.cmd --prefix frontend run build`

Expected: every command exits 0; only the already-known NU1900 feed audit warning is acceptable.

- [x] **Step 3: Perform a local UI smoke check without exposing data**

Start both launchers, log in, confirm SID searches still render, and exercise phone search with an authorized test number. Verify 3/5/10 selection, newest-first list, selected detail, empty state, and that browser/API output and SQLite audit contain only masked phone values.

- [x] **Step 4: Record the final checkpoint**

Record exact verification commands, exit codes, any accepted warning, and the fact that live Twilio verification requires an authorized test number. If Git metadata is later available, commit as `docs: document phone attempt search`.

