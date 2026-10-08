# Fast-Food Kiosk Ordering System — Technical Docs

**Status:** Backend MVP + kiosk app built (97 backend tests, 8 kiosk unit tests; kiosk flow verified in a browser). POS, KDS, board and admin apps not started.
**Last updated:** 2026-10-08
**Maintenance rule:** New or changed requirements go into `checklist.md` first, then this file is updated to match. Record every edit in §14 Change Log.

---

## 1. Problem Statement

Replicate a McDonald's-style self-order kiosk, software only. A customer orders at a touchscreen, pays by cash at the counter or by e-wallet/card on the kiosk, and either waits for a number to be called at the counter or has the food brought to a table they picked.

**In scope:** kiosk UI, cashier POS, kitchen display, customer-facing order board, admin back office, one shared API.
**Out of scope:** physical hardware, receipt printer drivers, cash drawers, payment terminals. Payments run against sandbox APIs only.

---

## 2. System Map

```
                     ┌──────────────────────────┐
                     │   ASP.NET Core Web API   │
                     │   (single backend)       │
   ┌─────────────┐   │  ┌────────────────────┐  │   ┌──────────────┐
   │   Kiosk     │──▶│  │ /api/kiosk/*       │  │   │ Neon         │
   │  (customer) │   │  │ /api/pos/*         │  │──▶│ (Postgres)   │
   └─────────────┘   │  │ /api/kds/*         │  │   └──────────────┘
   ┌─────────────┐   │  │ /api/display/*     │  │   ┌──────────────┐
   │ Cashier POS │──▶│  │ /api/admin/*       │  │──▶│ Cloudflare R2│
   └─────────────┘   │  │ /api/webhooks/*    │  │   │ (media)      │
   ┌─────────────┐   │  └────────────────────┘  │   └──────────────┘
   │ Kitchen KDS │──▶│  ┌────────────────────┐  │   ┌──────────────┐
   └─────────────┘   │  │ SignalR /hubs/     │  │──▶│ PayMongo     │
   ┌─────────────┐   │  │ orders             │  │   │ (sandbox)    │
   │ Order Board │◀──│  └────────────────────┘  │   └──────────────┘
   └─────────────┘   └──────────────────────────┘           │
   ┌─────────────┐              ▲                           │
   │ Admin       │──────────────┘◀──────── webhook ─────────┘
   └─────────────┘
```

### 2.1 The Five Frontends

| App | Who uses it | Auth | Core job |
|---|---|---|---|
| **Kiosk** | Customer | Device token | Browse menu, build cart, pick order type, pay |
| **POS** | Cashier | Clerk — `cashier` | Scan slip, take cash, confirm payment, reprint receipt |
| **KDS** | Kitchen crew | Clerk — `kitchen` | See paid orders, mark Preparing → Ready |
| **Board** | Public screen | Device token (read-only) | Show Preparing / Now Serving numbers |
| **Admin** | Manager | Clerk — `admin` / `manager` | CRUD menu, media, price, stock, reports |

---

## 3. Stack Decisions

### Backend
| Concern | Choice | Why |
|---|---|---|
| Framework | ASP.NET Core 10 LTS, C# | Requested; .NET 8 support ends 2026-11-10, so the project starts on the current LTS |
| API style | Controllers grouped by client | Clear security boundaries per route group |
| ORM | EF Core + Npgsql | Migrations, LINQ, good Postgres support |
| Database | PostgreSQL on **Neon** | Relational data, transactions, JSONB for modifier snapshots. See §3.1 |
| Realtime | SignalR | Native to .NET; no extra broker needed at one-instance scale |
| Auth | Clerk (JWT Bearer via JWKS) + device tokens | Requested; offloads staff identity management |
| Validation | FluentValidation | Keeps DTO rules out of controllers |
| Logging | Serilog → console + file/Seq | Structured logs |
| Jobs | `BackgroundService` (Hangfire if it grows) | Order expiry + stock release every 30 s. No reset job is needed for order numbers: the counter is one row per business date |
| PDF | QuestPDF | Receipts and cash slips |
| Docs | `Microsoft.AspNetCore.OpenApi` | Built-in OpenAPI document; source for the generated TS client |

### Frontend (all five apps)
React 19 + TypeScript 5.9 + Vite 8 · Tailwind CSS 4 · Zustand (cart/local UI state) · TanStack Query (server state) · `@microsoft/signalr` · `@clerk/clerk-react` (POS, KDS, Admin only) · `openapi-typescript` + `openapi-fetch` for a generated, typed API client. Dependency versions are pinned exactly, choosing the newest release that is at least 14 days old. TypeScript stays on 5.x because `openapi-typescript` requires it.

### Infrastructure — locked in, all free tier

| Layer | Service | Free allowance |
|---|---|---|
| Database | **Neon** (serverless Postgres) | 0.5 GB storage, 100 CU-hours/month per project, ~5 GB egress |
| Images + videos | **Cloudflare R2** | 10 GB storage, 1M Class A ops, 10M Class B ops, **zero egress** |
| Staff auth | **Clerk** | Free tier covers the staff headcount |
| Frontends (×5) | **Cloudflare Pages** | Static hosting |
| .NET API | **Render** (Docker web service) | Free instances sleep when idle; see §3.3 |
| Errors | **Sentry** | Free tier |

Local dev uses **portable PostgreSQL 17** started by `scripts/dev-db.ps1`, on **port 5433**: no Docker, no installer, no Windows service. It uses port 5433 because the dev PC already runs an unrelated Postgres on 5432. `docker-compose.yml` is kept for machines that do use Docker. There's no need to hit Neon while developing. `note.md` lists everything installed on the dev PC. Redis backplane for SignalR **only** if you run more than one API instance.

### 3.1 Why Neon, not Supabase

Both are real PostgreSQL; the difference is what wraps it.

Supabase's value is the bundle — auth, realtime, storage, auto-generated REST API. **This project uses none of it:** Clerk does auth, SignalR does realtime, R2 does media, and the .NET API *is* the API layer. That leaves Supabase's metering overhead with nothing to show for it, and its log-ingestion and log-query meters are the ones that bite first on the free tier — a chatty app can exhaust them while storing almost no data.

Neon meters compute-hours and storage instead. A single-branch kiosk doing a few hundred orders a day is nowhere near either cap.

**Operational notes**
- **Cold start.** Free-tier compute suspends after ~5 minutes idle; the first query after a quiet spell costs a second or two. Irrelevant during trading hours; a cron ping while the store is open removes it entirely.
- **Branching.** Neon clones the whole database like a Git branch — worth using before any risky migration.
- **No lock-in.** It is stock Postgres. `pg_dump` → restore anywhere → change the connection string. Nothing in the codebase knows it is Neon.
- **Connection pooling.** Use the pooled connection string (PgBouncer). If prepared statements misbehave through a transaction pooler, switch to the session pooler or disable prepared statements in Npgsql.
- **Backups.** Free tiers are not a backup strategy. Schedule a nightly `pg_dump` to R2 and keep 7 days.

### 3.2 Why R2 for both images and video

Video is where bandwidth caps kill free tiers. R2 does not charge egress at all, so there is no meter to drain. Cloudinary was considered and rejected: its free plan puts storage, delivery bandwidth and transformations on one shared 25-credit meter, and on fixed tiers exceeding it can block uploads rather than billing overage.

R2 has no on-the-fly image transforms — which this project does not need. Menu media is uploaded once by a manager, so **thumbnails and WebP variants are generated server-side in C# at upload time** (ImageSharp) and stored alongside the original. One vendor, one bucket, predictable cost.

**Egress is a non-problem here by design.** A fixed set of kiosks requests the same menu images all day. With long `Cache-Control` max-age, content-hashed filenames and the kiosk service worker, each asset is fetched roughly once per device per deploy — not once per customer.

### 3.3 Render for the API

Render has no native .NET runtime, so the API deploys as a **Docker web service** from a `Dockerfile` on `main`.

- **Sleeping.** Free instances spin down when idle, and the first request afterwards waits about a minute while it wakes. A customer at a kiosk won't wait that long, so production needs an always-on paid instance, or at least a keep-alive ping during trading hours (as with Neon's cold starts).
- **Background job.** The order-expiry job only runs while the instance is awake. Overdue orders still expire on the first run after waking, because the job catches up.
- **Config** comes from Render environment variables with the `__` separator: `ConnectionStrings__Default` (Neon pooled), `Clerk__Authority`, `Slip__SigningKey`, `PayMongo__SecretKey`, `PayMongo__WebhookSecret`, and `Cors__Origins__0…n` (one per Cloudflare Pages domain). The health check path is `/health`.
- **WebSockets** (SignalR) are supported on Render web services.
- **Proxy:** `ForwardedHeaders__Enabled=true` makes the API take the client IP from the last `X-Forwarded-For` hop only (the one Render adds), so per-IP rate limits see real clients and can't be fooled by a forged header. The container listens on Render's `$PORT`.
- **Bootstrap (until the Admin app exists):** a fresh production database has no menu and no kiosk device. `Bootstrap__SeedDemoMenu=true` adds the sample menu once, and `Bootstrap__KioskToken` registers one kiosk with a token you choose (`dev_` + at least 32 random characters; anything weaker stops the API at startup). Remove both once Admin manages the menu and devices.

**First deploy**
1. **Render:** Dashboard → New → **Blueprint** → this repo. Render reads `render.yaml` from `main` and asks for the `sync: false` values: `ConnectionStrings__Default` (Neon pooled, .NET format), `Clerk__Authority`, `Bootstrap__KioskToken`. `Cors__Origins__0` and the PayMongo values can stay empty for now.
2. **Cloudflare Pages:** create a project from this repo, production branch `frontend/kiosk`, with the build settings in that branch's README and `VITE_API_URL` set to the Render URL.
3. **Render again:** set `Cors__Origins__0` to the Pages URL (e.g. `https://<project>.pages.dev`); Render redeploys.
4. **Kiosk:** open the Pages URL. On the setup screen, enter the same `Bootstrap__KioskToken`.
5. **Later:** add the PayMongo keys. Until then, "Pay here" reports the provider as unavailable and the kiosk offers pay-at-counter.

---

## 4. Is One Shared Backend Safe?

Yes — as long as "shared" means one deployable with hard internal boundaries, not one undifferentiated API. The risk is not sharing a process; it is sharing a trust level. These eight rules enforce the boundary:

1. **Route group per client.** `/api/kiosk`, `/api/pos`, `/api/kds`, `/api/display`, `/api/admin`, `/api/webhooks`. A controller lives in exactly one group.
2. **A default-deny authorization policy** on every group. No endpoint is anonymous unless explicitly marked (`/api/webhooks/*` only, and that one verifies signatures instead).
3. **The server is the source of truth for money.** The kiosk sends `{productId, quantity, modifierIds}` — never prices, never totals. The backend recomputes subtotal, tax, and total from the database on every order. A tampered kiosk payload cannot change what is charged.
4. **Kiosk device tokens are narrowly scoped.** They can read the menu, create an order, and read *that* order by its own ID. They cannot list orders, read other orders, or touch any `/admin` or `/pos` route. Tokens are stored hashed, revocable per device.
5. **Stock is reserved at order creation inside a transaction.** The check and the decrement are one conditional `UPDATE … WHERE stock >= qty`. Two kiosks racing for the last item: one wins, one gets a clear error.
6. **Webhooks verify the provider signature and are idempotent** — the same event ID processed twice must not double-pay an order or double-decrement stock.
7. **CORS allowlist + per-route rate limits.** Kiosk order creation and webhook endpoints are the two routes most worth rate-limiting.
8. **Audit log on every admin mutation** (who, what, before/after). Price and stock edits are the ones you will want history for.

**When to split it up later:** if the kitchen display must survive the public kiosk going down, or if admin traffic starts affecting order latency. Neither applies at a single-branch scale, so one backend is the right call now.

---

## 5. Order Types and the Two Numbers

There are two different numbers and conflating them causes bugs. Keep them separate in the schema and the UI.

| | **Order number** | **Table number** |
|---|---|---|
| Who creates it | System, sequential, resets daily (`A-101`) | Customer takes a physical stand from the counter |
| Always present | Yes | Only for serve-to-table |
| Shown on | Slip, receipt, order board, KDS ticket | KDS ticket, server's handoff view |
| Typed by a human | No | Yes, on the kiosk |

### Dine in or take out (first screen after "Touch to start")

| Choice | What follows |
|---|---|
| **Dine in** | Order type screen below |
| **Take out** | Straight to the menu. Take-out is always counter pickup; the API rejects take-out + serve-to-table |

`Order.diningOption` (`DineIn` / `TakeOut`) is separate from `Order.type`. It goes on the receipt, the kitchen ticket and the board.

### Order type selection (dine in only)

| Type | Flow | Table number? |
|---|---|---|
| **Counter pickup** | Customer watches the board, collects at counter | No |
| **Serve to table** | Customer grabs a numbered stand, types that number on the kiosk | **Required** |

Rules:
- The table-number keypad only appears when *Serve to table* is chosen.
- The backend rejects a serve-to-table order with a missing, non-numeric, or out-of-range table number (range is configurable, e.g. 1–60).
- If the table number is already attached to another active order, the kiosk shows a soft warning ("Table 12 is already in use — continue?") rather than a hard block, since stands do get reused.

---

## 6. Order Lifecycle

```
                    ┌──────────────────┐
   Kiosk creates ──▶│     Created      │
                    └────────┬─────────┘
             cash ───────────┼─────────── e-wallet / card
                    ┌────────▼─────────┐   ┌────────▼─────────┐
                    │ AwaitingPayment  │   │ PaymentPending   │
                    └────────┬─────────┘   └────────┬─────────┘
          cashier confirms   │                      │  webhook confirms
                    ┌────────▼──────────────────────▼─────────┐
                    │                 Paid                    │ ──▶ appears on KDS
                    └────────────────────┬────────────────────┘
                                ┌────────▼─────────┐
                                │    Preparing     │ ──▶ board: "Preparing"
                                └────────┬─────────┘
                                ┌────────▼─────────┐
                                │      Ready       │ ──▶ board: "Now Serving"
                                └────────┬─────────┘
                                ┌────────▼─────────┐
                                │    Completed     │
                                └──────────────────┘

   Failure paths:  Any pre-Paid    ──expiresAt─▶ Expired   (stock released)
                   PaymentPending  ──failed────▶ Failed
                   Failed          ──retry─────▶ PaymentPending
                   Failed          ──cash──────▶ AwaitingPayment
                   Any pre-Paid    ──cashier───▶ Cancelled (stock released)

   "Pre-Paid" = Created, AwaitingPayment, PaymentPending, Failed. Every one of
   them carries expiresAt, so an abandoned order can never hold stock forever.
```

**Who moves each transition**

| Transition | Trigger |
|---|---|
| → Created | Kiosk submits cart |
| → AwaitingPayment | Customer picks "Pay at counter" |
| → PaymentPending | Customer picks e-wallet/card; checkout session opened |
| → Paid | Cashier confirms cash **or** PayMongo webhook arrives |
| → Preparing | Kitchen taps Start on the KDS |
| → Ready | Kitchen taps Ready |
| → Completed | Kitchen/server taps Handed Over |
| → Failed | PaymentPending; PayMongo reports a failed payment |
| → Expired | Background job, once any pre-Paid order passes `expiresAt` (default 15 min after creation) |
| → Cancelled | Cashier voids a pre-Paid order |

A webhook confirming payment for an order that has already expired or been cancelled does **not** revive it. It is recorded as an `OrderEvent` and flagged for a manual refund.

Transitions are enforced in the domain layer, not the controller. An illegal transition (e.g. Ready → Paid) throws rather than silently writing.

---

## 7. Payment Flows

### 7.1 Cash — "Pay at counter"

This is the flow you asked about. **The cashier never retypes the order.**

```
Customer                 Kiosk                   POS                    Backend
   │                       │                      │                        │
   │── confirm order ─────▶│                      │                        │
   │                       │── POST /orders ─────────────────────────────▶│
   │                       │◀── order id, A-101, expires 15:42 ───────────│
   │── picks "Pay at counter" ▶                   │                        │
   │                       │── POST /orders/{id}/pay {cash} ─────────────▶│
   │                       │◀── QR token + slip ──────────────────────────│
   │◀── slip on screen ────│                      │                        │
   │    (QR + A-101 + ₱285)│                      │                        │
   │                                              │                        │
   │────────── walks to counter, shows slip ─────▶│                        │
   │                                              │── scan QR ───────────▶│
   │                                              │◀── full order loads ──│
   │────────── hands over ₱500 ──────────────────▶│                        │
   │                                              │  enters tendered 500   │
   │                                              │  POS shows change ₱215 │
   │                                              │── confirm-cash ──────▶│
   │                                              │                        │── Paid
   │◀───────── receipt + change ──────────────────│                        │── SignalR ▶ KDS
```

**Details**
- The slip is rendered on the kiosk screen as a QR code plus a large order number. If a printer is added later, the same payload prints — nothing else changes.
- The QR encodes a short signed token, not the raw order ID, so a screenshot cannot be replayed against another order.
- Fallback: the cashier can type `A-101` manually if the QR will not scan.
- The POS also shows a live list of pending cash orders, so a customer who lost the slip can still be found by number or total.
- Unpaid after the window → `Expired`, stock restored, removed from the POS list.

### 7.2 E-wallet (GCash / Maya) and Card — sandbox

```
Kiosk ──▶ POST /orders ──▶ POST /orders/{id}/pay {method: ewallet}
      ◀── checkoutUrl / QR
Kiosk shows the sandbox checkout (embedded or QR for the customer's phone)
      │
PayMongo ──▶ POST /api/webhooks/paymongo  (signature verified, idempotent)
      │
Backend marks Paid ──▶ SignalR "OrderPaid" ──▶ KDS + Board + the waiting kiosk
      │
Kiosk auto-advances to the confirmation screen with the order number
```

- Both e-wallet (GCash, Maya) and card use a PayMongo **Checkout Session**. PayMongo's hosted page handles method selection, retries and card 3DS, so the backend never builds a payment-intent flow by hand.
- If the customer backs out on the kiosk, `POST /api/kiosk/orders/{id}/cancel-checkout` moves the order to `Failed`, and the kiosk offers Retry or Switch to cash. The payment record stays open: if they finish paying on their phone anyway, the webhook still moves the order to `Paid`.
- Every event carries the paid amount. A mismatch with the order total is never marked `Paid`; it is flagged for a human (`OrderEvent.RefundNeeded`).
- On the kiosk, **Pay here** offers **QR Ph** (any bank or e-wallet app: GCash, Maya, banks) and **Card**. Both open a Checkout Session; the kiosk shows a QR of the checkout page and the customer pays **on their own phone**, so nobody types card details on a public screen. A direct QR Ph image on the kiosk (Payment Intent API) is a later improvement.
- The order goes to the kitchen automatically, **with or without a table number** — the table number only changes what the KDS ticket and the server see, not whether the order flows.
- The kiosk never trusts a client-side "payment succeeded" redirect. The webhook is the only thing that sets `Paid`. The redirect just stops the spinner; if the webhook is slow, the kiosk polls `GET /kiosk/orders/{id}` as a fallback.
- Failed payment → `Failed`, and the kiosk offers Retry or Switch to cash. An abandoned checkout simply expires.

---

## 8. Data Model

```
Category ──< Product ──< ProductMedia
                 │
                 └──< ProductModifierGroup >── ModifierGroup ──< Modifier

Order ──< OrderItem ──< OrderItemModifier
   │
   ├──< Payment
   └──< OrderEvent        (status history / audit trail)

Device        (kiosk + board registration)
AuditLog      (admin mutations)
StockMovement (optional, per-item stock ledger)
```

**Key fields**

| Entity | Notable fields |
|---|---|
| `Product` | name, description, basePrice, stock? (null = untracked), isAvailable, sortOrder, categoryId |
| `ProductMedia` | type (Image/Video), url, thumbnailUrl, sortOrder |
| `ModifierGroup` | name, minSelect, maxSelect, isRequired (drives "pick a size", "add-ons") |
| `Order` | orderNumber, diningOption (DineIn/TakeOut), type (CounterPickup/ServeToTable), tableNumber?, status, subtotal, taxAmount, total, createdAt, expiresAt |
| `OrderItem` | lineNumber, productId, **nameSnapshot**, **unitPriceSnapshot**, quantity, lineTotal, notes |
| `OrderItemModifier` | sortOrder (question order), modifierId, nameSnapshot, priceDeltaSnapshot |
| `Payment` | method, provider, providerRef, status, amount, amountTendered?, changeDue?, processedByUserId? |
| `Device` | name, kind (Kiosk/Board), tokenHash, isActive, lastSeenAt |

**Two rules worth enforcing**
1. **Snapshot names and prices onto `OrderItem`.** When a manager raises a price at 2pm, yesterday's receipts must not change.
2. **Stock is reserved on `Created`** with a conditional decrement, so the last item cannot be sold twice. Abandoned orders cannot eat inventory because every pre-Paid order expires; expiry and cancellation release the stock inside the same transaction as the status change. `Product.stock` is nullable: `null` means not stock-tracked (e.g. fountain drinks).

---

## 9. API Surface

### Kiosk — device token
| Method | Route | Notes |
|---|---|---|
| GET | `/api/kiosk/menu` | Categories, products, modifiers, availability |
| POST | `/api/kiosk/orders` | `{diningOption, orderType, tableNumber?, items[]}` → order id, order number, totals, `expiresAt` |
| POST | `/api/kiosk/orders/{id}/pay` | `{method}` → cash slip (QR token) or checkout URL. Allowed from `Created` or `Failed` |
| POST | `/api/kiosk/orders/{id}/cancel-checkout` | Customer backed out of the online checkout → `Failed` |
| GET | `/api/kiosk/orders/{id}` | Status poll fallback. Only orders this kiosk created; others return 404 |
| GET | `/api/kiosk/tables/{n}` | `{inUse}` for the soft table-in-use warning |

### POS — Clerk `cashier` / `admin`
| Method | Route | Notes |
|---|---|---|
| GET | `/api/pos/orders` | Pending cash queue (`AwaitingPayment`) |
| GET | `/api/pos/orders/lookup?code=` | QR token or order number |
| POST | `/api/pos/orders/{id}/confirm-cash` | `{amountTendered}` → change due |
| POST | `/api/pos/orders/{id}/cancel` | `{reason}` |
| GET | `/api/pos/orders/{id}/receipt` | PDF *(not built yet)* |

### KDS — Clerk `kitchen` / `admin`
`GET /api/kds/orders` (Paid, Preparing, Ready) · `POST /api/kds/orders/{id}/preparing` · `/ready` · `/complete`

### Board — board device token or any staff role
`GET /api/display/board` → `{preparing[], ready[]}`

### Admin — Clerk `admin` / `manager`
CRUD `/api/admin/categories` · `/products` · `/modifier-groups` · `/modifiers`
`PUT /api/admin/categories/order` · `PUT /api/admin/products/order` (drag-to-reorder)
`POST /api/admin/products/{id}/media` (register by URL) · `POST /api/admin/media/presign` (image + video, *not built yet*) · `PATCH /api/admin/products/{id}/stock` · `/availability`
`GET /api/admin/reports/sales?from=&to=` *(not built yet)* · `GET/POST /api/admin/devices` · `POST /api/admin/devices/{id}/revoke`

### Webhooks — anonymous, signature-verified
`POST /api/webhooks/paymongo`

### Dev only: Development environment + PayMongo stub, else 404
`POST /api/dev/orders/{id}/simulate-payment` (kiosk token). Completes a stubbed online payment through the same webhook service. It is hidden from OpenAPI.

### SignalR `/hubs/orders`
**Groups:** `kitchen`, `pos`, `board`, `kiosks`, `kiosk-{orderId}`. Groups are assigned from the caller's identity on connect. A kiosk joins `kiosk-{orderId}` by calling `WatchOrder(orderId)`, and only for orders it created.
**Events:** `Order{Status}`, i.e. `OrderAwaitingPayment` · `OrderPaid` · `OrderPreparing` · `OrderReady` · `OrderCompleted` · `OrderExpired` · `OrderCancelled` · `OrderFailed`, plus `MenuChanged`. Staff groups get the full order; `board` gets only `{orderNumber, type, tableNumber, status}`.
**Auth:** browsers can't set WebSocket headers, so the token (Clerk JWT or device token) goes in `?access_token=`.

---

## 10. Folder Structure

```
kiosk-system/
├── docs.md
├── checklist.md
├── note.md                      # what was installed on the dev PC + how to remove it
├── docker-compose.yml           # optional; only for machines with Docker
├── scripts/dev-db.ps1           # portable Postgres: start | stop | status | psql
├── backend/global.json          # .NET 10 SDK + Microsoft.Testing.Platform runner
├── backend/dotnet-tools.json    # pins dotnet-ef
├── backend/
│   ├── KioskSystem.sln
│   ├── src/
│   │   ├── Kiosk.Api/
│   │   │   ├── Controllers/{Kiosk,Pos,Kds,Display,Admin,Webhooks}/
│   │   │   ├── Hubs/OrdersHub.cs
│   │   │   ├── Auth/            # Clerk JWT handler, device-token handler, policies
│   │   │   ├── Middleware/      # exception → ProblemDetails, request logging
│   │   │   └── Program.cs
│   │   ├── Kiosk.Application/   # use cases, DTOs, validators, port interfaces
│   │   ├── Kiosk.Domain/        # entities, enums, OrderStatus transition rules
│   │   └── Kiosk.Infrastructure/
│   │       ├── Persistence/     # DbContext, configurations, migrations
│   │       ├── Payments/        # PayMongoClient, webhook verifier
│   │       ├── Storage/         # R2 presigned uploads + ImageSharp variants
│   │       ├── Receipts/        # QuestPDF templates
│   │       ├── Security/        # signed slip-QR tokens
│   │       └── Jobs/            # order expiry
│   └── tests/
│       ├── Kiosk.UnitTests/
│       └── Kiosk.IntegrationTests/
└── frontend/                    # NOT on main: each app is its own frontend/* branch with this workspace at its root
    ├── apps/{kiosk,pos,kds,board,admin}/
    └── packages/
        ├── api-client/          # openapi.json snapshot + generated types + openapi-fetch client
        ├── realtime/            # SignalR hook wrapper
        └── ui/                  # shared Tailwind components
```

**Branches:** `main` holds the backend and docs and deploys to Render. Each frontend app has its own **frontend-only** branch (`frontend/kiosk`, `frontend/pos`, `frontend/kds`, `frontend/board`, `frontend/admin`), each deployed as its own Cloudflare Pages project. A frontend branch holds a pnpm workspace at its root (`apps/<app>`, `packages/api-client`) and no backend code; it talks to the API only through `packages/api-client/openapi.json`, a committed snapshot of the API contract. After an API change on `main`, the affected frontend branch runs `pnpm gen:api` and commits the refreshed snapshot. To run both locally, check the frontend branch out in a second folder with `git worktree` (see README).

Dependency direction: `Api → Application → Domain`, `Infrastructure → Application`. `Domain` depends on nothing. Keeping that one-way is what makes the backend testable without a database.

---

## 11. Auth & Roles

**Clerk** issues JWTs for staff. The backend validates them against Clerk's JWKS endpoint — no session lookup per request. Role comes from a custom session claim (`publicMetadata.role`) mapped to an ASP.NET policy.

Clerk is independent of Neon and R2; the three services never talk to each other. The flow is: staff signs in through Clerk's UI → React receives a JWT → React sends it as `Authorization: Bearer` → the API verifies the signature against cached JWKS and reads the role claim. **The backend makes no runtime call to Clerk** — it is standard JWT Bearer auth, roughly ten lines in `Program.cs`, with no Clerk SDK on the server.

The role claim must be exposed in the token explicitly. Without it the API receives a valid JWT with no way to tell a cashier from a manager.

**One-time Clerk setup**
1. Clerk dashboard → **Sessions** → *Customize session token* → add `{"role": "{{user.public_metadata.role}}"}`.
2. On each staff user, set **public metadata** to `{"role": "cashier"}` (or `kitchen`, `manager`, `admin`).
3. Set `Clerk:Authority` to the instance's **Frontend API URL** (API Keys page, e.g. `https://<name>.clerk.accounts.dev`). It is the token issuer and the JWKS source.
4. Optionally set `Clerk:AuthorizedParties` to the POS, KDS and Admin origins; tokens whose `azp` is not listed are rejected.

The API needs no Clerk secret key. Only the frontends use the publishable key.

**Policies** (one per route group): `Kiosk` (kiosk device), `Board` (board device or any staff role), `Pos` (cashier/manager/admin), `Kds` (kitchen/manager/admin), `Admin` (manager/admin). The fallback policy denies everything, so an endpoint without a policy is unreachable, and a test fails the build if one exists.

| Role | Kiosk | POS | KDS | Board | Admin |
|---|:--:|:--:|:--:|:--:|:--:|
| `admin` | — | ✅ | ✅ | ✅ | ✅ |
| `manager` | — | ✅ | ✅ | ✅ | ✅ |
| `cashier` | — | ✅ | — | ✅ | — |
| `kitchen` | — | — | ✅ | ✅ | — |
| *device token* | ✅ | — | — | ✅ | — |

Kiosks and boards have no human login. A manager registers the device in Admin, which shows a `dev_…` token **once**; the device stores it and sends it as `Authorization: Bearer dev_…`. The API routes `dev_` tokens to the device handler and everything else to Clerk JWT validation. Tokens are stored as SHA-256 hashes and are revocable.

---

## 12. Kiosk UX Rules

- **Flow:** Touch to start → Dine in / Take out → (dine in) Pick up at counter / Serve to my table → table number keypad (grab a stand first) → menu → product questions → cart → Pay at counter / Pay here (QR Ph, Card) → slip or receipt.
- **Product questions, one per screen,** in the order the manager set on the product's modifier groups. A **meal** asks: drink → upsize drink → upsize fries → fries flavor → anything to add. **À la carte** asks: add a drink → add a side → anything to add. Then comes quantity + "Add to cart". Required single-choice questions whose first option is free are pre-answered (e.g. Regular fries). Option names describe themselves ("Large fries", not "Large") because receipts and kitchen tickets show them without the question.
- **Printing:** the cash slip (order number + QR for the cashier) and the paid receipt print through `window.print()` with an 80 mm layout. Run Chrome with `--kiosk --kiosk-printing` so it prints silently to the default (thermal) printer, with no driver code in the app.
- **Attract screen** loops promo media (black-and-white shapes for now); any touch starts an order.
- **Idle reset**: 60s of inactivity → "Still there?" modal → 15s → cart cleared, back to attract. Prevents the next customer inheriting a stranger's cart.
- **Touch targets ≥ 48px**, image-first product tiles, no scrolling text walls.
- **Menu is fully data-driven.** A price or stock change in Admin pushes `MenuChanged` over SignalR, and the kiosk updates without a redeploy or restart. *(For now, the kiosk refetches the menu at the start of every order.)*
- **Demo data (Development only):** `Dev:SeedDemoData` seeds Rice Meals, Sandwiches, Pasta, Sides, Drinks and Desserts, plus a kiosk device whose token is `Dev:KioskToken`. The kiosk app's `.env.development` uses the same token.
- **Sold-out items** grey out in place rather than disappearing — a vanishing tile confuses people mid-order.
- **Service worker** caches the menu and images so a brief network drop does not blank the screen; order submission still requires connectivity and shows a clear retry.
- **Product videos** play muted and looped on the detail view only, never autoplay across the grid.

---

## 13. Open Decisions

| # | Question | Working assumption |
|---|---|---|
| 1 | Printed slip or on-screen QR? | On-screen QR + PDF for MVP; ESC/POS later |
| 2 | Cash payment window | 15 minutes |
| 3 | VAT / service charge on receipts | 12% VAT-inclusive, no service charge |
| 4 | Combos and promos in MVP? | No — modifiers only; combos in v2 |
| 5 | Single or multi-branch | Single branch; schema leaves room for `BranchId` |
| 6 | PayMongo or Xendit | PayMongo sandbox |
| 7 | Table number range | 1–60, configurable |

---

## 14. Change Log

| Date | Change |
|---|---|
| 2026-10-08 | Initial version. |
| 2026-10-08 | Rewritten: added system map, state machine, payment sequence diagrams, data-model rules, auth matrix, decisions table. |
| 2026-10-08 | Infrastructure locked in: Neon (Postgres), Cloudflare R2 (images + video), Clerk, Cloudflare Pages, Fly.io. Added §3.1 Neon vs Supabase, §3.2 R2 vs Cloudinary, Clerk independence note in §11. |
| 2026-10-08 | Design fixes before build: .NET 10 LTS (not 8); stock reserved at creation, released on expiry/cancel; one create → pay flow for all methods; every pre-Paid state expires; Failed can retry or switch to cash; late webhooks on expired orders flagged for refund; KDS lists Ready; Board accepts staff roles. |
| 2026-10-08 | Backend MVP built (Clean Architecture: Domain / Application / Infrastructure / Api with MVC controllers). Local dev moved off Docker to portable Postgres on port 5433. PayMongo Checkout Sessions for e-wallet **and** card. Added `cancel-checkout`, `tables/{n}`, reorder and device-revoke endpoints; documented the SignalR event set, Clerk setup steps and policy names. No order-number reset job needed. |
| 2026-10-08 | Kiosk app built (React 19, black-and-white placeholder design). Added **dine in / take out** (`Order.diningOption`), **QR Ph** payment method, one-question-per-screen product customization, browser printing for slip and receipt, a Development-only demo menu and simulated payment. Fixed: item and modifier order is now stored (`OrderItem.lineNumber`, `OrderItemModifier.sortOrder`). |
| 2026-10-08 | Branch layout: `main` = backend + docs; one `frontend/*` branch per app. |
| 2026-10-08 | Hosting: API on **Render** (Docker web service; free instances sleep, see §3.3) instead of Fly.io. Frontend branches are now frontend-only (one Cloudflare Pages project each); `main` holds no frontend code. |
| 2026-10-08 | Render deploy: `backend/Dockerfile`, `render.yaml` Blueprint (Singapore), proxy-aware client IP, `$PORT`, migrations on startup, startup check for the slip key, `Bootstrap__*` menu/kiosk seeding until the Admin app exists, first-deploy steps in §3.3. |
