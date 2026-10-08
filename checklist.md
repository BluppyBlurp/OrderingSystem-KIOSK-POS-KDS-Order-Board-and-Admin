# Fast-Food Kiosk Ordering System — Build Checklist

**Last updated:** 2026-10-08 (backend MVP built: 90 tests passing)
**Runtime:** .NET 10 LTS (API) · React + Vite (frontends)
**Infrastructure:** Neon (Postgres) · Cloudflare R2 (media) · Clerk (staff auth) · Cloudflare Pages (frontends) · **Render** (API)
**Branches:** `main` = backend + docs · `frontend/kiosk`, `frontend/pos`, `frontend/kds`, `frontend/board`, `frontend/admin` = frontend only, one per app
**Rule:** New requirements land here first (§Backlog), then `docs.md` is updated and its change log appended.
**Legend:** `[ ]` todo · `[~]` in progress · `[x]` done · 🔴 blocker · ⭐ MVP-critical

---

## Milestone 0 — Foundations
*Goal: `docker compose up` gives a running API, a database, and a blank React app that can call it.*

- [x] ⭐ Git repo on GitHub: `main` = backend + docs, one `frontend/*` branch per app
- [x] ⭐ .NET 10 solution: `Kiosk.Api`, `Kiosk.Application`, `Kiosk.Domain`, `Kiosk.Infrastructure`
- [x] Enforce dependency direction (Domain depends on nothing)
- [x] ⭐ Local Postgres without Docker: `scripts/dev-db.ps1` runs portable PostgreSQL 17 on port **5433** (`docker-compose.yml` kept for machines that have Docker)
- [x] ⭐ EF Core wired up, first migration runs on startup in dev
- [x] ⭐ Neon project created; pooled connection string set on Render (2026-10-08)
- [ ] Verify Npgsql prepared statements work through the pooler (switch to session pooler if not)
- [ ] Cron ping during trading hours to avoid cold starts
- [ ] Nightly `pg_dump` to R2, 7-day retention
- [ ] ⭐ R2 bucket created; credentials in env, never in any frontend
- [x] Serilog structured logging
- [x] Global exception handler → RFC 7807 `ProblemDetails`
- [x] OpenAPI document exposed in dev (`Microsoft.AspNetCore.OpenApi`)
- [x] CORS allowlist from config
- [x] Rate limiting middleware registered
- [~] ⭐ pnpm workspace: `apps/kiosk` + `packages/api-client` done; `pos`, `kds`, `board`, `admin`, `realtime`, `ui` to come
- [x] ⭐ Typed TS client generated from OpenAPI (`openapi-typescript` + `openapi-fetch`); `pnpm gen:api` refreshes it from the running API
- [~] Tailwind 4 in the kiosk; shared `packages/ui` not extracted yet
- [x] Smoke test: kiosk app renders the menu from the API (verified in a browser)

---

## Milestone 1 — Identity & Access
*Goal: the right person reaches the right app, and nothing else.*

- [~] ⭐ Clerk application created; roles `admin`, `manager`, `cashier`, `kitchen` still to be set on users (`publicMetadata.role`)
- [ ] ⭐ Role exposed as a custom session claim: `"role": "{{user.public_metadata.role}}"` *(set in the Clerk dashboard)*
- [x] ⭐ ASP.NET JWT Bearer validating Clerk JWKS *(set `Clerk:Authority` to the Frontend API URL)*
- [x] ⭐ Authorization policies per route group: `Kiosk`, `Board`, `Pos`, `Kds`, `Admin`
- [x] ⭐ Default-deny fallback policy (no accidentally anonymous endpoint)
- [x] ⭐ Device token scheme: `Device` entity, hashed token, `kind` (Kiosk/Board), `isActive`
- [x] ⭐ Device auth handler (`dev_…` bearer tokens) + `Kiosk` / `Board` policies
- [x] Admin endpoints to register, list, and revoke devices
- [ ] Clerk sign-in wired into POS, KDS, Admin apps
- [x] Route-group tests: kiosk/board tokens, each staff role, revoked tokens, cross-kiosk reads; plus a reflection test that every controller action declares a policy

---

## Milestone 2 — Menu & Admin Back Office
*Goal: a manager can build the entire menu without a developer.*

- [x] ⭐ Entities + migrations: `Category`, `Product`, `ProductMedia`, `ModifierGroup`, `Modifier`, `ProductModifierGroup`
- [x] ⭐ Admin CRUD: categories (name, sortOrder, isActive)
- [x] ⭐ Admin CRUD: products (name, description, basePrice, stock, isAvailable, sortOrder). Product edits never overwrite stock; stock changes go through `PATCH /stock`
- [x] ⭐ Admin CRUD: modifier groups (minSelect, maxSelect, isRequired) + modifiers with price deltas
- [ ] ⭐ R2 presigned upload for **images**
- [ ] R2 presigned upload for **videos** + size/duration limits
- [ ] ⭐ Server-side variants at upload time (ImageSharp): thumbnail + WebP, stored alongside original
- [ ] Content-hashed filenames + long `Cache-Control` max-age on R2 objects
- [x] Quick toggles: availability switch, stock adjust (`PATCH`)
- [x] `AuditLog` written on every admin mutation (actor, entity, before/after JSON; device token hashes excluded)
- [ ] ⭐ Admin UI: product list, create/edit form, media manager, drag-to-reorder
- [ ] Admin UI: category manager, modifier group manager
- [x] Validation: cannot delete a category that still has products

---

## Milestone 3 — Kiosk Ordering
*Goal: a customer can build a correct order and reach a payment choice.*

- [x] ⭐ `GET /api/kiosk/menu` — categories, products, modifiers, availability
- [~] ⭐ Attract screen *(black-and-white shapes for now; promo media loop later)*
- [x] ⭐ **Dine in** vs **Take out** screen (2026-10-08); take out always means counter pickup
- [x] ⭐ Order type screen (dine in only): **Counter pickup** vs **Serve to table**
- [x] ⭐ Table-number keypad, shown only for serve-to-table
- [x] ⭐ Backend validation: serve-to-table requires a table number in range (1–60)
- [x] Soft warning when a table number is already on an active order
- [x] ⭐ Menu browse: category nav, product grid, product detail
- [x] ⭐ Modifier selection honouring min/max/required rules (client mirrors the server; server enforces)
- [x] ⭐ Cart in Zustand: add, edit quantity, remove, running total
- [ ] Upsell prompt before checkout (add a drink / upsize)
- [x] ⭐ Guided customization: one question per modifier group (2026-10-08). Meals ask drink, upsize drink, upsize fries, fries flavor, add-ons; à la carte asks add a drink, add a side, add-ons
- [x] ⭐ Demo menu seeded in Development: Rice Meals, Sandwiches, Pasta, Sides, Drinks, Desserts (2026-10-08)
- [x] ⭐ `POST /api/kiosk/orders` — **server recomputes all prices**, client prices ignored
- [x] ⭐ Stock **reserved** at order creation: availability check + atomic conditional decrement in one transaction (last item → exactly one winner)
- [x] ⭐ Daily-resetting order number generator (`A-101`), concurrency-safe
- [x] ⭐ `OrderItem` snapshots name + unit price at time of order
- [x] ⭐ Idle timeout: 60s warning → 15s → cart cleared
- [x] Touch targets ≥ 48px; sold-out items greyed in place with a SOLD OUT band
- [ ] Service worker caches menu + images

---

## Milestone 4 — Payments (sandbox)
*Goal: all three payment paths reach `Paid` reliably.*

### Shared
- [x] ⭐ `Payment` entity + `OrderEvent` status history
- [x] ⭐ Payment method screen on kiosk
- [x] ⭐ Stock reserved at creation; released on `Expired` / `Cancelled` (never on `Paid`)

### Cash — pay at counter
- [x] ⭐ Order created as `Created` with `expiresAt`; `POST /pay {method: cash}` moves it to `AwaitingPayment`
- [x] ⭐ Slip payload: order number, total, **signed short-lived QR token** (not the raw ID)
- [x] ⭐ Kiosk renders the slip on screen (QR + large order number)
- [x] ⭐ Background job expires every pre-Paid order (`Created`, `AwaitingPayment`, `PaymentPending`, `Failed`) past `expiresAt`, releases stock
- [ ] PDF slip generation (QuestPDF) for a future printer

### E-wallet (GCash / Maya) — PayMongo sandbox
- [~] ⭐ `POST /orders/{id}/pay` creates a PayMongo **Checkout Session**, returns URL *(built against PayMongo's documented API; not yet run against the real sandbox — needs `sk_test_…` key)*
- [x] ⭐ Kiosk shows a QR of the checkout page; the customer pays on their phone (QR Ph or card)
- [x] ⭐ `POST /api/webhooks/paymongo` with **signature verification**
- [x] ⭐ Webhook idempotency (same event ID processed once)
- [x] ⭐ Webhook is the **only** thing that sets `Paid`; client redirect never is
- [x] ⭐ Kiosk polls `GET /kiosk/orders/{id}` as a webhook-delay fallback *(endpoint done; UI pending)*
- [~] Customer backs out → `cancel-checkout` → `Failed`; kiosk offers **Pay at counter instead** *(done)*; a Retry button is not built yet

### QR Ph — PayMongo sandbox (2026-10-08)
- [x] ⭐ `PaymentMethod.QrPh` → checkout session with `qrph` *(verified with the stub; real sandbox pending)*
- [ ] Direct QR Ph image on the kiosk (Payment Intent API) instead of a QR of the checkout page

### Card — PayMongo sandbox
- [~] ⭐ Card uses the same Checkout Session; PayMongo's hosted page handles 3DS *(replaces a hand-built payment-intent flow)*
- [x] ⭐ Same webhook path, same idempotency guarantees
- [ ] Declined card → clear kiosk message + retry

- [x] ⭐ Confirmation screen: order number, type, table number if any, ETA
- [x] ⭐ Kiosk prints the cash slip / paid receipt via browser print (80 mm layout; Chrome `--kiosk-printing` prints silently) (2026-10-08)
- [x] Dev-only "simulate payment" endpoint so the kiosk flow works before PayMongo is set up (2026-10-08)

---

## Milestone 5 — Cashier POS
*Goal: the cashier never retypes an order.*

- [~] ⭐ Clerk sign-in, `Pos` policy enforced *(policy done; sign-in UI pending)*
- [~] ⭐ Pending cash orders list, live via SignalR `pos` group *(backend done; UI pending)*
- [x] ⭐ `GET /api/pos/orders/lookup?code=` accepts QR token **or** typed order number
- [x] ⭐ Order detail: items, modifiers, total, order type, table number
- [x] ⭐ `POST /confirm-cash` with `{amountTendered}` → server computes change
- [ ] ⭐ Change-due displayed prominently
- [x] ⭐ On confirm: `Paid` → SignalR to KDS and Board
- [x] Cancel/void an unpaid order with a reason
- [ ] ⭐ Receipt PDF (QuestPDF) + reprint
- [ ] Shift summary: cash collected, order count
- [ ] ⭐ POS app on `frontend/pos`: scan-or-type box (USB QR scanners type like a keyboard), live pending list, order panel, tendered keypad with quick amounts, big change-due, cancel with reason, printed receipt (2026-10-08)
- [x] Development-only staff sign-in (`devstaff_<role>` tokens) so POS/KDS/Admin can be tested before Clerk roles exist; rejected outside Development (2026-10-08)

---

## Milestone 6 — Kitchen Display (KDS)
*Goal: the kitchen sees paid orders the moment they are paid.*

- [~] ⭐ Clerk sign-in, `Kds` policy enforced *(policy done; sign-in UI pending)*
- [x] ⭐ `GET /api/kds/orders` — `Paid` + `Preparing` + `Ready` tickets (Ready needed for Handed Over)
- [~] ⭐ SignalR `kitchen` group; new ticket appears without refresh *(push done; UI pending)*
- [x] ⭐ Ticket shows: order number, items + modifiers, order type, **table number**
- [x] ⭐ Actions: Start (→ `Preparing`), Ready (→ `Ready`), Handed Over (→ `Completed`)
- [x] ⭐ Illegal transitions rejected by the domain layer
- [ ] Audio + visual alert on new order
- [ ] Ticket age timer, colour-coded past a threshold
- [ ] Reconnect handling: refetch full state on SignalR reconnect

---

## Milestone 7 — Customer Order Board
*Goal: the McDo-style Preparing / Now Serving screen.*

- [x] ⭐ `GET /api/display/board` → `{preparing[], ready[]}`
- [x] ⭐ Board auth, read-only: board device token **or** any staff role (matches docs §11)
- [ ] ⭐ Two-column layout, very large numbers, readable across the room
- [~] ⭐ Live updates via SignalR `board` group *(push done, numbers only, no items or totals; UI pending)*
- [x] Ready orders show the table number for serve-to-table
- [ ] Completed orders auto-clear after N seconds
- [ ] Chime when an order moves to Ready
- [ ] Auto-reconnect + "reconnecting" indicator

---

## Milestone 8 — Hardening
- [x] ⭐ SignalR groups: `kitchen`, `pos`, `board`, `kiosks`, `kiosk-{orderId}` (assigned from identity on connect; a kiosk joins only its own orders)
- [~] ⭐ `MenuChanged` push *(server push done; kiosk currently refetches the menu at the start of every order instead of listening)*
- [ ] ⭐ Reconnect + full-state refetch on every realtime client
- [x] Unit tests: order state machine, price calculation, stock rules
- [x] Unit tests: change calculation, table-number validation
- [~] ⭐ Integration test: create → pay → KDS → Ready → Completed *(cash and e-wallet done; card shares the e-wallet path)*
- [x] Integration test: concurrent orders racing for the last stock item
- [x] Integration test: duplicate webhook delivery is a no-op
- [ ] ⭐ Security pass: every route group's policy verified, CORS, rate limits, secrets out of the frontend
- [ ] Load sanity check: ~50 concurrent kiosk orders

---

## Milestone 9 — Release
- [ ] Sales report endpoint + Admin reports page
- [ ] Sentry on backend and all five frontends
- [~] ⭐ API `Dockerfile` for Render *(written; the Release publish it runs was verified in Production mode, but the image itself hasn't been built: no Docker on the dev PC; Render builds it on first deploy)* (Render has no native .NET runtime; it deploys .NET as a Docker web service) (2026-10-08)
- [x] Render: env vars (`ConnectionStrings__Default` = Neon pooled, `Clerk__Authority`, `Slip__SigningKey`, `PayMongo__*`, `Cors__Origins__*` = Pages domains); health check `/health` (2026-10-08)
- [x] `render.yaml` Blueprint (Singapore, next to Neon's ap-southeast-1; secrets prompted, never committed) (2026-10-08)
- [x] API honours Render's `PORT` and its proxy's `X-Forwarded-For` (real client IP for rate limits, last hop only so it can't be spoofed) (2026-10-08)
- [x] Migrations on deploy: `Database__MigrateOnStartup=true` while the API runs as a single instance (2026-10-08)
- [x] Bootstrap until the Admin app exists: `Bootstrap__SeedDemoMenu` + `Bootstrap__KioskToken` (one kiosk, token chosen by you; weak tokens refused at startup). Remove once Admin manages menu and devices (2026-10-08)
- [x] Startup fails fast on a missing or invalid `Slip__SigningKey`; a missing PayMongo key shows as "payment provider unavailable", not a crash (2026-10-08)
- [ ] Decide the Render plan: free instances sleep when idle and take about a minute to wake, which a kiosk can't wait for (2026-10-08)
- [~] Frontends on Cloudflare (one project per `frontend/*` branch), API on Render *(API + kiosk live 2026-10-08; kiosk runs as a Cloudflare Worker with static assets)*
- [ ] HTTPS, environment secrets, database backups verified restorable
- [x] Sample menu for demos (`Dev:SeedDemoData` locally, `Bootstrap__SeedDemoMenu` on Render)
- [ ] Runbook: how to re-register a kiosk, reprint a receipt, void an order
- [ ] Redis backplane **only if** scaling past one API instance
- [ ] Swap PayMongo sandbox keys for live keys

---

## Open Decisions
*Move to a milestone once settled; update `docs.md` §13 at the same time.*

- [x] ~~Database~~ — **settled: Neon** (2026-10-08)
- [x] ~~Media storage~~ — **settled: Cloudflare R2, images and video** (2026-10-08)
- [x] ~~Backend runtime~~ — **settled: .NET 10 LTS**; .NET 8 support ends 2026-11-10 (2026-10-08)
- [x] ~~Stock timing~~ — **settled: reserve at creation, release on expiry/cancel**; decrement-on-Paid could oversell the last item (2026-10-08)
- [ ] Printed slip vs on-screen QR only — *assumed: on-screen + PDF*
- [ ] Cash payment window — *assumed: 15 min*
- [ ] VAT / service charge — *assumed: 12% VAT-inclusive, no service charge*
- [ ] Combos & promos in MVP — *assumed: no, v2*
- [ ] Single vs multi-branch — *assumed: single, schema leaves room for `BranchId`*
- [ ] PayMongo vs Xendit — *assumed: PayMongo*
- [ ] Table number range — *assumed: 1–60*

---

## Backlog / Future Changes
*Append new items here with a date, then update `docs.md`.*

- [ ] Loyalty / membership numbers
- [ ] Combo and promo engine
- [ ] Multi-language kiosk (EN / FIL)
- [ ] ESC/POS thermal printer integration
- [ ] Multi-branch support with per-branch menus and reports
- [ ] Customer SMS/notification when ready
- [ ] Accessibility: wheelchair-height UI mode, larger-text toggle
- [ ] *(2026-10-08)* Admin view of `OrderEvent` rows with `RefundNeeded` (payments that landed on expired/cancelled orders or with a mismatched amount)
