# Food Ordering Kiosk

Self-order kiosk, cashier POS, kitchen display, order board and admin, backed by one ASP.NET Core API.
Design and decisions: [`docs.md`](docs.md). Progress: [`checklist.md`](checklist.md).

## Branches

| Branch | Contains | Deploys to |
|---|---|---|
| `main` | Backend API, docs, dev scripts | Render |
| `frontend/kiosk` | Customer kiosk (frontend only) | Cloudflare Pages |
| `frontend/pos` | Cashier POS (frontend only) | Cloudflare Pages |
| `frontend/kds` | Kitchen display (frontend only) | Cloudflare Pages |
| `frontend/board` | Customer order board (frontend only) | Cloudflare Pages |
| `frontend/admin` | Admin back office (frontend only) | Cloudflare Pages |

Frontend branches contain no backend code. They reach the API through a typed client generated from
`packages/api-client/openapi.json`, a committed snapshot of the API contract. After an API change, refresh it on the
frontend branch with `pnpm gen:api` (with the API running).

## Run the backend (Windows, no Docker)

```powershell
# 1. Database: portable PostgreSQL on port 5433 (see scripts/dev-db.ps1)
./scripts/dev-db.ps1 start

# 2. API on http://localhost:5202. In Development it migrates, seeds the demo menu and a dev kiosk token.
dotnet run --project backend/src/Kiosk.Api

# Tests (needs the dev database running)
cd backend; dotnet test
```

## Run a frontend next to it

Check the frontend branch out in a second folder, so the backend and frontend run side by side:

```powershell
git worktree add ../FoodOrderingKiosk-kiosk frontend/kiosk   # once
cd ../FoodOrderingKiosk-kiosk
pnpm install
pnpm dev:kiosk                                               # http://localhost:5173
```

Locally, online payments use a stub. On the kiosk, **Simulate payment (dev)** completes the payment.

## Deploy

The API deploys to Render from `render.yaml` (Blueprint) and `backend/Dockerfile`; each frontend branch deploys to Cloudflare Pages. Step by step: `docs.md` §3.3.
