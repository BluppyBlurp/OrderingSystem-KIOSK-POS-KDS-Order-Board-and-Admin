# Kitchen display (KDS)

**Not started.** Frontend only; the API lives on the `main` branch (see `docs.md` there).

- **Job:** Paid orders appear live; tap Start → Ready → Handed over. Tickets show items, options, dine in / take out and table number.
- **Auth:** Clerk sign-in (`kitchen`, `manager`, `admin`)
- **API:** `/api/kds/*`, SignalR group `kitchen`
- **Deploy:** Cloudflare Pages (production branch `frontend/kds`), with `VITE_API_URL` pointing to the Render API.

When this app is started it will follow the `frontend/kiosk` layout: a pnpm workspace with `apps/kds` and
`packages/api-client` (typed client generated from the API's OpenAPI document).
