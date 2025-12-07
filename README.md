# n8n-webhook-proxy

.NET 8 Minimal API pełniące rolę proxy dla webhooków z automatycznym przełączaniem do trybu durable queue (Azure Storage Queue) i workerem odwadniającym kolejkę.

## Jak uruchomić lokalnie
```bash
dotnet run --project src/WebhookProxy/WebhookProxy.csproj
```

Kluczowe zmienne konfiguracji (możesz ustawić przez `appsettings.*` albo zmienne środowiskowe):
- `Forwarding__BaseUrl` – adres bazowy n8n (np. `https://automation.domain`)
- `Forwarding__HealthUrl` – endpoint health n8n
- `Queue__ConnectionString` – connection string do Azure Storage / Azurite
- `Queue__QueueName` – nazwa kolejki (domyślnie `webhooks`)
- `Validation__Mode` – `permissive` lub `strict`
- `Worker__PollIntervalSeconds` / `Worker__BatchSize` – parametry workera kolejki

## Endpoints
- `POST /webhook/{endpoint}` – odbiór webhooka; walidacja (JSON schema), forward do n8n w NORMAL MODE, enqueue w QUEUE MODE.
- `PUT /validations/{endpoint}` – zapis schema do katalogu `validations`.
- `GET /status` – tryb pracy, długość kolejki, ostatni błąd, wynik health-checku.
- `GET /health` – prosty liveness dla reverse proxy/K8s.

## Walidacje
Schema JSON zapisywane w `src/WebhookProxy/validations/{endpoint}.json` (fallback na `default.json`). W trybie `strict` brak pliku zwraca 422, w `permissive` payload przechodzi dalej.

## Tryby pracy
- **NORMAL MODE** – forward do n8n (timeout 10s domyślnie). Błędy forwardowania przełączają do **QUEUE MODE**.
- **QUEUE MODE** – każdy webhook trafia do kolejki. Worker co 30s (domyślnie) pobiera batch, po stabilnym health-checku i opróżnieniu kolejki wraca do NORMAL MODE.

## Testy (Azure DevOps friendly)
Uruchom:
```bash
dotnet test
```
Domyślny `coverlet.collector` jest w projekcie testowym, więc w Azure DevOps wystarczy task `DotNetCoreCLI@2` z `command: test`.

## Docker
Budowa i publikacja do Docker Hub:
```bash
docker build -t <user>/n8n-webhook-proxy:latest .
docker push <user>/n8n-webhook-proxy:latest
```
Kontener nasłuchuje na `8080` (`ASPNETCORE_URLS=http://+:8080`).
