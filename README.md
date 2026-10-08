# Admin back office

**Not started.** Frontend only; the API lives on the `main` branch (see `docs.md` there).

- **Job:** Menu, modifiers, prices, stock and availability, media, devices (kiosk/board tokens), reports.
- **Auth:** Clerk sign-in (`manager`, `admin`)
- **API:** `/api/admin/*`
- **Deploy:** Cloudflare Pages (production branch `frontend/admin`), with `VITE_API_URL` pointing to the Render API.

When this app is started it will follow the `frontend/kiosk` layout: a pnpm workspace with `apps/admin` and
`packages/api-client` (typed client generated from the API's OpenAPI document).
