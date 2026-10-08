# Admin back office

Frontend only. The API lives on the `main` branch (see `docs.md` there).

- **Menu:** categories (add, rename, hide, reorder, delete) and products (create, edit, price, availability, stock,
  reorder, the questions the kiosk asks and their order, photos and videos). Every save updates kiosks live.
- **Options:** option groups (the kiosk's questions) and their options with price changes.
- **Devices:** register kiosks and order boards (the token is shown once), revoke them.
- **Staff:** invite someone by email with their role, approve or reject people who signed up themselves, change
  roles, remove access. Admins manage everyone; managers manage assistant managers, cashiers and kitchen staff. Needs
  `Clerk__SecretKey` on the API.
- **Roles:** managers and admins get every tab; assistant managers get a read-only menu (they can still change
  availability and stock) and Reports.
- **Reports:** sales by date range (totals, VAT, by payment method, by day, top products) and payments that need a
  manual refund.
- **Uploads** go straight from the browser to Cloudflare R2 with a presigned URL, so the bucket's CORS policy must
  allow `PUT` with a `Content-Type` header from this app's origin. Until the API's `Storage__R2__*` settings are
  filled in, uploads say storage isn't configured; media can still be added by https URL.
- **Auth:** Clerk sign-in (`admin`, `manager`, `assistant_manager`); accounts without a role see "Waiting for approval"
- **API:** `/api/admin/*`

## Run locally

Start the backend from the `main` checkout first (`./scripts/dev-db.ps1 start`, then
`dotnet run --project backend/src/Kiosk.Api`). Then, in this branch:

```powershell
pnpm install
pnpm dev:admin          # http://localhost:5177, uses apps/admin/.env.development
```

In development the sign-in screen shows **Dev sign-in** buttons that use the API's Development-only
`devstaff_<role>` tokens. For real Clerk sign-in, put `VITE_CLERK_PUBLISHABLE_KEY` in `apps/admin/.env.local`.

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
| Branch | `frontend/admin` |
| Build command | `npx --yes pnpm@10.26.1 install --frozen-lockfile && npx --yes pnpm@10.26.1 --filter @kiosk/admin build` |
| Deploy command (Worker) | `npx wrangler deploy` (uploads `apps/admin/dist` as the Worker `kiosk-admin`: Cloudflare rejects the name `admin`) |
| Output directory (Pages) | `apps/admin/dist` |
| Build variables | `VITE_API_URL` = the Render API URL, `VITE_CLERK_PUBLISHABLE_KEY`, `NODE_VERSION` = `22` |

`VITE_*` values must be **build** variables: Vite bakes them into the files at build time. Add the deployed URL to the
API's `Cors__Origins__*` on Render, and to `Clerk__AuthorizedParties__*` if you use it.
