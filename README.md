# Food Ordering Kiosk

Self-order kiosk, cashier POS, kitchen display, order board and admin, backed by one ASP.NET Core API.
Design and decisions: [`docs.md`](docs.md). Progress: [`checklist.md`](checklist.md).

## Branches

| Branch | Contains |
|---|---|
| `main` | Backend API, docs, dev scripts |
| `frontend/kiosk` | `main` + the customer kiosk app (`frontend/apps/kiosk`) |
| `frontend/pos` | Cashier POS *(not started)* |
| `frontend/kds` | Kitchen display *(not started)* |
| `frontend/board` | Customer order board *(not started)* |
| `frontend/admin` | Admin back office *(not started)* |

Backend changes are committed to `main`. Each frontend branch then merges `main` (`git merge main`) to pick them up.

## Run the backend (Windows, no Docker)

```powershell
# 1. Database: portable PostgreSQL on port 5433 (see scripts/dev-db.ps1)
./scripts/dev-db.ps1 start

# 2. API on http://localhost:5202. In Development it migrates, seeds the demo menu and a dev kiosk token.
dotnet run --project backend/src/Kiosk.Api
```

Online payments use a stub until PayMongo keys are set. On the kiosk, **Simulate payment (dev)** completes the payment.

## Run the kiosk

```powershell
git checkout frontend/kiosk
cd frontend
pnpm install
pnpm dev:kiosk                     # http://localhost:5173
```

## Tests

```powershell
cd backend;  dotnet test           # unit + integration (needs the dev database running)
cd frontend; pnpm test; pnpm typecheck   # on a frontend branch
```
