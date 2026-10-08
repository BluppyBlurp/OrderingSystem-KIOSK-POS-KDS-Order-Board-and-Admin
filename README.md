# Customer order board

**Not started.** Frontend only; the API lives on the `main` branch (see `docs.md` there).

- **Job:** Public screen: Preparing / Now Serving order numbers, readable across the room.
- **Auth:** Board device token (or any staff sign-in)
- **API:** `/api/display/board`, SignalR group `board`
- **Deploy:** Cloudflare Pages (production branch `frontend/board`), with `VITE_API_URL` pointing to the Render API.

When this app is started it will follow the `frontend/kiosk` layout: a pnpm workspace with `apps/board` and
`packages/api-client` (typed client generated from the API's OpenAPI document).
