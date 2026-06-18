# DevLaunch IDP

[![CI](https://github.com/eridjonkrrashi/devlaunch-idp/actions/workflows/ci.yml/badge.svg)](https://github.com/eridjonkrrashi/devlaunch-idp/actions/workflows/ci.yml)

A production-grade **Internal Developer Platform** — a self-service portal where teams deploy and manage containerised applications on Kubernetes without touching kubectl or YAML. Includes a React UI, REST API, and full Kubernetes lifecycle management.

## What it does

- **React UI** — dashboard to deploy, inspect, scale, roll back, and delete apps; API key and project management; real-time status badges; pod logs and events viewer
- **Multi-tenant** — each project has its own namespace, API keys, resource quotas, and audit log
- **Deploy** — submit a spec (image, port, replicas, env vars, resource limits, HPA config) to ship a new app
- **Full lifecycle** — list, live status, scale, roll back, delete; rollout tracking with auto-rollback on crash-loops
- **Horizontal Pod Autoscaler** — create or manage an HPA per app; manual scale disables it automatically
- **Reconciliation loop** — background service keeps database status in sync with live cluster state every 30 s
- **API key auth** — SHA-256-hashed keys with `Admin` / `Developer` roles, cross-tenant isolation enforced
- **Audit log** — every deploy, scale, and delete is recorded with the acting key
- **Structured logs** — Serilog JSON, ready for aggregation
- **Prometheus metrics** — `/metrics` endpoint with a pre-built Grafana dashboard (`docs/grafana/devlaunch-dashboard.json`)
- **Health probes** — `/health` and `/ready` for k8s liveness/readiness checks
- **Swagger UI** — interactive API docs at `/swagger` in development
- **Helm chart** — `charts/devlaunch-idp/` for deploying the IDP itself to a cluster

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  React SPA (devlaunch-frontend)                     │
│  Vite + TypeScript + Tailwind CSS + React Router   │
│  → served as static files from DevLaunch.Api/wwwroot│
└────────────────────┬────────────────────────────────┘
                     │ /api/*  (X-API-Key header)
┌────────────────────▼────────────────────────────────┐
│  ASP.NET Core 9 API (DevLaunch.Api)                  │
│                                                     │
│  ApiKeyMiddleware  (auth, tenant context)           │
│                                                     │
│  ProjectsController    ApplicationsController       │
│       │                                             │
│  ProjectService ─── ApplicationService              │
│       └──────────── AppDbContext (SQLite/Postgres)  │
│                                                     │
│  KubernetesService (KubernetesClient v16)           │
│       └──────────── k8s cluster                    │
│                                                     │
│  ReconciliationService (background / 30 s)         │
│  AuditService · Serilog · Prometheus               │
└─────────────────────────────────────────────────────┘
```

## Quick start (Docker Compose)

> **Prerequisite**: Docker Desktop and a working kubeconfig at `~/.kube/config`.

```bash
docker compose up
```

On first start the API prints a one-time **bootstrap admin key** to stdout:

```
BOOTSTRAP ADMIN KEY (shown only once — save it now!):
  dlk_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Open `http://localhost:8080` in a browser — the React UI loads. Paste the bootstrap key to sign in.

## Running locally (dev mode with hot-reload)

```bash
# Terminal 1 — start a local cluster and kubectl proxy (Windows / kind)
kind create cluster --name devlaunch-local
kubectl proxy --port 8001

# Terminal 2 — API
Kubernetes__ProxyUrl=http://localhost:8001 dotnet run --project DevLaunch.Api

# Terminal 3 — frontend with HMR
cd devlaunch-frontend && npm install && npm run dev
# → http://localhost:5173 (proxied to API on port 5293)
```

## Running tests

```bash
dotnet test
```

167 tests across six suites — all green, ~78% line coverage:

| Suite | Tests | What it covers |
|---|---|---|
| `ApplicationServiceTests` | 50 | Full lifecycle with in-memory SQLite + mocked k8s |
| `IntegrationTests` | ~35 | HTTP-layer end-to-end: auth, CRUD, tenant isolation, roles, HPA, audit |
| `NegativeBoundaryTests` | ~45 | Bad inputs, injection strings, invalid ranges → correct 4xx |
| `FailureInjectionTests` | ~10 | k8s unavailable, crash-loop auto-rollback, quota exceeded, concurrency |
| `ProjectServiceTests` | ~28 | Project & API key lifecycle, bootstrap |
| `ManifestGenerationTests` / `ValidationTests` | 50 | Manifest correctness, validation rules |

## Helm chart

```bash
# Install with bundled Postgres (local/dev):
helm install devlaunch ./charts/devlaunch-idp \
  --set image.tag=latest \
  --set ingress.enabled=true \
  --set ingress.host=idp.example.com

# Production (external Postgres, existing registry image):
helm install devlaunch ./charts/devlaunch-idp \
  --set postgres.enabled=false \
  --set database.postgresConnectionString="Host=my-db;..." \
  --set image.repository=ghcr.io/eridjonkrrashi/devlaunch-idp \
  --set image.tag=<sha> \
  --set ingress.enabled=true \
  --set ingress.host=idp.yourdomain.com
```

## API reference

Swagger UI at `http://localhost:8080/swagger` (dev) or `http://localhost:5293/swagger` (direct dotnet run).

### Authentication

All endpoints except `/health`, `/ready`, and `/metrics` require `X-API-Key: dlk_...`.
Admin role is required for project management. Developer role is sufficient for applications.

### Projects

| Method | Path | Role | Description |
|--------|------|------|-------------|
| POST | `/api/projects` | Admin | Create project (namespace + quotas provisioned) |
| GET | `/api/projects` | Any | List projects (admin: all; developer: own) |
| GET | `/api/projects/{id}` | Any | Get project |
| PATCH | `/api/projects/{id}` | Admin | Update quotas/description |
| DELETE | `/api/projects/{id}` | Admin | Delete project |
| POST | `/api/projects/{id}/api-keys` | Admin | Create API key |
| GET | `/api/projects/{id}/api-keys` | Admin | List API keys |
| DELETE | `/api/projects/{id}/api-keys/{keyId}` | Admin | Revoke API key |
| GET | `/api/projects/{id}/audit` | Any | Audit log |

### Applications

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/applications` | Deploy application |
| GET | `/api/applications` | List apps (live status) |
| GET | `/api/applications/{name}` | App detail + live k8s status |
| PUT | `/api/applications/{name}` | Update spec + new revision |
| DELETE | `/api/applications/{name}` | Delete app + cluster resources |
| POST | `/api/applications/{name}/scale` | Scale replicas |
| POST | `/api/applications/{name}/rollback` | Roll back to previous revision |
| GET | `/api/applications/{name}/revisions` | Revision history |
| GET | `/api/applications/{name}/logs` | Pod logs (`?lines=N`) |
| GET | `/api/applications/{name}/events` | Kubernetes events |

### Observability

| Path | Description |
|------|-------------|
| `/health` | Liveness probe |
| `/ready` | Readiness probe |
| `/metrics` | Prometheus metrics |

Import `docs/grafana/devlaunch-dashboard.json` into Grafana to get pre-built panels for request rate, latency P95, app counters, and reconcile errors.

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `Database:Provider` | `sqlite` | `sqlite` or `postgres` |
| `ConnectionStrings:Postgres` | — | Postgres connection string |
| `Kubernetes:ProxyUrl` | — | kubectl proxy URL (Windows + kind) |

## Windows + kind note

On Windows, .NET's SChannel stack cannot load the PEM client cert embedded in a kind kubeconfig. Start `kubectl proxy --port 8001` and set `Kubernetes__ProxyUrl=http://localhost:8001`. Linux/macOS work with direct kubeconfig TLS.

## Tech stack

| Layer | Technology |
|-------|-----------|
| Frontend | React 19, Vite 8, TypeScript, Tailwind CSS 4, React Router 7, Lucide icons |
| API | ASP.NET Core 9, .NET 9 |
| Database | Entity Framework Core 9, SQLite (dev/test), PostgreSQL (prod) |
| Kubernetes | KubernetesClient v16, full CRD lifecycle |
| Observability | Serilog (compact JSON), Prometheus-net, health checks |
| Testing | xUnit, Moq, WebApplicationFactory, Coverlet |
| CI/CD | GitHub Actions, Docker multi-stage build, GHCR |
| Deploy | Helm chart, Docker Compose |

## License

MIT
