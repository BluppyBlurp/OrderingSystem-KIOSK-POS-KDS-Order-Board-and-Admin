# Kiosk (customer self-order)

Frontend only. The API lives on the `main` branch.
Design: `docs.md` on `main` (§5 order types, §7 payments, §12 kiosk UX).

Flow: Touch to start → Dine in / Take out → (dine in) counter pickup or table number → menu →
one question per screen → cart → "Anything else?" (once) → Pay at counter (printed slip) or Pay here (QR Ph / card) → printed receipt.

Live: SignalR `MenuChanged` updates prices and SOLD OUT mid-order; the payment screen watches its order and also polls.
Offline: production builds register `public/sw.js`, which serves the cached menu, images and app if the network drops
(orders still need the network). Bump `VERSION` in `sw.js` to drop old caches.

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

## Deploy: Cloudflare

Both Cloudflare project types work. Pick whichever the dashboard created.

**Worker** (the default under Workers & Pages → Create; it has a *Deploy command* and no output directory):

| Setting | Value |
|---|---|
| Branch | `frontend/kiosk` |
| Build command | `npx --yes pnpm@10.26.1 install --frozen-lockfile && npx --yes pnpm@10.26.1 --filter @kiosk/kiosk build` |
| Deploy command | `npx wrangler deploy` (uploads `apps/kiosk/dist`, as set in `wrangler.jsonc`) |
| Build variables (Settings → Build → Variables and secrets) | `VITE_API_URL` = the Render API URL, `NODE_VERSION` = `22` |

`VITE_API_URL` must be a **build** variable: Vite bakes it into the files at build time, so a runtime Worker variable has no effect.

**Pages project** (Create → Pages → Import an existing Git repository):

| Setting | Value |
|---|---|
| Production branch | `frontend/kiosk` |
| Build command | same as above |
| Build output directory | `apps/kiosk/dist` |
| Environment variables | `VITE_API_URL` = the Render API URL, `NODE_VERSION` = `22` |

Either way: the build fetches pnpm itself because Cloudflare's build image may not have it ("No preset version installed for command pnpm"). `VITE_API_URL` must have no trailing slash. The API must list the kiosk's URL in `Cors__Origins__0` (Render). On first launch each kiosk shows a setup screen; enter its device token.

## Printing

Run Chrome as `chrome --kiosk --kiosk-printing <url>` so the slip and receipt print silently
to the default printer (80 mm layout).
