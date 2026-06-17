# Stability Report — DevLaunch IDP

**Date:** 2026-06-17  
**Build:** `main` @ `59d47c3` + hardening commits  
**Test run:** `dotnet test` on .NET 9.0 / Windows 11

---

## 1. Build status

```
Build succeeded.  0 Error(s)  2 Warning(s)
```

Warnings:
- `NU1902` — KubernetesClient 16.0.1 has a known *moderate* severity vuln (GHSA-w7r3-mgwf-4mqq). Tracked; upgrade blocked on API compatibility with the k8s v1.30 client surface.
- `CS9113` — Unused `logger` parameter in `ProjectsController` constructor (cosmetic; no runtime impact).

---

## 2. Test results

```
Passed!  Failed: 0  Passed: 167  Skipped: 0  Total: 167  Duration: 1.1 s
```

### Suite breakdown

| Suite | Count | What it exercises |
|---|---|---|
| `ApplicationServiceTests` | 50 | Unit: full CRUD lifecycle, rollout tracking, quota enforcement, HPA, in-memory SQLite, mocked `IKubernetesService` |
| `IntegrationTests` | ~35 | HTTP: auth (401/403/200), CRUD over `WebApplicationFactory`, tenant isolation, role gates, audit log, API key revocation, HPA setting persistence |
| `NegativeBoundaryTests` | ~45 | Bad names (8 Theory cases), out-of-range replicas (4), invalid resources (6), injection strings (5), duplicate → 409, non-existent → 404, scale/rollback bounds |
| `FailureInjectionTests` | ~10 | k8s unavailable → 500 + DB consistent, crash-loop → auto-rollback, concurrent create → exactly 1 created, quota exceeded → 429, k8s namespace fail → project persisted, live-status null → graceful 200 |
| `ProjectServiceTests` | ~28 | Project CRUD, API key hashing, roles, revocation, bootstrap |
| `ManifestGenerationTests` + `ValidationTests` | ~50 | Kubernetes manifest correctness (labels, probes, resource limits, env vars, HPA), input validation regex |

---

## 3. Coverage

Collected with `coverlet.collector` (XPlat Code Coverage):

| Metric | Value |
|--------|-------|
| **Line coverage** | **78.3%** |
| Branch coverage | 49.1% |

Coverage threshold gate in CI: **75% lines** (hard fail).

Lower branch coverage is expected: many branches are in `KubernetesService` (the real k8s implementation) which is fully replaced by a mock in tests. The business logic in `ApplicationService`, `ProjectService`, `ApplicationsController`, and `ProjectsController` is well-covered by the integration and unit suites.

---

## 4. Failure injection evidence

### 4a. k8s deploy unavailable → 500 + DB consistent

Test: `Deploy_K8sThrows_Returns500WithJsonError` + `Deploy_K8sThrows_AppMarkedFailedInDb`

- Mock throws `Exception("Kubernetes API unavailable")` for a specific app name
- Controller returns `500` with `{ "error": "Deploy failed", "details": "..." }`
- DB record is written with `Status = Failed` (not orphaned, not missing)

### 4b. k8s delete unavailable → 204 + DB cleaned up

Test: `Delete_K8sThrows_StillRemovesFromDb`

- k8s `DeleteApplicationAsync` throws during delete
- API still returns `204 NoContent` — app is removed from the database
- Subsequent GET returns `404`

### 4c. Concurrent create of same app → exactly 1 created

Test: `ConcurrentCreate_SameAppName_ExactlyOneSucceeds`

- 5 simultaneous POST requests for the same app name
- Result: exactly 1 × `201 Created`, 4 × `409 Conflict`
- Race condition at `AnyAsync` check handled by unique index + `DbUpdateException → 409` in controller

### 4d. Crash-loop detection → auto-rollback

Test: `CheckRollout_CrashLoop_AutoRollsBack`

- App deployed at `nginx:1.0`, then updated to `bad:image`
- Live status mock: 0 ready replicas, pod restart count = 3
- `ApplicationService.CheckRolloutAsync` detects crash-loop, reverts image to `nginx:1.0`
- Post-rollback: `RolloutPhase = InProgress`, `Image = "nginx:1.0"`, `RolloutMessage` contains "crash-loop"

### 4e. Quota exceeded → 429

Test: `Create_QuotaExceeded_Returns429`

- Project quota set to `MaxApps = 1`
- First create: `201 Created`
- Second create: `429 Too Many Requests`

### 4f. k8s namespace failure on project create → project persisted

Test: `CreateProject_K8sNamespaceFails_ProjectStillPersisted`

- `EnsureNamespaceAsync` throws for a specific namespace name
- Project create returns `201` — project is in DB (reconciler can retry namespace creation)

---

## 5. Input rejection evidence

All of the following return `400 Bad Request` with a non-empty JSON body:

| Category | Test cases |
|----------|-----------|
| Invalid DNS names | `""`, `"UPPERCASE"`, `"1starts-digit"`, `"-starts-hyphen"`, `"ends-hyphen-"`, `"has spaces"`, `"has_underscore"`, 65-char string |
| Injection strings | SQL `'; DROP TABLE--`, XSS `<script>`, path traversal `../../../etc/passwd`, JNDI `${jndi:ldap://x/a}`, emoji `😀emoji` |
| Out-of-range replicas | `0`, `-1`, `51`, `int.MaxValue` |
| Invalid resource format | `"invalid"`, `"1.5"` (CPU), `"128bytes"` (memory), `"0"` (memory) |
| Invalid port | `0`, `-1`, `65536` |
| Invalid HPA | `minReplicas=0`, `maxReplicas=0`, `cpuTarget=0`, `cpuTarget=101` |
| Invalid project name | `""`, `"UPPERCASE-PROJECT"`, `"1starts-digit"`, `"has spaces"` |

---

## 6. Cross-tenant isolation evidence

Tests: `TenantA_ListDoesNotShowTenantBApps`, `TenantA_CannotAccessTenantBApp`, `TenantA_CannotReadTenantBAuditLog`

- Two separate projects created with separate API keys
- Tenant A cannot see, access, or get audit logs for Tenant B's resources
- All cross-tenant access returns `404` (resource not found — not `403` to avoid enumeration)

---

## 7. CI gates (`.github/workflows/ci.yml`)

| Gate | Behaviour |
|------|-----------|
| Build | `dotnet build --configuration Release` — zero warnings permitted at error level |
| Test | `dotnet test --no-build` — any failure blocks merge |
| Coverage | ReportGenerator extracts line rate; fails PR if < 75% |
| Docker | Image builds successfully on every push to `main` |
| E2E / chaos | `workflow_dispatch` only — runs a `kind` cluster smoke test on demand |

---

## 8. Known gaps

| Gap | Notes |
|-----|-------|
| Branch coverage < 60% | k8s implementation paths are mocked; would require a live cluster to cover |
| Load / soak tests | Blocked on a real or ephemeral cluster — not run locally |
| Grafana dashboard JSON | `/metrics` scraping is wired; dashboard JSON not yet exported |
| Frontend | Explicitly out of scope; API-only platform |
| KubernetesClient vuln | NU1902 — moderate; no upgrade path without API churn |
