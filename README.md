# Kitchen display (KDS)

Frontend only. The API lives on the `main` branch (see `docs.md` there).

- **Job:** paid orders appear live in **New**; tap Start → Ready → Handed over. Tickets show items, options, notes,
  dine in / take out and table number, and an age timer that turns amber at 5 minutes and red at 10.
- **Alerts:** a new ticket flashes and chimes. Browsers only allow sound after a tap, so tap **Enable sound** once
  per session (or run Chrome with `--autoplay-policy=no-user-gesture-required`).
- **Live:** SignalR group `kitchen`; refetches everything after a reconnect, and polls every 15 s as a safety net.
- **Auth:** Clerk sign-in (`kitchen`, `manager`, `admin`)
- **API:** `/api/kds/*`

## Run locally

Start the backend from the `main` checkout first (`./scripts/dev-db.ps1 start`, then
`dotnet run --project backend/src/Kiosk.Api`). Then, in this branch:

```powershell
pnpm install
pnpm dev:kds          # http://localhost:5175, uses apps/kds/.env.development
```

In development the sign-in screen shows **Dev sign-in** buttons that use the API's Development-only
`devstaff_<role>` tokens. For real Clerk sign-in, put `VITE_CLERK_PUBLISHABLE_KEY` in `apps/kds/.env.local`.

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
| Branch | `frontend/kds` |
| Build command | `npx --yes pnpm@10.26.1 install --frozen-lockfile && npx --yes pnpm@10.26.1 --filter @kiosk/kds build` |
| Deploy command (Worker) | `npx wrangler deploy` (uploads `apps/kds/dist`, as set in `wrangler.jsonc`) |
| Output directory (Pages) | `apps/kds/dist` |
| Build variables | `VITE_API_URL` = the Render API URL, `VITE_CLERK_PUBLISHABLE_KEY`, `NODE_VERSION` = `22` |

`VITE_*` values must be **build** variables: Vite bakes them into the files at build time. Add the deployed URL to the
API's `Cors__Origins__*` on Render, and to `Clerk__AuthorizedParties__*` if you use it.
