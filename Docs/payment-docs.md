# Docs

| File | Purpose |
|------|---------|
| `Zibal-docs.json` | Zibal IPG OpenAPI reference (source of truth for the gateway contract) |
| `README.md` (this file) | Implementation plan + design notes for **Online Bill Payment via Zibal** |

---

# Online bill payment with Zibal — implementation plan

## 1. What the Zibal docs tell us (summary)

| Step | Endpoint | Notes |
|------|----------|-------|
| Request | `POST https://gateway.zibal.ir/v1/request` | body `merchant, amount (Rial), callbackUrl, orderId, description, mobile` → `{ trackId, result:100 }` |
| Start | `GET https://gateway.zibal.ir/start/{trackId}` | browser redirect; `Referer` must match the registered site (browser does this automatically) |
| Callback | `GET {callbackUrl}?trackId&success&status&orderId` | **informational only** – must be confirmed server-side |
| Verify | `POST /v1/verify` `{merchant, trackId}` | result `100` ok, `201` already verified, `202` not paid, `203` bad trackId |
| Inquiry | `POST /v1/inquiry` `{merchant, trackId}` | read-only status: `-1` waiting, `1` paid+verified, `2` paid, not verified, `3…21` failures |

* Test merchant: `merchant = "zibal"` (simulated payments, **never use in production**).
* Amounts are in **Rial**. In this project `Bill.TotalAmount` is already Rial, so it is sent unchanged (minimum accepted by Zibal: > 1,000).

## 2. Current status of the repository (before this change)

* `Bill` has `Draft → Approved → Paid`. Paying was **admin-only & manual** (`BillingService.RecordPaymentAsync`, button in *Billing* page).
* Residents (`Resident` role) can only *view* bills on `/resident/dashboard`. `House.ApplicationUserId` links a house to a user.
* `Payment` rows exist only for manual payments. There was **no storage of gateway attempts**, no callback endpoint, no HTTP client for a gateway.
* Auth = ASP.NET Identity cookie (`Lax` by default), whole app is Blazor Server (`prerender: false`).
* `/Account/LoginPost` redirected to *any* `returnUrl` (open-redirect) – relevant because the payment result page relies on login `returnUrl`.

## 3. Design

### 3.1 Flow

```
Resident (logged in)                   Our server                          Zibal
  │ click "پرداخت آنلاین"                │                                   │
  ├────────────────────────────────────►│ authorize (bill belongs to user,    │
  │                                     │ bill Approved, amount ok, rate limit)│
  │                                     │ INSERT PaymentAttempt(Initiated)    │
  │                                     ├────────── POST /v1/request ────────►│
  │                                     │◄──────── trackId ──────────────────┤
  │                                     │ attempt → Requested (+trackId)      │
  │◄──── redirect /start/{trackId} ─────┤                                     │
  ├────────────────────────────── pay at bank page ──────────────────────────►│
  │◄──── 302 GET /payment/callback?trackId=..&success=..&status=..&orderId=.. ┤
  ├────────────────────────────────────►│ (anonymous endpoint, cookie sent    │
  │                                     │  because top-level GET + SameSite=Lax)
  │                                     │ record callback, atomically CLAIM   │
  │                                     ├────────── POST /v1/inquiry ────────►│
  │                                     │ status 2 → ────── POST /v1/verify ─►│
  │                                     │ check amount + orderId              │
  │                                     │ TX: bill→Paid, Payment, debt↓,      │
  │                                     │     attempt→Succeeded               │
  │◄──── 302 /payments/result/{publicId} ┤                                    │
  │ result page (login only if cookie lost; returnUrl restores the page)      │
```

### 3.2 Security rules (the important part)

1. **Never trust callback parameters.** `success`/`status`/`orderId` are only logged. The decision is always taken from Zibal's *inquiry/verify* answer (server-to-server). A forged callback can therefore neither fail nor pay an attempt.
2. **Amount & order binding.** Verified `amount` must equal the amount stored in our DB when the attempt was created and `orderId` must equal our stored `orderId` (random GUID). Mismatch ⇒ `NeedsReview`, bill is *not* marked paid.
3. **Exactly-once settlement.** Attempt is *claimed* with one atomic `UPDATE … WHERE Status IN (…)`; the bill is marked paid with `UPDATE … WHERE Status = Approved`; Payment + bill + house debt + attempt change happen in **one DB transaction**. Replayed/duplicate callbacks, double tabs and admin-manual payments cannot double-credit.
4. **Money taken but bill already paid** ⇒ attempt becomes `NeedsReview` (admin sees it in red and refunds via Zibal panel).
5. **Ownership.** Start requires `bill.House.ApplicationUserId == current user`. Result/history pages only show attempts of the current resident's bills (admins see all). Public ids are GUIDs (not sequential).
6. **Session survives the bank trip.** Auth cookie is explicitly `SameSite=Lax` (sent on the top-level GET back from Zibal). The callback endpoint itself is anonymous and idempotent, so even if the cookie is lost the payment is still verified/credited and the result page just asks for login and returns to the same URL. `returnUrl` is now validated (local URLs / same host only).
7. **Secrets.** `Zibal:Merchant` is empty in `appsettings.json` (feature disabled by default), `zibal` (test) only in `appsettings.Development.json`. Use env var `Zibal__Merchant` / user-secrets in production. The merchant id is never stored in logs/events.
8. Abuse limits: max 5 attempts / bill / 10 min; callback only triggers outbound calls for existing, non-final attempts.

### 3.3 Attempt state machine (`PaymentAttemptStatus`)

```
Initiated ─(request ok)─► Requested ─(callback)─► CallbackReceived ─┐
    │ (request failed)         │                                    ▼
    ▼                          └────────────(reconcile)──────────► Verifying (claim, 2-min lease)
RequestFailed                                                        │ inquiry/verify
                       ┌──────────────┬───────────────┬──────────────┼───────────────┐
                       ▼              ▼               ▼              ▼               ▼
                    Failed        Expired        NeedsReview    Verified ──TX──► Succeeded
              (cancelled/declined) (never paid)  (mismatch / bill   (paid at Zibal,
                                                  not payable)       settling)
```
`Verifying` is transient; on a transient error it is released back (`CallbackReceived` / `Verified`) so an admin/user "استعلام" retry continues exactly where it stopped (no second verify when Zibal already says `status=1`).

## 4. Changes

### 4.1 Domain (`ResidentialComplex.Domain`)
* `Enums/PaymentAttemptStatus.cs`, `Enums/PaymentEventType.cs` (new)
* `Entities/PaymentAttempt.cs` (new): public id, bill, amount, order id, track id, status, timestamps, callback data, Zibal status/ref/card/paid amount, note, link to `Payment`.
* `Entities/PaymentAttemptEvent.cs` (new): append-only trace of every gateway interaction (request/callback/inquiry/verify/settlement) with the sanitized JSON payload.

### 4.2 Application (`ResidentialComplex.Application`)
* `Interfaces/IPaymentGateway.cs`, `IPaymentAttemptRepository.cs`, `ITransactionRunner.cs` (new); `IBillRepository.TryMarkPaidAsync` (new member).
* `DTOs/PaymentDtos.cs`: gateway request/response models + service results.
* `Helpers/ZibalStatusCodes.cs`: Persian descriptions + classification of Zibal status/result codes.
* `Services/PaymentService.cs`: start, callback handling, reconcile (all rules of §3.2).

### 4.3 Infrastructure (`ResidentialComplex.Infrastructure`)
* `Settings/ZibalOptions.cs`, `Services/ZibalGateway.cs` (HttpClient + System.Text.Json, timeouts, sanitized payload capture).

### 4.4 Persistence (`ResidentialComplex.Persistence`)
* `DbSet`s + EF configurations, `PaymentAttemptRepository`, `EfTransactionRunner`, `BillRepository.TryMarkPaidAsync`.
* Migration `20250101000006_AddPaymentAttempts` (SQL Server + SQLite) — same hand-written style as the existing ones.

### 4.5 Web (`ResidentialComplex.Web`)
* `Program.cs`: DI, options, named `HttpClient`, **`GET /payment/callback`**, explicit cookie `SameSite=Lax`, safe `returnUrl`.
* Resident: *Dashboard* gets "پرداخت آنلاین" for approved bills; new `/payments/result/{id}` and `/resident/payments` pages.
* Admin: new `/admin/payments` page (all attempts, filter, details timeline, "استعلام وضعیت").
* Shared `PaymentAttemptDetailsDialog` (resident view = summary, admin view = full trace).
* Sidebar links (admin + resident), help texts, `appsettings*.json` keys.

### 4.6 Tests (`tests/ResidentialComplex.Tests`)
| File | Needs DB? | Covers |
|------|-----------|--------|
| `PaymentFakes.cs` | – | in-memory repos/transaction (rollback), fake Zibal (documented codes), fake clock |
| `PaymentServiceTests.cs` | no | happy path, replay, forged callbacks, amount/order mismatch, double payment, ownership, rate limit, parallel callbacks, gateway outages, rollback + retry, expiry, stale lease, auto-reconcile |
| `ZibalGatewayTests.cs` | no | HTTP contract with Zibal (bodies, parsing, errors, merchant never leaked) |
| `PaymentWebHelperTests.cs` | no | `returnUrl` open-redirect protection, callback URL building |
| `PaymentPersistenceTests.cs` | SQLite | atomic `Try*` updates, real transaction rollback, unique `TrackId`/`OrderId`, round trips, full flow on EF, hand-written **migration** against the EF model |
| `PaymentEndpointTests.cs` | SQLite file + host | `GET /payment/callback` end to end (anonymous, replay, forged), cookie `SameSite=Lax`, safe login redirect |
| `IntegrationTests.cs` | – | `TestBase` now also registers the payment services |

## 5. Configuration

```jsonc
"Zibal": {
  "Merchant": "",                         // REQUIRED in production ("zibal" = test merchant, Development only)
  "BaseAddress": "https://gateway.zibal.ir",
  "CallbackBaseUrl": "",                  // optional, e.g. https://resident.example.ir  (defaults to the host the user is browsing)
  "TimeoutSeconds": 30
}
```

## 6. Out of scope / future
* Zibal "lazy" mode, multiplexing (تسهیم), automatic refunds.
* SMS receipt after payment (can reuse the `SmsTemplate` mechanism).
* Background job that reconciles abandoned attempts (today: on-demand "استعلام" by admin/resident and automatically before a new attempt on the same bill).

## 7. Verification status

* The Application-layer logic (`PaymentService`), `ZibalGateway`, `ReturnUrlHelper`, `PaymentCallbackUrl` and **all of `PaymentServiceTests`, `ZibalGatewayTests`, `PaymentWebHelperTests` were compiled and executed (80 tests, 0 failed)** against a shim of the xunit API. Mutation checks (removing the amount check, the ownership check, the atomic bill guard, treating forged callbacks as truth) make these tests fail, i.e. they really guard the rules above.
* The SQLite DDL of the migration was executed in SQLite (tables, FKs, filtered unique `TrackId`).
* The EF Core / MudBlazor / `WebApplicationFactory` parts (`PaymentPersistenceTests`, `PaymentEndpointTests`, repository, pages) could only be **syntax- and Razor-checked** in the authoring environment (NuGet was not reachable there). **Run `dotnet test` once on your machine** and tell us about any failure — they are written against the same scenarios that passed above.

## 8. Go-live checklist

1. Set the real merchant: environment variable `Zibal__Merchant` (or user-secrets). The test merchant `zibal` only simulates payments.
2. In the Zibal panel register the site domain (the `Referer` of the redirect) and, if the server IP check is enabled, the server IP (result `115`).
3. Optional but recommended: `Zibal__CallbackBaseUrl=https://<your-public-domain>`; serve the site over HTTPS only.
4. Apply the migration (the app runs `MigrateAsync` on start).
5. Watch **/admin/payments** for the red "نیازمند بررسی" banner: money received that could not be applied automatically (refund from the Zibal panel using the trackId).
6. Hygiene: `appsettings.json` in the repository contains a real DB password and SMS API key — move them to environment variables / user-secrets and rotate them.
