# Architecture Reference & Project Starter Guide

A domain-agnostic description of the architecture, patterns, identity setup, testing approach, database usage and technology stack of this solution. Use it as a blueprint for new projects. Replace `<App>` with your project name.

---

## 1. Technology Stack

| Concern | Choice |
|---|---|
| Runtime / language | .NET 10, C# (nullable enabled, implicit usings) |
| UI | Blazor Web App, **Interactive Server** render mode (`AddRazorComponents().AddInteractiveServerComponents()`) |
| UI component library | MudBlazor |
| Auth state in UI | `Microsoft.AspNetCore.Components.Authorization` + `AddCascadingAuthenticationState()` |
| Identity | ASP.NET Core Identity (`AddIdentity<ApplicationUser, IdentityRole>`), cookie authentication |
| ORM | Entity Framework Core 10 (Fluent API configurations, migrations) |
| Production DB | SQL Server (`Microsoft.EntityFrameworkCore.SqlServer`) |
| Test DB | SQLite in-memory (`Microsoft.EntityFrameworkCore.Sqlite`) |
| External HTTP | `IHttpClientFactory` named clients (`Microsoft.Extensions.Http`) |
| Caching | `IMemoryCache` (short-lived state such as OTP codes) |
| Testing | xUnit, `Microsoft.AspNetCore.Mvc.Testing`, bUnit, `Microsoft.NET.Test.Sdk` |
| Package management | Central Package Management (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`) |
| Solution format | `.slnx` |
| Logging | `ILogger` built-in providers; file logs in a `logs/` folder |

---

## 2. Solution Layout

```
<App>.slnx
Directory.Packages.props          # all NuGet versions live here
Docs/
src/
  <App>.Domain/                   # entities, enums (no dependencies)
  <App>.Application/              # use cases, interfaces, DTOs, pure logic
  <App>.Persistence/              # EF Core DbContext, configurations, repositories, migrations
  <App>.Infrastructure/           # adapters to external systems (HTTP APIs, SMS, payment, audit)
  <App>.Web/                      # Blazor host, composition root, endpoints, UI
tests/
  <App>.Tests/                    # xUnit tests for all layers
```

### Dependency direction

```
Web ───────────► Application ───► Domain
 │                   ▲
 ├──► Persistence ───┤ (implements Application interfaces)
 └──► Infrastructure ┘ (implements Application interfaces)
```

- **Domain**: references nothing.
- **Application**: references Domain only; declares the ports (interfaces) outer layers implement.
- **Persistence** (EF Core, Identity.EntityFrameworkCore) and **Infrastructure** (`Microsoft.Extensions.Http`): reference Domain + Application.
- **Web**: references Application, Persistence, Infrastructure; it is the **composition root**.
- **Tests**: reference every project.

Inner layers never reference outer layers. Application code never touches EF Core types, HTTP clients or UI.

---

## 3. Layer Responsibilities

### 3.1 Domain
- `Entities/` – plain POCOs with properties and simple invariants; publicly exposed entities carry a separate public GUID so internal ids never leak into URLs.
- `Enums/` – statuses, types; `AppRoles` constants hold role names.
- No framework dependencies (`ApplicationUser` lives in Persistence because it derives from `IdentityUser`).

### 3.2 Application
- `Interfaces/` – repository interfaces (`I<Entity>Repository`), external ports (`IPaymentGateway`, `ISmsService`, `IAuditService`) and `ITransactionRunner`.
- `Services/` – use-case/orchestration classes (scoped, injected into UI and endpoints).
- `Dtos/` – request/response/read models.
- `Helpers/`, `Constants/` – pure, deterministic logic (calculators, calendar helpers, template renderers, constant keys), free of I/O and easy to unit test.

### 3.3 Persistence
- `ApplicationDbContext : IdentityDbContext<ApplicationUser>` – one context for Identity + domain tables.
- `DbSet<T>` properties are expression-bodied (`=> Set<T>()`).
- `OnModelCreating` calls `base.OnModelCreating` then `ApplyConfigurationsFromAssembly(...)`; one `IEntityTypeConfiguration<T>` per entity.
- `Repositories/` – concrete repositories; they own all queries, `Include`s and `SaveChanges`.
- `EfTransactionRunner` – unit-of-work helper (see 4.4).
- Migrations applied automatically at startup.

### 3.4 Infrastructure
- Adapters implementing Application ports: HTTP gateway clients, SMS sender, audit service.
- `Options/` – strongly typed settings (`SmsOptions`, `GatewayOptions`) bound from configuration sections.

### 3.5 Web
- `Program.cs` – DI, Identity, cookies, Blazor, middleware, minimal-API endpoints, migrate + seed on startup.
- `Components/` – `Layout/`, `Pages/` grouped by role (`Account/`, `Admin/`, `Worker/`, `Resident/`), `Shared/` reusable components, small feature folders (captcha, help).
- `Security/` – web-specific helpers/services (registration/OTP flow, account settings, safe return-URL validation, callback URL builder).
- `wwwroot/` – static assets.
- `public partial class Program { }` at the end of `Program.cs` enables `WebApplicationFactory<Program>`.

---

## 4. Design Patterns & Conventions

### 4.1 Clean / Onion architecture
Layers with inward-pointing dependencies; business rules in Domain/Application, framework details outside.

### 4.2 Repository pattern
One interface per aggregate in Application, one EF implementation in Persistence, registered `Scoped`.

### 4.3 Ports & Adapters (Dependency Inversion)
Anything crossing a process boundary (DB, HTTP APIs, SMS, clock) sits behind an Application interface, so services are testable with fakes.

### 4.4 Unit of Work via `ITransactionRunner`
```csharp
public interface ITransactionRunner
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> work);
}
```
The EF implementation opens a transaction on the shared scoped `DbContext`, commits on success, rolls back on exception, and **joins an existing transaction** if one is open (re-entrant). Multi-step atomic operations use it.

### 4.5 Service layer
Scoped `XxxService` classes orchestrate repositories and ports; UI injects services, not repositories.

### 4.6 Options pattern
Settings bound to typed classes (`Configure<T>(section)`) and consumed via `IOptions<T>`. Startup validates critical options and logs warnings (missing credentials, test credentials outside Development).

### 4.7 Named `HttpClient`
Each adapter registers its own client (`AddHttpClient(nameof(Adapter), ...)`) with base address/timeout from options.

### 4.8 Idempotent, server-verified external callbacks
- Callback endpoint is anonymous (session may be gone); all inputs are untrusted.
- The service re-verifies results server-to-server before changing state.
- Processing is idempotent and uses `CancellationToken.None` so it completes even if the client disconnects.
- Responses set `Cache-Control: no-store` and `Referrer-Policy: no-referrer`.
- An attempt/event table records each state transition.

### 4.9 Audit logging
`IAuditService` writes who/what/when for important mutations into an `AuditLog` table.

### 4.10 Time abstraction
Use `TimeProvider` instead of `DateTime.Now`; tests inject a fake clock.

### 4.11 Pure calculation helpers
Non-trivial computation is static/pure in Application with thorough unit tests.

### 4.12 Message templates
Message bodies (e.g. SMS) are DB templates identified by constant keys, rendered by a pure renderer with placeholders.

### 4.13 Coding conventions
- Constructor injection; `Scoped` lifetime for services/repositories.
- Async all the way; no sync-over-async.
- Namespaces mirror folders (`<App>.Layer.Folder`).
- XML `<summary>` on public types; comments explain *why* (security/regression-sensitive decisions).

---

## 5. Identity & Security

### 5.1 Setup
- `ApplicationUser : IdentityUser` with extra profile fields (e.g. `FullName`).
- `AddIdentity<ApplicationUser, IdentityRole>` on `ApplicationDbContext` (Identity tables share the database and migrations).
- Password policy: digit required, lowercase required, uppercase and non-alphanumeric not required, min length 6. Tighten for new projects.
- Roles are string constants (`AppRoles`): `Administrator`, `Worker`, `Resident` (rename/extend per project).

### 5.2 Cookie authentication
```csharp
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;   // Lax keeps the session on cross-site top-level returns (e.g. payment redirects); never Strict
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
});
```
Add `Cookie.SecurePolicy = Always` for HTTPS-only production.

### 5.3 Login/logout as minimal-API endpoints
Blazor Server circuits cannot set cookies over SignalR, so sign-in is a plain HTTP form POST:
- `POST /Account/LoginPost` – reads the form, calls `SignInManager.PasswordSignInAsync`, redirects to a **validated** return URL (`ReturnUrlHelper` rejects external/open-redirect URLs) or back with an encoded error.
- `GET /Account/LogoutPost` – `SignOutAsync`, then redirect to login.
- A captcha component protects public forms.
- Consider enabling `lockoutOnFailure: true` for new projects.

### 5.4 Authorization
- Pages use `[Authorize(Roles = "...")]`; folders are organized by role.
- Public pages (login, registration, external callbacks) use `[AllowAnonymous]` / `.AllowAnonymous()`.
- `AddAuthorization()` + cascading authentication state for `<AuthorizeView>`.
- `UseAntiforgery()` enabled.

### 5.5 Seeding (idempotent, on startup)
1. Apply migrations.
2. Create missing roles.
3. Create a default administrator if missing.

Do not ship default admin credentials; read them from secrets/environment variables.

### 5.6 Self-service registration / password reset
- One-time code sent by SMS; state stored in `IMemoryCache` with expiry.
- Implemented in a dedicated web service separate from UI components; account settings (profile/password) in another service; both have persistence tests.

### 5.7 HTTP hardening
`UseHsts` (non-Development), `UseHttpsRedirection`, anti-forgery, HttpOnly cookies, `no-store` on sensitive endpoints.

### 5.8 Secrets
Never commit real connection strings, API keys or passwords in `appsettings.json`. Use User Secrets locally and environment variables / a secret store in production; keep placeholders only in the repository.

---

## 6. Database Usage

### 6.1 Providers
- `Database:Provider` and `Database:ConnectionString` configuration keys; production uses **SQL Server**.
- Tests use **SQLite in-memory** with the same `ApplicationDbContext` and configurations.
- Keep Fluent API provider-neutral (avoid SQL-Server-only column types) so tests stay valid.

### 6.2 Modeling rules
- One `IEntityTypeConfiguration<T>` per entity, discovered by `ApplyConfigurationsFromAssembly`.
- Explicit max lengths, required flags, decimal precision, indexes and unique indexes (business uniqueness is enforced in the database too).
- Enums stored as int/string; public GUIDs for external references.
- Header/child entities use explicit relationships and delete behavior.

### 6.3 Migrations
- Live in Persistence; applied at startup via `Database.MigrateAsync()`.
- `PendingModelChangesWarning` is suppressed in `OnConfiguring` (migrations are hand-maintained).
- A migration test applies the whole chain on SQLite.
- Commands (from solution root):
  ```
  dotnet ef migrations add <Name> -p src/<App>.Persistence -s src/<App>.Web
  dotnet ef database update -p src/<App>.Persistence -s src/<App>.Web
  ```
  `dotnet-tools.json` in the Web project pins `dotnet-ef`.

### 6.4 Transactions & concurrency
- Multi-write operations run inside `ITransactionRunner.ExecuteAsync`.
- One scoped `DbContext` per request/circuit scope shared by repositories, so a transaction covers all of them.
- Callback flows rely on status checks + unique constraints to prevent double processing.

### 6.5 Auditing & event tables
`AuditLog` for user actions; append-only event table for external workflow transitions.

---

## 7. Testing Approach

One project, `tests/<App>.Tests`, using **xUnit**.

| Level | Technique |
|---|---|
| Pure unit tests | Call static/pure helpers directly (calculators, renderers, chart-data builders) |
| Service tests with fakes | In-memory fakes of ports: shared fake store, fake repositories, `FakeTx`, `FakeAudit`, `FakePaymentGateway`, `FakeClock : TimeProvider` |
| Integration tests with real EF | `TestBase` builds a `ServiceCollection`, registers `ApplicationDbContext` on **SQLite in-memory**, all repositories/services, creates the schema; fresh DB per test instance |
| Persistence tests | Same base; verify mappings, constraints, round-trips (`*PersistenceTests`) |
| Migration test | Applies the full migration chain to SQLite |
| Adapter tests | `HttpMessageHandler` stubs, no network |
| Endpoint tests | `WebApplicationFactory<Program>` (`Microsoft.AspNetCore.Mvc.Testing`) |
| Web helper tests | Plain unit tests for URL/return-URL helpers |
| Page access tests | Reflection on compiled components: assert `[Authorize]` roles, `[AllowAnonymous]`, `[Route]` templates |
| Layout tests | bUnit rendering to assert markup/structure |

Conventions:
- `TestBase : IDisposable` owns the DB/service provider and disposes the connection.
- Arrange data via repositories/services, not raw SQL.
- Fakes live in a shared `*Fakes.cs`; the fake store clones objects to mimic persistence boundaries.
- Time is controlled with `FakeClock`.
- One test class per feature; names like `Method_Condition_Expected`.
- Security regressions (cookie SameSite, anonymous callback, role access) have explicit tests.
- Run with `dotnet test` from the solution root.

---

## 8. Configuration

`appsettings.json` sections (placeholders only):

```json
{
  "Database": { "Provider": "SqlServer", "ConnectionString": "<set via secrets>" },
  "Sms": { "ApiBaseAddress": "<url>", "ApiKey": "<secret>", "ProviderPhone": "<sender>" },
  "Gateway": { "Merchant": "", "BaseAddress": "<url>", "CallbackBaseUrl": "<public https url>", "TimeoutSeconds": 30 },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*"
}
```

- `appsettings.Development.json` and User Secrets override per environment.
- Each section maps to an Options class in Infrastructure.

---

## 9. Startup Pipeline (Program.cs order)

1. `AddDbContext` (provider from config).
2. `AddIdentity` + password rules + `ConfigureApplicationCookie`.
3. Register repositories, `ITransactionRunner`, services.
4. `AddMemoryCache`, HTTP clients, adapters, options.
5. `AddRazorComponents().AddInteractiveServerComponents()`, `AddCascadingAuthenticationState`, `AddAuthorization`, `AddMudServices`.
6. `Build()`; in a scope: `MigrateAsync`, option sanity warnings, seed roles and admin.
7. Middleware: HSTS (non-Dev), HTTPS redirection, static files, authentication, authorization, anti-forgery.
8. Map minimal endpoints (login/logout/external callbacks) and `MapRazorComponents<App>().AddInteractiveServerRenderMode()`.
9. `app.Run()`; expose `public partial class Program { }`.

---

## 10. Starter Checklist for a New Project

1. Create `<App>.slnx`, `Directory.Packages.props` and the five `src` projects plus the test project with the reference graph from section 2.
2. Add Domain entities/enums and `AppRoles`.
3. Define Application interfaces (repositories, `ITransactionRunner`, external ports) and services.
4. Implement `ApplicationDbContext`, entity configurations, repositories, `EfTransactionRunner`; create the initial migration.
5. Add Infrastructure adapters and Options classes.
6. Compose everything in `Program.cs` (section 9); seed roles/admin from secrets.
7. Build layouts/pages with MudBlazor; protect pages with `[Authorize(Roles=...)]`.
8. Add tests: `TestBase` (SQLite in-memory), fakes, migration test, page-access tests, endpoint tests.
9. Keep secrets out of source control; enable `Secure` cookies and HTTPS in production.
10. Add CI: `dotnet restore`, `dotnet build`, `dotnet test`.
