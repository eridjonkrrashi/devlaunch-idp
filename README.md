# DevLaunch IDP

A self-service Internal Developer Platform (IDP) API that lets developers deploy and manage containerised applications on Kubernetes without touching kubectl or YAML.

## What it does

- **Deploy** a containerised app by POSTing a spec (image, port, replicas, env vars, resource limits)
- **Manage the full lifecycle** — list, get status, scale, roll back, delete
- **Reconciliation loop** — background service keeps database status in sync with live cluster state every 30 s
- **Structured logs** — Serilog JSON, ready for log aggregation
- **Prometheus metrics** — `/metrics` endpoint for scraping
- **Health probes** — `/health` and `/ready` for k8s liveness/readiness checks
- **Swagger UI** — interactive API docs at `/swagger` in development

## Architecture

```
┌─────────────────────────────────────────┐
│  ASP.NET Core 9 API (DevLaunch.Api)     │
│                                         │
│  ApplicationsController                 │
│       │                                 │
│  ApplicationService  ──── AppDbContext  │
│       │               (SQLite / Postgres)│
│  KubernetesService                      │
│       │                                 │
│  KubernetesClient v16 ──── cluster      │
│                                         │
│  ReconciliationService (background)     │
└─────────────────────────────────────────┘
```

## Quick start (Docker Compose)

> **Prerequisite**: Docker Desktop and a working kubeconfig at `~/.kube/config`.

```bash
# (optional) set a real postgres password
export POSTGRES_PASSWORD=changeme

docker compose up
```

The API is available at `http://localhost:8080`.

### Deploy your first app

```bash
curl -s -X POST http://localhost:8080/api/applications \
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
# List apps
curl -s http://localhost:8080/api/applications | jq .

# Get details + live k8s status
curl -s http://localhost:8080/api/applications/hello-world | jq .

# Scale to 3 replicas
curl -s -X POST http://localhost:8080/api/applications/hello-world/scale \
  -H 'Content-Type: application/json' -d '{"replicas": 3}'

# Roll back to the previous revision
curl -s -X POST http://localhost:8080/api/applications/hello-world/rollback \
  -H 'Content-Type: application/json' -d '{}'

# Get pod logs
curl -s "http://localhost:8080/api/applications/hello-world/logs?lines=50"

# Get k8s events
curl -s http://localhost:8080/api/applications/hello-world/events

# Delete
curl -s -X DELETE http://localhost:8080/api/applications/hello-world
```

## Running locally without Docker

```bash
dotnet run --project DevLaunch.Api
```

The API falls back to SQLite (stored in `DevLaunch.Api/data/devlaunch.db`) and auto-discovers your local kubeconfig.

## Running tests

```bash
dotnet test
```

40 tests across three suites:
- `ApplicationServiceTests` — full lifecycle using in-memory SQLite, mocked k8s service
- `ManifestGenerationTests` — verifies Deployment/Service manifests (labels, probes, resource limits, env vars)
- `ValidationTests` — input validation rules (DNS names, replica range, memory/CPU formats)

## API reference

Swagger UI: `http://localhost:8080/swagger` (development mode only)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/applications` | Deploy a new application |
| GET | `/api/applications` | List all applications |
| GET | `/api/applications/{name}` | Get details + live status |
| PUT | `/api/applications/{name}` | Update spec and roll out |
| DELETE | `/api/applications/{name}` | Delete application and k8s resources |
| POST | `/api/applications/{name}/scale` | Scale replicas |
| POST | `/api/applications/{name}/rollback` | Roll back to previous revision |
| GET | `/api/applications/{name}/revisions` | List revision history |
| GET | `/api/applications/{name}/logs` | Get pod logs |
| GET | `/api/applications/{name}/events` | Get k8s events |
| GET | `/health` | Liveness probe |
| GET | `/ready` | Readiness probe |
| GET | `/metrics` | Prometheus metrics |

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `Database:Provider` | `sqlite` | `sqlite` or `postgres` |
| `ConnectionStrings:Postgres` | — | Postgres connection string (set via env `ConnectionStrings__Postgres`) |

## What's next

See [REPORT.md](REPORT.md) for a full status report and prioritised next steps.

## Tech stack

- .NET 9 / ASP.NET Core
- Entity Framework Core 9 (SQLite dev / PostgreSQL prod)
- KubernetesClient 16
- Serilog + Prometheus
- xUnit + Moq

## License

MIT
