# DevLaunch IDP

[![CI](https://github.com/eridjonkrrashi/devlaunch-idp/actions/workflows/ci.yml/badge.svg)](https://github.com/eridjonkrrashi/devlaunch-idp/actions/workflows/ci.yml)

A self-service Internal Developer Platform (IDP) API that lets developers deploy and manage containerised applications on Kubernetes without touching kubectl or YAML.

## What it does

- **Multi-tenant** — each project has its own namespace, API keys, resource quotas, and audit log
- **Deploy** — POST a spec (image, port, replicas, env vars, resource limits, HPA config) to ship a new app
- **Full lifecycle** — list, get live status, scale, roll back, delete; rollout tracking with auto-rollback on crash-loops
- **Horizontal Pod Autoscaler** — create or manage an HPA per app; manual scale disables it automatically
- **Reconciliation loop** — background service keeps database status in sync with live cluster state every 30 s
- **API key auth** — SHA-256-hashed keys with `Admin` / `Developer` roles, cross-tenant isolation enforced
- **Audit log** — every deploy, scale, and delete is recorded with the acting key
- **Structured logs** — Serilog JSON, ready for aggregation
- **Prometheus metrics** — `/metrics` endpoint for scraping
- **Health probes** — `/health` and `/ready` for k8s liveness/readiness checks
- **Swagger UI** — interactive API docs at `/swagger` in development

## Architecture

```
┌─────────────────────────────────────────────┐
│  ASP.NET Core 9 API (DevLaunch.Api)          │
│                                             │
│  ApiKeyMiddleware  (auth, tenant context)   │
│                                             │
│  ProjectsController                         │
│  ApplicationsController                     │
│       │                                     │
│  ProjectService ─── ApplicationService      │
│       └──────────── AppDbContext            │
│                     (SQLite / Postgres)     │
│  KubernetesService                          │
│       │                                     │
│  KubernetesClient v16 ──── cluster          │
│                                             │
│  ReconciliationService (background / 30 s)  │
│  AuditService                               │
└─────────────────────────────────────────────┘
```

## Quick start (Docker Compose)

> **Prerequisite**: Docker Desktop and a working kubeconfig at `~/.kube/config`.

```bash
docker compose up
```

On first start, the API prints a one-time **bootstrap admin key** to stdout:

```
BOOTSTRAP ADMIN KEY (shown only once — save it now!):
  dlk_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Use that key in all subsequent `X-API-Key` headers.

### Create a project and deploy an app

```bash
# Create a developer project (admin key required)
curl -s -X POST http://localhost:8080/api/projects \
  -H 'X-API-Key: dlk_ADMIN_KEY' \
  -H 'Content-Type: application/json' \
  -d '{"name":"my-team"}' | jq .

# Create a developer API key for that project
curl -s -X POST http://localhost:8080/api/projects/my-team/keys \
  -H 'X-API-Key: dlk_ADMIN_KEY' \
  -H 'Content-Type: application/json' \
  -d '{"name":"ci-key","role":"Developer"}' | jq .

# Deploy an app (developer key)
curl -s -X POST http://localhost:8080/api/applications \
  -H 'X-API-Key: dlk_DEV_KEY' \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "hello-world",
    "image": "nginx:latest",
    "port": 80,
    "replicas": 2,
    "cpuRequest": "100m",
    "cpuLimit": "500m",
    "memoryRequest": "128Mi",
    "memoryLimit": "512Mi"
  }' | jq .
```

### Other operations

```bash
# Scale
curl -s -X POST http://localhost:8080/api/applications/hello-world/scale \
  -H 'X-API-Key: dlk_DEV_KEY' \
  -H 'Content-Type: application/json' -d '{"replicas":3}'

# Roll back to previous revision
curl -s -X POST http://localhost:8080/api/applications/hello-world/rollback \
  -H 'X-API-Key: dlk_DEV_KEY' \
  -H 'Content-Type: application/json' -d '{}'

# Enable HPA
curl -s -X PUT http://localhost:8080/api/applications/hello-world \
  -H 'X-API-Key: dlk_DEV_KEY' \
  -H 'Content-Type: application/json' \
  -d '{"name":"hello-world","image":"nginx:latest","port":80,"replicas":2,
       "cpuRequest":"100m","cpuLimit":"500m","memoryRequest":"128Mi","memoryLimit":"512Mi",
       "hpaEnabled":true,"hpaMinReplicas":2,"hpaMaxReplicas":10,"hpaCpuTargetPercent":70}'

# Audit log
curl -s http://localhost:8080/api/projects/my-team/audit \
  -H 'X-API-Key: dlk_DEV_KEY' | jq .

# Pod logs
curl -s "http://localhost:8080/api/applications/hello-world/logs?lines=50" \
  -H 'X-API-Key: dlk_DEV_KEY'

# Delete
curl -s -X DELETE http://localhost:8080/api/applications/hello-world \
  -H 'X-API-Key: dlk_DEV_KEY'
```

## Running locally without Docker

```bash
dotnet run --project DevLaunch.Api
```

Falls back to SQLite (`DevLaunch.Api/data/devlaunch.db`) and discovers your local kubeconfig.

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

## API reference

Swagger UI: `http://localhost:8080/swagger` (development mode)

### Authentication

All endpoints except `/health`, `/ready`, and `/metrics` require `X-API-Key: dlk_...`.
Admin role is required for project management. Developer role is sufficient for applications.

### Projects

| Method | Path | Role | Description |
|--------|------|------|-------------|
| POST | `/api/projects` | Admin | Create a project (gets its own k8s namespace + quotas) |
| GET | `/api/projects` | Admin | List all projects |
| GET | `/api/projects/{name}` | Admin | Get project details |
| PUT | `/api/projects/{name}` | Admin | Update quotas |
| DELETE | `/api/projects/{name}` | Admin | Delete project and namespace |
| POST | `/api/projects/{name}/keys` | Admin | Create an API key |
| GET | `/api/projects/{name}/keys` | Admin | List API keys |
| DELETE | `/api/projects/{name}/keys/{id}` | Admin | Revoke an API key |
| GET | `/api/projects/{name}/audit` | Developer | Audit log for this project |

### Applications

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/applications` | Deploy a new application |
| GET | `/api/applications` | List all applications in the project |
| GET | `/api/applications/{name}` | Get details + live k8s status |
| PUT | `/api/applications/{name}` | Update spec and roll out a new revision |
| DELETE | `/api/applications/{name}` | Delete application and k8s resources |
| POST | `/api/applications/{name}/scale` | Scale replicas (disables HPA) |
| POST | `/api/applications/{name}/rollback` | Roll back to a previous revision |
| GET | `/api/applications/{name}/revisions` | List revision history |
| GET | `/api/applications/{name}/logs` | Get pod logs (`?lines=N`) |
| GET | `/api/applications/{name}/events` | Get k8s events |

### Observability

| Path | Description |
|------|-------------|
| `/health` | Liveness probe |
| `/ready` | Readiness probe |
| `/metrics` | Prometheus metrics |

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `Database:Provider` | `sqlite` | `sqlite` or `postgres` |
| `ConnectionStrings:Postgres` | — | Postgres connection string |

## Tech stack

- .NET 9 / ASP.NET Core 9
- Entity Framework Core 9 (SQLite dev / PostgreSQL prod)
- KubernetesClient 16
- Serilog + Prometheus-net
- xUnit + Moq + WebApplicationFactory

## License

MIT
