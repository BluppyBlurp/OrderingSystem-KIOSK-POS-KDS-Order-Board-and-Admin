# Cashier POS

**Not started.** Frontend only; the API lives on the `main` branch (see `docs.md` there).

- **Job:** Scan the customer's slip QR (or type the order number), take cash, confirm payment, print the receipt, void unpaid orders.
- **Auth:** Clerk sign-in (`cashier`, `manager`, `admin`)
- **API:** `/api/pos/*`, SignalR group `pos`
- **Deploy:** Cloudflare Pages (production branch `frontend/pos`), with `VITE_API_URL` pointing to the Render API.

When this app is started it will follow the `frontend/kiosk` layout: a pnpm workspace with `apps/pos` and
`packages/api-client` (typed client generated from the API's OpenAPI document).
