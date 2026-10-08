# Cashier POS

Frontend only. The API lives on the `main` branch (see `docs.md` there).

- **Job:** Scan the customer's slip QR (or type the order number), take cash, confirm payment, print the receipt, void unpaid orders.
- **Auth:** Clerk sign-in (`cashier`, `manager`, `admin`)
- **API:** `/api/pos/*`, SignalR group `pos`

## Run locally

Start the backend from the `main` checkout first (`./scripts/dev-db.ps1 start`, then
`dotnet run --project backend/src/Kiosk.Api`). Then, in this branch:

```powershell
pnpm install
pnpm dev:pos          # http://localhost:5174, uses apps/pos/.env.development
```

In development the sign-in screen shows **Dev sign-in** buttons that use the API's Development-only
`devstaff_<role>` tokens. For real Clerk sign-in, put `VITE_CLERK_PUBLISHABLE_KEY` in `apps/pos/.env.local`.

## Checks

```powershell
pnpm test; pnpm typecheck; pnpm build
```

## API client

`packages/api-client/openapi.json` is a snapshot of the API's OpenAPI document. After the API changes on `main`,
run `pnpm gen:api` with the API running to refresh it and the generated types, then commit both.

## Deploy: Cloudflare

Same setup as the kiosk (see the `frontend/kiosk` README), with these values:

| Setting | Value |
|---|---|
| Branch | `frontend/pos` |
| Build command | `npx --yes pnpm@10.26.1 install --frozen-lockfile && npx --yes pnpm@10.26.1 --filter @kiosk/pos build` |
| Deploy command (Worker) | `npx wrangler deploy` (uploads `apps/pos/dist`, as set in `wrangler.jsonc`) |
| Output directory (Pages) | `apps/pos/dist` |
| Build variables | `VITE_API_URL` = the Render API URL, `VITE_CLERK_PUBLISHABLE_KEY`, `NODE_VERSION` = `22` |

`VITE_*` values must be **build** variables: Vite bakes them into the files at build time.
