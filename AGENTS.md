# AGENTS.md

Guidance for AI coding agents (and humans) working on this repository.
Read this file first; it is the single source of truth for conventions. It supersedes `.github/agents.md`.

## 1. Project

Financial management system for a residential complex: apartments, houses (units), financial items,
monthly bills, online payment (Zibal), debt tracking and reports.

- **Stack:** .NET 10, ASP.NET Core Blazor Server (interactive, `prerender: false`), **MudBlazor 7.x**,
  EF Core (SQL Server primary, SQLite for tests/dev), ASP.NET Identity (cookie auth), xUnit.
- **UI language:** Persian (Farsi), **RTL**. All user-facing text is Persian.
- **Dates:** the app uses local time — `DateTime.Now` (not `UtcNow`) and `PersianCalendarHelper` for display.
- **Money:** always `decimal`, amounts are **Rial**. Never `float`/`double`.

## 2. Commands

```bash
dotnet restore ResidentialComplex.slnx
dotnet build   ResidentialComplex.slnx
dotnet run --project src/ResidentialComplex.Web
dotnet test    ResidentialComplex.slnx
dotnet test    ResidentialComplex.slnx --filter "FullyQualifiedName~Payment"   # a subset
```

A change is **not done** until `dotnet build` is clean (no new errors) and `dotnet test` passes.

## 3. Layout and dependency rules

```
src/
  ResidentialComplex.Domain          Entities, Enums                       (no dependencies)
  ResidentialComplex.Application     Services, DTOs, Interfaces, Helpers   (-> Domain)
  ResidentialComplex.Persistence     DbContext, EF configs, repositories, migrations (-> Domain, Application)
  ResidentialComplex.Infrastructure  Audit, SMS, Zibal gateway             (-> Application, Persistence)
  ResidentialComplex.Web             Blazor UI, auth, Program.cs           (-> all)
tests/ResidentialComplex.Tests       xUnit (SQLite in-memory + fakes)
Docs/                                Zibal-docs.json, payment-docs.md (payment design + security rules)
```

- Do not create circular references. Business rules live in `Application`; UI code-behind stays thin.
- Prefer **reusing** existing services/helpers/repositories over adding new ones (e.g. `PaymentService.NormalizeMobile`
  for phone numbers, `IAuditService` for audit trail, `ISmsService` for SMS).
- Central package management: add versions to `Directory.Packages.props`, not in `.csproj` files.

## 4. Web / UI conventions

- Use **MudBlazor components** (`MudPaper`, `MudDataGrid`, `MudDialog`, `MudStack`, `MudAlert`, ...). Bootstrap RTL is only the base
  stylesheet; do not build new UI with raw Bootstrap classes. No CDN links — assets are served from `wwwroot`.
- Every page is a pair: `Name.razor` + `Name.razor.cs` (`public partial class`, `[Inject]` properties, Persian `Snackbar` messages,
  `try/catch/finally` with an `isLoading` flag).
- **Authorization on every page**, in both places: `@attribute [Authorize(Roles = "...")]` in the `.razor` and `[Authorize(Roles = "...")]`
  on the class. Public pages use `[AllowAnonymous]` (login, register, payment callback).
- Roles are exactly three: `Administrator`, `Worker`, `Resident` (`AppRoles`). Residents may only ever see **their own** data —
  select data by the signed-in user's house/id, never by a client-supplied id.
- New pages get a sidebar link in `Components/Layout/MainLayout.razor` inside the correct role section, and (for admin/resident pages)
  a `HelpButton PageKey="..."` with an entry in `Components/Help/HelpContent.cs`.
- Dialogs: put the dialog in its own component, open it with `DialogService.ShowAsync<T>` and `DialogParameters` built with `nameof(...)`,
  reuse one dialog for view/edit modes instead of inline panels (see `BillingGenerateDialog`).
- If an injected property has the same name as its type (e.g. `[Inject] PaymentService PaymentService`), call **static** members with the
  fully qualified name (`global::ResidentialComplex.Application.Services.PaymentService.IsOpen(...)`); going through the property is
  compile error CS0176.
- Numeric money inputs: `Format="N0"` with `Culture="@CultureInfo.InvariantCulture"` (thousand separators like `30,209`).
- Keep the existing markup patterns (e.g. conditional blocks wrapped in `<div>`, `display:contents` spans in loops) — they work around
  MudBlazor/RTL rendering quirks.

## 5. Database and migrations

- Migrations are **hand-written**; never run `dotnet ef migrations add`. Files live in `src/ResidentialComplex.Persistence/Migrations_<Name>.cs`
  (namespace `...Persistence.Migrations`) with `[DbContext(typeof(ApplicationDbContext))]` and `[Migration("2025010100000N_<Name>")]`.
  Use the next free number (currently `000013`; the last one is `000012_AddHouseSortOrder`). The model snapshot is intentionally minimal — do not regenerate it.
- Every migration implements **both providers**: `if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer") { ... } else { /* SQLite */ }`,
  plus a working `Down`.
- When you change an entity: update the entity, its configuration in `Configurations/EntityConfigurations.cs`, the migration, and the tests.
  Tests build the schema from the **model** (`EnsureCreated`) while `MigrationTests` run the real migrations on SQLite, so both must agree.
- Unique/filtered indexes, FKs (`Restrict` for financial data) and `RowVersion` concurrency tokens follow the existing configurations.
- Prefer atomic conditional updates (`ExecuteUpdateAsync` with a `Where` on the expected state) plus `ITransactionRunner` for multi-step money changes.

## 6. Security rules (do not weaken)

- **Never commit secrets** (DB passwords, SMS API keys, Zibal merchant). Use environment variables / user-secrets
  (e.g. `Zibal__Merchant`, `Sms__ApiKey`). Zibal test merchant `zibal` is for `Development` only.
- Login is a plain HTML form posting to `/Account/LoginPost` (logout: `/Account/LogoutPost`); the login page has a captcha component.
  `returnUrl` must always pass through `ReturnUrlHelper.GetSafeLocalUrl` (no open redirects).
- The auth cookie must stay `SameSite=Lax` — the user returns from the bank with a cross-site GET and must not be logged out.
- **Online payment** (`PaymentService`, `/payment/callback`): callback query values are untrusted; only Zibal's inquiry/verify decide.
  Amount and orderId are bound to what was stored; settlement is exactly-once (atomic claim + conditional bill update + one transaction).
  Read `Docs/payment-docs.md` before touching it.
- **Resident sign-up / password reset** (`ResidentRegistrationService`): OTP is hashed, rate limited per phone, and never logged or returned;
  Administrator/Worker accounts can never be created or reset from the public page. Do not log OTP codes anywhere.
- Validate ownership on the server (service layer), not only in the UI.

## 7. Testing

- xUnit. `TestBase` (in `IntegrationTests.cs`) gives a fresh SQLite in-memory DB + DI container per test; register new services there.
  Do **not** register a global `ISmsService`/gateway fake that changes the behavior of existing tests — wire it explicitly.
- Prefer service-level tests with fakes (`PaymentFakes.cs`: in-memory repositories, `FakePaymentGateway`, `FakeClock`, `FakeSmsService`,
  `InMemoryUserStore`) for rules/security, and EF/SQLite tests for persistence, constraints and migrations.
- Time-dependent logic takes an optional `TimeProvider` so tests can advance a `FakeClock`.
- Every behavior change needs new/updated tests in the same change; security rules need a test that fails if the rule is removed.
- Test names describe the behavior: `Method_Condition_ExpectedResult`.

## 8. Domain quick reference

- Houses are listed by `House.SortOrder` ascending (ties by `Id`) everywhere — repository queries do the ordering, so new house/bill/usage listings must `OrderBy` it too. New houses get `SortOrder` from a "واحد N" title (`House.DefaultSortOrderFromTitle`), else 0; `HouseForm` must copy it when editing.
- Bill: `Draft → Approved → Paid`. Approved bills are immutable; approving raises `House.CurrentDebt`, paying lowers it.
- Calculation types: `EqualDivision`, `Grouping` (whole-consumption bracket pricing: the total usage picks one tier and the whole usage is
  billed at that tier's rate — not incremental). Bill totals are rounded **up** to the nearest 10,000 Rial.
- Period types: `Once`, `Permanent`, `Installment`.
- Usage for Grouping items is entered as meter readings (previous/current); previous carries over from the last month.
- SMS texts come from editable `SmsTemplate` rows with `{Placeholders}` (admin page), not hard-coded strings.

## 9. Git and change hygiene

- Small, focused commits with imperative messages (`Add ...`, `Fix ...`); work on a feature branch and merge via PR.
- Don't reformat unrelated files or change line endings (the repo uses LF).
- Update `README.md` / `Docs/` when behavior or configuration changes; remove stale statements instead of adding contradictions.
