# Food Ordering Kiosk

Self-order kiosk, cashier POS, kitchen display, order board and admin, backed by one ASP.NET Core API.
Design and decisions: [`docs.md`](docs.md). Progress: [`checklist.md`](checklist.md).

## Run it locally (Windows, no Docker)

```powershell
# 1. Database: portable PostgreSQL on port 5433 (see scripts/dev-db.ps1)
./scripts/dev-db.ps1 start

# 2. API on http://localhost:5202. In Development it migrates, seeds the demo menu and a dev kiosk token.
dotnet run --project backend/src/Kiosk.Api

# 3. Kiosk on http://localhost:5173
cd frontend
pnpm install
pnpm dev:kiosk
```

Online payments use a stub until PayMongo keys are set. On the "Scan to pay" screen, press
**Simulate payment (dev)** to complete the payment.

## Tests

```powershell
cd backend;  dotnet test          # unit + integration (needs the dev database running)
cd frontend; pnpm test; pnpm typecheck
```

## After changing the API

```powershell
cd frontend; pnpm gen:api          # with the API running: refreshes openapi.json and the typed client
```
