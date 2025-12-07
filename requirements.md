# Webhook Proxy + Durable Queue Mode + Worker + Dynamic Validation  
## Specyfikacja Techniczna (Codex-ready)

## 1. Cel systemu
System ma zapewnić niezawodne, gwarantowane dostarczenie webhooków do n8n (MacBook → Raspberry Pi → Raspberry Pi Cluster), nawet w przypadku:

- braku prądu,
- restartu lokalnego serwera,
- awarii tunelu Cloudflare,
- problemów sieciowych,
- chwilowego lub dłuższego downtime n8n.

Główny wymóg:
### **Żaden webhook nie może zostać utracony.**

## 2. Architektura (High-Level)

[External Services] 
     │
     ▼
┌───────────────────────────┐
│        API PROXY (VPS)    │
│  - Receive Webhooks       │
│  - Validate Payload       │
│  - Try Forward to n8n     │
│  - Fallback → Azure Queue │
└───────────────────────────┘
               │
               ▼
┌───────────────────────────┐
│     Azure Storage Queue   │
│  (Durable Message Buffer) │
└───────────────────────────┘
               │
               ▼
┌───────────────────────────┐
│   n8n Worker (Mac/RPi)    │
│  - Polling Queue          │
│  - Forward to n8n         │
│  - Delete / Retry         │
└───────────────────────────┘

## 3. Komponenty systemu

3.1 API Proxy (VPS)
	•	odbiera webhooki HTTP POST,
	•	weryfikuje payload wg dynamicznych reguł walidacji,
	•	próbuje forwardować do n8n,
	•	w razie błędu → zapisuje do Azure Queue,
	•	przełącza system w tryb QUEUE MODE.

3.2 Azure Storage Queue
	•	trwałe przechowywanie komunikatów,
	•	FIFO w ramach Storage Queue,
	•	visibility timeout → retry,
	•	brak utraty danych.

3.3 Worker (n8n lub osobny serwis)
	•	w trybie QUEUE MODE polluje kolejkę,
	•	forwarduje komunikaty do n8n,
	•	usuwa wiadomości po udanym przetworzeniu,
	•	wraca do NORMAL MODE po ustabilizowaniu.

3.4 Dynamiczne walidacje (Validation Engine)
	•	przechowywane w /validations/<endpoint>.json,
	•	generowane z n8n,
	•	JSON Schema / XML Schema lub custom rule set.

## 4. Tryby pracy: NORMAL MODE / QUEUE MODE

4.1 NORMAL MODE

System działa jako zwykły forwarder webhooków:

External → Proxy → n8n

	•	Walidacja payloadu przed forwardem
	•	Forward timeout: 10 sekund
	•	Health-check co X sekund (np. 60s)
	•	Jeśli 3× błąd → przejście do QUEUE MODE

Wejście do NORMAL MODE:
	•	zdrowy health-check przez X prób (np. 3)
	•	kolejka pusta

4.2 QUEUE MODE (awaryjny)

Forwardowanie jest wyłączone.
Każdy webhook wpada do kolejki Azure.

External → Proxy → Queue → Worker → n8n

	•	Worker polluje co 30 sekund
	•	Batch size: 10–32
	•	Retry w oparciu o visibility timeout
	•	Po opróżnieniu kolejki i stabilnym health-check → powrót do NORMAL MODE

## 5. Dynamiczna walidacja Webhooków

5.1 Struktura walidacji

W katalogu API Proxy:

/validations
   ├── orders.json
   ├── payments.json
   ├── users.json
   └── default.json

Każdy plik JSON/XML zawiera:
	•	JSON Schema (preferowane)
	•	XML Schema (opcjonalnie)
	•	custom rule definitions (opcjonalne)

5.2 Zasada dopasowania

POST /webhook/{endpoint} odpowiada plikowi:

/validations/{endpoint}.json

Permissive mode (domyślny)
	•	brak pliku → przepuszczamy payload
	•	nie zatrzymujemy webhooka

Strict mode
	•	brak pliku → błąd 422 Validation Not Found

Konfiguracja:

VALIDATION_MODE=permissive | strict

5.3 Walidacja payload

Krok 1 — sprawdzenie poprawności JSON/XML
	•	jeśli invalid → 400 Bad Request

Krok 2 — schema validation
	•	jeśli niezgodne → 422 Unprocessable Entity
	•	nie wysyłamy do n8n
	•	nie wrzucamy do kolejki

Krok 3 — walidacja OK → przechodzimy do flow:

try forward
    ↓ success → 200
    ↓ failure → enqueue → 202

5.4 Integracja z n8n (Validation Builder)

W n8n tworzony jest workflow:

[Build Schema] → [Export Schema]

Eksport odbywa się przez:

PUT /validations/{endpoint}
Content-Type: application/json

Proxy zapisuje schema do katalogu ./validations.

## 6. Logika Proxy – pełen flow

INPUT:

POST /webhook/{endpoint}

1. Load schema for {endpoint}
2. Validate payload
3. If invalid → return 422
4. Try forward → POST https://automation.domain/webhook/{endpoint}
5. If successful → return 200
6. If error → enqueue in Azure Queue
7. Switch to QUEUE MODE
8. Return 202 Accepted

## 7. Health-check

Proxy lub Worker wykonuje:

GET https://automation.domain/health

Oczekiwany response:

{ "status": "ok" }

3 błędy z rzędu → QUEUE MODE
3 sukcesy z rzędu → NORMAL MODE

## 8. Worker Logic

Worker uruchamia się co 30 sekund:

if mode == NORMAL:
    if health-check FAIL → mode = QUEUE
    exit

if mode == QUEUE:
    if health-check FAIL → exit (dont process)
    messages = dequeue batch (max 32)
    if empty:
        if health-check OK 3x → mode=NORMAL
        exit
    for m in messages:
        if forward(m) success:
            delete(m)
        else:
            keep(m) (visibility timeout)

## 9. Retry & DLQ

Azure Queue stosuje:
	•	visibility timeout (np. 30 s)
	•	niewysłane wiadomości wracają do kolejki
	•	po X retry (np. 5) → DLQ

Worker powinien wysyłać alerty dla DLQ.

## 10. API Kontrakty

10.1 Odbiór webhooka

POST /webhook/{endpoint}

Request:
	•	JSON lub XML
	•	max 2 MB
	•	headers mogą zawierać secret/token

Response:
	•	200 OK — forwarded
	•	202 Accepted — queued
	•	400 — invalid JSON/XML
	•	422 — validation failed
	•	500 — internal proxy error

⸻

10.2 Upload Schema

PUT /validations/{endpoint}

Body:

{
  "$schema": "...",
  "type": "object",
  "properties": {...}
}

Response:

200 OK
{
  "status": "validation_updated",
  "endpoint": "orders"
}

10.3 Get Status

GET /status

Response:

{
  "mode": "NORMAL | QUEUE",
  "queue_length": 123,
  "last_error": "...",
  "health": "ok | error"
}

## 11. Wymagania funkcjonalne

F1 — Proxy musi odbierać webhooki POST

F2 — Walidacja payload przed przetwarzaniem

F3 — Forward do n8n w NORMAL MODE

F4 — Kolejkowanie w QUEUE MODE

F5 — Automatyczne przełączanie trybów

F6 — Retry via visibility timeout

F7 — Worker draining

F8 — DLQ dla wielokrotnych błędów

F9 — Logging i monitoring

F10 — Dynamiczna aktualizacja walidacji przez API

## 12. Wymagania niefunkcjonalne

N1 — Idempotency

Powtórne dostarczenie nie psuje danych.

N2 — Durability

Azure Queue jako storage-of-record.

N3 — Observability

Logi + metryki + tryby pracy.

N4 — Uptime Proxy ≥ 99% (VPS)

N5 — Backup walidacji i konfiguracji

N6 — Cache walidacji in-memory dla performance.

## 13.  Diagram Sequence

External → Proxy → validate → n8n → OK
External → Proxy → validate → n8n → ERROR → Queue → Worker → n8n

## 14. Folder Structure (Proxy)

/app
   /validations
       orders.json
       payments.json
       default.json
   /logs
   /config
docker-compose.yml
.env

## 15. Przenoszenie środowisk

System wspiera:
	•	MacBook (Etap 1)
	•	Raspberry Pi 5 (Etap 2)
	•	Raspberry Pi Cluster (Etap 3)

Proxy na VPS jest niezależne.

## 16. Zalecana implementacja

Proxy:
	•	.NET 8 Minimal API
	•	JSON Schema Validator (AJV lub FluentValidation.JsonSchema)
	•	Azure Storage Queue SDK

Worker:
	•	n8n workflow (CRON + Azure Queue)
lub
	•	dedykowany .NET Worker Service