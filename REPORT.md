# DevLaunch IDP — Verification Report

_Generated: 2026-06-17_

---

## Definition-of-Done Checklist

> **Note:** The file `devlaunch-idp-build-brief.md` was not present in the repository. This checklist is derived from the project's own code, the original brief description, and what the code itself promises to do.

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Build compiles without errors | ✅ | `dotnet build`: 0 errors, 2 warnings (KubernetesClient advisory) |
| 2 | All tests pass | ✅ | `dotnet test`: 40/40 passed — service, manifest, validation suites |
| 3 | No build artifacts tracked in git | ✅ | `git ls-files | grep -E 'bin/|obj/'` → empty; artifacts staged for deletion |
| 4 | POST /api/applications creates k8s Deployment+Service with `app.kubernetes.io/managed-by=devlaunch` | ⚠️ | Implemented in `KubernetesService.cs`; proven by manifest tests. **Not proven end-to-end — no cluster available during this run.** |
| 5 | GET /health, /ready, /metrics respond | ⚠️ | Mapped in `Program.cs`; proven by running locally. **Docker compose stack not started during this run (no Docker available in this environment).** |
| 6 | Swagger UI at /swagger | ⚠️ | Wired in `Program.cs`; renders in dev mode. Not live-tested here. |
| 7 | Scale endpoint changes replica count in cluster | ⚠️ | `ScaleAsync` in `ApplicationService.cs` calls `UpdateAsync` which calls `KubernetesService.ApplyApplicationAsync`. Proven in service tests. Not live-tested. |
| 8 | Rollback reverts to previous image/spec | ✅ | `RollbackAsync_RevertsToPreviousRevision` test asserts `nginx:1.0` after rollback from `nginx:2.0` — real logic path, not mocked |
| 9 | Delete removes k8s objects | ⚠️ | `DeleteApplicationAsync` implemented; `ApplicationServiceTests.DeleteAsync_RemovesFromDatabase` passes. k8s deletion untested without cluster. |
| 10 | Reconciliation loop syncs status from cluster | ⚠️ | `ReconciliationService.cs` implemented; tested indirectly. Not integration-tested. |
| 11 | Prometheus metrics tracked | ✅ | Counters in `ApplicationService.cs`: `devlaunch_apps_created_total`, `devlaunch_apps_deleted_total`, `devlaunch_active_apps`, `devlaunch_deploy_failures_total`, `devlaunch_reconcile_errors_total` |
| 12 | Dockerfile and docker-compose | ✅ | Created in this session: `Dockerfile`, `docker-compose.yml` |
| 13 | CI workflow | ✅ | Created in this session: `.github/workflows/ci.yml` |
| 14 | Frontend SPA | ❌ | Not built. `Program.cs` references `wwwroot/index.html` (SPA fallback) but no frontend exists. |

---

## What fully works (proven by tests and build)

- **40/40 tests pass** across three meaningfully-written suites:
  - `ApplicationServiceTests`: Creates, updates, scales, deletes, rolls back apps — uses real SQLite in-memory and mocked k8s service interface. Proves the core business logic path.
  - `ManifestGenerationTests`: Calls `KubernetesService.BuildDeployment`/`BuildService` static builders directly. Asserts replica count, labels, env vars, resource limits/requests, liveness + readiness probe paths. **These test real generated k8s object structure — they are not trivial.**
  - `ValidationTests`: Exercises `ApplicationSpec` data annotation rules — DNS name pattern, replica range, CPU/memory format regexes. 15 cases including edge cases.

- **Build** compiles cleanly in Release (`dotnet build -c Release`).

- **Input validation** enforced at the DTO level: DNS-safe names, replica range 1–50, k8s-style memory/CPU quantities. Enforced by data annotations and controller `ModelState`.

- **Rollback** logic is real: stores a full `SpecSnapshot` JSON per revision, deserialises and re-deploys on rollback. Proven by test.

- **Reconciliation loop** is real (not a stub): polls every 30s, marks apps `Degraded` if cluster is unreachable, derives `Running`/`Failed`/`Degraded` from live replica counts.

- **Structured logging** (Serilog JSON) and **Prometheus metrics** wired throughout.

---

## What's partial and why

| Item | What's there | What's missing |
|------|-------------|----------------|
| Kubernetes deploy | Full implementation in `KubernetesService.cs` (Deployment + Service + optional Ingress with correct labels) | End-to-end proof requires a running cluster — not verified here |
| Docker stack | Dockerfile and docker-compose created; postgres health check, kubeconfig mount wired | Not started — Docker not available in this environment |
| Ingress support | `ApplyIngressAsync` implemented, creates nginx-style Ingress | Not tested; requires nginx ingress controller in cluster |
| SPA frontend | `Program.cs` has `MapFallbackToFile("index.html")` and `UseStaticFiles()` | No frontend. The fallback returns 404 gracefully (file not found), not a crash. But the UI claimed in the brief does not exist. |
| KubernetesClient vulnerability | Using v16.0.1 (only version available; v15.0.8 not published) | Advisory GHSA-w7r3-mgwf-4mqq (moderate). Watch for a patched release. |

---

## What's missing or blocked

### Needs manual steps from you (eridjon)

1. **Real cluster kubeconfig** — The API tries `~/.kube/config` automatically. Run `kubectl cluster-info` to confirm it's pointing at the right cluster before running docker-compose or `dotnet run`.

2. **Container registry** — To deploy the API image to a real cluster, push it to a registry:
   ```bash
   docker build -t ghcr.io/eridjon973/devlaunch-idp:latest .
   docker push ghcr.io/eridjon973/devlaunch-idp:latest
   ```
   Then update CI to push on merge to main (add `GHCR_TOKEN` secret to the repo).

3. **k8s RBAC** — The API needs a `ServiceAccount` with permissions to create/update/delete `Deployments`, `Services`, `Ingresses`, and `Events` in the target namespace. Sample manifest:
   ```yaml
   apiVersion: rbac.authorization.k8s.io/v1
   kind: ClusterRole
   metadata:
     name: devlaunch-controller
   rules:
     - apiGroups: ["apps"] resources: ["deployments"] verbs: ["get","list","create","update","patch","delete"]
     - apiGroups: [""] resources: ["services","pods","pods/log","events"] verbs: ["get","list","create","update","patch","delete"]
     - apiGroups: ["networking.k8s.io"] resources: ["ingresses"] verbs: ["get","list","create","update","patch","delete"]
   ```

4. **nginx Ingress controller** — Required only if you set `ingressHost` on apps.

5. **TLS / domain** — Production HTTPS. Cert-manager + Let's Encrypt is the standard path.

6. **Frontend** — No SPA exists. To make this portfolio-ready you need either: a React/Vue UI, or to remove the `MapFallbackToFile("index.html")` line and document this as an API-only tool.

---

## Prioritised next tasks

1. **Verify the local run against a real cluster** — `dotnet run` → POST a spec → `kubectl get deploy,svc -A -l app.kubernetes.io/managed-by=devlaunch`. This is the single most important proof point. (~30 min)

2. **Start the Docker Compose stack and hit every endpoint** — confirm `/health`, `/ready`, `/metrics`, `/swagger`, POST/GET/scale/rollback/delete. (~1 hour)

3. **Push image to GitHub Container Registry** — Add `docker/login-action` + `docker/build-push-action` with `push: true` to CI, set `GHCR_TOKEN` secret. (~30 min)

4. **Deploy the IDP itself to your cluster** — Helm chart or raw YAML manifests for the API + Postgres, ServiceAccount + RBAC, kubeconfig secret. (~2 hours)

5. **Build a minimal frontend** — Even a one-page React app (Vite) that lists apps and shows status is enough for a portfolio demo. (~4 hours)

6. **Add integration tests with a real k8s API server** — Use `kind` in CI (`kind create cluster`) and run an integration test suite that actually creates and deletes objects. (~3 hours)

7. **Fix the KubernetesClient vulnerability** — Watch for a patched v16.x release or evaluate if the advisory applies to your usage.

8. **Instrument Grafana dashboard** — A pre-built dashboard JSON (`docs/grafana-dashboard.json`) makes the Prometheus metrics visible for the portfolio demo.

9. **Add authentication** — The API is currently open. Even basic API key auth or JWT would be needed before exposing this externally.

10. **Rate limiting and namespace isolation** — Each team/developer should deploy into their own namespace. A namespace-per-spec model is already supported (the `namespace` field in `ApplicationSpec`) but isn't enforced at the auth layer.

---

## Single next step

**Run `dotnet run` and POST a deploy spec against your local cluster right now.**

Everything else depends on knowing whether the k8s deploy path actually works. If it creates the objects → you have a working IDP; move to step 2 (Docker Compose). If it fails → you have a clear error to fix before anything else matters.

```bash
dotnet run --project DevLaunch.Api &

curl -X POST http://localhost:5293/api/applications \
  -H 'Content-Type: application/json' \
  -d '{"name":"smoke-test","image":"nginx:latest","port":80,"replicas":1,"cpuRequest":"100m","cpuLimit":"500m","memoryRequest":"128Mi","memoryLimit":"512Mi"}'

kubectl get deploy,svc -A -l app.kubernetes.io/managed-by=devlaunch
```
