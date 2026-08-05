# Agents.md - Residential Complex Financial Management System

## Project Overview

ASP.NET Core 10 Blazor Server application for managing financial operations of a residential complex. The UI is entirely in Persian (Farsi) with RTL layout. Built using Clean Architecture.

## Build & Run

```bash
dotnet restore ResidentialComplex.slnx
dotnet build ResidentialComplex.slnx
dotnet run --project src/ResidentialComplex.Web
dotnet test ResidentialComplex.slnx
```

## Project Structure

```
ResidentialComplex.slnx              # .slnx solution format
Directory.Packages.props             # Central Package Management
src/
  ResidentialComplex.Domain/         # Entities, Enums (no dependencies)
  ResidentialComplex.Application/    # Services, DTOs, Interfaces
  ResidentialComplex.Persistence/    # DbContext, EF configs, Repositories, Migrations
  ResidentialComplex.Infrastructure/ # Audit service implementation
  ResidentialComplex.Web/            # Blazor Server app, UI, Auth
tests/
  ResidentialComplex.Tests/          # Integration tests (xUnit, SQLite in-memory)
```

## Code Conventions

- All UI text is in **Persian (Farsi)**. Do not use English in user-facing strings.
- Layout direction is **RTL**. Use Bootstrap RTL variant.
- Font: **Vazirmatn** (loaded locally from `wwwroot/fonts/Vazirmatn.woff2`).
- Bootstrap 5.3.3 RTL is served locally from `wwwroot/lib/bootstrap/`. Do not use CDN links.
- All financial values must use **`decimal`** — never `float` or `double`.
- Use **manual EF Core migrations** in `ResidentialComplex.Persistence/Migrations/`. Do not use EF tooling to auto-generate migrations.
- The `PendingModelChangesWarning` is suppressed in `ApplicationDbContext.OnConfiguring`.
- Authentication is **username-based** (not email-based) via ASP.NET Identity.
- Auth endpoints are Minimal API at `/api/account/login` (POST) and `/api/account/logout` (GET) to avoid Blazor `NavigationException`.
- Login page uses a plain HTML `<form>` posting to the API endpoint, not a Blazor `EditForm`.
- Three roles: **Administrator**, **Worker**, **Resident**.
- Default admin credentials — username: `admin`, password: `Admin123`.
- Concurrency control uses `RowVersion` tokens on entities with financial impact.

## Database

- Supports **SQL Server** (primary) and **SQLite** (secondary/tests).
- Provider is configured in `appsettings.json` under `Database:Provider`.
- Migrations assembly is explicitly set to `ResidentialComplex.Persistence`.

## Testing

- 25 integration tests using **xUnit** with **SQLite in-memory**.
- Tests cover CRUD, billing calculations, identity, constraints, audit logging, and migrations.
- Run all tests: `dotnet test ResidentialComplex.slnx`

## Security Notes

- Never commit connection strings or credentials into source code.
- The default admin password in `Program.cs` is for initial setup only.
- Cookie authentication with `LoginPath = "/Account/Login"`.
- Anti-forgery is disabled on the login endpoint because it uses a plain HTML form outside the Blazor circuit.

## Architecture Rules

- **Domain** has zero external dependencies.
- **Application** depends only on Domain.
- **Persistence** depends on Domain and Application.
- **Infrastructure** depends on Application and Persistence.
- **Web** depends on all layers.
- Do not introduce circular dependencies between projects.
