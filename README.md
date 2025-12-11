# n8n-webhook-proxy

.NET 8 Minimal API acting as a webhook proxy with automatic switch to durable queue mode (Azure Storage Queue) and a worker that drains the queue.

## Run locally
```bash
dotnet run --project src/WebhookProxy/WebhookProxy.csproj
```

Key configuration values (via `appsettings.*` or environment variables):
- `Forwarding__BaseUrl` – n8n base address (e.g., `https://automation.domain`)
- `Forwarding__HealthUrl` – n8n health endpoint
- `Forwarding__PathPrefix` – optional path prefix before `{endpoint}` when forwarding (default `webhook`; set empty to forward raw endpoint path)
- `Queue__ConnectionString` – Azure Storage / Azurite connection string
- `Queue__QueueName` – queue name (default `webhooks`)
- `Validation__Mode` – `permissive` or `strict`
- `Worker__PollIntervalSeconds` / `Worker__BatchSize` – queue worker parameters

## Endpoints
- `POST /webhook/{endpoint}` – receive webhook; validate (JSON schema), forward to n8n in NORMAL MODE, enqueue in QUEUE MODE.
- `PUT /validations/{endpoint}` – save schema into `validations` folder.
- `GET /status` – current mode, queue length, last error, health-check result.
- `GET /health` – simple liveness for reverse proxy/K8s.

## Validations
JSON schema stored in `src/WebhookProxy/validations/{endpoint}.json` (fallback to `default.json`). In `strict` mode missing file returns 422, in `permissive` payload passes through.

## Modes
- **NORMAL MODE** – forward to n8n (10s timeout by default). Forwarding failures switch to **QUEUE MODE**.
- **QUEUE MODE** – each webhook goes to the queue. Worker polls every 30s (default), drains batch, and after stable health + empty queue returns to NORMAL MODE.

## Tests (Azure DevOps friendly)
Run:
```bash
dotnet test
```
`coverlet.collector` is included, so in Azure DevOps a `DotNetCoreCLI@2` task with `command: test` is enough.

## Docker
Build and push to Docker Hub:
```bash
docker build -t <user>/n8n-webhook-proxy:latest .
docker push <user>/n8n-webhook-proxy:latest
```
Container listens on `8080` (`ASPNETCORE_URLS=http://+:8080`).
