# Kiosk (customer self-order)

Frontend only. The API lives on the `main` branch.
Design: `docs.md` on `main` (§5 order types, §7 payments, §12 kiosk UX).

Flow: Touch to start → Dine in / Take out → (dine in) counter pickup or table number → menu →
one question per screen → cart → Pay at counter (printed slip) or Pay here (QR Ph / card) → printed receipt.

## Run locally

Start the backend from the `main` checkout first (`./scripts/dev-db.ps1 start`, then
`dotnet run --project backend/src/Kiosk.Api`). Then, in this branch:

```powershell
pnpm install
pnpm dev:kiosk        # http://localhost:5173, uses apps/kiosk/.env.development
```

In development the kiosk signs in with the API's seeded dev token, and **Simulate payment (dev)**
completes stubbed QR Ph / card payments.

## Checks

```powershell
pnpm test; pnpm typecheck; pnpm build
```

## API client

`packages/api-client/openapi.json` is a snapshot of the API's OpenAPI document. After the API changes on `main`,
run `pnpm gen:api` with the API running to refresh it and the generated types, then commit both.

## Deploy: Cloudflare Pages

| Setting | Value |
|---|---|
| Production branch | `frontend/kiosk` |
| Build command | `pnpm install --frozen-lockfile && pnpm --filter @kiosk/kiosk build` |
| Build output directory | `apps/kiosk/dist` |
| Environment variables | `VITE_API_URL` = the Render API URL (e.g. `https://<service>.onrender.com`), `NODE_VERSION` = `22` |

The API must list the Pages domain in `Cors:Origins`. On first launch each kiosk shows a setup screen;
enter the device token from Admin → Devices.

## Printing

Run Chrome as `chrome --kiosk --kiosk-printing <url>` so the slip and receipt print silently
to the default printer (80 mm layout).
