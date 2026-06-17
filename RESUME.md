# DevLaunch IDP — Resume Reference

## Project summary

**DevLaunch IDP** is a production-ready Internal Developer Platform API built in .NET 9 / ASP.NET Core 9. It lets development teams deploy and manage containerised applications on Kubernetes through a REST API — no kubectl, no YAML. Think a minimal Heroku-style self-service layer backed by a real Kubernetes cluster.

---

## Resume bullet points

> Pick 3–5 depending on role. All claims are verified by the test suite and code in this repo.

- **Built a multi-tenant Kubernetes IDP from scratch** in .NET 9/ASP.NET Core 9: REST API for full app lifecycle (deploy, scale, rollback, delete), per-project namespaces, HPA management, and a 30 s reconciliation loop that auto-rolls back crash-looping deployments.

- **Designed a layered auth system** with SHA-256-hashed API keys, `Admin`/`Developer` RBAC roles, and tenant isolation enforced at the middleware layer — cross-tenant access returns 404 to prevent enumeration.

- **Achieved 78% line coverage** across 167 tests spanning unit, integration (WebApplicationFactory), boundary/negative, failure injection, and concurrency tests — including a concurrent-create test verifying exactly 1 of 5 simultaneous requests succeeds.

- **Hardened against failure modes**: k8s unavailable → correct HTTP 5xx + DB consistency; concurrent duplicate creates handled by unique-index + `DbUpdateException` → 409; quota exceeded → 429; crash-loop detection triggers automatic rollback.

- **Wired end-to-end observability**: Serilog structured JSON logs, Prometheus metrics via prometheus-net, `/health` + `/ready` probes, full audit log per project, Swagger UI, and a CI pipeline with coverage gating (75% threshold).

---

## Skills demonstrated

| Area | Detail |
|------|--------|
| **Backend** | ASP.NET Core 9 minimal host, EF Core 9, controller-based API with `ProblemDetails`, middleware pipeline |
| **Kubernetes** | KubernetesClient v16; Deployment, Service, Ingress, HPA, ResourceQuota, LimitRange; in-cluster + out-of-cluster config |
| **Testing** | xUnit, Moq, `WebApplicationFactory<Program>`, `IClassFixture`, `ConfigureTestServices`, `coverlet.collector` |
| **Auth / Security** | API key hashing (SHA-256), RBAC, tenant isolation, input validation (regex + DataAnnotations), injection string rejection |
| **Observability** | Serilog (compact JSON), Prometheus-net, health checks, audit log |
| **DevOps / CI** | GitHub Actions (build → test → coverage gate → Docker build), multi-stage `Dockerfile`, Docker Compose |
| **Database** | EF Core migrations, SQLite (dev/test) + PostgreSQL (prod), unique indexes, DB-level constraint handling |

---

## Architecture decisions worth discussing

**Why EF Core + SQLite/Postgres instead of a document store?**
The data model is relational: projects own API keys own audit entries; applications own revisions. EF Core with migrations gives a clean upgrade path and makes the unique-index-based duplicate detection easy to reason about.

**Why is `Program.cs` a `public partial class`?**
`WebApplicationFactory<T>` needs the `Program` type to be public. Minimal API programs make it `internal` by default; `public partial class Program {}` is the idiomatic .NET 9 fix without restructuring the startup code.

**Why `CreateLogger()` instead of `CreateBootstrapLogger()` for Serilog?**
`CreateBootstrapLogger()` creates a `ReloadableLogger` that can only be `Freeze()`d once per process. With multiple `WebApplicationFactory` instances in a test run, the second factory would crash. `CreateLogger()` produces a final logger; `UseSerilog()` replaces it without calling `Freeze()`.

**How is crash-loop auto-rollback implemented?**
`ReconciliationService` calls `ApplicationService.CheckRolloutAsync` every 30 s. If `GetLiveStatusAsync` shows 0 ready replicas and any pod has `RestartCount >= 3`, the service updates `Image` and `RolloutPhase` back to the previous revision and calls `ApplyApplicationAsync` again. The test suite verifies this path directly without waiting for the timer.

---

## How to run and demo

```bash
git clone https://github.com/eridjonkrrashi/devlaunch-idp.git
cd devlaunch-idp

# Run all 167 tests
dotnet test

# Start the API (needs a kubeconfig)
dotnet run --project DevLaunch.Api
# → Bootstrap admin key printed once to stdout

# Swagger UI
open http://localhost:5146/swagger
```

---

## File map (for code review)

| Path | What to look at |
|------|----------------|
| `DevLaunch.Api/Services/ApplicationService.cs` | Full deploy/update/rollback/quota logic |
| `DevLaunch.Api/Services/ReconciliationService.cs` | Background reconciler + crash-loop detection |
| `DevLaunch.Api/Services/KubernetesService.cs` | Kubernetes manifest generation and apply |
| `DevLaunch.Api/Middleware/ApiKeyMiddleware.cs` | Auth + tenant context injection |
| `DevLaunch.Api/Controllers/ApplicationsController.cs` | REST endpoints, error handling |
| `DevLaunch.Api/Controllers/ProjectsController.cs` | Project/key management, audit log |
| `DevLaunch.Tests/DevLaunchFactory.cs` | Shared WebApplicationFactory with mock DI |
| `DevLaunch.Tests/IntegrationTests.cs` | Full HTTP integration test suite |
| `DevLaunch.Tests/FailureInjectionTests.cs` | Failure / resilience / concurrency tests |
| `.github/workflows/ci.yml` | CI pipeline with coverage gate |
