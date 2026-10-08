# Customer order board

Frontend only. The API lives on the `main` branch (see `docs.md` there).

- **Job:** the public "Preparing / Now serving" screen. Very large numbers, the table number for serve-to-table
  orders. Numbers leave the board as soon as the kitchen taps Handed over.
- **Alerts:** a number moving to Now serving chimes and is highlighted for 15 s. Tap **Tap to enable chime** once
  (or run Chrome with `--kiosk --autoplay-policy=no-user-gesture-required`).
- **Live:** SignalR group `board` (numbers only, never items or totals); refetches after a reconnect, polls every 10 s,
  keeps retrying forever and shows "Reconnecting…" while it does.
- **Auth:** a board device token. In Admin → Devices, register an **Order board** and enter the token on this
  screen's setup page. Any staff token also works (docs §11).
- **API:** `GET /api/display/board`

## Run locally

Start the backend from the `main` checkout first (`./scripts/dev-db.ps1 start`, then
`dotnet run --project backend/src/Kiosk.Api`). Then, in this branch:

```powershell
pnpm install
pnpm dev:board          # http://localhost:5176, uses apps/board/.env.development
```

In development the board falls back to the API's Development-only `devstaff_kitchen` token, so it works without
registering a device.

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
| Branch | `frontend/board` |
| Build command | `npx --yes pnpm@10.26.1 install --frozen-lockfile && npx --yes pnpm@10.26.1 --filter @kiosk/board build` |
| Deploy command (Worker) | `npx wrangler deploy` (uploads `apps/board/dist`, as set in `wrangler.jsonc`) |
| Output directory (Pages) | `apps/board/dist` |
| Build variables | `VITE_API_URL` = the Render API URL, `NODE_VERSION` = `22` |

`VITE_*` values must be **build** variables: Vite bakes them into the files at build time. Add the deployed URL to the
API's `Cors__Origins__*` on Render.
